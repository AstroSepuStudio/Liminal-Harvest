using UnityEngine;

namespace WS_ProceduralGeneration
{
    [CreateAssetMenu(menuName = "DungeonGen/BiomeData")]
    public class BiomeDataSO : ScriptableObject
    {
        public Biome biome;

        [Header("Bubble Size")]
        [Min(1)] public int minRadius = 2;
        [Min(1)] public int maxRadius = 5;

        [Header("Generation")]
        public WSDG_Tier.Tier tier;
        [Range(0f, 1f)] public float spawnWeight = 1f;
    }
}
