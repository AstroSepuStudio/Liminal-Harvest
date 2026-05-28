#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WS_ProceduralGeneration
{
    [CustomEditor(typeof(DungeonGenerator))]
    public class DungeonGeneratorEditor : Editor
    {
        private RoomDataSO _targetRoom;
        private int _maxAttempts = 10000;
        private bool _searching = false;
        private string _lastResult = null;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Seed Finder", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Simulates dungeon generation and searches " +
                "for a seed that places the target room. Result is printed to the Console.",
                MessageType.Info);

            _targetRoom = (RoomDataSO)EditorGUILayout.ObjectField(
                "Target Room", _targetRoom, typeof(RoomDataSO), false);

            _maxAttempts = EditorGUILayout.IntField("Max Attempts", _maxAttempts);

            EditorGUI.BeginDisabledGroup(_searching || _targetRoom == null);
            if (GUILayout.Button("Find Seed", GUILayout.Height(30)))
                RunSearch();
            EditorGUI.EndDisabledGroup();

            if (_targetRoom == null)
                EditorGUILayout.HelpBox("Assign a RoomDataSO to search for.", MessageType.Warning);

            if (!string.IsNullOrEmpty(_lastResult))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(_lastResult, MessageType.None);
            }
        }

        private void RunSearch()
        {
            var gen = (DungeonGenerator)target;

            var so = new SerializedObject(gen);

            var themeObj = so.FindProperty("theme").objectReferenceValue as ThemeDataSO;
            var gridSizeV = so.FindProperty("gridSize").vector3IntValue;
            int maxRooms = so.FindProperty("maxRooms").intValue;
            int maxDepth = so.FindProperty("maxDepth").intValue;
            int cellSize = so.FindProperty("cellSize").intValue;
            int mapSize = so.FindProperty("simMapSize").intValue;

            if (LobbySettings.Instance != null)
                mapSize = Mathf.Max(1, LobbySettings.Instance.MapSize);

            if (themeObj == null)
            {
                _lastResult = "No ThemeDataSO assigned on the DungeonGenerator.";
                return;
            }
            if (themeObj.startingRoom == null)
            {
                _lastResult = "ThemeDataSO has no startingRoom.";
                return;
            }

            _searching = true;
            _lastResult = "Searching?";
            Repaint();

            int? found = null;
            int attempts = 0;
            Vector3Int foundAnchor = default;

            Vector3Int effective = new(gridSizeV.x * mapSize, gridSizeV.y * mapSize, gridSizeV.z * mapSize);
            int realMaxRooms = maxRooms * mapSize;
            int realMaxDepth = maxDepth * mapSize;

            for (attempts = 0; attempts < _maxAttempts; attempts++)
            {
                int trySeed = Random.Range(int.MinValue, int.MaxValue);

                if (SimulateContainsRoom(
                        trySeed,
                        themeObj,
                        effective,
                        realMaxRooms,
                        realMaxDepth,
                        _targetRoom,
                        out foundAnchor))
                {
                    found = trySeed;
                    break;
                }
            }

            _searching = false;

            if (found.HasValue)
            {
                Vector3 worldPos = new(foundAnchor.x * cellSize, foundAnchor.y * cellSize, foundAnchor.z * cellSize);
                _lastResult = $"Found after {attempts + 1} attempt(s)!\nSeed: {found.Value}\nWorld Pos: {worldPos}";
                Debug.Log($"[SeedFinder] Seed <b>{found.Value}</b> generates room '{_targetRoom.name}' " +
                          $"at world position <b>{worldPos}</b> " +
                          $"(grid anchor {foundAnchor}, found in {attempts + 1} attempt(s)).");
            }
            else
            {
                _lastResult = $"Room not found after {_maxAttempts} attempts.";
                Debug.LogWarning($"[SeedFinder] Could not find a seed that places '{_targetRoom.name}' " +
                                 $"in {_maxAttempts} attempts.");
            }

            Repaint();
        }

        private static bool SimulateContainsRoom(
            int seed,
            ThemeDataSO theme,
            Vector3Int effectiveGridSize,
            int maxRooms,
            int maxDepth,
            RoomDataSO target,
            out Vector3Int foundAnchor)
        {
            foundAnchor = default;
            var rng = new System.Random(seed);

            void Shuffle<T>(IList<T> list)
            {
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(0, i + 1);
                    (list[i], list[j]) = (list[j], list[i]);
                }
            }

            WSDG_Tier.Tier RollTier(Dictionary<WSDG_Tier.Tier, float> weights)
            {
                float total = 0f;
                foreach (var kvp in weights) total += kvp.Value;
                float roll = (float)(rng.NextDouble() * total);
                foreach (var kvp in weights) { roll -= kvp.Value; if (roll <= 0f) return kvp.Key; }
                return WSDG_Tier.Tier.Common;
            }

            RoomDataSO[] GetWeightedCandidates(RoomDataSO[] candidates, Biome neighborBiome)
            {
                WSDG_Tier.Tier tier = RollTier(WSDG_Tier.BaseTierWeights);
                var weighted = new List<RoomDataSO>();
                foreach (var c in candidates)
                {
                    if (c == null) continue;
                    if (c.RoomTier != tier) continue;
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
                Shuffle(weighted);
                return weighted.ToArray();
            }

            int gx = effectiveGridSize.x, gy = effectiveGridSize.y, gz = effectiveGridSize.z;
            var occupancy = new int[gx, gy, gz];

            bool InBounds(Vector3Int p) =>
                p.x >= 0 && p.y >= 0 && p.z >= 0 &&
                p.x < gx && p.y < gy && p.z < gz;

            bool FootprintFits(RoomDataSO data, Vector3Int anchor)
            {
                if (data == null) return false;
                foreach (var fp in data.RoomFootprint)
                {
                    var w = anchor + fp.Footprint;
                    if (!InBounds(w)) return false;
                    if (occupancy[w.x, w.y, w.z] != 0) return false;
                }
                return true;
            }

            var placedRooms = new List<(RoomDataSO data, Vector3Int anchor, int depth, Biome biome)>();

            bool PlaceRoom(RoomDataSO data, Vector3Int anchor, int depth)
            {
                if (!FootprintFits(data, anchor)) return false;
                int id = placedRooms.Count + 1;
                placedRooms.Add((data, anchor, depth, data.biome));
                foreach (var fp in data.RoomFootprint)
                {
                    var w = anchor + fp.Footprint;
                    occupancy[w.x, w.y, w.z] = id;
                }
                return true;
            }

            var frontier = new List<(int roomIdx, Vector3Int worldCell, Direction face, Vector3Int localCell, int depth, RoomDataSO.PortType type)>();

            void PushPorts(int roomIdx)
            {
                var (data, anchor, depth, _) = placedRooms[roomIdx];
                foreach (var port in data.Ports)
                {
                    if (!data.ContainsLocalCell(port.localCell)) continue;
                    frontier.Add((roomIdx, anchor + port.localCell, port.face, port.localCell, depth, port.type));
                }
            }

            var center = new Vector3Int(gx / 2, Mathf.Clamp(gy / 2, 0, gy - 1), gz / 2);
            if (!PlaceRoom(theme.startingRoom, center, 0))
                PlaceRoom(theme.startingRoom, new Vector3Int(1, 0, 0), 0);

            if (placedRooms.Count == 0) return false;

            PushPorts(0);

            while (frontier.Count > 0 && placedRooms.Count < maxRooms)
            {
                int idx = rng.Next(frontier.Count);
                var open = frontier[idx];
                frontier.RemoveAt(idx);

                if (open.depth >= maxDepth) continue;

                var neighborBiome = placedRooms[open.roomIdx].biome;
                var candidates = GetWeightedCandidates(theme.spawnableRooms, neighborBiome);

                bool placedAny = false;
                int biomeSearchTries = candidates.Length / 10;

                foreach (var cand in candidates)
                {
                    if (cand == null) continue;
                    if (cand.biome != neighborBiome && biomeSearchTries > 0) { biomeSearchTries--; continue; }

                    foreach (var port in cand.Ports)
                    {
                        if (port.type != open.type) continue;
                        if (port.face != DirectionUtils.OppositeDirection(open.face)) continue;

                        var anchor = open.worldCell + DirectionUtils.DirectionVector(open.face) - port.localCell;
                        if (!FootprintFits(cand, anchor)) continue;

                        int newIdx = placedRooms.Count;
                        if (!PlaceRoom(cand, anchor, open.depth + 1)) continue;

                        var (newData, newAnchor, newDepth, _) = placedRooms[newIdx];
                        foreach (var np in newData.Ports)
                        {
                            if (!newData.ContainsLocalCell(np.localCell)) continue;
                            var npWorld = newAnchor + np.localCell;
                            if (npWorld == open.worldCell && np.face == DirectionUtils.OppositeDirection(open.face))
                                continue;
                            frontier.Add((newIdx, npWorld, np.face, np.localCell, newDepth, np.type));
                        }

                        placedAny = true;
                        break;
                    }
                    if (placedAny) break;
                }
            }

            foreach (var (data, anchor, _, _) in placedRooms)
            {
                if (data == target)
                {
                    foundAnchor = anchor;
                    return true;
                }
            }

            return false;
        }
    }
#endif
}
