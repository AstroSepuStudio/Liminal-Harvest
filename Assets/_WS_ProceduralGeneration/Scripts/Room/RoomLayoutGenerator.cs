#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.ProBuilder;
using System;

namespace WS_ProceduralGeneration
{
    public class RoomLayoutGenerator : EditorWindow
    {
        [Header("Data")]
        RoomDataSO roomData;
        DungeonSettingsSO settings;
        ThemeDataSO theme;
        PortWallBindingsSO portWallBindings;

        [Header("Generation Toggles")]
        bool generateFloor = true;
        bool generateCeiling = true;
        bool generateWalls = true;
        Transform parentOverride;

        [Header("Random Generation Settings")]
        Vector3Int maxRoomSize = new(3, 2, 3);
        int minPorts = 1;
        Vector3 offset = new(0, 0, 0);

        Vector2 scroll;

        [MenuItem("LethalLive/Room Layout Generator")]
        static void Open() => GetWindow<RoomLayoutGenerator>("Room Layout Generator");

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Room Layout Generator", EditorStyles.boldLabel);

            roomData = (RoomDataSO)EditorGUILayout.ObjectField("Room Data SO", roomData, typeof(RoomDataSO), false);
            settings = (DungeonSettingsSO)EditorGUILayout.ObjectField("Global Settings", settings, typeof(DungeonSettingsSO), false);
            theme = (ThemeDataSO)EditorGUILayout.ObjectField("Theme", theme, typeof(ThemeDataSO), false);
            portWallBindings = (PortWallBindingsSO)EditorGUILayout.ObjectField("Port Wall Binding", portWallBindings, typeof(PortWallBindingsSO), false);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Random Generation", EditorStyles.boldLabel);
            maxRoomSize = EditorGUILayout.Vector3IntField("Max Bounds Size", maxRoomSize);
            minPorts = EditorGUILayout.IntField("Min Ports", minPorts);
            offset = EditorGUILayout.Vector3Field("Offset", offset);

            if (GUILayout.Button("Generate Random Room SO & Layout", GUILayout.Height(32)))
            {
                GenerateRandomRoom();
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Visuals & Toggles", EditorStyles.boldLabel);
            generateFloor = EditorGUILayout.Toggle("Generate Floor", generateFloor);
            generateCeiling = EditorGUILayout.Toggle("Generate Ceiling", generateCeiling);
            generateWalls = EditorGUILayout.Toggle("Generate Walls", generateWalls);

            EditorGUILayout.Space(4);
            parentOverride = (Transform)EditorGUILayout.ObjectField("Parent Override", parentOverride, typeof(Transform), true);

            EditorGUILayout.Space(8);

            bool canGenerate = roomData != null && settings != null && theme != null && roomData.RoomFootprint?.Length > 0;

            EditorGUI.BeginDisabledGroup(!canGenerate);
            if (GUILayout.Button("Regenerate Current SO Layout", GUILayout.Height(32)))
                Generate();
            EditorGUI.EndDisabledGroup();

            if (settings == null) EditorGUILayout.HelpBox("Assign DungeonSettingsSO.", MessageType.Info);
            else if (roomData == null) EditorGUILayout.HelpBox("Assign RoomDataSO.", MessageType.Info);

            EditorGUILayout.EndScrollView();
        }

        #region Random Generation Logic

        void GenerateRandomRoom()
        {
            if (settings == null || theme == null)
            {
                Debug.LogError("Settings or Theme missing!");
                return;
            }

            HashSet<Vector3Int> cells = new HashSet<Vector3Int>();
            cells.Add(Vector3Int.zero);

            int targetCellCount = UnityEngine.Random.Range(1, (maxRoomSize.x * maxRoomSize.y * maxRoomSize.z) + 1);

            List<Vector3Int> cellList = new List<Vector3Int> { Vector3Int.zero };
            for (int i = 0; i < targetCellCount; i++)
            {
                Vector3Int randomBase = cellList[UnityEngine.Random.Range(0, cellList.Count)];
                Direction randomDir = (Direction)UnityEngine.Random.Range(0, 6);
                Vector3Int neighbor = randomBase + DirectionUtils.DirectionVector(randomDir);

                if (neighbor.x >= 0 && neighbor.x < maxRoomSize.x &&
                    neighbor.y >= 0 && neighbor.y < maxRoomSize.y &&
                    neighbor.z >= 0 && neighbor.z < maxRoomSize.z)
                {
                    if (!cells.Contains(neighbor))
                    {
                        cells.Add(neighbor);
                        cellList.Add(neighbor);
                    }
                }
            }

            List<RoomDataSO.RoomPort> possiblePorts = new List<RoomDataSO.RoomPort>();
            foreach (var cell in cells)
            {
                foreach (Direction dir in Enum.GetValues(typeof(Direction)))
                {
                    if (dir == Direction.Up || dir == Direction.Down) continue;
                    if (!cells.Contains(cell + DirectionUtils.DirectionVector(dir)))
                    {
                        possiblePorts.Add(new RoomDataSO.RoomPort { localCell = cell, face = dir, type = RoomDataSO.PortType.Doorway });
                    }
                }
            }

            List<RoomDataSO.RoomPort> selectedPorts = new List<RoomDataSO.RoomPort>();
            int portCount = System.Math.Max(minPorts, UnityEngine.Random.Range(minPorts, possiblePorts.Count));
            for (int i = 0; i < portCount; i++)
            {
                int idx = UnityEngine.Random.Range(0, possiblePorts.Count);
                selectedPorts.Add(possiblePorts[idx]);
                possiblePorts.RemoveAt(idx);
            }

            RoomDataSO newRoom = ScriptableObject.CreateInstance<RoomDataSO>();
            newRoom.name = $"RndRoom_{DateTime.Now:HHmmss}";

            List<RoomDataSO.FootprintStr> footprintStrs = new List<RoomDataSO.FootprintStr>();
            foreach (var c in cells) footprintStrs.Add(new RoomDataSO.FootprintStr { Footprint = c });

            newRoom.RoomFootprint = footprintStrs.ToArray();
            newRoom.Ports = selectedPorts.ToArray();

            string folderPath = "Assets/_WS_ProceduralGeneration/GeneratedRooms";
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                string[] parts = folderPath.Split('/');
                string current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    if (!AssetDatabase.IsValidFolder(current + "/" + parts[i]))
                        AssetDatabase.CreateFolder(current, parts[i]);
                    current += "/" + parts[i];
                }
            }

            string assetPath = $"{folderPath}/{newRoom.name}.asset";
            AssetDatabase.CreateAsset(newRoom, assetPath);
            AssetDatabase.SaveAssets();

            roomData = newRoom;
            Generate();

            Debug.Log($"Generated random room: {assetPath}");
        }

        #endregion

        void Generate()
        {
            var cellSet = new HashSet<Vector3Int>();
            foreach (var entry in roomData.RoomFootprint)
                cellSet.Add(entry.Footprint);

            string rootName = $"{roomData.name}_Layout";
            var existing = GameObject.Find(rootName);
            if (existing) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject(rootName);
            Undo.RegisterCreatedObjectUndo(root, "Generate Room Layout");

            if (parentOverride != null)
                root.transform.SetParent(parentOverride, false);

            RoomData rdComponent = root.AddComponent<RoomData>();
            rdComponent.Data = roomData;

            foreach (var cell in cellSet)
            {
                Vector3 origin = CellToWorld(cell);
                Vector3 centerOffset = new Vector3(settings.cellSize * 0.5f, 0, settings.cellSize * 0.5f);

                bool hasAbove = cellSet.Contains(cell + Vector3Int.up);
                float currentHeight = hasAbove ? settings.cellSize : settings.wallHeight;

                if (generateFloor && !cellSet.Contains(cell + Vector3Int.down))
                {
                    var pb = ShapeGenerator.GeneratePlane(PivotLocation.Center, settings.cellSize, settings.cellSize, 1, 1, Axis.Up);
                    FinalizePiece(pb, root.transform, origin + centerOffset, Quaternion.identity, $"Floor_{cell}", theme.floorMaterial);
                }

                if (generateCeiling && !hasAbove)
                {
                    var pb = ShapeGenerator.GeneratePlane(PivotLocation.Center, settings.cellSize, settings.cellSize, 1, 1, Axis.Up);
                    Vector3 ceilCenter = origin + centerOffset + new Vector3(0, settings.wallHeight, 0);
                    FinalizePiece(pb, root.transform, ceilCenter, Quaternion.Euler(180, 0, 0), $"Ceiling_{cell}", theme.ceilingMaterial);
                }

                if (generateWalls)
                    GenerateWallsForCell(root.transform, cell, cellSet, currentHeight);
            }

            SyncRoomDataPorts(rdComponent);
            BakeBoundsToSO(roomData);
            PopulatePortWallsDoors(rdComponent, portWallBindings);

            Selection.activeGameObject = root;
        }

        void GenerateWallsForCell(Transform parent, Vector3Int cell, HashSet<Vector3Int> allCells, float currentHeight)
        {
            var directions = new (Vector3Int offset, Vector3 anchorOffset, Quaternion rotation, Direction dirEnum)[]
            {
            (new Vector3Int(0,0,1),  new Vector3(0, 0, settings.cellSize), Quaternion.identity,          Direction.North),
            (new Vector3Int(0,0,-1), new Vector3(settings.cellSize, 0, 0), Quaternion.Euler(0, 180, 0), Direction.South),
            (new Vector3Int(1,0,0),  new Vector3(settings.cellSize, 0, settings.cellSize), Quaternion.Euler(0, 90, 0), Direction.East),
            (new Vector3Int(-1,0,0), new Vector3(0, 0, 0), Quaternion.Euler(0, -90, 0),                 Direction.West),
            };

            Vector3 origin = CellToWorld(cell);

            foreach (var d in directions)
            {
                if (allCells.Contains(cell + d.offset)) continue;

                bool isPort = false;
                foreach (var port in roomData.Ports)
                {
                    if (port.localCell == cell && port.face == d.dirEnum) { isPort = true; break; }
                }

                Vector3 wallAnchor = origin + d.anchorOffset;

                if (isPort)
                    GenerateDoorway(parent, wallAnchor, d.rotation, cell, currentHeight);
                else
                    GenerateSolidWall(parent, wallAnchor, d.rotation, cell, currentHeight);
            }
        }

        void GenerateSolidWall(Transform parent, Vector3 anchor, Quaternion rotation, Vector3Int cell, float currentHeight)
        {
            var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, new Vector3(settings.cellSize, currentHeight, settings.wallThickness));
            Vector3 cornerPos = anchor + rotation * new Vector3(0, 0, -settings.wallThickness);
            Vector3 localCenter = new Vector3(settings.cellSize * 0.5f, currentHeight * 0.5f, settings.wallThickness * 0.5f);
            FinalizePiece(pb, parent, cornerPos + rotation * localCenter, rotation, $"Wall_{cell}", theme.wallMaterial, true);
        }

        void GenerateDoorway(Transform parent, Vector3 anchor, Quaternion rotation, Vector3Int cell, float currentHeight)
        {
            float sideWidth = (settings.cellSize - settings.doorwayWidth) * 0.5f;
            float topHeight = currentHeight - settings.doorwayHeight;
            float halfThick = settings.wallThickness * 0.5f;
            Vector3 cornerPos = anchor + rotation * new Vector3(0, 0, -settings.wallThickness);

            var pbL = ShapeGenerator.GenerateCube(PivotLocation.Center, new Vector3(sideWidth, currentHeight, settings.wallThickness));
            Vector3 centerL = new Vector3(sideWidth * 0.5f, currentHeight * 0.5f, halfThick);
            FinalizePiece(pbL, parent, cornerPos + rotation * centerL, rotation, "Door_L", theme.wallMaterial, true);

            var pbR = ShapeGenerator.GenerateCube(PivotLocation.Center, new Vector3(sideWidth, currentHeight, settings.wallThickness));
            Vector3 centerR = new Vector3(sideWidth + settings.doorwayWidth + (sideWidth * 0.5f), currentHeight * 0.5f, halfThick);
            FinalizePiece(pbR, parent, cornerPos + rotation * centerR, rotation, "Door_R", theme.wallMaterial, true);

            if (topHeight > 0.01f)
            {
                var pbT = ShapeGenerator.GenerateCube(PivotLocation.Center, new Vector3(settings.doorwayWidth, topHeight, settings.wallThickness));
                Vector3 centerT = new Vector3(sideWidth + (settings.doorwayWidth * 0.5f), settings.doorwayHeight + (topHeight * 0.5f), halfThick);
                FinalizePiece(pbT, parent, cornerPos + rotation * centerT, rotation, "Door_T", theme.wallMaterial, true);
            }
        }

        void FinalizePiece(ProBuilderMesh pb, Transform parent, Vector3 pos, Quaternion rot, string name, Material mat, bool flipInner = false)
        {
            pb.gameObject.name = name;
            pb.transform.SetParent(parent, false);
            pb.transform.position = pos + offset;
            pb.transform.rotation = rot;
            if (flipInner && pb.faces.Count > 0) pb.faces[0].Reverse();
            pb.ToMesh();
            pb.Refresh();
            var renderer = pb.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat != null ? mat : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");
            Undo.RegisterCreatedObjectUndo(pb.gameObject, "Room Piece");
        }

        Vector3 CellToWorld(Vector3Int cell) => new(cell.x * settings.cellSize, cell.y * settings.cellSize, cell.z * settings.cellSize);

        private void SyncRoomDataPorts(RoomData roomData)
        {
            SerializedObject so = new SerializedObject(roomData);
            SerializedProperty portsProp = so.FindProperty("ports");
            so.Update();

            portsProp.arraySize = roomData.Data.Ports.Length;
            for (int i = 0; i < roomData.Data.Ports.Length; i++)
            {
                SerializedProperty element = portsProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("portIndex").intValue = i;
            }

            so.ApplyModifiedProperties();
        }

        private void BakeBoundsToSO(RoomDataSO roomData)
        {
            if (roomData.RoomFootprint.Length == 0) return;

            float cellSize = settings.cellSize;
            float halfCell = cellSize * 0.5f;

            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;

            foreach (var entry in roomData.RoomFootprint)
            {
                Vector3 cellCenter = new Vector3(
                    entry.Footprint.x * cellSize,
                    entry.Footprint.y * cellSize + halfCell,
                    entry.Footprint.z * cellSize);

                Bounds cellBounds = new Bounds(cellCenter, Vector3.one * cellSize);

                if (first) { bounds = cellBounds; first = false; }
                else bounds.Encapsulate(cellBounds);
            }

            roomData.ComputedBounds = bounds;
            EditorUtility.SetDirty(roomData);
        }

        private void PopulatePortWallsDoors(RoomData roomData, PortWallBindingsSO bindings)
        {
            if (roomData.Data == null)
            {
                Debug.LogWarning("[RoomData] No RoomDataSO assigned.");
                return;
            }
            if (bindings == null || bindings.Bindings.Length == 0)
            {
                Debug.LogWarning("[RoomData] No PortBindings defined.");
                return;
            }

            Undo.SetCurrentGroupName("Populate Port Walls & Doors");
            int undoGroup = Undo.GetCurrentGroup();

            SerializedObject so = new(roomData);
            SerializedProperty portsProp = so.FindProperty("ports");
            so.Update();

            const float cellSize = 5f;
            const float halfCell = cellSize * 0.5f;
            Vector3 origin = roomData.transform.position;
            int spawned = 0;

            for (int i = 0; i < roomData.Data.Ports.Length; i++)
            {
                var port = roomData.Data.Ports[i];

                if (!bindings.TryGetBinding(port.face, port.type, out var binding))
                {
                    Debug.LogWarning($"[RoomData] No PortBinding for {port.face}/{port.type}, skipping port [{i}].");
                    continue;
                }

                Vector3 cellCenter = origin + new Vector3(
                    port.localCell.x * cellSize,
                    port.localCell.y * cellSize,
                    port.localCell.z * cellSize);

                Vector3 dir = (Vector3)DirectionUtils.DirectionVector(port.face);

                float multiplier = roomData.Data.biome switch
                {
                    //Biome.Pillar => halfCell,
                    Biome.Hallway => halfCell,
                    _ => (halfCell - 0.5f)
                };

                multiplier = port.type switch
                {
                    RoomDataSO.PortType.Continuous3x3 => halfCell,
                    _ => multiplier,
                };

                Vector3 spawnPos = cellCenter + dir * multiplier;

                SerializedProperty element = portsProp.GetArrayElementAtIndex(i);
                SerializedProperty wallProp = element.FindPropertyRelative("wall");
                SerializedProperty doorProp = element.FindPropertyRelative("door");

                if (binding.wallPrefab != null && wallProp.objectReferenceValue == null)
                {
                    GameObject wall = (GameObject)PrefabUtility.InstantiatePrefab(binding.wallPrefab, roomData.transform);
                    wall.transform.SetPositionAndRotation(spawnPos, Quaternion.identity);
                    wall.name = $"Wall_Port{i}_{port.face}";
                    Undo.RegisterCreatedObjectUndo(wall, "Spawn Wall");
                    wallProp.objectReferenceValue = wall;
                    spawned++;
                }

                if (binding.doorPrefab != null && doorProp.objectReferenceValue == null)
                {
                    GameObject door = (GameObject)PrefabUtility.InstantiatePrefab(binding.doorPrefab, roomData.transform);
                    door.transform.SetPositionAndRotation(spawnPos, Quaternion.identity);
                    door.name = $"Door_Port{i}_{port.face}";
                    Undo.RegisterCreatedObjectUndo(door, "Spawn Door");
                    doorProp.objectReferenceValue = door;
                    spawned++;
                }
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(roomData);
            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log($"[RoomData] '{roomData.name}' ? spawned {spawned} object(s) across {roomData.Data.Ports.Length} port(s).");
        }
    }
#endif
}