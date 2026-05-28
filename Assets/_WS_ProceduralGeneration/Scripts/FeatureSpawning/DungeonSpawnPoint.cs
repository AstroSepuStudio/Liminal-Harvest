using UnityEngine;

namespace WS_ProceduralGeneration
{
    public class DungeonSpawnPoint : MonoBehaviour
    {
        [SerializeField] SpawnChannel channel;

        [Header("Placement Variation")]
        public Vector3 maxOffset = Vector3.one * 0.25f;
        public float maxRotation = 0f;

        [Header("Spawn Chance")]
        [Range(0f, 100f)] public float chance = 100f;
        [Min(1)] public int tries = 1;

        [Header("Surface Snapping")]
        public bool snapToSurface = false;
        public Direction[] snapDirections = { Direction.Down };
        [Min(0.01f)] public float snapMaxDistance = 2f;
        public LayerMask snapLayers = Physics.DefaultRaycastLayers;
        public float snapSurfaceOffset = 0f;
        public bool alignToNormal = false;

        [Tooltip("Which snap direction's normal to align to. If null, all hit normals are blended together.")]
        public bool useSpecificNormalDirection = false;
        public Direction normalAlignDirection = Direction.Down;

        [Tooltip("Which local axis of the spawned prefab points 'away' from the surface. Y = standing upright on a floor, Z = back flush against a wall, X = side against a wall.")]
        public Direction prefabUpAxis = Direction.Up;

        public SpawnChannel Channel => channel;

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Color c = channel != null ? ColorForChannel(channel) : Color.grey;

            Gizmos.color = new Color(c.r, c.g, c.b, 0.85f);
            Gizmos.DrawWireCube(transform.position, maxOffset * 2f);

            Gizmos.color = new Color(c.r, c.g, c.b, 0.4f);
            Gizmos.DrawSphere(transform.position, 0.08f);

            if (maxRotation > 0f)
            {
                UnityEditor.Handles.color = new Color(c.r, c.g, c.b, 0.5f);
                UnityEditor.Handles.DrawWireArc(
                    transform.position,
                    Vector3.up,
                    Quaternion.Euler(0f, -maxRotation, 0f) * transform.forward,
                    maxRotation * 2f,
                    0.3f);
            }

            if (snapToSurface && snapDirections != null)
            {
                foreach (var dir in snapDirections)
                {
                    Vector3 rayDir = (Vector3)DirectionUtils.DirectionVector(dir);
                    bool hit = Physics.Raycast(transform.position, rayDir,
                        out RaycastHit hitInfo, snapMaxDistance, snapLayers);

                    Gizmos.color = hit ? Color.green : new Color(1f, 0.5f, 0f, 0.8f);
                    Gizmos.DrawRay(transform.position, rayDir * snapMaxDistance);

                    if (hit)
                    {
                        Gizmos.color = Color.green;
                        Gizmos.DrawSphere(hitInfo.point, 0.06f);

                        if (alignToNormal)
                        {
                            UnityEditor.Handles.color = new Color(0f, 1f, 0.5f, 0.7f);
                            UnityEditor.Handles.ArrowHandleCap(0,
                                hitInfo.point,
                                Quaternion.LookRotation(hitInfo.normal),
                                0.25f, EventType.Repaint);
                        }
                    }
                }
            }
        }

        static Color ColorForChannel(SpawnChannel ch)
        {
            int h = ch.name.GetHashCode();
            return Color.HSVToRGB(Mathf.Abs(h % 1000) / 1000f, 0.8f, 0.9f);
        }
#endif
    }
}
