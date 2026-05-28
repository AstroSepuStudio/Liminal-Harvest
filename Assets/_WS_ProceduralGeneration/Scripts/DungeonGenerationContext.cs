using UnityEngine;

namespace WS_ProceduralGeneration
{
    public class DungeonGenerationContext
    {
        public System.Random RNG;
        public Vector3Int GridSize;
        public int CellSize;
        public DungeonSettingsSO Settings;
        public ThemeDataSO Theme;

        public bool InBounds(Vector3Int p) =>
            p.x >= 0 && p.y >= 0 && p.z >= 0 &&
            p.x < GridSize.x && p.y < GridSize.y && p.z < GridSize.z;

        public bool FootprintFits(RoomDataSO data, DungeonGenerator.Cell[,,] grid, Vector3Int anchor)
        {
            if (data == null) return false;
            foreach (var local in data.RoomFootprint)
            {
                var w = anchor + local.Footprint;
                if (!InBounds(w)) return false;
                if (grid[w.x, w.y, w.z].placedRoom != null) return false;
            }
            return true;
        }
    }
}
