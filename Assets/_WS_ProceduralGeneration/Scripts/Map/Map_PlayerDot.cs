using UnityEngine;
using UnityEngine.UI;

namespace WS_ProceduralGeneration
{
    public class Map_PlayerDot : MonoBehaviour
    {
        [SerializeField] Image iconImg;

        public void SetPlayerIcon(Sprite avatar)
        {
            iconImg.sprite = avatar;
        }
    }
}
