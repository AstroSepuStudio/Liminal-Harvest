using System.Collections.Generic;

namespace WS_ProceduralGeneration
{
    public interface IDungeonSpawner
    {
        SpawnChannel Channel { get; }
        int SpawnOrder { get; }  // lower = runs first
        void Collect(IEnumerable<DungeonSpawnPoint> points);
        void Spawn(DungeonGenerator generator);
        void Clear();
        IReadOnlyList<DungeonSpawnPoint> DiscoveredPoints { get; }
    }
}
