using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace WS_ProceduralGeneration
{
    public class RoomData : MonoBehaviour
    {
        [Serializable]
        public struct WallPortKey
        {
            public int portIndex;
            public GameObject wall;
            public GameObject door;
        }

        [SerializeField] WallPortKey[] ports = Array.Empty<WallPortKey>();
        public RoomDataSO Data;
        public DungeonGenerator.PlacedRoom PlacedRoom;

        public List<DungeonSpawnPoint> SpawnPoints = new();

        public Renderer[] roomRenderers;
        public LED_Light[] roomLights;

        public Transform rotationAnchor;
        [SerializeField] bool beingRendered = true;

        [Header("Gizmo Settings")]
        [SerializeField] bool showFootprint = false;
        [SerializeField] bool showPorts = false;
        [SerializeField] bool showBounds = false;
        [SerializeField] bool showFootprintCorners = false;
        [SerializeField] bool showFootprintCoords = false;
        [SerializeField] bool showFootprintSprites = false;
        [SerializeField] Color fontColor = Color.red;
        [SerializeField] int fontSize = 25;
        [SerializeField] Color spriteColor = Color.green;
        [SerializeField] int spriteSize = 150;
        [SerializeField] bool useLayers = false;
        [SerializeField] int currentLayer = 0;

        public readonly List<int> closedPorts = new();
        bool overrideLightState = false;

        void Start()
        {
            roomRenderers = roomRenderers.Where(r => r != null && r.enabled).ToArray();
        }

        public void SetPort(int portIndex, bool open)
        {
            if (!open) closedPorts.Add(portIndex);
            if (portIndex < 0 || portIndex >= ports.Length) return;
            if (ports[portIndex].wall) ports[portIndex].wall.SetActive(!open);
            if (ports[portIndex].door) ports[portIndex].door.SetActive(open);
        }

        public void SetRender(bool shouldRender)
        {
            if (beingRendered == shouldRender || overrideLightState) return;
            beingRendered = shouldRender;
            foreach (var r in roomRenderers) r.enabled = shouldRender;
            foreach (var l in roomLights) l.RenderLight(shouldRender);
        }

        public void OverrideLightRenderState(bool shouldRender)
        {
            overrideLightState = shouldRender;

            foreach (var l in roomLights)
            {
                if (l == null)
                {
                    Debug.Log("[RoomData] Null reference on roomlights", this);
                    continue;
                }
                l.RenderLight(shouldRender);
            }
        }

        public Vector3 GetRandomPositionInRoom(float yOffset = 0f)
        {
            var footprint = PlacedRoom.data.RoomFootprint;
            var entry = footprint[UnityEngine.Random.Range(0, footprint.Length)];
            float cellSize = DungeonGenerator.Instance.CellSize;

            Vector3 cellOrigin = transform.position + new Vector3(
                entry.Footprint.x * cellSize,
                entry.Footprint.y * cellSize + yOffset,
                entry.Footprint.z * cellSize);

            float half = cellSize * 0.35f;
            float x = UnityEngine.Random.Range(-half, half);
            float z = UnityEngine.Random.Range(-half, half);
            Vector3 candidate = cellOrigin + new Vector3(x, 0f, z);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, half, NavMesh.AllAreas))
                return hit.position;
            if (NavMesh.SamplePosition(cellOrigin, out NavMeshHit centerHit, half, NavMesh.AllAreas))
                return centerHit.position;

            return candidate;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (Data == null || Data.RoomFootprint == null) return;

            Vector3 origin = transform.position;
            const float cellSize = 5f;

            if (showPorts)
            {
                for (int pi = 0; pi < Data.Ports.Length; pi++)
                {
                    var port = Data.Ports[pi];
                    if (useLayers && port.localCell.y != currentLayer) continue;

                    Vector3 dir = (Vector3)DirectionUtils.DirectionVector(port.face);
                    Vector3 cellCenter = origin + new Vector3(
                        port.localCell.x * cellSize + cellSize * 0.5f,
                        port.localCell.y * cellSize + cellSize * 0.5f,
                        port.localCell.z * cellSize + cellSize * 0.5f);

                    Vector3 faceCentre = cellCenter + dir * (cellSize * 0.5f);

                    Gizmos.color = DirectionColor(port.face);
                    Vector3 right = Vector3.Cross(dir, Vector3.up);
                    if (right == Vector3.zero) right = Vector3.right;
                    Vector3 up = Vector3.Cross(right, dir);
                    float half = cellSize * 0.45f;
                    Vector3 tl = faceCentre + (-right + up) * half;
                    Vector3 tr = faceCentre + (right + up) * half;
                    Vector3 br = faceCentre + (right - up) * half;
                    Vector3 bl = faceCentre + (-right - up) * half;
                    Gizmos.DrawLine(tl, tr); Gizmos.DrawLine(tr, br);
                    Gizmos.DrawLine(br, bl); Gizmos.DrawLine(bl, tl);

                    UnityEditor.Handles.color = Gizmos.color;
                    UnityEditor.Handles.ArrowHandleCap(0, faceCentre,
                        Quaternion.LookRotation(dir), cellSize * 0.35f, EventType.Repaint);

                    UnityEditor.Handles.Label(
                        faceCentre + dir * (cellSize * 0.4f) + Vector3.up * 0.3f,
                        $"[{pi}] {port.face} {port.localCell}",
                        new GUIStyle { normal = { textColor = DirectionColor(port.face) }, fontSize = 25, alignment = TextAnchor.MiddleCenter });

                    bool wired = false;
                    foreach (var wpk in ports)
                    {
                        if (wpk.portIndex != pi) continue;
                        wired = true;
                        string wallStr = wpk.wall != null ? $"W: {wpk.wall.name}" : "W: -";
                        string doorStr = wpk.door != null ? $"D: {wpk.door.name}" : "D: -";
                        bool incomplete = wpk.wall == null || wpk.door == null;
                        UnityEditor.Handles.Label(
                            faceCentre + dir * (cellSize * 0.4f) + Vector3.up * -0.5f,
                            $"{wallStr} | {doorStr}",
                            new GUIStyle { normal = { textColor = incomplete ? Color.yellow : Color.green }, fontSize = 20, alignment = TextAnchor.MiddleCenter });
                        break;
                    }

                    if (!wired)
                        UnityEditor.Handles.Label(
                            faceCentre + dir * (cellSize * 0.4f) + Vector3.up * -0.5f,
                            "NOT WIRED",
                            new GUIStyle { normal = { textColor = Color.red }, fontSize = 20, alignment = TextAnchor.MiddleCenter });

                    if (port.deadEndOverride != null)
                    {
                        Texture2D tex = UnityEditor.AssetPreview.GetAssetPreview(port.deadEndOverride)
                                     ?? port.deadEndOverride.texture;
                        if (tex != null)
                        {
                            UnityEditor.Handles.BeginGUI();
                            Vector2 guiCenter = UnityEditor.HandleUtility.WorldToGUIPoint(faceCentre);
                            Rect guiRect = new Rect(guiCenter.x - spriteSize * 0.5f, guiCenter.y - spriteSize * 0.5f, spriteSize, spriteSize);
                            GUI.DrawTexture(guiRect, tex, ScaleMode.ScaleToFit, true);
                            UnityEditor.Handles.EndGUI();
                        }
                    }
                }
            }

            if (Data.ComputedBounds.size != Vector3.zero && showBounds)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
                Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
                Gizmos.DrawWireCube(Data.ComputedBounds.center, Data.ComputedBounds.size);
                Gizmos.matrix = Matrix4x4.identity;
            }

            foreach (var entry in Data.RoomFootprint)
            {
                if (useLayers && entry.Footprint.y != currentLayer) continue;

                Vector3 cellCenter = origin + new Vector3(
                    entry.Footprint.x * cellSize + cellSize * 0.5f,
                    entry.Footprint.y * cellSize + cellSize * 0.5f,
                    entry.Footprint.z * cellSize + cellSize * 0.5f);

                if (showFootprint)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawWireCube(cellCenter, Vector3.one * cellSize);
                }

                if (showFootprintSprites && entry.MapSprite != null)
                {
                    Texture2D tex = UnityEditor.AssetPreview.GetAssetPreview(entry.MapSprite)
                                 ?? entry.MapSprite.texture;
                    if (tex != null)
                    {
                        UnityEditor.Handles.BeginGUI();
                        Vector2 guiCenter = UnityEditor.HandleUtility.WorldToGUIPoint(cellCenter);
                        Rect guiRect = new Rect(guiCenter.x - spriteSize * 0.5f, guiCenter.y - spriteSize * 0.5f, spriteSize, spriteSize);
                        GUI.color = spriteColor;
                        GUI.DrawTexture(guiRect, tex, ScaleMode.ScaleToFit, true);
                        UnityEditor.Handles.EndGUI();
                    }
                }

                if (showFootprintCoords)
                    UnityEditor.Handles.Label(
                        cellCenter + Vector3.up * 0.25f,
                        $"{entry.Footprint.x},{entry.Footprint.y},{entry.Footprint.z}",
                        new GUIStyle { normal = { textColor = fontColor }, fontSize = fontSize, alignment = TextAnchor.MiddleCenter });
            }

            if (showFootprintCorners && Data.FootprintCorners != null)
            {
                foreach (var entry in Data.FootprintCorners)
                {
                    if (useLayers && entry.Footprint.y != currentLayer) continue;
                    Vector3 worldCenter = origin + new Vector3(
                        entry.Footprint.x * cellSize + cellSize * 0.5f,
                        entry.Footprint.y * cellSize + cellSize * 0.5f,
                        entry.Footprint.z * cellSize + cellSize * 0.5f);
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireCube(worldCenter, Vector3.one * cellSize * 0.4f);
                }
            }
        }

        static Color DirectionColor(Direction d) => d switch
        {
            Direction.North => Color.blue,
            Direction.South => Color.red,
            Direction.East => Color.green,
            Direction.West => Color.yellow,
            Direction.Up => Color.cyan,
            Direction.Down => Color.magenta,
            _ => Color.white
        };
#endif
    }
}