using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ItemHurtbox : NetworkBehaviour
{
    [SerializeField] ItemBase ownerItem;
    [SerializeField] Collider hitbox;

    [Header("Config")]
    [SerializeField] bool ignoreSelf = true;
    [SerializeField] bool startEnabled = false;
    [SerializeField] bool applyDmgOnHit = true;
    [SerializeField] bool ignoreRepeat = true;

    [Header("Cooldown")]
    [SerializeField] bool useCooldown = false;
    [SerializeField] float cooldown = 0.2f;

    IControlHurtbox hurtboxCller;
    readonly Dictionary<GameObject, float> hitCooldowns = new();
    readonly HashSet<GameObject> hitEntities = new();

    private void Start()
    {
        if (ownerItem != null)
            ownerItem.TryGetComponent(out hurtboxCller);

        if (!startEnabled)
            enabled = false;
    }

    public void Initialize(IControlHurtbox hurtboxCller)
    {
        this.hurtboxCller = hurtboxCller;
    }

    public void EnableHitbox()
    {
        hitEntities.Clear();
        hitCooldowns.Clear();
        hitbox.enabled = true;
    }

    public void EnableHitbox(float delay) => StartCoroutine(DelayedEnableHitbox(delay));

    IEnumerator DelayedEnableHitbox(float delay)
    {
        yield return new WaitForSeconds(delay);
        EnableHitbox();
    }

    public void DisableHitbox()
    {
        hitbox.enabled = false;
        hitEntities.Clear();
        hitCooldowns.Clear();
    }

    private EntityStats TryGetValidTarget(GameObject targetGO)
    {
        if (ignoreRepeat)
        {
            if (useCooldown)
            {
                if (hitCooldowns.TryGetValue(targetGO, out float nextHitTime))
                {
                    if (Time.time < nextHitTime) return null;
                }
            }
            else if (hitEntities.Contains(targetGO))
            {
                return null;
            }
        }

        if (!targetGO.TryGetComponent(out EntityStats target))
        {
            hurtboxCller.HurtBoxHit();
            return null;
        }

        return target;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isServer) return;

        ItemBase item = hurtboxCller.GetOwner();
        if (item == null) return;

        PlayerData source = item.TryGetValidPlayer();
        if (source == null) return;

        if (ignoreSelf && other.gameObject == source.gameObject) return;

        EntityStats target = TryGetValidTarget(other.gameObject);
        if (target == null) return;
        
        if (useCooldown)
            hitCooldowns[other.gameObject] = Time.time + cooldown;
        else
            hitEntities.Add(other.gameObject);

        hurtboxCller.HurtBoxHurt(target);

        if (applyDmgOnHit)
        {
            target.ReceiveAttack(AttackEvent.From(source, target, hurtboxCller.GetAttackStat()));
        }
    }
}
