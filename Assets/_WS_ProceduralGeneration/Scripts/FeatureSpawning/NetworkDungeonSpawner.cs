using Mirror;
using UnityEngine;

namespace WS_ProceduralGeneration
{
    public abstract class NetworkDungeonSpawner : DungeonSpawner
    {
        protected bool IsServer => NetworkServer.active;

        public override void Clear()
        {
            if (!IsServer) return;
            base.Clear();
        }

        protected GameObject NetworkSpawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (!NetworkServer.active)
                return Spawn(prefab, position, rotation, parent);

            if (!IsServer) return null;

            GameObject go = Instantiate(prefab, position, rotation, parent);
            NetworkServer.Spawn(go);
            return go;
        }

        protected void DestroyChildren(Transform parent, bool hasNetID, bool isServer)
        {
            if (parent == null) return;
            if (hasNetID && !isServer) return;

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                if (hasNetID) NetworkServer.Destroy(child);
                else Destroy(child);
            }
        }
    }
}
