using Mirror;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using WS_ProceduralGeneration;

namespace WS_ProceduralGeneration
{
public class DNG_MapModule : NetworkBehaviour
{
    [System.Serializable]
    struct DirectionalSprite
    { public Sprite sprite; public RoomDataSO.PortType type; public Direction direction; }

    [Header("References")]
    [SerializeField] LobbyManagerScreen lobbyScreen;
    [SerializeField] RectTransform mapViewport;
    [SerializeField] GameObject mapParent;
    [SerializeField] GameObject mapCellPrefab;
    [SerializeField] GameObject deadEndPrefab;
    [SerializeField] GameObject mapFeaturePrefab;
    [SerializeField] GameObject playerDotPrefab;
    [SerializeField] Image followPlayerImg;

    [SerializeField] RectTransform mapAnchor;
    [SerializeField] RectTransform featureOverlay;
    [SerializeField] Transform chunkParent;
    [SerializeField] Transform itemParent;
    [SerializeField] Transform furnParent;
    [SerializeField] Transform extraParent;

    [SerializeField] GameObject mapChunkPrefab;

    [Header("Visual")]
    [SerializeField] DirectionalSprite[] deadEndSprites;
    [SerializeField] Sprite itemSprite;
    [SerializeField] Color itemColor = Color.softYellow;
    [SerializeField] Sprite furnSprite;
    [SerializeField] Color furnColor = Color.rosyBrown;
    [SerializeField] Sprite beaconSprite;
    [SerializeField] Color beaconColor = Color.cyan;
    [SerializeField] Sprite entitySprite;
    [SerializeField] Color entityColor = Color.red;

    [Header("Config")]
    [SerializeField] float cellSize = 200;
    [SerializeField] int chunkSize = 8;
    [SerializeField] Color mapColor = new Color(0, 150, 0);

    public bool IsFollowingPlayer => followPlayer;
    public Vector2 MapAnchorPosition => mapAnchor.anchoredPosition;
    public bool IsLocalPlayerController =>
        lobbyScreen.playerOnLMS == GameManager.Instance.playMod.LocalPlayer.Index;

    bool followPlayer = true;
    bool displayFeatures = true;

    [SyncVar(hook = nameof(OnFollowPlayerChanged))] bool _syncFollowPlayer = true;
    [SyncVar(hook = nameof(OnDisplayFeaturesChanged))] bool _syncDisplayFeatures = true;
    [SyncVar(hook = nameof(OnFollowTargetChanged))] int _syncFollowTargetIdx = 0;
    [SyncVar(hook = nameof(OnMapAnchorChanged))] Vector2 _syncMapAnchor;
    [SyncVar(hook = nameof(OnCurrentLayerChanged))] int _syncCurrentLayer = 0;

    readonly Dictionary<Vector2Int, MapChunk> chunks = new();
    readonly Dictionary<int, List<MapChunk>> layerChunks = new();
    readonly Dictionary<Vector3Int, GameObject> cellObjects = new();

    readonly Dictionary<int, List<GameObject>> featureLayers = new();
    readonly Dictionary<Transform, RectTransform> trackedIcons = new();
    readonly Dictionary<Transform, int> trackedIconLayers = new();
    readonly HashSet<GameObject> trackedIconObjects = new();
    readonly Dictionary<PlayerData, RectTransform> playerIcons = new();
    readonly Dictionary<uint, RectTransform> entityIcons = new();

    IMapFollowTarget followTarget;
    readonly List<IMapFollowTarget> followTargets = new();

    int currentLayer = 0;
    Vector2 _lastAnchorPos = Vector2.positiveInfinity;
    Vector3Int _mapMin;
    Coroutine _w84PlayerCor;

    #region Unity / Lifecycle

    private void Start()
    {
        DungeonGenerator.Instance.OnDungeonGenerated.AddListener(OnDungeonOpens);
        DungeonGenerator.Instance.OnDungeonClear.AddListener(OnDungeonCloses);
    }

    private void OnDestroy()
    {
        DungeonGenerator.Instance.OnDungeonGenerated.RemoveListener(OnDungeonOpens);
        DungeonGenerator.Instance.OnDungeonClear.RemoveListener(OnDungeonCloses);
    }

    public void OnDungeonOpens()
    {
        GameTick.OnTick += OnTick;

        var input = GameManager.Instance.playMod.LocalPlayer.Player_Input;
        input.actions["mapnext"].started += SetNextLayer;
        input.actions["mapprevious"].started += SetPreviousLayer;
        input.actions["mapfollow"].started += ToggleFollowPlayer;
        input.actions["mapnextplayer"].started += CycleFollowTargetNext;
        input.actions["mapprevplayer"].started += CycleFollowTargetPrevious;
        input.actions["mapfeatures"].started += ToggleFeatures;

        followTarget = followTargets.Find(t => t.IsAvailable);
        _w84PlayerCor = StartCoroutine(WaitForFirstPlayer());
        StartCoroutine(GenerateMapDelayed());
    }

    public void OnDungeonCloses()
    {
        GameTick.OnTick -= OnTick;

        var input = GameManager.Instance.playMod.LocalPlayer.Player_Input;
        input.actions["mapnext"].started -= SetNextLayer;
        input.actions["mapprevious"].started -= SetPreviousLayer;
        input.actions["mapfollow"].started -= ToggleFollowPlayer;
        input.actions["mapnextplayer"].started -= CycleFollowTargetNext;
        input.actions["mapprevplayer"].started -= CycleFollowTargetPrevious;
        input.actions["mapfeatures"].started -= ToggleFeatures;

        if (_w84PlayerCor != null) { StopCoroutine(_w84PlayerCor); _w84PlayerCor = null; }
        ClearMap();
    }

    private void Update() => UpdateMap();

    #endregion

    #region Update

    private void OnTick()
    {
        if (!mapParent.activeInHierarchy) return;
        if (!GameManager.Instance.dngMod.dungeonOpen) return;

        var gen = DungeonGenerator.Instance;
        if (gen == null) return;

        UpdateVisibleChunks();
        UpdateDynamicIcons(gen);
    }

    private void UpdateMap()
    {
        if (!mapParent.activeInHierarchy) return;
        if (!GameManager.Instance.dngMod.dungeonOpen) return;

        var gen = DungeonGenerator.Instance;
        if (gen == null) return;

        if (followTarget == null || !followTarget.IsAvailable)
            CycleFollowTargetNext();

        Vector3 focusPos = followTarget != null
            ? followTarget.FollowTransform.position
            : GameManager.Instance.dngMod.HomewardBeacon.transform.position;

        int focusLayer = Mathf.RoundToInt(focusPos.y / gen.CellSize);
        if (focusLayer != currentLayer && followPlayer)
            SetRenderLayer(focusLayer);

        float uiX = (focusPos.x / gen.CellSize - _mapMin.x) * cellSize;
        float uiY = (focusPos.z / gen.CellSize - _mapMin.z) * cellSize;

        if (followPlayer)
            mapAnchor.anchoredPosition = -new Vector2(uiX, uiY);
    }

    private void UpdateVisibleChunks()
    {
        if (mapAnchor.anchoredPosition == _lastAnchorPos) return;
        _lastAnchorPos = mapAnchor.anchoredPosition;

        if (!layerChunks.TryGetValue(currentLayer, out var currentChunks)) return;

        Vector3[] corners = new Vector3[4];
        mapViewport.GetWorldCorners(corners);

        Vector2 min = chunkParent.InverseTransformPoint(corners[0]);
        Vector2 max = chunkParent.InverseTransformPoint(corners[2]);

        float chunkWorldSize = chunkSize * cellSize;
        foreach (var chunk in currentChunks)
        {
            Vector2 pos = chunk.rectTransform.anchoredPosition;
            bool visible =
                pos.x + chunkWorldSize > min.x && pos.x < max.x &&
                pos.y + chunkWorldSize > min.y && pos.y < max.y;
            chunk.SetVisible(visible);
        }
    }

    private void UpdateDynamicIcons(DungeonGenerator gen)
    {
        foreach (var kvp in playerIcons)
        {
            if (kvp.Key == null || kvp.Value == null) continue;

            Vector3 pos = kvp.Key.transform.position;
            kvp.Value.anchoredPosition = WorldToFeaturePos(pos.x, pos.z, gen);

            int layer = Mathf.RoundToInt(pos.y / gen.CellSize);
            kvp.Value.gameObject.SetActive(layer == currentLayer);
        }

        if (!displayFeatures) return;

        foreach (var kvp in trackedIcons)
        {
            if (kvp.Key == null)
            {
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
                continue;
            }
            if (kvp.Value == null) continue;

            Vector3 pos = kvp.Key.position;
            kvp.Value.anchoredPosition = WorldToFeaturePos(pos.x, pos.z, gen);

            int newLayer = Mathf.RoundToInt(pos.y / gen.CellSize);
            int oldLayer = trackedIconLayers[kvp.Key];
            if (oldLayer == newLayer) continue;

            featureLayers[oldLayer].Remove(kvp.Value.gameObject);
            if (!featureLayers.ContainsKey(newLayer))
                featureLayers[newLayer] = new();
            featureLayers[newLayer].Add(kvp.Value.gameObject);
            kvp.Value.gameObject.SetActive(newLayer == currentLayer);
            trackedIconLayers[kvp.Key] = newLayer;
        }
    }

    private Vector2 WorldToFeaturePos(float worldX, float worldZ, DungeonGenerator gen) =>
        new((worldX / gen.CellSize - _mapMin.x) * cellSize,
            (worldZ / gen.CellSize - _mapMin.z) * cellSize);

    #endregion

    #region Input

    private void SetNextLayer(InputAction.CallbackContext ctx) => SetNextLayer();
    private void SetPreviousLayer(InputAction.CallbackContext ctx) => SetPreviousLayer();
    private void ToggleFollowPlayer(InputAction.CallbackContext ctx) => ToggleFollowPlayer();
    public void CycleFollowTargetNext(InputAction.CallbackContext ctx) => CycleFollowTarget(1);
    public void CycleFollowTargetPrevious(InputAction.CallbackContext ctx) => CycleFollowTarget(-1);
    public void ToggleFeatures(InputAction.CallbackContext ctx) => ToggleFeatures();

    #endregion

    #region Management

    [Command(requiresAuthority = false)] void CmdSetFollowPlayer(bool value) => _syncFollowPlayer = value;
    [Command(requiresAuthority = false)] void CmdSetDisplayFeatures(bool value) => _syncDisplayFeatures = value;
    [Command(requiresAuthority = false)] void CmdSetFollowTarget(int idx) => _syncFollowTargetIdx = idx;
    [Command(requiresAuthority = false)] public void CmdSetMapAnchor(Vector2 p) => _syncMapAnchor = p;
    [Command(requiresAuthority = false)] void CmdSetCurrentLayer(int layer) => _syncCurrentLayer = layer;

    [Command(requiresAuthority = false)]
    public void CmdSnapshotMapAnchor(int senderIndex, Vector2 position)
    {
        if (lobbyScreen.playerOnLMS != senderIndex) return;
        _syncMapAnchor = position;
    }

    void OnFollowPlayerChanged(bool o, bool n)
    {
        if (isOwned) return;
        followPlayer = n;
        UpdateFollowPlayerVisual();
    }

    void OnDisplayFeaturesChanged(bool o, bool n)
    {
        if (isOwned) return;
        displayFeatures = n;
        ApplyFeatureVisibility();
    }

    void OnFollowTargetChanged(int o, int n)
    {
        if (isOwned) return;
        followTarget = (n < 0 || n >= followTargets.Count) ? null : followTargets[n];
    }

    void OnMapAnchorChanged(Vector2 o, Vector2 n)
    {
        if (isOwned) return;
        mapAnchor.anchoredPosition = n;
    }

    void OnCurrentLayerChanged(int o, int n)
    {
        if (isOwned) return;
        SetRenderLayer(n);
    }

    public void SetNextLayer()
    {
        if (followPlayer) ToggleFollowPlayer();
        if (layerChunks.Keys.Count == 0) return;
        if (currentLayer >= layerChunks.Keys.Max()) return;
        SetRenderLayer(currentLayer + 1);
    }

    public void SetPreviousLayer()
    {
        if (followPlayer) ToggleFollowPlayer();
        if (layerChunks.Keys.Count == 0) return;
        if (currentLayer <= layerChunks.Keys.Min()) return;
        SetRenderLayer(currentLayer - 1);
    }

    public void ToggleFollowPlayer()
    {
        followPlayer = !followPlayer;
        UpdateFollowPlayerVisual();
        if (isClient) CmdSetFollowPlayer(followPlayer);
    }

    private void UpdateFollowPlayerVisual()
    {
        if (followPlayerImg == null) return;
        Color target;
        ColorUtility.TryParseHtmlString(followPlayer ? "#DC9632" : "#4D4D4D", out target);
        followPlayerImg.color = target;
    }

    public void CycleFollowTargetNext() => CycleFollowTarget(1);
    public void CycleFollowTargetPrevious() => CycleFollowTarget(-1);

    private void CycleFollowTarget(int direction)
    {
        if (!followPlayer) { ToggleFollowPlayer(); return; }
        if (followTargets.All(t => !t.IsAvailable)) { followTarget = null; return; }

        int currentIdx = followTargets.IndexOf(followTarget);
        int checkedCount = 0;
        int nextIdx = currentIdx;

        while (checkedCount < followTargets.Count)
        {
            nextIdx = (nextIdx + direction + followTargets.Count) % followTargets.Count;
            if (followTargets[nextIdx].IsAvailable) break;
            checkedCount++;
        }

        followTarget = followTargets[nextIdx];
        if (isClient) CmdSetFollowTarget(nextIdx);
    }

    public void ToggleFeatures()
    {
        displayFeatures = !displayFeatures;
        ApplyFeatureVisibility();
        if (isClient) CmdSetDisplayFeatures(displayFeatures);
    }

    private void ApplyFeatureVisibility()
    {
        foreach (var kv in trackedIcons)
        {
            if (!trackedIconLayers.ContainsKey(kv.Key)) continue;
            if (trackedIconLayers[kv.Key] != currentLayer) continue;
            kv.Value.gameObject.SetActive(displayFeatures);
        }
    }

    private void SetRenderLayer(int layer)
    {
        if (layerChunks.TryGetValue(currentLayer, out var oldChunks))
            foreach (var chunk in oldChunks)
                chunk.SetVisible(false);

        if (featureLayers.TryGetValue(currentLayer, out var oldFeatures))
            foreach (var go in oldFeatures)
                if (go) go.SetActive(false);

        currentLayer = layer;

        if (featureLayers.TryGetValue(layer, out var newFeatures))
            foreach (var go in newFeatures)
                if (go) go.SetActive(displayFeatures);

        if (isClient) CmdSetCurrentLayer(layer);

        _lastAnchorPos = Vector2.positiveInfinity;
        UpdateVisibleChunks();
    }

    private void OnEntityNetIdsChanged(SyncList<uint>.Operation op, int index, uint oldNetId, uint newNetId)
    {
        switch (op)
        {
            case SyncList<uint>.Operation.OP_ADD:
                StartCoroutine(SpawnEntityIconDelayed(newNetId));
                break;
            case SyncList<uint>.Operation.OP_REMOVEAT:
                RemoveEntityIcon(oldNetId);
                break;
            case SyncList<uint>.Operation.OP_CLEAR:
                foreach (var netId in new List<uint>(entityIcons.Keys))
                    RemoveEntityIcon(netId);
                break;
        }
    }

    #endregion

    #region Helpers

    private Sprite GetDeadEnd(RoomDataSO.RoomPort port)
    {
        //if (port.deadEndOverride != null) return port.deadEndOverride;

        foreach (var des in deadEndSprites)
            if (des.type == port.type && des.direction == port.face)
                return des.sprite;
        return null;
    }

    private Vector3Int ComputeMapMin(IReadOnlyList<DungeonGenerator.PlacedRoom> rooms)
    {
        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
        foreach (var pr in rooms)
            foreach (var fp in pr.data.RoomFootprint)
            {
                var w = pr.anchor + fp.Footprint;
                if (w.x < minX) minX = w.x;
                if (w.y < minY) minY = w.y;
                if (w.z < minZ) minZ = w.z;
            }
        return new Vector3Int(minX, minY, minZ);
    }

    private Vector2Int GetChunkCoord(int x, int z) =>
        new(Mathf.FloorToInt((float)x / chunkSize),
            Mathf.FloorToInt((float)z / chunkSize));

    private MapChunk GetOrCreateChunk(int x, int y, int z)
    {
        Vector2Int coord = GetChunkCoord(x - _mapMin.x, z - _mapMin.z);
        Vector2Int layeredCoord = new(coord.x + y * 10000, coord.y);

        if (chunks.TryGetValue(layeredCoord, out var existing))
            return existing;

        GameObject go = Instantiate(mapChunkPrefab, chunkParent);
        MapChunk chunk = go.GetComponent<MapChunk>();
        chunk.chunkCoord = coord;
        chunk.rectTransform.anchoredPosition = new Vector2(
            coord.x * chunkSize * cellSize,
            coord.y * chunkSize * cellSize);
        chunk.SetVisible(false);

        chunks[layeredCoord] = chunk;

        if (!layerChunks.ContainsKey(y))
            layerChunks[y] = new();
        layerChunks[y].Add(chunk);

        return chunk;
    }

    private int GetLayerFromNetId(NetworkIdentity ni) =>
        Mathf.RoundToInt(ni.transform.position.y / DungeonGenerator.Instance.CellSize);

    private IEnumerator WaitForFirstPlayer()
    {
        yield return new WaitUntil(() =>
            GameManager.Instance.playMod.Players.Any(p => !p._PlayerInOffice));

        var beacon = GameManager.Instance.dngMod.HomewardBeacon;
        if (followTarget == (IMapFollowTarget)beacon)
            followTarget = GameManager.Instance.playMod.Players.First(p => !p._PlayerInOffice);

        _w84PlayerCor = null;
    }

    private IEnumerator GenerateMapDelayed()
    {
        var gen = DungeonGenerator.Instance;
        //yield return new WaitUntil(() =>
        //    gen.RoomItemNetIds.Count > 0 || gen.RoomFurnitureNetIds.Count > 0);
        yield return null;
        GenerateMap();
    }

    private void SpawnContinuousCell(Vector3Int worldCell, Sprite sprite, Color color)
    {
        if (sprite == null) return;
        if (cellObjects.TryGetValue(worldCell, out var parent))
            SpawnDeadEnd(parent, sprite, color);
    }

    #endregion

    #region Generation

    private void GenerateMap()
    {
        var gen = DungeonGenerator.Instance;
        _mapMin = ComputeMapMin(gen.PlacedRooms);

        foreach (var sr in gen.SpawnedRooms)
        {
            var pr = sr.Value.PlacedRoom;

            foreach (var fp in pr.data.RoomFootprint)
            {
                Vector3Int worldCell = pr.anchor + fp.Footprint;
                var go = SpawnCell(worldCell.x, worldCell.y, worldCell.z, fp.MapSprite, pr.data.roomColor);
                if (go != null) cellObjects[worldCell] = go;
            }

            foreach (var port in sr.Value.closedPorts)
            {
                var portData = pr.data.Ports[port];
                Sprite defaultSprite = GetDeadEnd(portData);

                Vector3Int portWorld = pr.anchor + portData.localCell;

                if (portData.type == RoomDataSO.PortType.Doorway)
                {
                    Sprite sprite = portData.deadEndOverride != null ? portData.deadEndOverride : defaultSprite;
                    if (sprite == null) continue;

                    if (cellObjects.TryGetValue(portWorld, out var parent))
                        SpawnDeadEnd(parent, sprite, pr.data.roomColor);
                }
                else
                {
                    if (defaultSprite == null) continue;

                    Vector3Int offset = DirectionUtils.RightOf(portData.face);
                    Vector3Int center = portWorld;
                    Vector3Int right = portWorld + offset;
                    Vector3Int left = portWorld - offset;

                    bool hasOverride = portData.deadEndOverride != null;
                    Vector3Int deoWorld = hasOverride ? pr.anchor + portData.deoCell : Vector3Int.zero;

                    Sprite sprt = hasOverride && deoWorld == center ? portData.deadEndOverride : defaultSprite;
                    SpawnContinuousCell(center, sprt, pr.data.roomColor);

                    sprt = hasOverride && deoWorld == right ? portData.deadEndOverride : defaultSprite;
                    SpawnContinuousCell(right, sprt, pr.data.roomColor);

                    sprt = hasOverride && deoWorld == left ? portData.deadEndOverride : defaultSprite;
                    SpawnContinuousCell(left, sprt, pr.data.roomColor);
                }
            }
        }

        //foreach (var entry in gen.RoomItemNetIds)
        //{
        //    if (!NetworkClient.spawned.TryGetValue(entry, out var ni)) continue;
        //    SpawnDynamicIcon(ni.transform, itemSprite, itemColor, itemParent, GetLayerFromNetId(ni));
        //}

        //foreach (var entry in gen.RoomFurnitureNetIds)
        //{
        //    if (!NetworkClient.spawned.TryGetValue(entry, out var ni)) continue;
        //    SpawnDynamicIcon(ni.transform, furnSprite, furnColor, furnParent, GetLayerFromNetId(ni));
        //}

        followTargets.Clear();

        var beacon = GameManager.Instance.dngMod.HomewardBeacon;
        if (beacon != null)
        {
            int beaconLayer = Mathf.RoundToInt(beacon.transform.position.y / gen.CellSize);
            SpawnDynamicIcon(beacon.transform, beaconSprite, beaconColor, extraParent, beaconLayer, false);
            followTargets.Add(beacon);
        }

        foreach (var player in GameManager.Instance.playMod.Players)
        {
            SpawnPlayerIcon(player);
            followTargets.Add(player);
        }

        followTarget = followTargets.Find(t => t.IsAvailable);

        int startLayer = gen.StartRoomPos.y / gen.CellSize;
        SetRenderLayer(startLayer);

        EntitySpawnerManager.Instance.EntityNetIds.Callback += OnEntityNetIdsChanged;
        foreach (var netId in EntitySpawnerManager.Instance.EntityNetIds)
            SpawnEntityIcon(netId);
    }

    private GameObject SpawnCell(int x, int y, int z, Sprite sprite, Color color)
    {
        if (sprite == null) return null;

        MapChunk chunk = GetOrCreateChunk(x, y, z);
        GameObject go = Instantiate(mapCellPrefab, chunk.rectTransform);
        RectTransform rt = go.GetComponent<RectTransform>();

        Vector2Int coord = GetChunkCoord(x - _mapMin.x, z - _mapMin.z);
        rt.anchoredPosition = new Vector2(
            (x - _mapMin.x - coord.x * chunkSize) * cellSize,
            (z - _mapMin.z - coord.y * chunkSize) * cellSize);
        rt.sizeDelta = new Vector2(cellSize, cellSize);
        rt.localRotation = Quaternion.identity;

        if (go.TryGetComponent(out Image img))
        {
            img.sprite = sprite;
            img.color = color;

            if (Mathf.Approximately(color.r, Color.white.r) &&
                Mathf.Approximately(color.g, Color.white.g) &&
                Mathf.Approximately(color.b, Color.white.b))
                img.color = mapColor;

            return go;
        }

        Destroy(go);
        return null;
    }

    private void SpawnDeadEnd(GameObject parent, Sprite sprite, Color color)
    {
        GameObject go = Instantiate(deadEndPrefab, parent.transform);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(cellSize, cellSize);
        rt.localRotation = Quaternion.identity;

        if (go.TryGetComponent(out Image img))
        {
            img.sprite = sprite;
            img.color = color;

            if (Mathf.Approximately(color.r, Color.white.r) &&
                Mathf.Approximately(color.g, Color.white.g) &&
                Mathf.Approximately(color.b, Color.white.b))
                img.color = mapColor;

            img.maskable = true;
        }
        else Destroy(go);

        go.SetActive(true);
    }

    private void SpawnDynamicIcon(Transform worldTransform, Sprite sprite, Color color,
        Transform parent, int layer, bool track = true)
    {
        if (sprite == null) return;

        var gen = DungeonGenerator.Instance;
        Vector3 worldPos = worldTransform.position;

        GameObject go = Instantiate(mapFeaturePrefab, parent);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = WorldToFeaturePos(worldPos.x, worldPos.z, gen);
        rt.sizeDelta = new Vector2(cellSize * 0.25f, cellSize * 0.25f);
        rt.localRotation = Quaternion.identity;

        if (go.TryGetComponent(out Image img))
        {
            img.sprite = sprite;
            img.color = color;

            if (!featureLayers.ContainsKey(layer))
                featureLayers[layer] = new();
            featureLayers[layer].Add(go);

            if (track)
            {
                trackedIcons[worldTransform] = rt;
                trackedIconLayers[worldTransform] = layer;
                trackedIconObjects.Add(go);
            }

            go.SetActive(false);
        }
        else Destroy(go);
    }

    private void SpawnPlayerIcon(PlayerData player)
    {
        var gen = DungeonGenerator.Instance;
        Vector3 worldPos = player.transform.position;

        GameObject go = Instantiate(playerDotPrefab, extraParent);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = WorldToFeaturePos(worldPos.x, worldPos.z, gen);
        rt.sizeDelta = new Vector2(cellSize * 0.375f, cellSize * 0.375f);
        rt.localRotation = Quaternion.identity;

        if (go.TryGetComponent(out Map_PlayerDot pDot))
        {
            pDot.SetPlayerIcon(player.GetAvatar());

            int layer = Mathf.RoundToInt(worldPos.y / gen.CellSize);
            if (!featureLayers.ContainsKey(layer))
                featureLayers[layer] = new();
            featureLayers[layer].Add(go);

            playerIcons[player] = rt;
            go.SetActive(false);
        }
        else Destroy(go);
    }

    private IEnumerator SpawnEntityIconDelayed(uint netId)
    {
        yield return new WaitUntil(() => NetworkClient.spawned.ContainsKey(netId));
        SpawnEntityIcon(netId);
    }

    private void SpawnEntityIcon(uint netId)
    {
        if (!NetworkClient.spawned.TryGetValue(netId, out var ni)) return;

        var gen = DungeonGenerator.Instance;
        Vector3 worldPos = ni.transform.position;
        int layer = Mathf.RoundToInt(worldPos.y / gen.CellSize);

        GameObject go = Instantiate(mapFeaturePrefab, extraParent);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = WorldToFeaturePos(worldPos.x, worldPos.z, gen);
        rt.sizeDelta = new Vector2(cellSize * 0.25f, cellSize * 0.25f);
        rt.localRotation = Quaternion.identity;

        if (go.TryGetComponent(out Image img))
        {
            img.sprite = entitySprite;
            img.color = entityColor;

            if (!featureLayers.ContainsKey(layer))
                featureLayers[layer] = new();
            featureLayers[layer].Add(go);

            trackedIcons[ni.transform] = rt;
            trackedIconLayers[ni.transform] = layer;
            trackedIconObjects.Add(go);

            entityIcons[netId] = rt;
            go.SetActive(layer == currentLayer && displayFeatures);
        }
        else Destroy(go);
    }

    private void RemoveEntityIcon(uint netId)
    {
        if (!entityIcons.TryGetValue(netId, out var rt)) return;

        foreach (var layer in featureLayers.Values)
            layer.Remove(rt.gameObject);

        Transform toRemove = null;
        foreach (var kvp in trackedIcons)
            if (kvp.Value == rt) { toRemove = kvp.Key; break; }

        if (toRemove != null)
        {
            trackedIcons.Remove(toRemove);
            trackedIconLayers.Remove(toRemove);
            trackedIconObjects.Remove(rt.gameObject);
        }

        entityIcons.Remove(netId);
        Destroy(rt.gameObject);
    }

    #endregion

    #region Cleanup

    private void ClearMap()
    {
        foreach (var chunk in chunks.Values)
            if (chunk) Destroy(chunk.gameObject);

        foreach (var layer in featureLayers.Values)
            foreach (var go in layer)
                if (go) Destroy(go);

        foreach (var icon in playerIcons.Values)
            if (icon) Destroy(icon.gameObject);

        if (EntitySpawnerManager.Instance != null)
            EntitySpawnerManager.Instance.EntityNetIds.Callback -= OnEntityNetIdsChanged;

        chunks.Clear();
        layerChunks.Clear();
        cellObjects.Clear();
        featureLayers.Clear();
        trackedIcons.Clear();
        trackedIconLayers.Clear();
        trackedIconObjects.Clear();
        playerIcons.Clear();
        entityIcons.Clear();
        followTargets.Clear();
        followTarget = null;
        currentLayer = 0;
    }

    #endregion
}
}
