using System.Collections.Generic;
using UnityEngine;
using WS_ProceduralGeneration;

public class AIModule_Home : AIModule
{
    [SerializeField] float homeDistanceFraction = 0.5f;
    [SerializeField] float homeDistanceTolerance = 0.2f;

    private RoomData HomeRoom;

    readonly HashSet<ItemBase> homeItems = new();

    RoomData overrideRoom;
    bool hasOverride;

    public override void OnModuleInit(AIBrain brain)
    {
        if (!brain.isServer) return;
        AssignHomeRoom();
    }

    public RoomData GetEffectiveHome() => hasOverride ? overrideRoom : HomeRoom;

    public void SetOverride(RoomData room)
    {
        overrideRoom = room;
        hasOverride = room != null;
    }

    public void ClearOverride()
    {
        overrideRoom = null;
        hasOverride = false;
    }

    public bool HasOverride => hasOverride;

    public bool IsItemAtHome(ItemBase item)
    {
        RoomData home = GetEffectiveHome();
        if (home == null || item == null) return false;
        float threshold = DungeonGenerator.Instance.CellSize;
        return Vector3.Distance(item.transform.position, home.transform.position) <= threshold;
    }

    void AssignHomeRoom()
    {
        var gen = DungeonGenerator.Instance;
        if (gen == null || gen.SpawnedRooms == null) return;

        float maxDist = gen.MaxDistance;
        float prefDist = maxDist * homeDistanceFraction;
        float minBand = prefDist - maxDist * homeDistanceTolerance;
        float maxBand = prefDist + maxDist * homeDistanceTolerance;

        Vector3 startPos = gen.StartRoomPos;

        List<RoomData> candidates = new();
        List<RoomData> fallback = new();

        foreach (var kvp in gen.SpawnedRooms)
        {
            RoomData rd = kvp.Value;
            if (rd == null) continue;
            if (rd.Data.BiomeData == Biome.Hallway || !rd.Data.ValidVortexHome) continue;

            float dist = Vector3.Distance(startPos, rd.transform.position);
            if (dist >= minBand && dist <= maxBand)
                candidates.Add(rd);
            else
                fallback.Add(rd);
        }

        HomeRoom = candidates.Count > 0
            ? candidates[Random.Range(0, candidates.Count)]
            : fallback.Count > 0 ? fallback[Random.Range(0, fallback.Count)] : null;
    }

    public void ScanHomeItems()
    {
        homeItems.Clear();
        RoomData home = GetEffectiveHome();
        if (home == null) return;

        Collider[] hits = Physics.OverlapSphere(home.transform.position,
            DungeonGenerator.Instance.CellSize);
        foreach (var hit in hits)
        {
            if (!hit.CompareTag("Item")) continue;
            ItemBase item = hit.GetComponent<ItemBase>();
            if (item != null && !item.HasOwner)
                homeItems.Add(item);
        }
    }

    public void ScanAndCheckStolenItems(AIBrain brain)
    {
        if (homeItems.Count == 0) return;

        homeItems.RemoveWhere(item => item == null);

        foreach (var item in homeItems)
        {
            if (!item.HasOwner) continue;
            homeItems.Remove(item);
            brain.GetModule<AIModule_Patience>()?.DrainOnItemStolen();
            break;
        }
    }
}
