using System;
using UnityEngine;

namespace WS_ProceduralGeneration
{
    [CreateAssetMenu(menuName = "LethalLive/PortWallBindings")]
    public class PortWallBindingsSO : ScriptableObject
    {
        [Serializable]
        public struct PortWallBinding
        {
            public Direction direction;
            public RoomDataSO.PortType portType;
            public GameObject wallPrefab;
            public GameObject doorPrefab;
        }

        public PortWallBinding[] Bindings;

        public bool TryGetBinding(Direction direction, RoomDataSO.PortType portType, out PortWallBinding result)
        {
            foreach (var b in Bindings)
            {
                if (b.direction == direction && b.portType == portType)
                {
                    result = b;
                    return true;
                }
            }
            result = default;
            return false;
        }
    }
}