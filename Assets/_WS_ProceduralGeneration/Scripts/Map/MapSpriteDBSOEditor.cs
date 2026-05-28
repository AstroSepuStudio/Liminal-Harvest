#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WS_ProceduralGeneration
{
    [CustomEditor(typeof(MapSpriteDBSO))]
    public class MapSpriteDBSOEditor : Editor
    {
        const float ColPreview = 52f;
        const float ColSprite = 96f;
        const float ColType = 96f;
        const float ColSides = 108f;
        const float ColLayer = 68f;
        const float ColBtns = 46f;
        const float RowH = 68f;
        const float BtnSz = 20f;
        const float DirBtnW = 20f;
        const float DirBtnH = 15f;

        SerializedProperty _parts;
        Vector2 _scroll;
        string _filter = "";
        int _filterType = -1;
        bool _showImport = true;
        Texture2D _importTex;

        void OnEnable() => _parts = serializedObject.FindProperty("mapParts");

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawImport();
            EditorGUILayout.Space(4);
            DrawFilterBar();
            EditorGUILayout.Space(2);
            DrawTable();
            EditorGUILayout.Space(4);
            DrawFooter();
            serializedObject.ApplyModifiedProperties();
        }

        void DrawImport()
        {
            _showImport = EditorGUILayout.BeginFoldoutHeaderGroup(_showImport, "Bulk Import from Sprite Sheet");
            if (_showImport)
            {
                EditorGUILayout.HelpBox(
                    "Drop a Texture2D (Sprite Mode: Multiple). All sub-sprites are added as new entries.",
                    MessageType.Info);

                EditorGUILayout.BeginHorizontal();
                _importTex = (Texture2D)EditorGUILayout.ObjectField("Sprite Sheet", _importTex,
                    typeof(Texture2D), false, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUI.BeginDisabledGroup(_importTex == null);
                if (GUILayout.Button("Import", GUILayout.Width(68))) DoImport(_importTex);
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Add single sprite:", GUILayout.Width(130));
                var dropped = (Sprite)EditorGUILayout.ObjectField(null, typeof(Sprite), false);
                if (dropped != null) AddSingle(dropped);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DoImport(Texture2D tex)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path)
                                 .OfType<Sprite>().OrderBy(s => s.name).ToArray();
            if (sprites.Length == 0)
            {
                EditorUtility.DisplayDialog("No sprites found",
                    "Set Sprite Mode to 'Multiple' and slice first.", "OK");
                return;
            }
            Undo.RecordObject(target, "Bulk Import Sprites");
            ((MapSpriteDBSO)target).AddFromSprites(sprites);
            serializedObject.Update();
        }

        void AddSingle(Sprite s)
        {
            int idx = _parts.arraySize++;
            var e = _parts.GetArrayElementAtIndex(idx);
            e.FindPropertyRelative("partSprite").objectReferenceValue = s;
            e.FindPropertyRelative("partType").enumValueIndex = 0;
            e.FindPropertyRelative("layerDir").enumValueIndex = 0;
            e.FindPropertyRelative("openSides").ClearArray();
            e.FindPropertyRelative("connectedSides").ClearArray();
        }

        void DrawFilterBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("Filter:", GUILayout.Width(38));
            _filter = EditorGUILayout.TextField(_filter, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("???", EditorStyles.toolbarButton, GUILayout.Width(18))) _filter = "";
            EditorGUILayout.LabelField("Type:", GUILayout.Width(34));
            var names = new[] { "All" }.Concat(Enum.GetNames(typeof(MapSpriteDBSO.PartType))).ToArray();
            int nf = EditorGUILayout.Popup(_filterType + 1, names,
                         EditorStyles.toolbarPopup, GUILayout.Width(84)) - 1;
            if (nf != _filterType) _filterType = nf;
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"{_parts.arraySize} entries",
                EditorStyles.toolbarButton, GUILayout.Width(74));
            EditorGUILayout.EndHorizontal();
        }

        void DrawTable()
        {
            DrawColumnHeader();

            float h = Mathf.Clamp(_parts.arraySize * RowH + 4f, 60f, 520f);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(h));

            int removeAt = -1, moveUp = -1;

            for (int i = 0; i < _parts.arraySize; i++)
            {
                var elem = _parts.GetArrayElementAtIndex(i);
                var sprite = elem.FindPropertyRelative("partSprite").objectReferenceValue as Sprite;
                if (!PassesFilter(sprite, elem)) continue;

                Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(RowH));
                if (i % 2 == 0) EditorGUI.DrawRect(rowRect, new Color(0f, 0f, 0f, 0.07f));

                CenterV(RowH, () => DrawPreview(sprite), ColPreview);
                CenterV(RowH, () => DrawSpriteField(elem), ColSprite);
                CenterV(RowH, () => DrawTypeField(elem), ColType);

                // Two stacked direction rows: Open | Connected
                CenterV(RowH, () => DrawBothSides(elem, i), ColSides);

                CenterV(RowH, () => DrawLayerDir(elem), ColLayer);
                CenterV(RowH, () =>
                {
                    if (i > 0 && GUILayout.Button("???", GUILayout.Width(BtnSz), GUILayout.Height(BtnSz))) moveUp = i;
                    if (GUILayout.Button("???", GUILayout.Width(BtnSz), GUILayout.Height(BtnSz))) removeAt = i;
                }, ColBtns);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            if (removeAt >= 0) _parts.DeleteArrayElementAtIndex(removeAt);
            if (moveUp >= 0) _parts.MoveArrayElement(moveUp, moveUp - 1);
        }

        void DrawColumnHeader()
        {
            var hs = new GUIStyle(EditorStyles.boldLabel)
            { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            var subS = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            { fontSize = 9 };

            Rect hr = EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(28));
            EditorGUI.DrawRect(hr, new Color(0f, 0f, 0f, 0.15f));

            GUILayout.Space(ColPreview);
            EditorGUILayout.LabelField("Sprite", hs, GUILayout.Width(ColSprite));
            EditorGUILayout.LabelField("Type", hs, GUILayout.Width(ColType));

            EditorGUILayout.BeginVertical(GUILayout.Width(ColSides));
            EditorGUILayout.LabelField("Open Sides", hs, GUILayout.Width(ColSides));
            EditorGUILayout.LabelField("Connected Sides", subS, GUILayout.Width(ColSides));
            EditorGUILayout.EndVertical();

            EditorGUILayout.LabelField("Layer", hs, GUILayout.Width(ColLayer));
            GUILayout.Space(ColBtns);
            EditorGUILayout.EndHorizontal();
        }

        void DrawBothSides(SerializedProperty elem, int row)
        {
            var openProp = elem.FindPropertyRelative("openSides");
            var connProp = elem.FindPropertyRelative("connectedSides");

            EditorGUILayout.BeginVertical(GUILayout.Width(ColSides));
            GUILayout.Space(2);

            DrawDirRow("Port", openProp, ActivePortColor);
            GUILayout.Space(3);
            DrawDirRow("Room", connProp, ActiveConnColor);

            GUILayout.Space(2);
            EditorGUILayout.EndVertical();
        }

        static readonly Color ActivePortColor = new(0.25f, 0.72f, 0.25f, 1f);
        static readonly Color ActiveConnColor = new(0.25f, 0.55f, 0.90f, 1f);
        static readonly Color InactiveCol = new(0.35f, 0.35f, 0.35f, 1f);

        static readonly Direction[] CardDirs = { Direction.North, Direction.East, Direction.South, Direction.West };
        static readonly string[] CardLabels = { "N", "E", "S", "W" };

        static void DrawDirRow(string label, SerializedProperty arrProp, Color activeColor)
        {
            var current = new HashSet<Direction>();
            for (int k = 0; k < arrProp.arraySize; k++)
                current.Add((Direction)arrProp.GetArrayElementAtIndex(k).enumValueIndex);

            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            { fixedWidth = 26, alignment = TextAnchor.MiddleRight };

            var btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 8,
                fontStyle = FontStyle.Bold,
                fixedWidth = DirBtnW,
                fixedHeight = DirBtnH,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(1, 1, 1, 1)
            };

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", labelStyle);

            for (int d = 0; d < CardDirs.Length; d++)
            {
                var dir = CardDirs[d];
                bool active = current.Contains(dir);

                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = active ? activeColor : InactiveCol;

                if (GUILayout.Button(CardLabels[d], btnStyle))
                {
                    if (active) current.Remove(dir); else current.Add(dir);
                    WriteBack(arrProp, current);
                }

                GUI.backgroundColor = prev;
            }

            EditorGUILayout.EndHorizontal();
        }

        static void WriteBack(SerializedProperty arrProp, HashSet<Direction> dirs)
        {
            arrProp.ClearArray();
            int i = 0;
            foreach (var v in dirs) { arrProp.arraySize++; arrProp.GetArrayElementAtIndex(i++).enumValueIndex = (int)v; }
        }

        static void CenterV(float rowH, Action draw, float width)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(width), GUILayout.Height(rowH));
            GUILayout.FlexibleSpace();
            draw();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }

        static void DrawSpriteField(SerializedProperty elem) =>
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("partSprite"),
                GUIContent.none, GUILayout.Width(ColSprite - 4));

        static void DrawTypeField(SerializedProperty elem) =>
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("partType"),
                GUIContent.none, GUILayout.Width(ColType - 4));

        static readonly string[] LayerLabels = { "???", "??? Up", "??? Down" };
        static void DrawLayerDir(SerializedProperty elem)
        {
            var prop = elem.FindPropertyRelative("layerDir");
            int next = EditorGUILayout.Popup(prop.enumValueIndex, LayerLabels, GUILayout.Width(ColLayer - 4));
            if (next != prop.enumValueIndex) prop.enumValueIndex = next;
        }

        static void DrawPreview(Sprite sprite)
        {
            float sz = ColPreview - 6f;
            Rect rect = GUILayoutUtility.GetRect(sz, sz, GUILayout.Width(sz), GUILayout.Height(sz));
            if (sprite != null)
            {
                var tex = sprite.texture;
                var tr = sprite.textureRect;
                GUI.DrawTextureWithTexCoords(rect, tex,
                    new Rect(tr.x / tex.width, tr.y / tex.height,
                             tr.width / tex.width, tr.height / tex.height), true);
            }
            else
            {
                EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
                GUI.Label(rect, "???", new GUIStyle(EditorStyles.centeredGreyMiniLabel));
            }
        }

        void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Add Entry", GUILayout.Width(96)))
            {
                int idx = _parts.arraySize++;
                var e = _parts.GetArrayElementAtIndex(idx);
                e.FindPropertyRelative("partSprite").objectReferenceValue = null;
                e.FindPropertyRelative("partType").enumValueIndex = 0;
                e.FindPropertyRelative("layerDir").enumValueIndex = 0;
                e.FindPropertyRelative("openSides").ClearArray();
                e.FindPropertyRelative("connectedSides").ClearArray();
            }
            EditorGUILayout.EndHorizontal();
        }

        bool PassesFilter(Sprite sprite, SerializedProperty elem)
        {
            if (_filterType >= 0 && elem.FindPropertyRelative("partType").enumValueIndex != _filterType)
                return false;
            if (!string.IsNullOrEmpty(_filter))
            {
                string n = sprite != null ? sprite.name.ToLowerInvariant() : "";
                if (!n.Contains(_filter.ToLowerInvariant())) return false;
            }
            return true;
        }
    }
#endif
}
