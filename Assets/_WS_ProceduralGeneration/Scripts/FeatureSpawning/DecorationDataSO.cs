using UnityEngine;

namespace WS_ProceduralGeneration
{
    [CreateAssetMenu(menuName = "LethalLive/Decoration")]
    public class DecorationDataSO : ScriptableObject
    {
        public WSDG_Tier.Tier Tier;
        public ThemeDataSO.SpawnableSize Size;
        public GameObject Prefab;
    }
}
