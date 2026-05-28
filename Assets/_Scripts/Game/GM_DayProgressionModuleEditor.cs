#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;
using WS_ProceduralGeneration;
using static GM_DayProgressionModule;

[CustomEditor(typeof(GM_DayProgressionModule))]
public class GM_DayProgressionModuleEditor : Editor
{
    const float W_DAY = 44f;
    const float W_MAP = 52f;
    const float W_DIFF = 58f;
    const float W_CAP = 48f;
    const float W_TIER = 158f;
    const float W_BTN = 20f;
    const float ROW_H = 22f;
    const float HDR_H = 20f;
    const float PAD = 4f;

    static readonly string[] TIER_NAMES = { "Common", "Uncommon", "Rare", "Epic", "Legendary" };

    static readonly Color[] TIER_COLORS = {
        new(0.82f, 0.82f, 0.78f),
        new(0.71f, 0.83f, 0.96f),
        new(0.62f, 0.88f, 0.79f),
        new(0.81f, 0.79f, 0.96f),
        new(0.98f, 0.78f, 0.46f),
    };

    static readonly Color ERR_ROW = new(1f, 0.85f, 0.85f);
    static readonly Color HDR_BG = new(0.22f, 0.22f, 0.22f, 0.08f);
    static readonly Color FORMULA_BG = new(0.18f, 0.55f, 0.34f, 0.08f);

    SerializedProperty _overrides;
    int _previewDay = 1;
    bool _showPreview = false;

    void OnEnable() => _overrides = serializedObject.FindProperty("overrides");

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Fallback formulas", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("maxMapSize"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("difficultyOffset"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultEntityCap"));

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Day overrides", EditorStyles.boldLabel);
        DrawTable();

        EditorGUILayout.Space(4);
        DrawAddRemoveButtons();

        EditorGUILayout.Space(8);
        DrawPreviewScrubber();

        if (Application.isPlaying)
        {
            EditorGUILayout.Space(4);
            DrawRuntimeValues();
        }

        serializedObject.ApplyModifiedProperties();
    }

    void DrawTable()
    {
        var mod = (GM_DayProgressionModule)target;
        float totalW = EditorGUIUtility.currentViewWidth - 32f;

        Rect hdr = GUILayoutUtility.GetRect(totalW, HDR_H + PAD * 2);
        EditorGUI.DrawRect(hdr, HDR_BG);

        float hx = hdr.x + PAD;
        float hy = hdr.y + PAD;
        GUI.Label(ColRect(ref hx, hy, W_DAY, HDR_H), "Day", HeaderStyle());
        GUI.Label(ColRect(ref hx, hy, W_MAP, HDR_H), "Map", HeaderStyle());
        GUI.Label(ColRect(ref hx, hy, W_DIFF, HDR_H), "Diff", HeaderStyle());
        GUI.Label(ColRect(ref hx, hy, W_CAP, HDR_H), "Ent cap", HeaderStyle());
        GUI.Label(ColRect(ref hx, hy, W_TIER, HDR_H), "Entity tiers", HeaderStyle());
        GUI.Label(ColRect(ref hx, hy, W_TIER, HDR_H), "Loot tiers", HeaderStyle());

        if (_overrides == null || _overrides.arraySize == 0)
        {
            DrawFormulaRow(totalW, 1, mod);
            EditorGUILayout.HelpBox(
                "No overrides defined. All days use the fallback formulas. Add overrides for specific days.",
                MessageType.Info);
            return;
        }

        var indices = Enumerable.Range(0, _overrides.arraySize)
            .OrderBy(i => _overrides.GetArrayElementAtIndex(i)
                                    .FindPropertyRelative("day").intValue)
            .ToList();

        int prevDay = int.MinValue;
        foreach (int i in indices)
            DrawOverrideRow(i, totalW, ref prevDay, mod);
    }

    void DrawFormulaRow(float totalW, int day, GM_DayProgressionModule mod)
    {
        Rect row = GUILayoutUtility.GetRect(totalW, ROW_H + PAD * 2);
        EditorGUI.DrawRect(row, FORMULA_BG);

        float x = row.x + PAD;
        float y = row.y + PAD;

        GUI.Label(ColRect(ref x, y, W_DAY, ROW_H), $">={day}", FormulaStyle());
        GUI.Label(ColRect(ref x, y, W_MAP, ROW_H), $"={mod.FormulaMapSize(day)}", FormulaStyle());
        GUI.Label(ColRect(ref x, y, W_DIFF, ROW_H), $"={mod.FormulaDifficulty(day):F2}", FormulaStyle());
        GUI.Label(ColRect(ref x, y, W_CAP, ROW_H), $"={mod.DefaultEntityCap}", FormulaStyle());
        GUI.Label(ColRect(ref x, y, W_TIER, ROW_H), "carried forward", FormulaStyle());
        GUI.Label(ColRect(ref x, y, W_TIER, ROW_H), "carried forward", FormulaStyle());
    }

    void DrawOverrideRow(int i, float totalW, ref int prevDay, GM_DayProgressionModule mod)
    {
        var el = _overrides.GetArrayElementAtIndex(i);
        var dayP = el.FindPropertyRelative("day");
        var mapP = el.FindPropertyRelative("mapSize");
        var diffP = el.FindPropertyRelative("difficultyMultiplier");
        var capP = el.FindPropertyRelative("entityCap");
        var minEntP = el.FindPropertyRelative("minEntityTier");
        var maxEntP = el.FindPropertyRelative("maxEntityTier");
        var minLootP = el.FindPropertyRelative("minLootTier");
        var maxLootP = el.FindPropertyRelative("maxLootTier");

        bool entErr = minEntP.enumValueIndex > maxEntP.enumValueIndex;
        bool lootErr = minLootP.enumValueIndex > maxLootP.enumValueIndex;
        bool dupErr = dayP.intValue == prevDay;

        Color rowBg = (entErr || lootErr || dupErr)
            ? ERR_ROW
            : (i % 2 == 0 ? Color.clear : new Color(0, 0, 0, 0.03f));

        Rect row = GUILayoutUtility.GetRect(totalW, ROW_H + PAD * 2);
        EditorGUI.DrawRect(row, rowBg);

        float x = row.x + PAD;
        float y = row.y + PAD;

        dayP.intValue = Mathf.Max(1, EditorGUI.IntField(ColRect(ref x, y, W_DAY - 2, ROW_H), dayP.intValue, DayStyle()));
        mapP.intValue = Mathf.Max(1, EditorGUI.IntField(ColRect(ref x, y, W_MAP - 2, ROW_H), mapP.intValue, NumStyle()));
        diffP.floatValue = Mathf.Max(0.1f, EditorGUI.FloatField(ColRect(ref x, y, W_DIFF - 2, ROW_H), diffP.floatValue, NumStyle()));
        capP.intValue = Mathf.Max(1, EditorGUI.IntField(ColRect(ref x, y, W_CAP - 2, ROW_H), capP.intValue, NumStyle()));

        DrawTierRange(ref x, y, minEntP, maxEntP);
        DrawTierRange(ref x, y, minLootP, maxLootP);

        if (GUI.Button(ColRect(ref x, y, W_BTN, ROW_H), "x", DeleteStyle()))
        {
            _overrides.DeleteArrayElementAtIndex(i);
            serializedObject.ApplyModifiedProperties();
            GUIUtility.ExitGUI();
        }

        prevDay = dayP.intValue;
    }

    void DrawTierRange(ref float x, float y, SerializedProperty minP, SerializedProperty maxP)
    {
        float halfW = (W_TIER - 16f) / 2f;
        DrawTierPopup(ColRect(ref x, y, halfW, ROW_H), minP);
        GUI.Label(ColRect(ref x, y, 16f, ROW_H), "to", ArrowStyle());
        DrawTierPopup(ColRect(ref x, y, halfW, ROW_H), maxP);
    }

    void DrawTierPopup(Rect r, SerializedProperty prop)
    {
        int idx = prop.enumValueIndex;
        Color bg = idx >= 0 && idx < TIER_COLORS.Length ? TIER_COLORS[idx] : Color.white;
        bg.a = 0.35f;
        EditorGUI.DrawRect(r, bg);
        int newIdx = EditorGUI.Popup(r, idx, TIER_NAMES, TierPopupStyle());
        if (newIdx != idx) prop.enumValueIndex = newIdx;
    }

    void DrawAddRemoveButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Add override", GUILayout.Width(100), GUILayout.Height(22)))
            {
                var usedDays = new System.Collections.Generic.HashSet<int>();
                for (int i = 0; i < _overrides.arraySize; i++)
                    usedDays.Add(_overrides.GetArrayElementAtIndex(i)
                                           .FindPropertyRelative("day").intValue);

                int nextDay = 1;
                while (usedDays.Contains(nextDay)) nextDay++;

                _overrides.InsertArrayElementAtIndex(_overrides.arraySize);
                var newEl = _overrides.GetArrayElementAtIndex(_overrides.arraySize - 1);
                newEl.FindPropertyRelative("day").intValue = nextDay;
                newEl.FindPropertyRelative("mapSize").intValue = nextDay;
                newEl.FindPropertyRelative("difficultyMultiplier").floatValue = Mathf.Max(1f, nextDay - 6f);
                newEl.FindPropertyRelative("entityCap").intValue = ((GM_DayProgressionModule)target).DefaultEntityCap;
                newEl.FindPropertyRelative("minEntityTier").enumValueIndex = 0;
                newEl.FindPropertyRelative("maxEntityTier").enumValueIndex = 0;
                newEl.FindPropertyRelative("minLootTier").enumValueIndex = 0;
                newEl.FindPropertyRelative("maxLootTier").enumValueIndex = 0;
            }

            if (_overrides.arraySize > 0 &&
                GUILayout.Button("Remove last", GUILayout.Width(95), GUILayout.Height(22)))
                _overrides.DeleteArrayElementAtIndex(_overrides.arraySize - 1);
        }
    }

    void DrawPreviewScrubber()
    {
        _showPreview = EditorGUILayout.Foldout(_showPreview, "Preview day", true);
        if (!_showPreview) return;

        var mod = (GM_DayProgressionModule)target;

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Day", GUILayout.Width(32));
            _previewDay = EditorGUILayout.IntSlider(_previewDay, 1, 50);
        }

        int mapSize = mod.FormulaMapSize(_previewDay);
        float diff = mod.FormulaDifficulty(_previewDay);
        int cap = mod.DefaultEntityCap;
        bool hasExact = false;

        var ovr = mod.Overrides;
        DayProgressionOverride lastBefore = default;
        bool foundLast = false;
        DayProgressionOverride exactOvr = default;

        if (ovr != null)
        {
            foreach (var o in ovr)
            {
                if (o.day == _previewDay)
                {
                    exactOvr = o;
                    hasExact = true;
                }

                if (o.day <= _previewDay && (!foundLast || o.day > lastBefore.day))
                {
                    lastBefore = o;
                    foundLast = true;
                }
            }
        }

        if (hasExact)
        {
            mapSize = exactOvr.mapSize;
            diff = exactOvr.difficultyMultiplier;
            cap = exactOvr.entityCap;
        }
        else if (foundLast)
        {
            cap = lastBefore.entityCap;
        }

        WSDG_Tier.Tier minEnt = hasExact ? exactOvr.minEntityTier : (foundLast ? lastBefore.minEntityTier : WSDG_Tier.Tier.Common);
        WSDG_Tier.Tier maxEnt = hasExact ? exactOvr.maxEntityTier : (foundLast ? lastBefore.maxEntityTier : WSDG_Tier.Tier.Common);
        WSDG_Tier.Tier minLoot = hasExact ? exactOvr.minLootTier : (foundLast ? lastBefore.minLootTier : WSDG_Tier.Tier.Common);
        WSDG_Tier.Tier maxLoot = hasExact ? exactOvr.maxLootTier : (foundLast ? lastBefore.maxLootTier : WSDG_Tier.Tier.Common);

        string source = hasExact
            ? "exact override"
            : (foundLast ? $"carried from day {lastBefore.day}" : "defaults");

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField("Map size", mapSize);
            EditorGUILayout.FloatField("Difficulty", diff);
            EditorGUILayout.IntField("Entity cap", cap);
            EditorGUILayout.LabelField("Entity tiers", $"{minEnt} to {maxEnt}");
            EditorGUILayout.LabelField("Loot tiers", $"{minLoot} to {maxLoot}");
        }

        EditorGUILayout.LabelField("Source", source, EditorStyles.miniLabel);
    }

    void DrawRuntimeValues()
    {
        EditorGUILayout.LabelField("Active runtime values", EditorStyles.boldLabel);
        var mod = (GM_DayProgressionModule)target;
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField("Map size", mod.CurrentMapSize);
            EditorGUILayout.FloatField("Difficulty", mod.CurrentDifficultyMultiplier);
            EditorGUILayout.IntField("Entity cap", mod.CurrentEntityCap);
            EditorGUILayout.LabelField("Entity tiers", $"{mod.CurrentMinEntityTier} to {mod.CurrentMaxEntityTier}");
            EditorGUILayout.LabelField("Loot tiers", $"{mod.CurrentMinLootTier} to {mod.CurrentMaxLootTier}");
        }
    }

    static Rect ColRect(ref float x, float y, float w, float h)
    {
        var r = new Rect(x, y, w, h);
        x += w + PAD;
        return r;
    }

    static GUIStyle HeaderStyle() => new(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
    static GUIStyle FormulaStyle() => new(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.35f, 0.65f, 0.45f) } };
    static GUIStyle DayStyle() => new(EditorStyles.numberField) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    static GUIStyle NumStyle() => new(EditorStyles.numberField) { alignment = TextAnchor.MiddleCenter };
    static GUIStyle ArrowStyle() => new(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
    static GUIStyle TierPopupStyle() => new(EditorStyles.popup) { fontSize = 11 };
    static GUIStyle DeleteStyle() => new(EditorStyles.miniButton)
    {
        fontSize = 12,
        fontStyle = FontStyle.Bold,
        normal = { textColor = new Color(0.8f, 0.2f, 0.2f) },
        hover = { textColor = new Color(0.9f, 0.1f, 0.1f) }
    };
}
#endif
