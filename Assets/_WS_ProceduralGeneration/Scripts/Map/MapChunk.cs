using UnityEngine;

namespace WS_ProceduralGeneration
{
    public class MapChunk : MonoBehaviour
    {
        public Canvas chunkCanvas;
        public RectTransform rectTransform;
        public Vector2Int chunkCoord;

        public void SetVisible(bool visible) => chunkCanvas.enabled = visible;
    }
}
