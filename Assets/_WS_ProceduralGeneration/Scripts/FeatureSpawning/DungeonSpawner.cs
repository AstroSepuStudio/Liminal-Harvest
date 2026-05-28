using System;
using System.Collections.Generic;
using UnityEngine;

namespace WS_ProceduralGeneration
{
    public abstract class DungeonSpawner : MonoBehaviour, IDungeonSpawner
    {
        [SerializeField] SpawnChannel channel;
        [SerializeField] int spawnOrder = 0;

        readonly List<DungeonSpawnPoint> collectedPoints = new();
        protected IReadOnlyList<DungeonSpawnPoint> CollectedPoints => collectedPoints;

        public SpawnChannel Channel => channel;
        public virtual int SpawnOrder => spawnOrder;

        public virtual IReadOnlyList<DungeonSpawnPoint> DiscoveredPoints => Array.Empty<DungeonSpawnPoint>();

        public void Collect(IEnumerable<DungeonSpawnPoint> points)
        {
            collectedPoints.Clear();
            foreach (var p in points)
                if (p != null) collectedPoints.Add(p);
            OnCollected();
        }

        public virtual void Spawn(DungeonGenerator generator)
        {
            int spawned = 0;
            foreach (var point in collectedPoints)
                for (int t = 0; t < point.tries; t++)
                {
                    if (!EvaluateChance(point, generator)) continue;
                    SpawnOne(point, generator);
                    spawned++;
                }
            OnSpawnComplete(spawned);
        }

        protected GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            return Instantiate(prefab, position, rotation, parent);
        }

        public virtual void Clear()
        {
            collectedPoints.Clear();
            OnClear();
        }

        protected virtual void OnCollected() { }
        protected abstract void SpawnOne(DungeonSpawnPoint point, DungeonGenerator generator);
        protected virtual void OnSpawnComplete(int spawnedCount) { }
        protected virtual void OnClear() { }

        protected virtual bool EvaluateChance(DungeonSpawnPoint point, DungeonGenerator generator)
        {
            float roll = (float)(generator.RNG.NextDouble() * 100f);

            float effective = point.chance * generator.GetDificultyMultiplier(point.transform.position);
            return roll <= effective;
        }

        protected static Vector3 ResolvePosition(DungeonSpawnPoint point, System.Random rng)
        {
            Vector3 pos = RandomisedPosition(point, rng);
            if (!point.snapToSurface || point.snapDirections == null || point.snapDirections.Length == 0)
                return pos;
            return TrySnap(pos, point, out Vector3 snapped) ? snapped : pos;
        }

        protected static Quaternion ResolveRotation(DungeonSpawnPoint point, System.Random rng)
        {
            if (!point.snapToSurface || !point.alignToNormal ||
                point.snapDirections == null || point.snapDirections.Length == 0)
                return RandomisedRotation(point, rng);

            Vector3 origin = point.transform.position;
            Vector3 blendedNormal = Vector3.zero;

            foreach (var dir in point.snapDirections)
            {
                Vector3 rayDir = (Vector3)DirectionUtils.DirectionVector(dir);
                if (!Physics.Raycast(origin, rayDir, out RaycastHit hit,
                        point.snapMaxDistance, point.snapLayers)) continue;
                if (point.useSpecificNormalDirection && dir != point.normalAlignDirection) continue;
                blendedNormal += hit.normal;
            }

            if (blendedNormal == Vector3.zero) return RandomisedRotation(point, rng);

            blendedNormal.Normalize();
            Vector3 prefabUp = (Vector3)DirectionUtils.DirectionVector(point.prefabUpAxis);
            Quaternion normalRot = Quaternion.FromToRotation(prefabUp, blendedNormal);
            float ySpin = (float)(rng.NextDouble() * 2f - 1f) * point.maxRotation;
            return normalRot * Quaternion.Euler(0f, point.transform.rotation.eulerAngles.y + ySpin, 0f);
        }

        static bool TrySnap(Vector3 origin, DungeonSpawnPoint point, out Vector3 result)
        {
            result = origin;
            bool anyHit = false;

            foreach (var dir in point.snapDirections)
            {
                Vector3 rayDir = (Vector3)DirectionUtils.DirectionVector(dir);
                if (!Physics.Raycast(origin, rayDir, out RaycastHit hit,
                        point.snapMaxDistance, point.snapLayers)) continue;

                Vector3 displacement = hit.point - origin + hit.normal * point.snapSurfaceOffset;
                Vector3 axisContribution = Vector3.Scale(displacement, Abs(rayDir));
                result += axisContribution;

                anyHit = true;
            }

            return anyHit;
        }

        static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static Vector3 RandomisedPosition(DungeonSpawnPoint point, System.Random rng)
        {
            Vector3 o = point.maxOffset;
            return point.transform.position + new Vector3(
                (float)(rng.NextDouble() * 2f - 1f) * o.x,
                (float)(rng.NextDouble() * 2f - 1f) * o.y,
                (float)(rng.NextDouble() * 2f - 1f) * o.z);
        }

        static Quaternion RandomisedRotation(DungeonSpawnPoint point, System.Random rng) =>
            point.transform.rotation * Quaternion.Euler(
                0f, (float)(rng.NextDouble() * 2f - 1f) * point.maxRotation, 0f);

        protected virtual void DestroyChildren(Transform parent)
        {
            if (parent == null) return;

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                Destroy(child);
            }
        }
    }
}
