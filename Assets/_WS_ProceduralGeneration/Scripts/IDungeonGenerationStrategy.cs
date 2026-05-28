using System.Collections.Generic;
using UnityEngine;

namespace WS_ProceduralGeneration
{
public interface IDungeonGenerationStrategy
{
    Vector3Int Generate(
        DungeonGenerationContext ctx,
        List<DungeonGenerator.PlacedRoom> placed,
        DungeonGenerator.Cell[,,] grid);
}
}
