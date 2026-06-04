using System.Collections.Generic;
using System.Linq;
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

        Dictionary<Biome, RoomDataSO[]> BuildBiomeRoomMap(RoomDataSO[] rooms)
        {
            var map = new Dictionary<Biome, List<RoomDataSO>>();
            foreach (var r in rooms)
            {
                if (r == null) continue;
                if (!map.TryGetValue(r.BiomeData, out var list))
                    map[r.BiomeData] = list = new();
                list.Add(r);
            }
            return map.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
        }

        RoomDataSO[] GetWeightedCandidates(DungeonGenerationContext ctx, RoomDataSO[] candidates)
        {
            var rolledTier = RollTier(ctx, WSDG_Tier.BaseTierWeights);
            var weighted = new List<RoomDataSO>();

            foreach (var c in candidates)
            {
                if (c == null || c.RoomTier != rolledTier) continue;
                weighted.Add(c);
            }

            if (weighted.Count == 0)
                foreach (var c in candidates)
                    if (c != null) weighted.Add(c);

            Shuffle(ctx.RNG, weighted);
            return weighted.ToArray();
        }

        public Vector3Int Generate(
            DungeonGenerationContext ctx,
            List<DungeonGenerator.PlacedRoom> placed,
            DungeonGenerator.Cell[,,] grid)
        {
            _spawnCounts.Clear();

            if (ctx.Theme == null)
            {
                Debug.LogError("[DunGen] Theme is missing.");
                return Vector3Int.zero;
            }

            if (ctx.Theme.startingRoom == null)
            {
                Debug.LogError("[DunGen] StartingRoom is missing.");
                return Vector3Int.zero;
            }

            int nextRoomId = 1;

            var alignment = ctx.Theme.startRoomAlignment;
            var raw = new Vector3Int(
                ThemeDataSO.EvaluateOrigin(alignment.x, ctx.GridSize.x),
                ThemeDataSO.EvaluateOrigin(alignment.y, ctx.GridSize.y),
                ThemeDataSO.EvaluateOrigin(alignment.z, ctx.GridSize.z));

            var center = ClampAnchorToGrid(ctx.Theme.startingRoom, raw, ctx.GridSize);

            FillBiomeGrid(ctx, center);
            var biomeRoomMap = BuildBiomeRoomMap(ctx.Theme.spawnableRooms);

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
                var biome = ctx.BiomeGrid[open.worldCell.x, open.worldCell.y, open.worldCell.z];

                if (!biomeRoomMap.TryGetValue(biome, out var pool) || pool.Length == 0)
                {
                    if (!biomeRoomMap.TryGetValue(Biome.Default, out pool) || pool.Length == 0)
                        continue;
                }

                var candidates = GetWeightedCandidates(ctx, pool);
                bool placedAny = false;

                for (int c = 0; c < candidates.Length; c++)
                {
                    var cand = candidates[c];
                    if (cand == null) continue;

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

        public void FillBiomeGrid(DungeonGenerationContext ctx, Vector3Int startCell)
        {
            var grid = new Biome[ctx.GridSize.x, ctx.GridSize.y, ctx.GridSize.z];

            for (int x = 0; x < ctx.GridSize.x; x++)
                for (int y = 0; y < ctx.GridSize.y; y++)
                    for (int z = 0; z < ctx.GridSize.z; z++)
                        grid[x, y, z] = Biome.None;

            ctx.BiomeGrid = grid;

            BiomeDataSO startBiome = ctx.Theme.GetStartingBiome();
            if (startBiome != null)
            {
                int r = ctx.RNG.Next(startBiome.minRadius, startBiome.maxRadius + 1);
                PlaceBiomeBubble(ctx, startCell, startBiome.biome, r);
            }
            else
                Debug.LogWarning("Starting biome data is not set on spawnable biomes on the theme");
            
            int volume = ctx.GridSize.x * ctx.GridSize.y * ctx.GridSize.z;
            int bubbleCount = Mathf.Max(1, volume / 20);

            var biomes = ctx.Theme.spawnableBiomes;
            if (biomes == null || biomes.Length == 0) return;

            float totalWeight = 0f;
            foreach (var b in biomes) totalWeight += b.spawnWeight;

            for (int i = 0; i < bubbleCount; i++)
            {
                var biomeData = RollWeightedBiome(ctx.RNG, biomes, totalWeight);
                if (biomeData == null) continue;

                var pos = new Vector3Int(
                    ctx.RNG.Next(ctx.GridSize.x),
                    ctx.RNG.Next(ctx.GridSize.y),
                    ctx.RNG.Next(ctx.GridSize.z));

                int radius = ctx.RNG.Next(biomeData.minRadius, biomeData.maxRadius + 1);
                PlaceBiomeBubble(ctx, pos, biomeData.biome, radius);
            }

            FloodFillRemainingCells(ctx);
        }

        void FloodFillRemainingCells(DungeonGenerationContext ctx)
        {
            var queue = new Queue<Vector3Int>();

            for (int x = 0; x < ctx.GridSize.x; x++)
                for (int y = 0; y < ctx.GridSize.y; y++)
                    for (int z = 0; z < ctx.GridSize.z; z++)
                        if (ctx.BiomeGrid[x, y, z] != Biome.None)
                            queue.Enqueue(new Vector3Int(x, y, z));

            int[] dx = { 1, -1, 0, 0, 0, 0 };
            int[] dy = { 0, 0, 1, -1, 0, 0 };
            int[] dz = { 0, 0, 0, 0, 1, -1 };

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                var biome = ctx.BiomeGrid[cell.x, cell.y, cell.z];

                for (int i = 0; i < 6; i++)
                {
                    var nb = new Vector3Int(cell.x + dx[i], cell.y + dy[i], cell.z + dz[i]);
                    if (!ctx.InBounds(nb)) continue;
                    if (ctx.BiomeGrid[nb.x, nb.y, nb.z] != Biome.None) continue;
                    ctx.BiomeGrid[nb.x, nb.y, nb.z] = biome;
                    queue.Enqueue(nb);
                }
            }
        }

        void PlaceBiomeBubble(DungeonGenerationContext ctx, Vector3Int center, Biome biome, int radius)
        {
            for (int x = -radius; x <= radius; x++)
                for (int y = -radius; y <= radius; y++)
                    for (int z = -radius; z <= radius; z++)
                    {
                        float dist = Mathf.Sqrt(x * x + (y * y * 4f) + z * z);
                        if (dist > radius) continue;

                        var cell = center + new Vector3Int(x, y, z);
                        if (!ctx.InBounds(cell)) continue;

                        if (ctx.BiomeGrid[cell.x, cell.y, cell.z] != Biome.None) continue;

                        ctx.BiomeGrid[cell.x, cell.y, cell.z] = biome;
                    }
        }

        static BiomeDataSO RollWeightedBiome(System.Random rng, BiomeDataSO[] biomes, float totalWeight)
        {
            float roll = (float)(rng.NextDouble() * totalWeight);
            foreach (var b in biomes)
            {
                roll -= b.spawnWeight;
                if (roll <= 0f) return b;
            }
            return biomes[^1];
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
                biome = ctx.BiomeGrid != null
                    ? ctx.BiomeGrid[anchor.x, anchor.y, anchor.z]
                    : data.BiomeData
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
