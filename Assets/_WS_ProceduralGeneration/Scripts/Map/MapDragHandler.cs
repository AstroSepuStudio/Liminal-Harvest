using UnityEngine;
using UnityEngine.EventSystems;
using WS_ProceduralGeneration;

namespace WS_ProceduralGeneration
{
    public class MapDragHandler : MonoBehaviour, IDragHandler, IPointerDownHandler
    {
        [SerializeField] RectTransform mapTarget;
        [SerializeField] DNG_MapModule mapModule;

        float _snapshotTimer;
        const float SnapshotInterval = 0.2f;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!mapModule.IsLocalPlayerController) return;

            if (mapModule.IsFollowingPlayer)
                mapModule.ToggleFollowPlayer();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!mapModule.IsLocalPlayerController) return;

            mapTarget.anchoredPosition += eventData.delta;

            _snapshotTimer += Time.deltaTime;
            if (_snapshotTimer >= SnapshotInterval)
            {
                _snapshotTimer = 0f;
                mapModule.CmdSnapshotMapAnchor(GameManager.Instance.playMod.LocalPlayer.Index, mapTarget.anchoredPosition);
            }
        }
    }
}
