using System.Collections.Generic;
using Mirror;
using UnityEngine;
using WS_ProceduralGeneration;

namespace WS_ProceduralGeneration
{
    public class LootSpawner : NetworkDungeonSpawner
    {
        [Header("Loot Spawner")]
        [SerializeField] Transform itemsParent;

        public List<ItemBase> SpawnedItems { get; } = new();
        public List<uint> ItemNetIds { get; } = new();

        float totalItemsValue = 0f;

        readonly List<DungeonSpawnPoint> extraPoints = new();

        public void AddExtraPoints(IEnumerable<DungeonSpawnPoint> points)
        {
            foreach (var p in points)
                if (p != null) extraPoints.Add(p);
        }

        protected override void OnCollected()
        {
            SpawnedItems.Clear();
            ItemNetIds.Clear();
            extraPoints.Clear();
        }

        public override void Spawn(DungeonGenerator generator)
        {
            totalItemsValue = 0f;

            base.Spawn(generator);

            foreach (var point in extraPoints)
            {
                for (int t = 0; t < point.tries; t++)
                {
                    if (!EvaluateChance(point, generator)) continue;
                    SpawnOne(point, generator);
                }
            }

            float target = GameManager.Instance.ecoMod.targetQuota;
            if (totalItemsValue < target)
                SpawnUntilQuota(target, generator);

            float deficit = target - totalItemsValue;
            if (deficit <= 0f) return;

            float multiplier = (float)(generator.RNG.NextDouble() * 0.3f) + 1f;
            float adjustedTarget = target * multiplier;
            deficit = adjustedTarget - totalItemsValue;

            float totalWeight = 0f;
            foreach (var item in SpawnedItems)
                totalWeight += item.ItemValue;

            foreach (var item in SpawnedItems)
            {
                float weight = item.ItemValue / totalWeight;
                float add = deficit * weight;

                int increase = Mathf.RoundToInt(add);
                item.ItemValue += increase;

                totalItemsValue += increase;
            }
        }

        void SpawnUntilQuota(float target, DungeonGenerator generator)
        {
            var allPoints = new List<DungeonSpawnPoint>(CollectedPoints);
            allPoints.AddRange(extraPoints);

            if (allPoints.Count == 0) return;

            var prog = GameManager.Instance.progressionMod;
            int safetyLimit = 8 * prog.MaxMapSize;
            int overcapItem = 2 + generator.RNG.Next(prog.MaxMapSize);
            int spawnedCount = 0;

            while ((totalItemsValue < target || spawnedCount < overcapItem) && safetyLimit-- > 0)
            {
                int idx = generator.RNG.Next(allPoints.Count);
                var point = allPoints[idx];

                ItemSO item = generator.Theme.GetWeightedItem(
                    point.transform.position,
                    generator.RNG,
                    prog.CurrentMinLootTier,
                    prog.CurrentMaxLootTier);

                if (item == null) continue;
                if (item.itemPrefab == null)
                {
                    Debug.LogWarning($"[Loot] item prefab of {item.name} is null!");
                    continue;
                }

                float yaw = (float)(generator.RNG.NextDouble() * 360f);
                Quaternion rot = Quaternion.Euler(
                    (float)(generator.RNG.NextDouble() * 360f),
                    yaw,
                    (float)(generator.RNG.NextDouble() * 360f));

                Vector3 pos = ResolvePosition(point, generator.RNG);

                GameObject go = NetworkSpawn(item.itemPrefab, pos, rot, itemsParent);
                if (go == null) continue;

                if (!go.TryGetComponent<ItemBase>(out var itemBase)) continue;
                if (!go.TryGetComponent<NetworkIdentity>(out var ni)) continue;

                SpawnedItems.Add(itemBase);
                ItemNetIds.Add(ni.netId);

                totalItemsValue += itemBase.ItemValue;

                if (totalItemsValue > target)
                {
                    spawnedCount++;
                }
            }

            if (safetyLimit <= 0)
                Debug.LogWarning("[LootSpawner] SpawnUntilQuota hit safety limit � quota may not be fully met.");
        }

        protected override void SpawnOne(DungeonSpawnPoint point, DungeonGenerator generator)
        {
            if (!IsServer) return;

            var prog = GameManager.Instance.progressionMod;
            ItemSO item = generator.Theme.GetWeightedItem(point.transform.position, generator.RNG,
                prog.CurrentMinLootTier, prog.CurrentMaxLootTier);

            if (item == null) return;
            if (item.itemPrefab == null)
            {
                Debug.LogWarning($"[Loot] item prefab of {item.name} is null!");
                return;
            }

            Quaternion rot = Quaternion.Euler(
                (float)(generator.RNG.NextDouble() * 360f),
                (float)(generator.RNG.NextDouble() * 360f),
                (float)(generator.RNG.NextDouble() * 360f));

            Vector3 pos = ResolvePosition(point, generator.RNG);

            GameObject go = NetworkSpawn(item.itemPrefab, pos, rot, itemsParent);
            if (go == null) return;

            if (!go.TryGetComponent<ItemBase>(out var itemBase)) return;
            if (!go.TryGetComponent<NetworkIdentity>(out var ni)) return;

            SpawnedItems.Add(itemBase);
            ItemNetIds.Add(ni.netId);

            totalItemsValue += itemBase.ItemValue;
        }

        protected override void OnSpawnComplete(int count) =>
            Debug.Log($"[LootSpawner] Spawned {count} item(s).");

        protected override void OnClear()
        {
            SpawnedItems.Clear();
            ItemNetIds.Clear();
            extraPoints.Clear();
        }
    }
}
