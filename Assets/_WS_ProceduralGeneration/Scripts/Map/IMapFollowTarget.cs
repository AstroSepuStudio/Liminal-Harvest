using UnityEngine;

namespace WS_ProceduralGeneration
{
    public interface IMapFollowTarget
    {
        Transform FollowTransform { get; }
        bool IsAvailable { get; }
    }
}
