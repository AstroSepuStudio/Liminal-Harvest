using Mirror;
using UnityEngine;
using WS_ProceduralGeneration;

public class GM_DayProgressionModule : NetworkBehaviour
{
    [System.Serializable]
    public struct DayProgressionOverride
    {
        [Tooltip("The exact day this override applies to.")]
        public int day;
        [Min(1)] public int mapSize;
        [Min(0.1f)] public float difficultyMultiplier;
        [Min(1)] public int entityCap;
        public WSDG_Tier.Tier minEntityTier;
        public WSDG_Tier.Tier maxEntityTier;
        public WSDG_Tier.Tier minLootTier;
        public WSDG_Tier.Tier maxLootTier;
    }

    [Header("Fallback Formulas")]
    [SerializeField, Min(1)] int maxMapSize = 10;
    [SerializeField] float difficultyOffset = 6f;
    [SerializeField, Min(1)] int defaultEntityCap = 5;

    [Header("Day Overrides")]
    [SerializeField] DayProgressionOverride[] overrides;

    [SyncVar] public int CurrentMapSize = 1;
    [SyncVar] public float CurrentDifficultyMultiplier = 1f;
    [SyncVar] public int CurrentEntityCap = 5;
    [SyncVar] public WSDG_Tier.Tier CurrentMinEntityTier = WSDG_Tier.Tier.Common;
    [SyncVar] public WSDG_Tier.Tier CurrentMaxEntityTier = WSDG_Tier.Tier.Common;
    [SyncVar] public WSDG_Tier.Tier CurrentMinLootTier = WSDG_Tier.Tier.Common;
    [SyncVar] public WSDG_Tier.Tier CurrentMaxLootTier = WSDG_Tier.Tier.Common;

    public int FormulaMapSize(int day) => Mathf.Clamp(day, 1, maxMapSize);
    public float FormulaDifficulty(int day) => Mathf.Max(1f, day - difficultyOffset);

    public DayProgressionOverride[] Overrides => overrides;
    public int MaxMapSize => maxMapSize;
    public float DifficultyOffset => difficultyOffset;
    public int DefaultEntityCap => defaultEntityCap;

    [Server]
    public void ApplyForDay(int day)
    {
        CurrentMapSize = Mathf.Clamp(day, 1, maxMapSize);
        CurrentDifficultyMultiplier = Mathf.Max(1f, day - difficultyOffset);
        CurrentEntityCap = defaultEntityCap;

        if (TryGetExactOverride(day, out var exact))
        {
            CurrentMapSize = exact.mapSize;
            CurrentDifficultyMultiplier = exact.difficultyMultiplier;
            CurrentEntityCap = exact.entityCap;
            CurrentMinEntityTier = exact.minEntityTier;
            CurrentMaxEntityTier = exact.maxEntityTier;
            CurrentMinLootTier = exact.minLootTier;
            CurrentMaxLootTier = exact.maxLootTier;
        }
        else if (TryGetLastOverrideBefore(day, out var last))
        {
            CurrentEntityCap = last.entityCap;
            CurrentMinEntityTier = last.minEntityTier;
            CurrentMaxEntityTier = last.maxEntityTier;
            CurrentMinLootTier = last.minLootTier;
            CurrentMaxLootTier = last.maxLootTier;
        }

        Debug.Log($"[DayProgression] Day {day} — " +
                  $"MapSize={CurrentMapSize}, Difficulty=x{CurrentDifficultyMultiplier}, " +
                  $"EntityCap={CurrentEntityCap}, " +
                  $"EntityTier=[{CurrentMinEntityTier}-{CurrentMaxEntityTier}], " +
                  $"LootTier=[{CurrentMinLootTier}-{CurrentMaxLootTier}]");
    }

    public bool IsEntityTierAllowed(WSDG_Tier.Tier tier) =>
        tier >= CurrentMinEntityTier && tier <= CurrentMaxEntityTier;

    public bool IsLootTierAllowed(WSDG_Tier.Tier tier) =>
        tier >= CurrentMinLootTier && tier <= CurrentMaxLootTier;

    bool TryGetExactOverride(int day, out DayProgressionOverride result)
    {
        if (overrides != null)
            foreach (var o in overrides)
                if (o.day == day) { result = o; return true; }

        result = default;
        return false;
    }

    bool TryGetLastOverrideBefore(int day, out DayProgressionOverride result)
    {
        result = default;
        bool found = false;

        if (overrides == null) return false;

        foreach (var o in overrides)
        {
            if (o.day > day) continue;
            if (!found || o.day > result.day) { result = o; found = true; }
        }

        return found;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (overrides == null) return;

        for (int i = 0; i < overrides.Length; i++)
        {
            var o = overrides[i];

            if (o.minEntityTier > o.maxEntityTier)
                Debug.LogWarning($"[DayProgression] Override day {o.day}: minEntityTier > maxEntityTier.");

            if (o.minLootTier > o.maxLootTier)
                Debug.LogWarning($"[DayProgression] Override day {o.day}: minLootTier > maxLootTier.");

            for (int j = i + 1; j < overrides.Length; j++)
                if (overrides[j].day == o.day)
                    Debug.LogWarning($"[DayProgression] Duplicate override for day {o.day} at indices {i} and {j}.");
        }
    }
#endif
}
