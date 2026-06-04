using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.Events;

namespace WS_ProceduralGeneration
{
    public class DungeonGenerator : MonoBehaviour
    {
        public static DungeonGenerator Instance;

        [Header("Content")]
        [SerializeField] Transform roomsParent;
        [SerializeField] DungeonSettingsSO settings;
        [SerializeField] ThemeDataSO theme;
        [SerializeField] MonoBehaviour generationStrategyOverride;

        [Header("Optional")]
        [SerializeField] float generationDelay = 0f; // Useful for UI animations
        [SerializeField] NavMeshSurface surface;
        [SerializeField] AudioReverbZone reverbZone;

        [Header("Debug")]
        [SerializeField, Min(1)] int simMapSize = 10;
        [SerializeField] bool showBiomeGrid = false;

        public System.Random RNG { get; private set; }

        Cell[,,] grid;
        Biome[,,] _biomeGrid;

        int currentMapSize = -1;
        float maxDistance = 0;
        bool _generated = false;
        Vector3Int effectiveGridSize;
        readonly List<PlacedRoom> placed = new();
        readonly List<IDungeonSpawner> spawners = new();
        readonly Dictionary<int, RoomData> spawned = new();
        IDungeonGenerationStrategy _strategy;

        public UnityEvent<float, float> OnDungeonSetUp;
        public UnityEvent<int> OnGenerationStarted;
        public UnityEvent<int> OnGenerationFinished;
        public UnityEvent OnDungeonGenerated;
        public UnityEvent OnDungeonClear;

        public Vector3Int GridSize => effectiveGridSize;
        public Dictionary<int, HashSet<int>> RoomAdjacency = new();
        public Vector3Int StartRoomPos;

        public Cell[,,] Grid => grid;
        public ThemeDataSO Theme => theme;
        public float MaxDistance => maxDistance;
        public int CellSize => settings.cellSize;
        public DungeonSettingsSO Settings => settings;
        public IReadOnlyList<PlacedRoom> PlacedRooms => placed;
        public IReadOnlyDictionary<int, RoomData> SpawnedRooms => spawned;

        public class PlacedRoom
        {
            public int id;
            public RoomDataSO data;
            public Vector3Int anchor;
            public int depth;
            public Biome biome;
        }

        public class Cell
        {
            public PlacedRoom placedRoom;
            public Vector3Int local;
        }

        void Awake()
        {
            Instance = this;

            _strategy = generationStrategyOverride as IDungeonGenerationStrategy
                ?? new DefaultDungeonGenerationStrategy();

            GetComponentsInChildren(true, spawners);
            spawners.Clear();
            foreach (var s in GetComponentsInChildren<MonoBehaviour>(true))
                if (s is IDungeonSpawner ds) spawners.Add(ds);

            Debug.Log($"[Generator] spawners count: {spawners.Count}", this);
        }

        public void StartGeneration()
        {
            if (theme == null)
            {
                Debug.LogWarning("[Generator] There is no theme assigned to the dungeon generator", this);
                return;
            }

            StartGeneration(theme);
        }

        public void StartGeneration(int mapSize)
        {
            StartGeneration(mapSize, theme);
        }

        public void StartGeneration(ThemeDataSO theme)
        {
            StartGeneration(Random.Range(5, 10), theme);
        }

        public void StartGeneration(int mapSize, ThemeDataSO theme)
        {
            StartGeneration(mapSize, theme, Random.Range(int.MinValue, int.MaxValue));
        }

        public void StartGeneration(int mapSize, ThemeDataSO theme, int seed)
        {
            if (_generated) return;
            _generated = true;

            if (mapSize <= 0) mapSize = 1;
            currentMapSize = mapSize;

            this.theme = theme;
            effectiveGridSize = CalculateEffectiveGrid(mapSize);

            grid = new Cell[effectiveGridSize.x, effectiveGridSize.y, effectiveGridSize.z];
            for (int x = 0; x < effectiveGridSize.x; x++)
                for (int y = 0; y < effectiveGridSize.y; y++)
                    for (int z = 0; z < effectiveGridSize.z; z++)
                        grid[x, y, z] = new Cell();

            RNG = new System.Random(seed);

            OnDungeonSetUp?.Invoke(mapSize, seed);
            StartCoroutine(GenerationCoroutine(seed));
        }

        void Generate()
        {
            var ctx = new DungeonGenerationContext
            {
                RNG = RNG,
                GridSize = effectiveGridSize,
                CellSize = CellSize,
                Settings = settings,
                Theme = theme
            };

            var startAnchor = _strategy.Generate(ctx, placed, grid);
            StartRoomPos = startAnchor * CellSize;
            _biomeGrid = ctx.BiomeGrid;
        }

        #region Helpers

        private Vector3Int CalculateEffectiveGrid(int size)
        {
            Vector3Int res = settings.baseGridSize;
            int steps = size - 1;
            var growth = theme.gridGrowth;

            res.x *= 1 + ThemeDataSO.EvaluateGrowthSteps(growth.x, steps);
            res.y *= 1 + ThemeDataSO.EvaluateGrowthSteps(growth.y, steps);
            res.z *= 1 + ThemeDataSO.EvaluateGrowthSteps(growth.z, steps);

            return res;
        }

        public bool InBounds(Vector3Int p) =>
            p.x >= 0 && p.y >= 0 && p.z >= 0 &&
            p.x < effectiveGridSize.x && p.y < effectiveGridSize.y && p.z < effectiveGridSize.z;

        void BuildRoomAdjacency()
        {
            foreach (var pr in placed)
            {
                if (!RoomAdjacency.ContainsKey(pr.id)) RoomAdjacency[pr.id] = new();

                foreach (var port in pr.data.Ports)
                {
                    Vector3Int worldCell = pr.anchor + port.localCell;
                    Vector3Int neighborPos = worldCell + DirectionUtils.DirectionVector(port.face);
                    if (!InBounds(neighborPos)) continue;

                    var neighborCell = grid[neighborPos.x, neighborPos.y, neighborPos.z];
                    if (neighborCell?.placedRoom == null) continue;

                    var neighborRoom = neighborCell.placedRoom;
                    bool hasMatchingPort = false;
                    foreach (var np in neighborRoom.data.Ports)
                    {
                        if (np.type == port.type &&
                            np.localCell == neighborPos - neighborRoom.anchor &&
                            np.face == DirectionUtils.OppositeDirection(port.face))
                        { hasMatchingPort = true; break; }
                    }

                    if (hasMatchingPort) RoomAdjacency[pr.id].Add(neighborRoom.id);
                }
            }
        }

        public int GetRoomIdAtPosition(Vector3 worldPos)
        {
            Vector3Int cellPos = new(
                Mathf.FloorToInt(worldPos.x / CellSize),
                Mathf.FloorToInt(worldPos.y / CellSize),
                Mathf.FloorToInt(worldPos.z / CellSize));

            if (!InBounds(cellPos)) return -1;
            var cell = grid[cellPos.x, cellPos.y, cellPos.z];
            if (cell?.placedRoom != null) return cell.placedRoom.id;

            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;
                        var nb = cellPos + new Vector3Int(dx, dy, dz);
                        if (!InBounds(nb)) continue;
                        var nbCell = grid[nb.x, nb.y, nb.z];
                        if (nbCell?.placedRoom != null) return nbCell.placedRoom.id;
                    }

            return -1;
        }

        public Vector3 GetRandomPosition(float maxOffset = 4f)
        {
            int index = Random.Range(0, spawned.Count);
            var world = Vector3.Scale((Vector3)placed[index].anchor, new Vector3(CellSize, CellSize, CellSize));
            world.x += Random.Range(-maxOffset, maxOffset);
            world.z += Random.Range(-maxOffset, maxOffset);
            return world;
        }

        public RoomData GetRoomDataAtPosition(Vector3 worldPos)
        {
            int roomId = GetRoomIdAtPosition(worldPos);
            if (roomId == -1) return null;
            spawned.TryGetValue(roomId, out var rd);
            return rd;
        }

        #endregion

        #region Instantiate

        void InstantiateRooms()
        {
            var pointsByChannel = new Dictionary<SpawnChannel, List<DungeonSpawnPoint>>();

            foreach (var pr in placed)
            {
                var world = Vector3.Scale((Vector3)pr.anchor, new Vector3(CellSize, CellSize, CellSize));
                var go = Instantiate(pr.data.Prefab, world, Quaternion.identity, roomsParent);
                go.name = $"Room {pr.id} (depth {pr.depth})";

                if (!go.TryGetComponent<RoomData>(out var rd))
                    Debug.LogWarning($"[DunGen] Prefab {pr.data.Prefab.name} lacks RoomData component.");

                rd.PlacedRoom = pr;
                spawned[pr.id] = rd;

                float distance = Vector3.Distance(spawned[placed[0].id].transform.position, go.transform.position);
                if (distance > maxDistance) maxDistance = distance;

                foreach (var sp in rd.SpawnPoints)
                {
                    if (sp == null || sp.Channel == null) continue;
                    if (!pointsByChannel.TryGetValue(sp.Channel, out var list))
                        pointsByChannel[sp.Channel] = list = new();
                    list.Add(sp);
                }
            }

            BuildRoomAdjacency();

            if (reverbZone != null && spawned.Count > 0)
            {
                reverbZone.transform.position = spawned[placed[0].id].transform.position;
                reverbZone.minDistance = maxDistance * settings.reverbMinDistanceMultiplier;
                reverbZone.maxDistance = maxDistance * settings.reverbMaxDistanceMultiplier;
                reverbZone.reverbPreset = theme.reverbPreset;
            }

            RunSpawners(pointsByChannel);

            if (surface != null)
                StartCoroutine(GenerateNavMeshSurface());
        }

        void RunSpawners(Dictionary<SpawnChannel, List<DungeonSpawnPoint>> pointsByChannel)
        {
            var sorted = spawners
                .Where(s => s.Channel != null)
                .OrderBy(s => s.SpawnOrder)
                .ToList();

            foreach (var spawner in sorted)
            {
                pointsByChannel.TryGetValue(spawner.Channel, out var pts);
                spawner.Collect(pts ?? new List<DungeonSpawnPoint>());
                spawner.Spawn(this);

                foreach (var newPoint in spawner.DiscoveredPoints)
                {
                    if (newPoint == null || newPoint.Channel == null) continue;
                    if (!pointsByChannel.TryGetValue(newPoint.Channel, out var list))
                        pointsByChannel[newPoint.Channel] = list = new();
                    list.Add(newPoint);
                }
            }
        }

        void ResolveDoors()
        {
            foreach (var pr in placed)
            {
                if (!spawned.TryGetValue(pr.id, out var rd) || rd == null) continue;

                for (int i = 0; i < pr.data.Ports.Length; i++)
                {
                    var port = pr.data.Ports[i];
                    var worldCell = pr.anchor + port.localCell;
                    var nb = worldCell + DirectionUtils.DirectionVector(port.face);

                    bool open = false;
                    if (InBounds(nb))
                    {
                        var nOcc = grid[nb.x, nb.y, nb.z].placedRoom;
                        if (nOcc != null)
                        {
                            foreach (var np in nOcc.data.Ports)
                            {
                                if (np.type == port.type &&
                                    np.localCell == nb - nOcc.anchor &&
                                    np.face == DirectionUtils.OppositeDirection(port.face))
                                { open = true; break; }
                            }
                        }
                    }

                    rd.SetPort(i, open);
                }
            }
        }

        IEnumerator GenerateNavMeshSurface()
        {
            yield return null;
            surface.BuildNavMesh();
        }

        #endregion

        public bool GeneratedDungeon =>
            spawned != null && placed != null && spawned.Count > 0 && placed.Count > 0;

        IEnumerator GenerationCoroutine(int seed)
        {
            yield return null;

            OnGenerationStarted?.Invoke(seed);

            yield return new WaitForSeconds(generationDelay);

            Generate();
            InstantiateRooms();
            ResolveDoors();
            GenSeedSaver.SaveSeed(seed);

            OnDungeonGenerated?.Invoke();
            OnGenerationFinished?.Invoke(seed);
        }

        public void ClearMap()
        {
            OnDungeonClear?.Invoke();

            foreach (var spawner in spawners)
                spawner.Clear();

            DestroyChildren(roomsParent);

            placed.Clear();
            spawned.Clear();
            RoomAdjacency.Clear();

            grid = null;
            _biomeGrid = null;

            _generated = false;
            maxDistance = 0;
            RNG = null;

            if (surface != null) surface.RemoveData();
        }

        void DestroyChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        public float GetDificultyMultiplier(Vector3 targetPos)
        {
            Vector3 initialRoomPos = spawned[placed[0].id].transform.position;
            float gridDistance = Vector3.Distance(initialRoomPos, targetPos) / 5f;
            return settings.difficultyCurve.Evaluate(gridDistance);
        }

        static Dictionary<Biome, Color> BuildBiomeColorMap() => new()
        {
            { Biome.Default,  Color.gray },
            { Biome.Dark,     new Color(0.24f, 0.20f, 0.54f) },
            { Biome.Hallway,  new Color(0.11f, 0.62f, 0.46f) },
            { Biome.Holes,    new Color(0.85f, 0.35f, 0.19f) },
            { Biome.Pillar,   new Color(0.83f, 0.33f, 0.49f) },
        };

        void OnDrawGizmosSelected()
        {
            if (settings == null || theme == null || theme.startingRoom == null) return;

            int size = currentMapSize != -1 ? currentMapSize : simMapSize;
            size = Mathf.Max(1, size);

            Vector3Int effective = CalculateEffectiveGrid(size);
            float cs = CellSize;

            Vector3 totalSize = new(effective.x * cs, effective.y * cs, effective.z * cs);
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(transform.position + totalSize * 0.5f, totalSize);

            var alignment = theme.startRoomAlignment;
            var raw = new Vector3Int(
                ThemeDataSO.EvaluateOrigin(alignment.x, effective.x),
                ThemeDataSO.EvaluateOrigin(alignment.y, effective.y),
                ThemeDataSO.EvaluateOrigin(alignment.z, effective.z));

            Vector3Int startCell = DefaultDungeonGenerationStrategy.ClampAnchorToGrid(
                theme.startingRoom, raw, effective);

            Gizmos.color = Color.green;
            Vector3 anchorWorld = transform.position + new Vector3(startCell.x * cs, startCell.y * cs, startCell.z * cs);

            foreach (var fp in theme.startingRoom.RoomFootprint)
            {
                Vector3 cellWorld = anchorWorld + new Vector3(
                    fp.Footprint.x * cs + cs * 0.5f,
                    fp.Footprint.y * cs + cs * 0.5f,
                    fp.Footprint.z * cs + cs * 0.5f);
                Gizmos.DrawWireCube(cellWorld, Vector3.one * cs);
            }

            if (showBiomeGrid && _biomeGrid != null)
            {
                var biomeColors = BuildBiomeColorMap();

                for (int x = 0; x < effectiveGridSize.x; x++)
                    for (int y = 0; y < effectiveGridSize.y; y++)
                        for (int z = 0; z < effectiveGridSize.z; z++)
                        {
                            var biome = _biomeGrid[x, y, z];
                            if (!biomeColors.TryGetValue(biome, out var col)) continue;

                            col.a = 0.18f;
                            Gizmos.color = col;
                            Vector3 center = transform.position + new Vector3(
                                x * cs + cs * 0.5f,
                                y * cs + cs * 0.5f,
                                z * cs + cs * 0.5f);
                            Gizmos.DrawCube(center, 0.92f * cs * Vector3.one);
                        }
            }
        }
    }
}
