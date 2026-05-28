using System;
using UnityEngine;

namespace WS_ProceduralGeneration
{
    [CreateAssetMenu(menuName = "LethalLive/Furniture")]
    public class FurnitureDataSO : ScriptableObject, IHaveTier
    {
        public GameObject Prefab;

        public WSDG_Tier.Tier Tier;
        public WSDG_Tier.Tier GetTier() => Tier;

        public ItemDropThreshold[] dropThresholds;
        public ItemDrop[] lootTable;

        [Serializable]
        public struct ItemDrop
        {
            public ItemSO Item;
            public float dropChance;
            public int minQuantity;
            public int maxQuantity;
        }

        [Serializable]
        public struct ItemDropThreshold
        {
            public ItemDrop Item_Drop;
            public float dropThreshold; // Ex: 50% (currentHP)
            public bool triggered;
        }
    }
}

