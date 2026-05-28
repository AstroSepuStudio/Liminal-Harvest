using UnityEngine;
using WS_ProceduralGeneration;

[CreateAssetMenu(menuName = "LethalLive/Entity Data")]
public class EntityDataSO : ScriptableObject
{
    public WSDG_Tier.Tier EntityTier;
    public GameObject entityPrefab;
    [Range(1, 5)] public int minSpawnCount;
    [Range(1, 5)] public int maxSpawnCount;
}
