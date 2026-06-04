using System;
using System.Collections.Generic;
using UnityEngine;

namespace WS_ProceduralGeneration
{
    [CreateAssetMenu(menuName = "DungeonGen/RoomData")]
    public class RoomDataSO : ScriptableObject
    {
        public enum PortType { Doorway, Continuous3x3, Continuous3x3Edge, Hole }
        public enum AxisConstraint { Any, Center, Positive, Negative, Edges }

        [System.Serializable]
        public struct RoomConstraints
        {
            public AxisConstraint x;
            public AxisConstraint y;
            public AxisConstraint z;

            [Min(-1), Tooltip("-1 = unlimited")]
            public int maxSpawns;

            [Min(0)]
            public int minDepth;
        }

        [Serializable]
        public struct RoomPort
        {
            public Vector3Int localCell;
            public Direction face;
            public PortType type;

            public Sprite deadEndOverride;
            public Vector3Int deoCell;
        }

        [Serializable]
        public struct FootprintStr
        {
            public Vector3Int Footprint;
            public Sprite MapSprite;
            public MapSpriteDBSO.LayerDir layerDir;
        }

        [Header("Configuration")]
        public GameObject Prefab;
        public Biome biome = Biome.Default;
        public WSDG_Tier.Tier RoomTier;
        public bool ValidVortexHome = true;
        public RoomConstraints constraints = new()
        {
            x = AxisConstraint.Any,
            y = AxisConstraint.Any,
            z = AxisConstraint.Any,
            maxSpawns = -1,
            minDepth = 0
        };

        [Header("Data")]
        public Color roomColor = Color.white;
        public FootprintStr[] RoomFootprint = Array.Empty<FootprintStr>();
        public RoomPort[] Ports = Array.Empty<RoomPort>();

        public FootprintStr[] FootprintCorners = Array.Empty<FootprintStr>();
        public Bounds ComputedBounds;

        public bool ContainsLocalCell(in Vector3Int local)
        {
            for (int i = 0; i < RoomFootprint.Length; i++) if (RoomFootprint[i].Footprint == local) return true;
            return false;
        }

        public static bool SatisfiesAxisConstraint(AxisConstraint constraint, int cell, int gridSize, int padding = 0)
        {
            return constraint switch
            {
                AxisConstraint.Any => true,
                AxisConstraint.Center => cell == gridSize / 2,
                AxisConstraint.Positive => cell >= gridSize - 1 - padding,
                AxisConstraint.Negative => cell <= padding,
                AxisConstraint.Edges => cell <= padding || cell >= gridSize - 1 - padding,
                _ => true
            };
        }

#if UNITY_EDITOR
        [ContextMenu("Populate Footprint From Corners")]
        public void PopulateFootprintFromCorners()
        {
            if (FootprintCorners.Length % 2 != 0)
            {
                Debug.LogWarning("footprintCorners must have an even number of entries (pairs of corners).");
                return;
            }

            var result = new List<FootprintStr>();

            for (int i = 0; i < FootprintCorners.Length; i += 2)
            {
                FootprintStr cornerA = FootprintCorners[i];
                FootprintStr cornerB = FootprintCorners[i + 1];

                Vector3Int min = Vector3Int.Min(cornerA.Footprint, cornerB.Footprint);
                Vector3Int max = Vector3Int.Max(cornerA.Footprint, cornerB.Footprint);

                for (int x = min.x; x <= max.x; x++)
                    for (int y = min.y; y <= max.y; y++)
                        for (int z = min.z; z <= max.z; z++)
                        {
                            var cell = new Vector3Int(x, y, z);

                            result.Add(new FootprintStr
                            {
                                Footprint = cell
                            });
                        }
            }

            RoomFootprint = result.ToArray();
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"Populated {RoomFootprint.Length} footprint cells from {FootprintCorners.Length / 2} corner pair(s).");
        }
#endif
    }
}