using UnityEngine;

namespace WS_ProceduralGeneration
{
    [CreateAssetMenu(fileName = "DungeonSettings", menuName = "DungeonGen/Settings")]
    public class DungeonSettingsSO : ScriptableObject
    {
        [Header("Seed & Randomness")]
        public string gameSeed = "67";
        public bool useSetSeed = false;

        [Header("Grid Layout")]
        public Vector3Int baseGridSize = new(3, 1, 3);
        public int cellSize = 5;

        [Header("Generation Limits")]
        public int maxRoomsBase = 30;
        public int maxDepthBase = 30;

        [Header("Doorway Dimensions")]
        public float doorwayWidth = 2.5f;
        public float doorwayHeight = 3.0f;
        public float wallThickness = 0.1f;
        public float wallHeight = 4.0f;

        [Header("Difficulty")]
        public AnimationCurve difficultyCurve = AnimationCurve.EaseInOut(0, 1, 20, 3);

        [Header("Audio Multipliers")]
        public float reverbMinDistanceMultiplier = 0.5f;
        public float reverbMaxDistanceMultiplier = 1.5f;
    }
}
