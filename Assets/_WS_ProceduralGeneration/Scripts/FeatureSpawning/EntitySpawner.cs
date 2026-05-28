using System.Collections.Generic;
using UnityEngine;

namespace WS_ProceduralGeneration
{
    public class EntitySpawner : NetworkDungeonSpawner
    {
        [Header("Entity Spawner")]
        [SerializeField] GameObject entitySpawnerPrefab;
        [SerializeField] Transform entitySpawnerParent;
        [SerializeField, Range(0f, 100f)] float baseChance = 1f;
        [SerializeField, Min(1)] int minimumSpawners = 3;

        readonly List<Transform> spawnedTransforms = new();
        public IReadOnlyList<Transform> SpawnedTransforms => spawnedTransforms;

        protected override void OnCollected()
        {
            spawnedTransforms.Clear();
        }

        public override void Spawn(DungeonGenerator generator)
        {
            if (!IsServer) return;
            if (CollectedPoints.Count == 0) return;

            float accumulatedChance = baseChance;

            foreach (var point in CollectedPoints)
            {
                float roll = (float)(generator.RNG.NextDouble() * 100f);
                float effective = accumulatedChance * generator.GetDificultyMultiplier(point.transform.position);

                if (roll > effective)
                {
                    accumulatedChance += baseChance;
                    continue;
                }

                accumulatedChance = baseChance;
                PlaceSpawner(ResolvePosition(point, generator.RNG), ResolveRotation(point, generator.RNG));
            }

            if (spawnedTransforms.Count < minimumSpawners)
            {
                int qty = Mathf.Min(minimumSpawners, CollectedPoints.Count);
                for (int i = 0; i < qty; i++)
                {
                    var pt = CollectedPoints[i];
                    bool alreadyPlaced = spawnedTransforms.Exists(t =>
                        Vector3.SqrMagnitude(t.position - pt.transform.position) < 0.01f);

                    if (!alreadyPlaced)
                        PlaceSpawner(ResolvePosition(pt, generator.RNG), ResolveRotation(pt, generator.RNG));

                    if (spawnedTransforms.Count >= minimumSpawners) break;
                }
            }

            OnSpawnComplete(spawnedTransforms.Count);
        }

        protected override void SpawnOne(DungeonSpawnPoint point, DungeonGenerator generator)
        {
            PlaceSpawner(ResolvePosition(point, generator.RNG), ResolveRotation(point, generator.RNG));
        }

        protected override void OnSpawnComplete(int count)
        {
            Debug.Log($"[EntitySpawner] Placed {count} entity spawner(s).");
            EntitySpawnerManager.Instance.SetSpawnerPositions(spawnedTransforms);
        }

        protected override void OnClear()
        {
            DestroyChildren(entitySpawnerParent, true, IsServer);

            spawnedTransforms.Clear();
        }

        void PlaceSpawner(Vector3 position, Quaternion rotation)
        {
            GameObject go = NetworkSpawn(entitySpawnerPrefab, position, rotation, entitySpawnerParent);
            if (go != null) spawnedTransforms.Add(go.transform);
        }
    }
}
