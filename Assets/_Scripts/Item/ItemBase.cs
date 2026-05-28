using Mirror;
using UnityEngine;

public enum ItemActionType { None, Primary, Secondary }

[RequireComponent(typeof(AudioSource))]
public class ItemBase : InteractableObject
{
    public ItemSO ItemData;

    [SerializeField] protected NetworkIdentity identity;
    [SerializeField] protected Rigidbody rb;
    [SerializeField] protected Collider coll;
    [SerializeField] protected Collider[] colls;
    [SerializeField] protected Renderer itemRenderer;
    [SerializeField] protected ItemAnimationModule animationModule;
    [SerializeField] protected AudioSource itemAudioSrc;

    [SerializeField] protected ItemAction primaryAction;
    [SerializeField] protected ItemAction secondaryAction;

    public uint ID => identity.netId;

    [SyncVar] public int ItemValue;
    [SyncVar] public bool InUse = false;
    [SyncVar] public bool HasOwner = false;

    public PlayerData PData { get; private set; }
    public PlayerData LastPlayer { get; private set; }
    public PlayerData TryGetValidPlayer()
    {
        if (PData != null) return PData;
        if (LastPlayer != null) return LastPlayer;
        return null;
    }

    public GameObject TryGetValidPlayerGameObject()
    {
        PlayerData pData = TryGetValidPlayer();
        if (pData != null) return pData.gameObject;
        return null;
    }

    public Rigidbody RigidBody => rb;
    public ItemAnimationModule AnimationModule => animationModule;
    public ItemAction PrimaryAction => primaryAction;
    public ItemAction SecondaryAction => secondaryAction;

    [System.Serializable] protected struct AudioLoudnessPair
    {
        public AudioSFX sfx;
        public SoundLoudness sfxLoudness;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (ItemData == null)
        {
            Debug.LogError($"[ItemBase] ItemData is not assigned on '{gameObject.name}'!", this);
            return;
        }

        if (ItemData.isSellable)
            ItemValue = Random.Range(ItemData.minValue, ItemData.maxValue);

        if (animationModule != null) animationModule.Initialize(this);
        if (primaryAction != null) primaryAction.Initialize(this);
        if (secondaryAction != null) secondaryAction.Initialize(this);
    }

    [Server]
    public float MultiplyValue(float multiplier)
    {
        float baseValue = ItemValue;
        ItemValue = Mathf.RoundToInt(ItemValue * multiplier);
        return ItemValue - baseValue;
    }

    [Server]
    public void SetScale(Vector3 scale)
    {
        transform.localScale = scale;
        RpcSetScale(scale);
    }

    public ItemActionType GetActionType(ItemAction ia)
    {
        if (primaryAction == ia) return ItemActionType.Primary;
        if (secondaryAction == ia) return ItemActionType.Secondary;
        return ItemActionType.None;
    }

    public virtual bool IsUsageCriteriaMet() => true;

    #region Interaction

    [Server]
    public override void OnInteract(PlayerData sourceData)
    {
        if (!ItemData.pickable) return;

        if (sourceData.PlayerInventory.AddItem(this))
        {
            PData = sourceData;
            RpcGetPlayerData(sourceData.netId);
        }
    }

    [ClientRpc]
    protected void RpcGetPlayerData(uint playerID)
    {
        if (isServer) return;

        NetworkClient.spawned.TryGetValue(playerID, out NetworkIdentity netIdentity);
        if (netIdentity == null) return;
        PData = netIdentity.GetComponent<PlayerData>();
    }

    #endregion

    #region Lifecycle

    public virtual void OnPickUp()
    {
        rb.isKinematic = true;
        coll.enabled = false;
        foreach (var col in colls)
        {
            col.enabled = false;
        }

        canvas.DisableCanvas();
    }

    public virtual void OnEquip(ItemInventory handler)
    {
        SetRender(true);
        canvas.DisableCanvas();

        if (ItemData.EquipSFX.Length <= 0) return;
        GameObject src = PData != null ? PData.gameObject : LastPlayer != null ? LastPlayer.gameObject : gameObject;

        int rand = Random.Range(0, ItemData.EquipSFX.Length);
        AudioManager.Instance.PlayOneShot(itemAudioSrc, ItemData.EquipSFX[rand], src, SoundLoudness.Quiet);
    }

    public virtual void OnUnequip(ItemInventory handler)
    {
        SetRender(false);
    }

    public virtual void OnDrop(ItemInventory handler)
    {
        gameObject.SetActive(true);
        coll.enabled = true;
        foreach (var col in colls)
        {
            col.enabled = true;
        }

        transform.SetParent(null);
        rb.isKinematic = false;

        LastPlayer = PData;
        PData = null;
    }

    #endregion

    #region Item Actions

    public void StartPrimaryAction()
    {
        if (primaryAction == null) return;
        if (!IsUsageCriteriaMet()) return;
        primaryAction.Execute();
    }

    public void CancelPrimaryAction()
    {
        if (primaryAction == null) return;
        primaryAction.Cancel();
    }

    public void StartSecondaryAction()
    {
        if (secondaryAction == null) return;
        if (!IsUsageCriteriaMet()) return;
        secondaryAction.Execute();
    }

    public void CancelSecondaryAction()
    {
        if (secondaryAction == null) return;
        secondaryAction.Cancel();
    }

    public void PrimaryAnimationTrigger()
    {
        if (primaryAction == null) return;
        primaryAction.OnAnimationTrigger();
    }

    public void PrimaryAnimationFinish()
    {
        if (primaryAction == null) return;
        primaryAction.OnAnimationFinish();
    }

    public void SecondaryAnimationTrigger()
    {
        if (primaryAction == null) return;
        primaryAction.OnAnimationTrigger();
    }

    public void SecondaryAnimationFinish()
    {
        if (primaryAction == null) return;
        primaryAction.OnAnimationFinish();
    }

    #endregion

    #region Visual / Misc

    public override void EnableCanvas()
    {
        canvas.EnableCanvas();
        canvas.SetLabel(ItemData.itemName);
        canvas.SetDescription($"${ItemValue}");
    }

    [Server]
    public void SetKinematic(bool kinematic) => rb.isKinematic = kinematic;

    public void DisplayItem(bool visible)
    {
        SetRender(visible);
        SetCollider(visible);
    }

    public void SetRender(bool render)
    {
        if (itemRenderer == null)
        {
            Debug.LogWarning($"[ItemBase] itemRenderer not assigned on '{gameObject.name}'", this);
            return;
        }
        itemRenderer.enabled = render;
    }

    public void SetCollider(bool enabled) => coll.enabled = enabled;

    public void DisableColliderTemporarily(float time) => StartCoroutine(EnableColliderAfterDelay(time));

    public void AddForce(Vector3 force, ForceMode mode) => rb.AddForce(force, mode);

    public void SetVelocity(Vector3 velocity)
    {
        if (HasOwner) return;
        rb.linearVelocity = velocity;
    }

    System.Collections.IEnumerator EnableColliderAfterDelay(float time)
    {
        coll.enabled = false;
        float timer = 0;
        while (timer < time)
        {
            timer += Time.deltaTime;
            yield return null;
        }
        coll.enabled = true;
    }

    [ClientRpc]
    void RpcSetScale(Vector3 scale) => transform.localScale = scale;

    #endregion

    private void OnCollisionEnter(Collision collision)
    {
        if (ItemData.DropSFX.Length <= 0) return;
        GameObject src = PData != null ? PData.gameObject : LastPlayer != null ? LastPlayer.gameObject : gameObject;

        int rand = Random.Range(0, ItemData.DropSFX.Length);
        AudioManager.Instance.PlayOneShot(itemAudioSrc, ItemData.DropSFX[rand], src, SoundLoudness.Moderate);
    }
}
