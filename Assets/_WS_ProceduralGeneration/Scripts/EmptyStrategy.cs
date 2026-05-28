using System.Collections.Generic;
using UnityEngine;

namespace WS_ProceduralGeneration
{
    public class EmptyStrategy : MonoBehaviour, IDungeonGenerationStrategy
    {
        public Vector3Int Generate(DungeonGenerationContext ctx, List<DungeonGenerator.PlacedRoom> placed, DungeonGenerator.Cell[,,] grid)
        {
            Debug.Log("[Strategy] Empty strategy generated!");
            return Vector3Int.zero;
        }
    }
}
