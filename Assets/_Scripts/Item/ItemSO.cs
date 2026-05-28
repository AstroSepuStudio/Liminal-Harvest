using UnityEngine;
using WS_ProceduralGeneration;

[CreateAssetMenu(menuName = "LethalLive/Item")]
public class ItemSO : ScriptableObject, IHaveTier
{
    public enum ItemAnimationType { Default, OneHanded, TwoHanded, Crowbar, Sword }

    public GameObject itemPrefab;
    public WSDG_Tier.Tier Tier;
    public ItemAnimationType animationType;
    public AudioSFX[] DropSFX;
    public AudioSFX[] EquipSFX;

    public WSDG_Tier.Tier GetTier() => Tier;

    public string itemName;
    public Sprite icon;
    public bool isTwoHanded;
    public bool isSellable;
    public int minValue = 10;
    public int maxValue = 100;
    public bool hasPrimaryAction = false;
    public bool hasSecondaryAction = false;
    public bool pickable = true;
    public bool droppable = true;
    public bool stackable = false;

    public Vector3 gOffset;
    public Vector3 gRotation;
}

public interface IStackable
{
    void SetQuantity(int amount);
}
