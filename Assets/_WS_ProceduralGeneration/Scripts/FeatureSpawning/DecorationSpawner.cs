using UnityEngine;

namespace WS_ProceduralGeneration
{
    public class DecorationSpawner : NetworkDungeonSpawner
    {
        [Header("Decoration Spawner")]
        [SerializeField] Transform decoParent;
        [SerializeField] float baseChance = 1f;
        float accumulatedChance;

        protected override void OnCollected()
        {
            accumulatedChance = baseChance;
        }

        public override void Spawn(DungeonGenerator generator)
        {
            accumulatedChance = baseChance;
            int spawned = 0;

            foreach (var point in CollectedPoints)
            {
                if (!EvaluateAccumulating(point, generator))
                {
                    accumulatedChance += baseChance;
                    continue;
                }

                accumulatedChance = baseChance;
                SpawnOne(point, generator);
                spawned++;
            }

            OnSpawnComplete(spawned);
        }

        bool EvaluateAccumulating(DungeonSpawnPoint point, DungeonGenerator generator)
        {
            float roll = (float)(generator.RNG.NextDouble() * 100f);
            float effective = accumulatedChance * generator.GetDificultyMultiplier(point.transform.position);
            return roll <= effective;
        }

        protected override void SpawnOne(DungeonSpawnPoint point, DungeonGenerator generator)
        {
            if (!IsServer) return;

            if (point is not DecorationSpawnPoint docSpawn) return;

            DecorationDataSO deco = generator.Theme.GetWeightedDecoration(
                point.transform.position, docSpawn.maxSize, generator.RNG);
            if (deco == null) return;

            NetworkSpawn(deco.Prefab, ResolvePosition(point, generator.RNG), ResolveRotation(point, generator.RNG), decoParent);
        }

        protected override void OnClear()
        {
            DestroyChildren(decoParent, true, IsServer);
        }

        protected override void OnSpawnComplete(int count) =>
            Debug.Log($"[DecorationSpawner] Spawned {count} decoration(s).");
    }
}
