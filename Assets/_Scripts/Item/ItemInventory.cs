using Mirror;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class ItemInventory : NetworkBehaviour
{
    [SerializeField] PlayerData pData;
    [SerializeField] CanvasGroup nameGroup;
    [SerializeField] TextMeshProUGUI itemName;
    [SerializeField] HotbarSlot[] hotbarSlots;

    bool onDelay = false;
    readonly WaitForSeconds actionDelay = new(0.05f);
    IEnumerator ActionDelayCor() { onDelay = true; yield return actionDelay; onDelay = false; }
    void ActionTriggered() => StartCoroutine(ActionDelayCor());

    [SyncVar(hook = nameof(OnSelectedSlotChanged))]
    public int selectedSlotIndex = 0;

    ItemBase[] inventorySlots = new ItemBase[4];
    ItemBase equippedItem;

    WaitForSeconds sleepNameDisplay = new(1f);
    Coroutine nameDisplayCoroutine;

    public ItemBase EquippedItem => equippedItem;
    public bool ItemEquipped => equippedItem != null;
    public bool HasPrimaryAction => equippedItem != null && equippedItem.ItemData.hasPrimaryAction;
    public bool HasSecondaryAction => equippedItem != null && equippedItem.ItemData.hasSecondaryAction;
    public bool HasTwoHandedEquipped => equippedItem != null && equippedItem.ItemData.isTwoHanded;
    public bool IsItemInUse => equippedItem != null && equippedItem.InUse;

    public override void OnStartClient()
    {
        base.OnStartClient();
        hotbarSlots[selectedSlotIndex].SelectSlot();
    }

    public bool IsFull()
    {
        foreach (var slot in inventorySlots)
        {
            if (slot == null)
                return false;
        }
        return true;
    }

    #region Input

    public void PrimaryInput(InputAction.CallbackContext context)
    {
        if (context.started) CmdUseAction(true);
        else if (context.canceled) CmdCancelAction(true);
    }

    public void SecondaryInput(InputAction.CallbackContext context)
    {
        if (context.started) CmdUseAction(false);
        else if (context.canceled) CmdCancelAction(false);
    }

    public void OnClientDrop(InputAction.CallbackContext context)
    {
        if (!pData.isLocalPlayer) return;
        if (!context.started) return;
        CmdDropItem();
    }

    public void SelectNextSlot(InputAction.CallbackContext context)
    {
        if (!context.started) return;
        CmdSelectNextSlot();
    }

    public void SelectSlot(InputAction.CallbackContext context, int index)
    {
        if (!context.started) return;
        CmdSelectSlot(index);
    }

    public void SelectSlot_00(InputAction.CallbackContext context) => SelectSlot(context, 0);
    public void SelectSlot_01(InputAction.CallbackContext context) => SelectSlot(context, 1);
    public void SelectSlot_02(InputAction.CallbackContext context) => SelectSlot(context, 2);
    public void SelectSlot_03(InputAction.CallbackContext context) => SelectSlot(context, 3);
    public void SelectSlot_04(InputAction.CallbackContext context) => SelectSlot(context, 4);
    public void SelectSlot_05(InputAction.CallbackContext context) => SelectSlot(context, 5);
    public void SelectSlot_06(InputAction.CallbackContext context) => SelectSlot(context, 6);
    public void SelectSlot_07(InputAction.CallbackContext context) => SelectSlot(context, 7);
    public void SelectSlot_08(InputAction.CallbackContext context) => SelectSlot(context, 8);
    public void SelectSlot_09(InputAction.CallbackContext context) => SelectSlot(context, 9);

    #endregion

    #region Add / Remove Items

    [Server]
    public bool AddItem(ItemBase item)
    {
        if (pData._LockPlayer) return false;
        if (onDelay) return false;
        if (pData.Player_Stats.dead || pData.Player_Stats.knocked) return false;
        if (equippedItem != null && equippedItem.ItemData.isTwoHanded) return false;
        if (IsFull()) return false;
        if (item == null) return false;

        ActionTriggered();

        int index = -1;
        for (int i = 0; i < inventorySlots.Length; i++)
        {
            if (inventorySlots[i] == null)
            {
                index = i;
                break;
            }
        }
        if (index == -1) return false;

        item.HasOwner = true;
        inventorySlots[index] = item;

        bool shouldSelect = equippedItem == null || !equippedItem.InUse;

        if (shouldSelect && index != selectedSlotIndex)
            selectedSlotIndex = index;

        RpcAddItem(item.ID, index, shouldSelect);
        return true;
    }

    [ClientRpc]
    void RpcAddItem(uint itemID, int slotIndex, bool shouldSelect)
    {
        NetworkClient.spawned.TryGetValue(itemID, out NetworkIdentity netIdentity);
        if (netIdentity == null) return;

        var item = netIdentity.gameObject.GetComponent<ItemBase>();
        inventorySlots[slotIndex] = item;
        item.OnPickUp();
        hotbarSlots[slotIndex].SetItem(item);

        if (shouldSelect)
            EquipItemFromSlot(slotIndex);
    }

    public void RemoveCurrentItem()
    {
        if (equippedItem == null) return;
        if (onDelay) return;

        ActionTriggered();

        int slot = selectedSlotIndex;
        uint id = equippedItem.ID;
        equippedItem.HasOwner = false;
        inventorySlots[slot] = null;
        equippedItem = null;
        SetEquipAnimState(null);

        int next = slot;
        for (int i = 1; i < inventorySlots.Length; i++)
        {
            int candidate = (slot + i) % inventorySlots.Length;
            if (inventorySlots[candidate] != null)
            {
                next = candidate;
                break;
            }
        }

        if (next != slot)
            selectedSlotIndex = next;

        Debug.Log("[Inventory] Remove current item");
        RpcDropItem(slot, id);
    }

    [Server]
    public void DropEverything()
    {
        foreach (var slot in inventorySlots)
            if (slot != null) slot.HasOwner = false;

        RpcDropAllItems();
    }

    [Server]
    public void DestroyAllItems()
    {
        for (int i = 0; i < inventorySlots.Length; i++)
        {
            if (inventorySlots[i] == null) continue;

            ItemBase item = inventorySlots[i];
            inventorySlots[i] = null;
            NetworkServer.Destroy(item.gameObject);
        }

        if (equippedItem != null)
        {
            NetworkServer.Destroy(equippedItem.gameObject);
            equippedItem = null;
        }

        RpcOnAllItemsDestroyed();
    }

    #endregion

    #region Drop Commands

    [Command]
    void CmdDropItem()
    {
        if (pData._LockPlayer) return;
        if (onDelay) return;
        if (equippedItem == null || IsItemInUse || !equippedItem.ItemData.droppable) return;

        ActionTriggered();

        int droppedSlot = selectedSlotIndex;
        uint droppedItemId = equippedItem.ID;

        equippedItem.HasOwner = false;
        inventorySlots[droppedSlot] = null;
        equippedItem = null;

        int next = (droppedSlot + 1) % inventorySlots.Length;
        for (int i = 0; i < inventorySlots.Length; i++)
        {
            int candidate = (droppedSlot + 1 + i) % inventorySlots.Length;
            if (inventorySlots[candidate] != null)
            {
                next = candidate;
                break;
            }
        }
        selectedSlotIndex = next;

        RpcDropItem(droppedSlot, droppedItemId);
    }

    [ClientRpc]
    void RpcOnAllItemsDestroyed()
    {
        for (int i = 0; i < inventorySlots.Length; i++)
        {
            hotbarSlots[i].RemoveItem();
            hotbarSlots[i].UnlockSlot();
            inventorySlots[i] = null;
        }

        equippedItem = null;
        SetEquipAnimState(null);
    }

    [ClientRpc] void RpcDropItem(int slotIndex, uint itemId) => LocalDropItem(slotIndex, itemId);
    [ClientRpc] void RpcDropAllItems() => LocalDropAllItems();

    void LocalDropItem(int slotIndex, uint itemId, bool drop = true)
    {
        ItemBase dropping = null;

        if (inventorySlots[slotIndex] != null)
        {
            dropping = inventorySlots[slotIndex];
        }
        else if (NetworkClient.spawned.TryGetValue(itemId, out var ni))
        {
            dropping = ni.GetComponent<ItemBase>();
            for (int i = 0; i < inventorySlots.Length; i++)
            {
                if (inventorySlots[i] == dropping)
                {
                    slotIndex = i;
                    break;
                }
            }
        }

        if (dropping == null) return;

        inventorySlots[slotIndex] = null;
        hotbarSlots[slotIndex].RemoveItem();

        if (dropping.ItemData.isTwoHanded)
        {
            for (int i = 0; i < hotbarSlots.Length; i++)
                hotbarSlots[i].UnlockSlot();
        }

        if (equippedItem == dropping)
        {
            equippedItem = null;
            SetEquipAnimState(null);
        }

        if (drop)
            dropping.OnDrop(this);

        Debug.Log("[Inventory] Local drop item");
    }

    void LocalDropAllItems(bool drop = true)
    {
        for (int i = 0; i < inventorySlots.Length; i++)
        {
            if (inventorySlots[i] == null) continue;

            hotbarSlots[i].RemoveItem();
            hotbarSlots[i].UnlockSlot();
            if (drop)
                inventorySlots[i].OnDrop(this);
            inventorySlots[i] = null;
        }

        equippedItem = null;
        SetEquipAnimState(null);
    }

    #endregion

    #region Slot Selection

    [Command]
    void CmdSelectNextSlot()
    {
        if (pData._LockPlayer) return;
        if (onDelay) return;
        if (pData.Player_Stats.dead || pData.Player_Stats.knocked) return;
        if (HasTwoHandedEquipped || IsItemInUse) return;

        ActionTriggered();

        int startIndex = selectedSlotIndex;
        int nextIndex = (selectedSlotIndex + 1) % inventorySlots.Length;

        while (nextIndex != startIndex)
        {
            if (inventorySlots[nextIndex] != null)
            {
                selectedSlotIndex = nextIndex;
                return;
            }
            nextIndex = (nextIndex + 1) % inventorySlots.Length;
        }

        selectedSlotIndex = (selectedSlotIndex + 1) % inventorySlots.Length;
    }

    [Command]
    void CmdSelectSlot(int index)
    {
        if (pData._LockPlayer) return;
        if (onDelay) return;
        if (pData.Player_Stats.dead || pData.Player_Stats.knocked) return;
        if (HasTwoHandedEquipped || IsItemInUse) return;
        if (index < 0 || index >= inventorySlots.Length) return;

        ActionTriggered();

        selectedSlotIndex = index;
    }

    void OnSelectedSlotChanged(int oldIndex, int newIndex)
    {
        if (oldIndex != newIndex)
            hotbarSlots[oldIndex].DeselectSlot();

        hotbarSlots[newIndex].SelectSlot();
        EquipItemFromSlot(newIndex);
    }

    void EquipItemFromSlot(int index)
    {
        if (index < 0 || index >= inventorySlots.Length) return;

        if (equippedItem != null)
            equippedItem.OnUnequip(this);

        ItemBase item = inventorySlots[index];

        if (item == null)
        {
            equippedItem = null;
            SetEquipAnimState(null);
            return;
        }

        equippedItem = item;
        item.OnEquip(this);

        item.transform.SetParent(pData.Skin_Data.GrabPoint);
        item.transform.SetLocalPositionAndRotation(item.ItemData.gOffset,
            Quaternion.Euler(item.ItemData.gRotation));

        if (nameDisplayCoroutine != null) StopCoroutine(nameDisplayCoroutine);
        nameDisplayCoroutine = StartCoroutine(DisplayItemName(item.ItemData.itemName));

        if (item.ItemData.isTwoHanded)
        {
            for (int i = 0; i < hotbarSlots.Length; i++)
            {
                if (i != index) hotbarSlots[i].LockSlot();
                else hotbarSlots[i].UnlockSlot();
            }
        }

        SetEquipAnimState(item);
    }

    void SetEquipAnimState(ItemBase item)
    {
        bool hasItem = item != null;
        bool twoHanded = hasItem && item.ItemData.isTwoHanded;
        bool isCrowbar = hasItem && item.ItemData.animationType == ItemSO.ItemAnimationType.Crowbar;
        bool isSword = hasItem && item.ItemData.animationType == ItemSO.ItemAnimationType.Sword;

        pData.Skin_Data.CharacterAnimator.SetBool("G_TwoH", twoHanded);
        pData.Skin_Data.CharacterAnimator.SetBool("G_Sword", !twoHanded && isSword);
        pData.Skin_Data.CharacterAnimator.SetBool("G_Crowbar", !twoHanded && isCrowbar);
        pData.Skin_Data.CharacterAnimator.SetBool("G_OneH", hasItem && !twoHanded && !isCrowbar && !isSword);
    }

    #endregion

    #region Item Actions

    [Command]
    void CmdUseAction(bool isPrimary)
    {
        if (equippedItem == null || IsItemInUse || pData._LockPlayer) return;
        if (!pData.InputHandler.IsDefaultController) return;

        ItemAction action = isPrimary ? equippedItem.PrimaryAction : equippedItem.SecondaryAction;
        if (action == null) return;
        if (action.IsOnCooldown()) return;

        pData.EmoteManager.ServerClearLoopEmote();
        action.EnterOnCooldown();

        FireAction(isPrimary);
        RpcUseAction(isPrimary);
    }

    [Command]
    void CmdCancelAction(bool isPrimary)
    {
        if (equippedItem == null) return;
        FireCancel(isPrimary);
        RpcCancelAction(isPrimary);
    }

    [ClientRpc]
    void RpcUseAction(bool isPrimary)
    {
        if (isServer) return;
        FireAction(isPrimary);
    }

    [ClientRpc]
    void RpcCancelAction(bool isPrimary)
    {
        if (isServer) return;
        FireCancel(isPrimary);
    }

    void FireAction(bool isPrimary)
    {
        if (isPrimary) equippedItem?.StartPrimaryAction();
        else equippedItem?.StartSecondaryAction();
    }

    void FireCancel(bool isPrimary)
    {
        if (isPrimary) equippedItem?.CancelPrimaryAction();
        else equippedItem?.CancelSecondaryAction();
    }

    #endregion

    #region UI

    IEnumerator DisplayItemName(string displayName)
    {
        itemName.SetText(displayName);

        const float fade = 0.5f;
        float timer = 0f;

        while (timer < fade)
        {
            nameGroup.alpha = timer / fade;
            timer += Time.deltaTime;
            yield return null;
        }

        nameGroup.alpha = 1f;
        yield return sleepNameDisplay;

        timer = fade;
        while (timer > 0f)
        {
            nameGroup.alpha = timer / fade;
            timer -= Time.deltaTime;
            yield return null;
        }

        nameGroup.alpha = 0f;
        nameDisplayCoroutine = null;
    }

    #endregion
}
