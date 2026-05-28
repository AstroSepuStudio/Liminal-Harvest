using System.Collections.Generic;
using Mirror;
using UnityEngine;
using WS_ProceduralGeneration;

namespace WS_ProceduralGeneration
{
    public class FurnitureSpawner : NetworkDungeonSpawner
    {
        [Header("Furniture Spawner")]
        [SerializeField] Transform furnitureParent;

        public List<FurnitureEntity> SpawnedFurniture { get; } = new();
        public List<uint> FurnitureNetIds { get; } = new();

        readonly List<DungeonSpawnPoint> discoveredLootPoints = new();
        public override IReadOnlyList<DungeonSpawnPoint> DiscoveredPoints => discoveredLootPoints;

        protected override void OnCollected()
        {
            SpawnedFurniture.Clear();
            FurnitureNetIds.Clear();
            discoveredLootPoints.Clear();
        }

        protected override void SpawnOne(DungeonSpawnPoint point, DungeonGenerator generator)
        {
            if (!IsServer) return;

            FurnitureDataSO data = generator.Theme.GetWeigthedFurniture(point.transform.position, generator.RNG);
            if (data == null) return;

            Vector3 pos = ResolvePosition(point, generator.RNG);
            Quaternion rot = ResolveRotation(point, generator.RNG);

            GameObject go = NetworkSpawn(data.Prefab, pos, rot, furnitureParent);
            if (go == null) return;

            if (!go.TryGetComponent(out FurnitureEntity furnEnt)) return;
            if (!go.TryGetComponent<NetworkIdentity>(out var ni)) return;

            if (furnEnt.lootPositions != null)
            {
                foreach (var lp in furnEnt.lootPositions)
                    if (lp != null) discoveredLootPoints.Add(lp);
            }

            SpawnedFurniture.Add(furnEnt);
            FurnitureNetIds.Add(ni.netId);
        }

        protected override void OnSpawnComplete(int count) =>
            Debug.Log($"[FurnitureSpawner] Spawned {count} furniture piece(s).");

        protected override void OnClear()
        {
            DestroyChildren(furnitureParent, true, IsServer);

            SpawnedFurniture.Clear();
            FurnitureNetIds.Clear();
            discoveredLootPoints.Clear();
        }
    }
}
