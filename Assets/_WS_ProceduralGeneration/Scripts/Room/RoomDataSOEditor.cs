#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WS_ProceduralGeneration
{
    [CustomEditor(typeof(RoomDataSO))]
    public class RoomDataSOEditor : Editor
    {
        MapSpriteDBSO _db;
        bool _showMapFill = true;
        bool _showPreview = false;

        Sprite[] _previewSprites;
        string[] _previewDebug;
        Vector2 _previewScroll;

        int _previewLayer = 0;
        bool _previewDirty = true;

        static readonly Color ColFloor = new(0.18f, 0.48f, 0.18f, 1f);
        static readonly Color ColPort = new(0.92f, 0.70f, 0.10f, 1f);
        static readonly Color ColMissing = new(0.72f, 0.15f, 0.15f, 1f);
        static readonly Color ColBg = new(0.08f, 0.08f, 0.08f, 1f);
        static readonly Color ColWall = new(0.13f, 0.13f, 0.13f, 1f);

        const int PreviewCellPx = 48;

        HashSet<Vector3Int> _footprintSet;
        Dictionary<Vector3Int, List<RoomDataSO.RoomPort>> _portMap;

        static readonly Direction[] Cardinals = { Direction.North, Direction.South, Direction.East, Direction.West };

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var so = (RoomDataSO)target;
            if (so.RoomFootprint == null || so.RoomFootprint.Length == 0) return;

            EditorGUILayout.Space(10);

            _showMapFill = EditorGUILayout.BeginFoldoutHeaderGroup(_showMapFill, "Map Sprite Auto-Fill");
            if (_showMapFill)
            {
                DrawMapFillSection(so);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawMapFillSection(RoomDataSO so)
        {
            EditorGUI.BeginChangeCheck();
            _db = (MapSpriteDBSO)EditorGUILayout.ObjectField(
                "Sprite Database", _db, typeof(MapSpriteDBSO), false);
            if (EditorGUI.EndChangeCheck()) _previewDirty = true;

            if (_db == null)
            {
                EditorGUILayout.HelpBox("Assign a MapSpriteDBSO to enable auto-fill.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4);

            var layers = CollectLayers(so);
            if (layers.Count > 1)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Preview Layer Y:", GUILayout.Width(112));
                if (GUILayout.Button("???", GUILayout.Width(24)))
                {
                    int idx = layers.IndexOf(_previewLayer);
                    if (idx > 0) { _previewLayer = layers[idx - 1]; _previewDirty = true; }
                }
                EditorGUILayout.LabelField(_previewLayer.ToString(), GUILayout.Width(24));
                if (GUILayout.Button("???", GUILayout.Width(24)))
                {
                    int idx = layers.IndexOf(_previewLayer);
                    if (idx < layers.Count - 1) { _previewLayer = layers[idx + 1]; _previewDirty = true; }
                }
                EditorGUILayout.EndHorizontal();
            }
            else if (layers.Count == 1)
            {
                _previewLayer = layers[0];
            }

            if (_previewDirty) { Resolve(so); _previewDirty = false; }

            int total = so.RoomFootprint.Length;
            int matched = 0;
            if (_previewSprites != null)
                foreach (var s in _previewSprites) if (s != null) matched++;

            EditorGUILayout.HelpBox(
                $"Resolved {matched}/{total} cells  |  {total - matched} missing in DB.",
                matched == total ? MessageType.None : MessageType.Warning);

            _showPreview = EditorGUILayout.Foldout(_showPreview, "Visual Preview", true);
            if (_showPreview) DrawVisualPreview(so);

            EditorGUILayout.Space(6);

            using (new EditorGUI.DisabledScope(true))
            {
                if (_previewDebug != null && GUILayout.Button("Log Resolution Details"))
                {
                    for (int i = 0; i < _previewDebug.Length; i++)
                        Debug.Log(_previewDebug[i]);
                }
            }

            EditorGUILayout.Space(4);

            EditorGUI.BeginDisabledGroup(matched == 0);
            if (GUILayout.Button($"Apply Sprites to RoomFootprint  ({matched} cells)", GUILayout.Height(30)))
                Apply(so);
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Clear All MapSprites"))
            {
                Undo.RecordObject(so, "Clear Map Sprites");
                for (int i = 0; i < so.RoomFootprint.Length; i++)
                {
                    var fp = so.RoomFootprint[i];
                    fp.MapSprite = null;
                    so.RoomFootprint[i] = fp;
                }
                EditorUtility.SetDirty(so);
            }
        }

        void Resolve(RoomDataSO so)
        {
            BuildLookups(so);

            int count = so.RoomFootprint.Length;
            _previewSprites = new Sprite[count];
            _previewDebug = new string[count];

            for (int i = 0; i < count; i++)
            {
                var cell = so.RoomFootprint[i].Footprint;
                var layerDir = so.RoomFootprint[i].layerDir;

                GetSides(cell, out var openSides, out var connectedSides);

                bool found = _db.TryGetSprite(openSides, connectedSides, layerDir, out var sprite);
                _previewSprites[i] = sprite;

                string openStr = openSides.Length == 0 ? "none" : string.Join(",", openSides);
                string connStr = connectedSides.Length == 0 ? "none" : string.Join(",", connectedSides);
                _previewDebug[i] = found
                    ? $"[{i}] {cell}  open=[{openStr}] conn=[{connStr}]  ??? {sprite.name}"
                    : $"[{i}] {cell}  open=[{openStr}] conn=[{connStr}]  ??? NO MATCH";
            }
        }

        void Apply(RoomDataSO so)
        {
            if (_previewSprites == null) Resolve(so);

            Undo.RecordObject(so, "Auto-Fill Map Sprites");
            for (int i = 0; i < so.RoomFootprint.Length; i++)
            {
                if (_previewSprites[i] == null) continue;
                var fp = so.RoomFootprint[i];
                fp.MapSprite = _previewSprites[i];
                so.RoomFootprint[i] = fp;
            }
            EditorUtility.SetDirty(so);
            Debug.Log($"[MapFill] Applied sprites to {so.name}.");
        }

        void GetSides(Vector3Int cell,
                      out Direction[] openSides,
                      out Direction[] connectedSides)
        {
            var open = new List<Direction>();
            var connected = new List<Direction>();

            foreach (var dir in Cardinals)
            {
                Vector3Int nb = cell + DirectionUtils.DirectionVector(dir);

                bool hasNeighbour = _footprintSet.Contains(nb);
                bool hasPort = CellHasPort(cell, dir);

                if (hasPort) open.Add(dir);
                if (hasNeighbour) connected.Add(dir);
            }

            openSides = open.ToArray();
            connectedSides = connected.ToArray();
        }

        bool CellHasPort(Vector3Int cell, Direction dir)
        {
            if (!_portMap.TryGetValue(cell, out var ports)) return false;
            foreach (var p in ports) if (p.face == dir) return true;
            return false;
        }

        void BuildLookups(RoomDataSO so)
        {
            _footprintSet = new HashSet<Vector3Int>();
            foreach (var fp in so.RoomFootprint) _footprintSet.Add(fp.Footprint);

            _portMap = new Dictionary<Vector3Int, List<RoomDataSO.RoomPort>>();
            foreach (var port in so.Ports)
            {
                if (!_portMap.ContainsKey(port.localCell)) _portMap[port.localCell] = new();
                _portMap[port.localCell].Add(port);
            }
        }

        void DrawVisualPreview(RoomDataSO so)
        {
            var layerCells = new List<(Vector3Int cell, int idx)>();
            for (int i = 0; i < so.RoomFootprint.Length; i++)
            {
                var fp = so.RoomFootprint[i];
                if (fp.Footprint.y == _previewLayer)
                    layerCells.Add((fp.Footprint, i));
            }
            if (layerCells.Count == 0) return;

            int minX = int.MaxValue, maxX = int.MinValue;
            int minZ = int.MaxValue, maxZ = int.MinValue;
            foreach (var (c, _) in layerCells)
            {
                if (c.x < minX) minX = c.x; if (c.x > maxX) maxX = c.x;
                if (c.z < minZ) minZ = c.z; if (c.z > maxZ) maxZ = c.z;
            }

            int cols = maxX - minX + 1;
            int rows = maxZ - minZ + 1;
            int cp = PreviewCellPx;
            float tw = cols * cp;
            float th = rows * cp;

            float displayW = Mathf.Min(EditorGUIUtility.currentViewWidth - 30f, tw);
            float displayH = displayW * th / tw;

            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll,
                GUILayout.Height(Mathf.Min(displayH + 4f, 400f)));

            Rect canvasRect = GUILayoutUtility.GetRect(displayW, displayH);
            float scale = displayW / tw;

            EditorGUI.DrawRect(canvasRect, ColBg);

            foreach (var (cell, idx) in layerCells)
            {
                int col = cell.x - minX;
                int row = cell.z - minZ;

                float px = canvasRect.x + col * cp * scale;
                float py = canvasRect.y + (rows - 1 - row) * cp * scale;
                float cs = cp * scale;

                Rect cellRect = new(px, py, cs, cs);

                var sprite = (_previewSprites != null && idx < _previewSprites.Length)
                    ? _previewSprites[idx] : null;

                if (sprite != null)
                {
                    var tex = sprite.texture;
                    var tr = sprite.textureRect;
                    var uv = new Rect(tr.x / tex.width, tr.y / tex.height,
                                       tr.width / tex.width, tr.height / tex.height);

                    Color prev = GUI.color;
                    GUI.color = so.roomColor;
                    GUI.DrawTextureWithTexCoords(cellRect, tex, uv, true);
                    GUI.color = prev;
                }
                else
                {
                    EditorGUI.DrawRect(cellRect, ColMissing * new Color(1, 1, 1, 0.5f));
                    var missingStyle = new GUIStyle(EditorStyles.boldLabel)
                    { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white }, fontSize = 9 };
                    GUI.Label(cellRect, "?", missingStyle);
                }

                foreach (var dir in Cardinals)
                {
                    if (!CellHasPort(cell, dir)) continue;
                    Rect dotRect = PortDotRect(cellRect, dir, cs * 0.18f);
                    EditorGUI.DrawRect(dotRect, ColPort);
                }

                var coordStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                { fontSize = 7, normal = { textColor = new Color(1, 1, 1, 0.45f) } };
                GUI.Label(new Rect(px, py + cs - 11f, cs, 11f),
                    $"{cell.x},{cell.z}", coordStyle);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            DrawLegendDot(ColPort, "Port");
            DrawLegendDot(ColMissing, "No DB match");
            EditorGUILayout.EndHorizontal();
        }

        static Rect PortDotRect(Rect cell, Direction dir, float size)
        {
            float cx = cell.x + cell.width * 0.5f - size * 0.5f;
            float cy = cell.y + cell.height * 0.5f - size * 0.5f;
            float m = 2f;
            return dir switch
            {
                Direction.North => new Rect(cx, cell.y + m, size, size),
                Direction.South => new Rect(cx, cell.yMax - size - m, size, size),
                Direction.East => new Rect(cell.xMax - size - m, cy, size, size),
                Direction.West => new Rect(cell.x + m, cy, size, size),
                _ => new Rect(cx, cy, size, size),
            };
        }

        static void DrawLegendDot(Color color, string label)
        {
            var dotRect = GUILayoutUtility.GetRect(10, 10, GUILayout.Width(10), GUILayout.Height(10));
            EditorGUI.DrawRect(dotRect, color);
            EditorGUILayout.LabelField(label, GUILayout.Width(90));
        }

        static List<int> CollectLayers(RoomDataSO so)
        {
            var set = new SortedSet<int>();
            foreach (var fp in so.RoomFootprint) set.Add(fp.Footprint.y);
            return new List<int>(set);
        }
    }
#endif
}