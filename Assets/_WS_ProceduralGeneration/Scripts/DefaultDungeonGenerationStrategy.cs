using System.Collections.Generic;
using UnityEngine;

namespace WS_ProceduralGeneration
{
    public class DefaultDungeonGenerationStrategy : IDungeonGenerationStrategy
    {
        readonly Dictionary<RoomDataSO, int> _spawnCounts = new();

        struct OpenPort
        {
            public int roomId;
            public Vector3Int worldCell;
            public Direction face;
            public Vector3Int localCell;
            public int depth;
            public RoomDataSO.PortType type;
        }

        public Vector3Int Generate(
            DungeonGenerationContext ctx,
            List<DungeonGenerator.PlacedRoom> placed,
            DungeonGenerator.Cell[,,] grid)
        {
            _spawnCounts.Clear();

            if (ctx.Theme?.startingRoom == null)
            {
                Debug.LogError("[DunGen] Theme or startingRoom is missing.");
                return Vector3Int.zero;
            }

            int nextRoomId = 1;

            var alignment = ctx.Theme.startRoomAlignment;
            var raw = new Vector3Int(
                ThemeDataSO.EvaluateOrigin(alignment.x, ctx.GridSize.x),
                ThemeDataSO.EvaluateOrigin(alignment.y, ctx.GridSize.y),
                ThemeDataSO.EvaluateOrigin(alignment.z, ctx.GridSize.z));

            var center = ClampAnchorToGrid(ctx.Theme.startingRoom, raw, ctx.GridSize);

            var start = Place(ctx.Theme.startingRoom, center, 0, ctx, placed, grid, ref nextRoomId);
            if (start == null)
            {
                center = new Vector3Int(1, 0, 0);
                start = Place(ctx.Theme.startingRoom, center, 0, ctx, placed, grid, ref nextRoomId);
            }
            if (start == null) { Debug.LogError("[DunGen] Failed to place starting room."); return center; }

            var frontier = BuildOpenPorts(start);

            int maxRooms = ctx.Settings.maxRoomsBase * LobbySettings.Instance.MapSize;
            int maxDepth = ctx.Settings.maxDepthBase * LobbySettings.Instance.MapSize;

            while (frontier.Count > 0 && placed.Count < maxRooms)
            {
                int idx = ctx.RNG.Next(frontier.Count);
                var open = frontier[idx]; frontier.RemoveAt(idx);

                if (open.depth >= maxDepth) continue;

                var neighborRoom = placed.Find(r => r.id == open.roomId);
                var candidates = GetWeightedCandidates(ctx, ctx.Theme.spawnableRooms, neighborRoom.biome);

                bool placedAny = false;
                int biomeSearchTries = candidates.Length / 10;

                for (int c = 0; c < candidates.Length; c++)
                {
                    var cand = candidates[c];
                    if (cand == null) continue;
                    if (cand.biome != neighborRoom.biome && biomeSearchTries > 0) { biomeSearchTries--; continue; }

                    for (int p = 0; p < cand.Ports.Length; p++)
                    {
                        var port = cand.Ports[p];
                        if (port.type != open.type) continue;
                        if (port.face != DirectionUtils.OppositeDirection(open.face)) continue;

                        var anchor = open.worldCell + DirectionUtils.DirectionVector(open.face) - port.localCell;
                        if (!ctx.FootprintFits(cand, grid, anchor)) continue;
                        if (!SatisfiesConstraints(cand, anchor, open.depth + 1, ctx.GridSize)) continue;

                        var pr = Place(cand, anchor, open.depth + 1, ctx, placed, grid, ref nextRoomId);
                        if (pr == null) continue;

                        var newPorts = BuildOpenPorts(pr);
                        for (int i = newPorts.Count - 1; i >= 0; i--)
                        {
                            var np = newPorts[i];
                            if (np.worldCell == open.worldCell &&
                                np.face == DirectionUtils.OppositeDirection(open.face))
                            { newPorts.RemoveAt(i); break; }
                        }

                        frontier.AddRange(newPorts);
                        placedAny = true;
                        break;
                    }
                    if (placedAny) break;
                }
            }

            return center;
        }

        private DungeonGenerator.PlacedRoom Place(
            RoomDataSO data, Vector3Int anchor, int depth,
            DungeonGenerationContext ctx,
            List<DungeonGenerator.PlacedRoom> placed,
            DungeonGenerator.Cell[,,] grid,
            ref int nextRoomId)
        {
            if (!ctx.FootprintFits(data, grid, anchor)) return null;

            var pr = new DungeonGenerator.PlacedRoom
            {
                id = nextRoomId++,
                data = data,
                anchor = anchor,
                depth = depth,
                biome = data.biome
            };

            placed.Add(pr);
            if (data.constraints.maxSpawns >= 0)
            {
                _spawnCounts.TryGetValue(data, out int count);
                _spawnCounts[data] = count + 1;
            }

            foreach (var local in data.RoomFootprint)
            {
                var w = anchor + local.Footprint;
                grid[w.x, w.y, w.z].placedRoom = pr;
                grid[w.x, w.y, w.z].local = local.Footprint;
            }

            return pr;
        }

        static List<OpenPort> BuildOpenPorts(DungeonGenerator.PlacedRoom pr)
        {
            var list = new List<OpenPort>(pr.data.Ports.Length);
            foreach (var port in pr.data.Ports)
            {
                if (!pr.data.ContainsLocalCell(port.localCell)) continue;
                list.Add(new OpenPort
                {
                    roomId = pr.id,
                    worldCell = pr.anchor + port.localCell,
                    face = port.face,
                    localCell = port.localCell,
                    depth = pr.depth,
                    type = port.type
                });
            }
            return list;
        }

        public static Vector3Int ClampAnchorToGrid(RoomDataSO room, Vector3Int anchor, Vector3Int gridSize)
        {
            Vector3Int fpMin = Vector3Int.zero;
            Vector3Int fpMax = Vector3Int.zero;

            foreach (var fp in room.RoomFootprint)
            {
                if (fp.Footprint.x < fpMin.x) fpMin.x = fp.Footprint.x;
                if (fp.Footprint.y < fpMin.y) fpMin.y = fp.Footprint.y;
                if (fp.Footprint.z < fpMin.z) fpMin.z = fp.Footprint.z;

                if (fp.Footprint.x > fpMax.x) fpMax.x = fp.Footprint.x;
                if (fp.Footprint.y > fpMax.y) fpMax.y = fp.Footprint.y;
                if (fp.Footprint.z > fpMax.z) fpMax.z = fp.Footprint.z;
            }

            return new Vector3Int(
                Mathf.Clamp(anchor.x, -fpMin.x, gridSize.x - 1 - fpMax.x),
                Mathf.Clamp(anchor.y, -fpMin.y, gridSize.y - 1 - fpMax.y),
                Mathf.Clamp(anchor.z, -fpMin.z, gridSize.z - 1 - fpMax.z));
        }

        RoomDataSO[] GetWeightedCandidates(DungeonGenerationContext ctx, RoomDataSO[] candidates, Biome neighborBiome)
        {
            var rolledTier = RollTier(ctx, WSDG_Tier.BaseTierWeights);
            var weighted = new List<RoomDataSO>();

            foreach (var c in candidates)
            {
                if (c == null || c.RoomTier != rolledTier) continue;
                weighted.Add(c);
                if (c.biome == neighborBiome) { weighted.Add(c); weighted.Add(c); }
            }

            if (weighted.Count == 0)
                foreach (var c in candidates)
                {
                    if (c == null) continue;
                    weighted.Add(c);
                    if (c.biome == neighborBiome) { weighted.Add(c); weighted.Add(c); }
                }

            Shuffle(ctx.RNG, weighted);
            return weighted.ToArray();
        }

        bool SatisfiesConstraints(RoomDataSO room, Vector3Int anchor, int depth, Vector3Int gridSize)
        {
            var c = room.constraints;

            if (c.minDepth > 0 && depth < c.minDepth) return false;

            if (c.maxSpawns >= 0)
            {
                _spawnCounts.TryGetValue(room, out int count);
                if (count >= c.maxSpawns) return false;
            }

            if (!RoomDataSO.SatisfiesAxisConstraint(c.x, anchor.x, gridSize.x)) return false;
            if (!RoomDataSO.SatisfiesAxisConstraint(c.y, anchor.y, gridSize.y)) return false;
            if (!RoomDataSO.SatisfiesAxisConstraint(c.z, anchor.z, gridSize.z)) return false;

            return true;
        }

        static WSDG_Tier.Tier RollTier(DungeonGenerationContext ctx, Dictionary<WSDG_Tier.Tier, float> weights)
        {
            float total = 0f;
            foreach (var kvp in weights) total += kvp.Value;
            float roll = (float)(ctx.RNG.NextDouble() * total);
            foreach (var kvp in weights) { roll -= kvp.Value; if (roll <= 0f) return kvp.Key; }
            return WSDG_Tier.Tier.Common;
        }

        static void Shuffle<T>(System.Random rng, IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
