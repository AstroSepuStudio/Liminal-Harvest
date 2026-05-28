using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WS_ProceduralGeneration;

public class FurnitureEntity : EntityStats
{
    [Header("Furniture")]
    [SerializeField] protected Renderer furRenderer;
    [SerializeField] protected FurnitureDataSO dataSO;
    [SerializeField] protected Rigidbody rb;
    [SerializeField] protected Vector3 center;
    [SerializeField] protected Vector3 dropArea;

    public List<DungeonSpawnPoint> lootPositions;

    protected bool _dying;
    public bool IsDying => _dying;

    public void SetRender(bool active)
        => furRenderer.enabled = active;

    public bool gizmos;

    protected override void Awake()
    {
        base.Awake();
        
        dataSO = Instantiate(dataSO);
    }

    [Server]
    public override void ApplyDamage(AttackEvent source)
    {
        if (_dying) return;

        currentHP = Mathf.Clamp(currentHP - source.AttackStat_.AttackDamage, 0f, maxHP);

        if (currentHP <= 0f)
        {
            CheckDropThresholds(false);
            HandleDeath(source);
            return;
        }

        CheckDropThresholds(true);

        if (source.AttackStat_.AttackDamage >= 10f) 
            PlaySFX(SFXEvent.StrongHit);
        else 
            PlaySFX(SFXEvent.TakeDamage);
    }

    [Server]
    public override void AddKnock(float amount, Vector3 momentum)
    {
        if (_dying) return;

        base.AddKnock(amount, momentum);
        rb.AddForce(momentum, ForceMode.Impulse);
    }

    [Server]
    protected override void HandleDeath(AttackEvent source)
    {
        if (_dying) return;
        _dying = true;

        OnDeath?.Invoke(source);

        if (!sfxMap.TryGetValue(SFXEvent.Died, out var group) || group.Clips.Length == 0) return;
        RpcFurnitureBreaks(Random.Range(0, group.Clips.Length));
        foreach (var item in dataSO.lootTable) TryDropItem(item);
        StartCoroutine(DelayedDestroy());
    }

    [ClientRpc]
    protected virtual void RpcFurnitureBreaks(int clipIndex)
    {
        SetRender(false);
        if (!sfxMap.TryGetValue(SFXEvent.Died, out var group) || clipIndex >= group.Clips.Length) return;
        AudioManager.Instance.PlayOneShotAndDestroy(transform.position, group.Clips[clipIndex], gameObject, SoundLoudness.Average);
    }

    IEnumerator DelayedDestroy()
    {
        yield return new WaitForSeconds(0.2f);
        NetworkServer.Destroy(gameObject);
    }

    protected void CheckDropThresholds(bool playSFX)
    {
        bool anyDropped = false;

        for (int i = 0; i < dataSO.dropThresholds.Length; i++)
        {
            if (dataSO.dropThresholds[i].triggered) continue;

            float hpThreshold = maxHP * (dataSO.dropThresholds[i].dropThreshold / 100f);
            if (currentHP > hpThreshold) continue;

            if (TryDropItem(dataSO.dropThresholds[i].Item_Drop))
                anyDropped = true;

            dataSO.dropThresholds[i].triggered = true;
        }

        if (anyDropped && playSFX) PlaySFX(SFXEvent.PartialBreak);
    }

    [Server]
    protected virtual bool TryDropItem(FurnitureDataSO.ItemDrop drop)
    {
        float rand = Random.Range(0f, 100f);
        if (rand > drop.dropChance) return false;

        int qty = Random.Range(drop.minQuantity, drop.maxQuantity);
        for (int i = 0; i < qty; i++)
        {
            Vector3 worldCenter = transform.position + center;

            Vector3 offset = new(
                Random.Range(-dropArea.x, dropArea.x),
                Random.Range(-dropArea.y, dropArea.y),
                Random.Range(-dropArea.z, dropArea.z));

            Vector3 position = worldCenter + offset;

            GameObject spawned = Instantiate(drop.Item.itemPrefab, position, Quaternion.identity);
            NetworkServer.Spawn(spawned);

            if (spawned.TryGetComponent(out ItemBase item))
                item.AddForce((position - transform.position).normalized * Random.Range(1f, 2f), ForceMode.Impulse);
        }
        return true;
    }

    protected virtual void OnDrawGizmosSelected()
    {
        if (!gizmos) return;

        Vector3 worldCenter = transform.position + center;

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
        Gizmos.DrawCube(worldCenter, dropArea * 2f);

        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(worldCenter, dropArea * 2f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(worldCenter, 0.1f);
    }
}