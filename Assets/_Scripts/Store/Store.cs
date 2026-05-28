using System.Collections;
using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using WS_ProceduralGeneration;

public class Store : NetworkBehaviour
{
    [System.Serializable]
    public class CartEntry
    {
        public StoreItemSO storeItem;
        public int quantity;
        public int unitPrice;
        public int TotalPrice => unitPrice * quantity;
    }

    [System.Serializable]
    public struct SyncCartEntry : System.IEquatable<SyncCartEntry>
    {
        public int catalogueIndex;
        public int quantity;
        public int unitPrice;
        public int TotalPrice => unitPrice * quantity;

        public bool Equals(SyncCartEntry other) =>
            catalogueIndex == other.catalogueIndex &&
            quantity == other.quantity &&
            unitPrice == other.unitPrice;
    }

    [System.Serializable]
    public struct SyncSaleEntry : System.IEquatable<SyncSaleEntry>
    {
        public bool onSale;
        public int discountPercent;
        public int originalPrice;

        public bool Equals(SyncSaleEntry other) =>
            onSale == other.onSale &&
            discountPercent == other.discountPercent &&
            originalPrice == other.originalPrice;
    }

    [Header("References")]
    [SerializeField] StoreItemSO[] catalogue;
    [SerializeField] Transform storeContentParent;
    [SerializeField] GameObject storeItemUIPrefab;
    [SerializeField] TextMeshProUGUI balanceTxt;
    [SerializeField] TextMeshProUGUI costTxt;

    [SerializeField] GameObject checkoutPanel;
    [SerializeField] GameObject notEnough4quota;
    [SerializeField] Button checkoutConfirmBtn;
    [SerializeField] TextMeshProUGUI checkoutCost;
    [SerializeField] TextMeshProUGUI checkoutConfirmBtnTxt;

    [Header("Spawn")]
    [SerializeField] Transform spawnOrigin;
    [SerializeField] float spawnMaxOffset = 1.5f;

    [Header("Events")]
    public UnityEvent OnCartChanged;

    public readonly SyncList<SyncSaleEntry> SyncedSales = new();
    public readonly SyncList<SyncCartEntry> SyncedCart = new();
    public readonly SyncList<int> SyncedPrices = new();

    PlayerData currentPlayer;

    public IReadOnlyList<SyncCartEntry> Cart => SyncedCart;
    public StoreItemSO[] Catalogue => catalogue;

    public int CartTotal
    {
        get
        {
            int total = 0;
            foreach (var entry in SyncedCart) total += entry.TotalPrice;
            return total;
        }
    }

    public bool CanAffordOne(StoreItemSO storeItem)
    {
        if (storeItem == null) return false;
        int price = GetDailyPrice(storeItem);
        return CartTotal + price <= GetCurrentBalance();
    }

    int GetCatalogueIndex(StoreItemSO item)
    {
        for (int i = 0; i < catalogue.Length; i++)
            if (catalogue[i] == item) return i;
        return -1;
    }

    public StoreItemSO GetCatalogueItem(int index) =>
        index >= 0 && index < catalogue.Length ? catalogue[index] : null;

    public int GetDailyPrice(StoreItemSO item)
    {
        if (catalogue == null) return item.minPrice;
        for (int i = 0; i < catalogue.Length; i++)
            if (catalogue[i] == item)
                return i < SyncedPrices.Count ? SyncedPrices[i] : item.minPrice;
        return item.minPrice;
    }

    public SyncSaleEntry GetSaleEntry(StoreItemSO item)
    {
        if (catalogue == null) return default;
        for (int i = 0; i < catalogue.Length; i++)
            if (catalogue[i] == item)
                return i < SyncedSales.Count ? SyncedSales[i] : default;
        return default;
    }

    void Start()
    {
        SyncedPrices.Callback += OnSyncedPricesChanged;
        SyncedSales.Callback += OnSyncedSalesChanged;
        SyncedCart.Callback += OnSyncedCartChanged;

        PopulateCatalogue();

        if (!isServer) return;

        GameManager.Instance.ecoMod.OnTeamBalanceChangedEv.AddListener(OnBalanceUpdate);
        GameManager.Instance.dayMod.OnDayEnded.AddListener(OnDayEnded);
        RollDailyDeals();
    }

    private void OnBalanceUpdate(PlayerTeam arg0, float arg1) => RpcUpdateUI();

    void OnDestroy()
    {
        SyncedPrices.Callback -= OnSyncedPricesChanged;
        SyncedSales.Callback -= OnSyncedSalesChanged;
        SyncedCart.Callback -= OnSyncedCartChanged;

        if (isServer)
            GameManager.Instance.dayMod.OnDayEnded.RemoveListener(OnDayEnded);
    }

    void OnSyncedPricesChanged(SyncList<int>.Operation op, int index, int oldValue, int newValue)
    {
        RefreshAllPrices();
        RefreshBalanceDisplay();
    }

    void OnSyncedCartChanged(SyncList<SyncCartEntry>.Operation op, int index,
        SyncCartEntry oldItem, SyncCartEntry newItem)
    {
        OnCartChanged?.Invoke();
        RefreshAllCartUI();
        RefreshBalanceDisplay();
    }

    void OnSyncedSalesChanged(SyncList<SyncSaleEntry>.Operation op, int index,
        SyncSaleEntry oldItem, SyncSaleEntry newItem)
    {
        RefreshAllPrices();
    }

    [Server]
    void RollDailyDeals()
    {
        if (catalogue == null) return;

        SyncedPrices.Clear();
        SyncedSales.Clear();

        bool globalSale = Random.value <= 0.01f;

        foreach (var item in catalogue)
        {
            if (item == null)
            {
                SyncedPrices.Add(0);
                SyncedSales.Add(default);
                continue;
            }

            int basePrice = item.RolledPrice;
            bool itemOnSale = globalSale || Random.value <= 0.10f;

            if (itemOnSale)
            {
                WSDG_Tier.Tier discountTier = WSDG_Tier.RollRandomTier();
                int discount = RollDiscount(discountTier);
                int salePrice = Mathf.Max(1, Mathf.RoundToInt(basePrice * (1f - discount / 100f)));

                SyncedPrices.Add(salePrice);
                SyncedSales.Add(new SyncSaleEntry
                {
                    onSale = true,
                    discountPercent = discount,
                    originalPrice = basePrice
                });
            }
            else
            {
                SyncedPrices.Add(basePrice);
                SyncedSales.Add(new SyncSaleEntry
                {
                    onSale = false,
                    discountPercent = 0,
                    originalPrice = basePrice
                });
            }
        }
    }

    static int RollDiscount(WSDG_Tier.Tier tier) => tier switch
    {
        WSDG_Tier.Tier.Common => Random.Range(5, 11),
        WSDG_Tier.Tier.Uncommon => Random.Range(15, 21),
        WSDG_Tier.Tier.Rare => Random.Range(25, 36),
        WSDG_Tier.Tier.Epic => Random.Range(40, 66),
        WSDG_Tier.Tier.Legendary => Random.Range(70, 91),
        _ => Random.Range(5, 11)
    };

    [Server]
    public void RerollDayStore()
    {
        RollDailyDeals();
        RpcClearCart();
    }

    void OnDayEnded(int day)
    {
        RollDailyDeals();
        RpcClearCart();
    }

    public void SetCurrentPlayer(PlayerData player)
    {
        currentPlayer = player;
        RefreshBalanceDisplay();
    }

    public void ClearCurrentPlayer()
    {
        currentPlayer = null;
    }

    void RefreshAllPrices()
    {
        foreach (Transform child in storeContentParent)
            if (child.TryGetComponent(out StoreItemUI ui))
                ui.RefreshPrice();
    }

    void PopulateCatalogue()
    {
        if (storeContentParent == null || storeItemUIPrefab == null || catalogue == null) return;

        foreach (Transform child in storeContentParent)
            Destroy(child.gameObject);

        foreach (var item in catalogue)
        {
            if (item == null) continue;
            GameObject go = Instantiate(storeItemUIPrefab, storeContentParent);
            if (go.TryGetComponent(out StoreItemUI ui))
                ui.Initialize(item, this);
        }

        if (SyncedPrices.Count > 0)
            RefreshAllPrices();
    }

    public void AddToCart(StoreItemSO storeItem, int quantity = 1)
    {
        if (storeItem == null || quantity <= 0) return;
        CmdAddToCart(GetCatalogueIndex(storeItem), quantity);
    }

    public void RemoveFromCart(StoreItemSO storeItem, int quantity = 1)
    {
        if (storeItem == null) return;
        CmdRemoveFromCart(GetCatalogueIndex(storeItem), quantity);
    }

    public void RemoveAllFromCart(StoreItemSO storeItem)
    {
        if (storeItem == null) return;
        CmdRemoveAllFromCart(GetCatalogueIndex(storeItem));
    }

    public void ClearCart()
    {
        CmdClearCart();
    }

    [Command(requiresAuthority = false)]
    void CmdAddToCart(int catalogueIndex, int quantity)
    {
        if (catalogueIndex < 0 || catalogueIndex >= catalogue.Length) return;

        int unitPrice = SyncedPrices[catalogueIndex];
        int addedCost = unitPrice * quantity;

        if (CartTotal + addedCost > GetCurrentBalance()) return;

        for (int i = 0; i < SyncedCart.Count; i++)
        {
            if (SyncedCart[i].catalogueIndex != catalogueIndex) continue;
            var updated = SyncedCart[i];
            updated.quantity += quantity;
            SyncedCart[i] = updated;
            return;
        }

        SyncedCart.Add(new SyncCartEntry
        {
            catalogueIndex = catalogueIndex,
            quantity = quantity,
            unitPrice = unitPrice
        });
    }

    [Command(requiresAuthority = false)]
    void CmdRemoveFromCart(int catalogueIndex, int quantity)
    {
        for (int i = 0; i < SyncedCart.Count; i++)
        {
            if (SyncedCart[i].catalogueIndex != catalogueIndex) continue;

            var updated = SyncedCart[i];
            updated.quantity -= quantity;

            if (updated.quantity <= 0)
                SyncedCart.RemoveAt(i);
            else
                SyncedCart[i] = updated;

            return;
        }
    }

    [Command(requiresAuthority = false)]
    void CmdRemoveAllFromCart(int catalogueIndex)
    {
        for (int i = SyncedCart.Count - 1; i >= 0; i--)
            if (SyncedCart[i].catalogueIndex == catalogueIndex)
                SyncedCart.RemoveAt(i);
    }

    [Command(requiresAuthority = false)]
    void CmdClearCart()
    {
        SyncedCart.Clear();
        RpcUpdateUI();
    }

    public void RequestCheckout()
    {
        if (Cart.Count == 0) return;

        float balance = GetCurrentBalance();
        float finalBalance = balance - CartTotal;

        checkoutPanel.SetActive(true);
        checkoutCost.SetText(
            $"${CartTotal}\n" +
            $"${balance} -\n" +
            $"--------\n" +
            $"${finalBalance}");

        if (finalBalance < GameManager.Instance.ecoMod.targetQuota)
        {
            StopAllCoroutines();
            StartCoroutine(CountdownBelowQuota());
        }
        else
        {
            notEnough4quota.SetActive(false);
            checkoutConfirmBtn.interactable = true;
            checkoutConfirmBtnTxt.SetText("CONFIRM");
        }
    }

    public void CloseCheckout()
    {
        checkoutPanel.SetActive(false);
        StopAllCoroutines();
    }

    IEnumerator CountdownBelowQuota()
    {
        notEnough4quota.SetActive(true);
        checkoutConfirmBtn.interactable = false;
        float timer = 3f;
        while (timer > 0f)
        {
            checkoutConfirmBtnTxt.SetText($"CONFIRM ({Mathf.RoundToInt(timer)})");
            timer -= Time.deltaTime;
            yield return null;
        }
        checkoutConfirmBtnTxt.SetText("CONFIRM");
        checkoutConfirmBtn.interactable = true;
    }

    public void ConfirmPurchase()
    {
        if (currentPlayer == null) return;
        CloseCheckout();
        CmdRequestPurchase();
    }

    [Command(requiresAuthority = false)]
    void CmdRequestPurchase() => ServerTryPurchase();

    [Server]
    void ServerTryPurchase()
    {
        if (SyncedCart.Count == 0) return;

        var eco = GameManager.Instance.ecoMod;

        if (LobbySettings.Instance.TeamsShareBalance)
        {
            if (eco.TotalBalance < CartTotal) return;
            eco.TakeBalance(CartTotal);
        }
        else
        {
            if (!eco.CanAfford(currentPlayer.Team, CartTotal)) return;
            eco.Deduct(currentPlayer.Team, CartTotal);
        }

        RpcUpdateUI();
        SpawnCartItems();
        SyncedCart.Clear();
    }

    [ClientRpc]
    void RpcClearCart()
    {
        ClearCart();
    }

    [Server]
    void SpawnCartItems()
    {
        if (spawnOrigin == null) { Debug.LogWarning("[Store] No spawnOrigin assigned."); return; }

        foreach (var entry in SyncedCart)
        {
            var storeItem = GetCatalogueItem(entry.catalogueIndex);
            if (storeItem.item == null) continue;
            for (int i = 0; i < entry.quantity; i++)
                SpawnOne(storeItem.item);
        }
    }

    [Server]
    void SpawnOne(ItemSO itemData)
    {
        Vector2 circle = Random.insideUnitCircle * spawnMaxOffset;
        Vector3 pos = spawnOrigin.position + new Vector3(circle.x, 0f, circle.y);

        GameObject go = Instantiate(itemData.itemPrefab, pos, Quaternion.identity);
        NetworkServer.Spawn(go);

        if (go.TryGetComponent(out ItemBase item))
        {
            item.ItemValue = 0;

            if (itemData.stackable && item is IStackable stackable)
                stackable.SetQuantity(1);
        }
    }

    [ClientRpc]
    void RpcUpdateUI()
    {
        RefreshAllCartUI();
        RefreshBalanceDisplay();
    }

    float GetCurrentBalance()
    {
        var eco = GameManager.Instance.ecoMod;
        if (LobbySettings.Instance.TeamsShareBalance)
            return eco.TotalBalance;

        if (currentPlayer != null)
            return eco.GetTeamBalance(currentPlayer.Team);

        return 0f;
    }

    void RefreshBalanceDisplay()
    {
        if (balanceTxt != null)
            balanceTxt.SetText($"Balance:\n${GetCurrentBalance()}");

        if (costTxt != null)
            costTxt.SetText("Cost:\n" + (CartTotal > 0 ? $"-${CartTotal}" : "$0"));
    }

    void RefreshAllCartUI()
    {
        foreach (Transform child in storeContentParent)
            if (child.TryGetComponent(out StoreItemUI ui))
                ui.RefreshQuantity();
    }
}
