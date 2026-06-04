using System.Collections.Generic;
using UnityEngine;

namespace WS_ProceduralGeneration
{
    public enum Biome { None, Default, Pillar, Dark, Hallway, Holes }

    [CreateAssetMenu(menuName = "DungeonGen/ThemeData")]
    public class ThemeDataSO : ScriptableObject
    {
        public enum SpawnableSize { Tiny, Small, Medium, Large, ExtraLarge, Colossal }
        public enum AxisGrowthPattern { None, Every, Odd, Even, EveryThree }
        public enum AxisAlignment { Center, Positive, Negative }

        [System.Serializable]
        public struct EntitySpawn
        {
            public EntityDataSO EntityData;
            public float SpawnWeight;
        }

        [System.Serializable]
        public struct GridGrowth
        {
            public AxisGrowthPattern x;
            public AxisGrowthPattern y;
            public AxisGrowthPattern z;
        }

        [System.Serializable]
        public struct GridAlignment
        {
            public AxisAlignment x;
            public AxisAlignment y;
            public AxisAlignment z;
        }

        public string levelName;

        [Header("Room Generation")]
        public RoomDataSO startingRoom;
        public RoomDataSO[] spawnableRooms;
        public RoomDataSO[] deadEndRooms;
        public GridGrowth gridGrowth = new()
        {
            x = AxisGrowthPattern.Odd,
            y = AxisGrowthPattern.EveryThree,
            z = AxisGrowthPattern.Even,
        };

        public GridAlignment startRoomAlignment = new()
        {
            x = AxisAlignment.Center,
            y = AxisAlignment.Center,
            z = AxisAlignment.Center,
        };

        public BiomeDataSO[] spawnableBiomes;

        [Header("Visuals")]
        public Material floorMaterial;
        public Material ceilingMaterial;
        public Material wallMaterial;

        [Header("Features Generation")]
        public ItemSO[] spawnableItems;
        public FurnitureDataSO[] spawnableFurniture;
        public EntitySpawn[] EntitySpawns;
        public DecorationDataSO[] spawnableDecoration;

        [Header("Ambient")]
        public Color AmbienceClr;
        public float AmbienceIntensity;
        public AudioSFX loopingMusic;
        public AudioSFX[] eerySFX;
        public AudioReverbPreset reverbPreset = AudioReverbPreset.Cave;

        public int MinItems = 3;
        public int MaxItems = 12;

        private WSDG_Tier.Tier RollTier(Vector3 position, System.Random rng, WSDG_Tier.Tier min, WSDG_Tier.Tier max)
        {
            float multiplier = DungeonGenerator.Instance.GetDificultyMultiplier(position);
            Dictionary<WSDG_Tier.Tier, float> modified = WSDG_Tier.GetWeightedTiers(multiplier);

            float totalWeight = 0f;
            foreach (var kvp in modified)
                if (kvp.Key >= min && kvp.Key <= max) totalWeight += kvp.Value;

            if (totalWeight <= 0f) return min;

            float roll = (float)(rng.NextDouble() * totalWeight);
            foreach (var kvp in modified)
            {
                if (kvp.Key < min || kvp.Key > max) continue;
                roll -= kvp.Value;
                if (roll <= 0f) return kvp.Key;
            }

            return min;
        }

        public FurnitureDataSO GetWeigthedFurniture(Vector3 position, System.Random rng,
            WSDG_Tier.Tier min = WSDG_Tier.Tier.Common, WSDG_Tier.Tier max = WSDG_Tier.Tier.Legendary)
        {
            WSDG_Tier.Tier selectedTier = RollTier(position, rng, min, max);

            List<FurnitureDataSO> candidates = new();
            foreach (var f in spawnableFurniture)
                if (f.Tier >= min && f.Tier <= max && f.Tier == selectedTier)
                    candidates.Add(f);

            if (candidates.Count == 0)
                foreach (var f in spawnableFurniture)
                    if (f.Tier >= min && f.Tier <= max)
                        candidates.Add(f);

            if (candidates.Count == 0)
                return spawnableFurniture[(int)(rng.NextDouble() * spawnableFurniture.Length)];

            return candidates[(int)(rng.NextDouble() * candidates.Count)];
        }

        public ItemSO GetWeightedItem(Vector3 position, System.Random rng,
            WSDG_Tier.Tier min = WSDG_Tier.Tier.Common, WSDG_Tier.Tier max = WSDG_Tier.Tier.Legendary)
        {
            WSDG_Tier.Tier selectedTier = RollTier(position, rng, min, max);

            List<ItemSO> candidates = new();
            foreach (var item in spawnableItems)
                if (item.Tier >= min && item.Tier <= max && item.Tier == selectedTier)
                    candidates.Add(item);

            if (candidates.Count == 0)
                foreach (var item in spawnableItems)
                    if (item.Tier >= min && item.Tier <= max)
                        candidates.Add(item);

            if (candidates.Count == 0)
                return spawnableItems[(int)(rng.NextDouble() * spawnableItems.Length)];

            return candidates[(int)(rng.NextDouble() * candidates.Count)];
        }

        public DecorationDataSO GetWeightedDecoration(Vector3 position, SpawnableSize maxSize, System.Random rng,
            WSDG_Tier.Tier min = WSDG_Tier.Tier.Common, WSDG_Tier.Tier max = WSDG_Tier.Tier.Legendary)
        {
            WSDG_Tier.Tier selectedTier = RollTier(position, rng, min, max);

            List<DecorationDataSO> candidates = new();
            foreach (var d in spawnableDecoration)
            {
                if ((int)d.Size > (int)maxSize) continue;
                if (d.Tier >= min && d.Tier <= max && d.Tier == selectedTier)
                    candidates.Add(d);
            }

            if (candidates.Count == 0)
                foreach (var d in spawnableDecoration)
                {
                    if ((int)d.Size > (int)maxSize) continue;
                    if (d.Tier >= min && d.Tier <= max)
                        candidates.Add(d);
                }

            if (candidates.Count == 0)
                foreach (var d in spawnableDecoration)
                    if ((int)d.Size <= (int)maxSize)
                        return d;

            if (candidates.Count == 0) return null;
            return candidates[(int)(rng.NextDouble() * candidates.Count)];
        }

        public EntitySpawn GetWeightedEntitySpawn(
        Vector3 position, System.Random rng,
        WSDG_Tier.Tier min = WSDG_Tier.Tier.Common, WSDG_Tier.Tier max = WSDG_Tier.Tier.Legendary)
        {
            List<EntitySpawn> pool = new();

            foreach (var entity in EntitySpawns)
            {
                var tier = entity.EntityData.EntityTier;

                if (tier < min || tier > max)
                    continue;

                float difficulty = DungeonGenerator.Instance.GetDificultyMultiplier(position);
                float adjustedWeight = entity.SpawnWeight * GetTierModifier(tier, difficulty);

                pool.Add(new EntitySpawn
                {
                    EntityData = entity.EntityData,
                    SpawnWeight = adjustedWeight
                });
            }

            if (pool.Count == 0)
                return EntitySpawns[rng.Next(EntitySpawns.Length)];

            return RollWeighted(pool, rng);
        }

        float GetTierModifier(WSDG_Tier.Tier tier, float multiplier)
        {
            return tier switch
            {
                WSDG_Tier.Tier.Common => 1f / multiplier,
                WSDG_Tier.Tier.Uncommon => 1f / Mathf.Sqrt(multiplier),
                WSDG_Tier.Tier.Rare => multiplier,
                WSDG_Tier.Tier.Epic => multiplier * multiplier,
                WSDG_Tier.Tier.Legendary => multiplier * multiplier * multiplier,
                _ => 1f,
            };
        }

        public BiomeDataSO GetStartingBiome()
        {
            foreach (var b in spawnableBiomes)
            {
                if (b.biome == startingRoom.BiomeData)
                    return b;
            }

            return null;
        }

        static EntitySpawn RollWeighted(List<EntitySpawn> pool, System.Random rng)
        {
            float total = 0f;
            foreach (var e in pool) total += e.SpawnWeight;

            float roll = (float)(rng.NextDouble() * total);
            foreach (var e in pool)
            {
                roll -= e.SpawnWeight;
                if (roll <= 0f) return e;
            }

            return pool[^1];
        }

        public static int EvaluateGrowthSteps(AxisGrowthPattern pattern, int sizeSteps)
        {
            int count = 0;
            for (int i = 1; i <= sizeSteps; i++)
            {
                count += pattern switch
                {
                    AxisGrowthPattern.None => 0,
                    AxisGrowthPattern.Every => 1,
                    AxisGrowthPattern.Odd => i % 2 != 0 ? 1 : 0,
                    AxisGrowthPattern.Even => i % 2 == 0 ? 1 : 0,
                    AxisGrowthPattern.EveryThree => i % 3 == 0 ? 1 : 0,
                    _ => 0
                };
            }
            return count;
        }

        public static int EvaluateOrigin(AxisAlignment origin, int axisSize, int padding = 0)
        {
            return origin switch
            {
                AxisAlignment.Center => axisSize / 2,
                AxisAlignment.Positive => axisSize - 1 - padding,
                AxisAlignment.Negative => padding,
                _ => axisSize / 2
            };
        }
    }
}
