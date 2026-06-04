#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;

namespace WS_ProceduralGeneration
{
    [CustomEditor(typeof(RoomData))]
    public class RoomDataEditor : Editor
    {
        private PortWallBindingsSO _bindingsSO;
        private const string BindingsSOPath = "Assets/_WS_ProceduralGeneration/Ports/Port Bindings SO.asset";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            RoomData roomData = (RoomData)target;

            if (_bindingsSO == null) _bindingsSO = AssetDatabase.LoadAssetAtPath<PortWallBindingsSO>(BindingsSOPath);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Port Tools", EditorStyles.boldLabel);

            _bindingsSO = (PortWallBindingsSO)EditorGUILayout.ObjectField(
                "Port Bindings SO",
                _bindingsSO,
                typeof(PortWallBindingsSO),
                allowSceneObjects: false);

            if (GUILayout.Button("Refresh Room Lights"))
            {
                Undo.RecordObject(roomData, "Refresh Room Lights");
                roomData.roomLights = roomData.GetComponentsInChildren<LED_Light>(includeInactive: true);
                EditorUtility.SetDirty(roomData);
            }

            if (GUILayout.Button("Refresh Room Renderers"))
            {
                Undo.RecordObject(roomData, "Refresh Room Renderers");
                roomData.roomRenderers = roomData.GetComponentsInChildren<Renderer>(includeInactive: true);
                EditorUtility.SetDirty(roomData);
            }

            if (GUILayout.Button("Refresh Spawn Positions"))
            {
                Undo.RecordObject(roomData, "Refresh Spawn Positions");
                roomData.SpawnPoints = new System.Collections.Generic.List<DungeonSpawnPoint>(
                    roomData.GetComponentsInChildren<DungeonSpawnPoint>(includeInactive: true));

                Debug.Log($"[RoomData] '{roomData.name}' ? found {roomData.SpawnPoints.Count} spawn Positions");
            }

            if (GUILayout.Button("Sync Ports from RoomDataSO"))
            {
                SyncPorts(roomData);
            }

            if (GUILayout.Button("Bake Footprint Bounds"))
            {
                BakeFootprintBounds(roomData);
            }

            EditorGUI.BeginDisabledGroup(_bindingsSO == null || roomData.Data == null);
            if (GUILayout.Button("Populate Port Walls & Doors"))
                PopulatePortWallsDoors(roomData, _bindingsSO);
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(roomData.Data == null);
            if (GUILayout.Button("Toggle Port Objects"))
                TogglePortObjects(roomData);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Rotation Tools", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(roomData.Data == null);
            if (GUILayout.Button("Rotate 90° Clockwise (Y)"))
                RotateClockwise90(roomData, _bindingsSO);
            EditorGUI.EndDisabledGroup();
        }

        private void SyncPorts(RoomData roomData)
        {
            SerializedObject so = new(roomData);
            SerializedProperty portsProp = so.FindProperty("ports");
            RoomDataSO.RoomPort[] sourcePorts = roomData.Data.Ports;

            so.Update();
            portsProp.arraySize = sourcePorts.Length;

            for (int i = 0; i < sourcePorts.Length; i++)
            {
                SerializedProperty element = portsProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("portIndex").intValue = i;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(roomData);
            Debug.Log($"[RoomData] '{roomData.name}' ? synced {sourcePorts.Length} port(s) from '{roomData.Data.name}'.");
        }

        private void BakeFootprintBounds(RoomData roomData)
        {
            if (roomData.Data == null || roomData.Data.RoomFootprint.Length == 0)
            {
                Debug.LogWarning("[RoomData] No footprint data to bake.");
                return;
            }

            const float cellSize = 5f;
            const float halfCell = cellSize * 0.5f;

            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;

            foreach (var entry in roomData.Data.RoomFootprint)
            {
                Vector3 cellCenter = new Vector3(
                    entry.Footprint.x * cellSize + halfCell,
                    entry.Footprint.y * cellSize + halfCell,
                    entry.Footprint.z * cellSize + halfCell);

                Bounds cellBounds = new Bounds(cellCenter, Vector3.one * cellSize);

                if (first) { bounds = cellBounds; first = false; }
                else bounds.Encapsulate(cellBounds);
            }

            Undo.RecordObject(roomData.Data, "Bake Footprint Bounds");
            roomData.Data.ComputedBounds = bounds;
            EditorUtility.SetDirty(roomData.Data);

            Debug.Log($"[RoomData] '{roomData.Data.name}' ? baked bounds: center={bounds.center}, size={bounds.size}");
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
                    port.localCell.x * cellSize + halfCell,
                    port.localCell.y * cellSize,
                    port.localCell.z * cellSize + halfCell);

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

                multiplier = port.face switch
                {
                    Direction.Up => cellSize - 0.5f,
                    Direction.Down => -0.5f,
                    _ => multiplier
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

        private void TogglePortObjects(RoomData roomData)
        {
            SerializedObject so = new(roomData);
            SerializedProperty portsProp = so.FindProperty("ports");
            so.Update();

            bool? currentState = null;
            for (int i = 0; i < portsProp.arraySize; i++)
            {
                var element = portsProp.GetArrayElementAtIndex(i);
                var wall = element.FindPropertyRelative("wall").objectReferenceValue as GameObject;
                var door = element.FindPropertyRelative("door").objectReferenceValue as GameObject;

                if (wall != null) { currentState = wall.activeSelf; break; }
                if (door != null) { currentState = door.activeSelf; break; }
            }

            if (currentState == null) return;
            bool newState = !currentState.Value;

            for (int i = 0; i < portsProp.arraySize; i++)
            {
                var element = portsProp.GetArrayElementAtIndex(i);
                var wall = element.FindPropertyRelative("wall").objectReferenceValue as GameObject;
                var door = element.FindPropertyRelative("door").objectReferenceValue as GameObject;

                if (wall != null) { Undo.RecordObject(wall, "Toggle Port Objects"); wall.SetActive(newState); }
                if (door != null) { Undo.RecordObject(door, "Toggle Port Objects"); door.SetActive(newState); }
            }

            EditorUtility.SetDirty(roomData);
        }

        private void RotateClockwise90(RoomData roomData, PortWallBindingsSO bindings)
        {
            if (roomData.Data == null) return;

            Undo.SetCurrentGroupName("Rotate Room 90° Clockwise");
            int undoGroup = Undo.GetCurrentGroup();

            Undo.RecordObject(roomData.Data, "Rotate Footprint");

            var newFootprint = new RoomDataSO.FootprintStr[roomData.Data.RoomFootprint.Length];
            for (int i = 0; i < roomData.Data.RoomFootprint.Length; i++)
            {
                var fp = roomData.Data.RoomFootprint[i];
                fp.Footprint = RotateCellCW(fp.Footprint);
                newFootprint[i] = fp;
            }
            roomData.Data.RoomFootprint = newFootprint;

            if (roomData.Data.FootprintCorners != null)
            {
                var newCorners = new RoomDataSO.FootprintStr[roomData.Data.FootprintCorners.Length];
                for (int i = 0; i < roomData.Data.FootprintCorners.Length; i++)
                {
                    var fp = roomData.Data.FootprintCorners[i];
                    fp.Footprint = RotateCellCW(fp.Footprint);
                    newCorners[i] = fp;
                }
                roomData.Data.FootprintCorners = newCorners;
            }

            var newPorts = new RoomDataSO.RoomPort[roomData.Data.Ports.Length];
            for (int i = 0; i < roomData.Data.Ports.Length; i++)
            {
                var port = roomData.Data.Ports[i];
                port.localCell = RotateCellCW(port.localCell);
                port.face = RotateDirectionCW(port.face);
                if (port.deadEndOverride != null)
                    port.deoCell = RotateCellCW(port.deoCell);
                newPorts[i] = port;
            }
            roomData.Data.Ports = newPorts;

            EditorUtility.SetDirty(roomData.Data);

            Transform pivot = roomData.rotationAnchor != null
                ? roomData.rotationAnchor
                : roomData.transform;

            Undo.RecordObject(pivot, "Rotate Model");
            pivot.Rotate(Vector3.up, 90f);

            SerializedObject so = new(roomData);
            SerializedProperty portsProp = so.FindProperty("ports");
            so.Update();

            for (int i = 0; i < portsProp.arraySize; i++)
            {
                var element = portsProp.GetArrayElementAtIndex(i);

                var wall = element.FindPropertyRelative("wall").objectReferenceValue as GameObject;
                if (wall != null)
                {
                    Undo.DestroyObjectImmediate(wall);
                    element.FindPropertyRelative("wall").objectReferenceValue = null;
                }

                var door = element.FindPropertyRelative("door").objectReferenceValue as GameObject;
                if (door != null)
                {
                    Undo.DestroyObjectImmediate(door);
                    element.FindPropertyRelative("door").objectReferenceValue = null;
                }
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(roomData);

            if (bindings != null)
                PopulatePortWallsDoors(roomData, bindings);
            else
                Debug.LogWarning("[RoomData] No PortBindingsSO assigned — skipping port wall repopulation.");

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[RoomData] '{roomData.name}' rotated 90° clockwise.");
        }

        static Vector3Int RotateCellCW(Vector3Int cell) => new(cell.z, cell.y, -cell.x);

        static Direction RotateDirectionCW(Direction d) => d switch
        {
            Direction.North => Direction.East,
            Direction.East => Direction.South,
            Direction.South => Direction.West,
            Direction.West => Direction.North,
            _ => d
        };
    }
#endif
}