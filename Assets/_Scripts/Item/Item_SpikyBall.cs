using Mirror;
using UnityEngine;
using UnityEngine.Events;

public class Item_SpikyBall : ItemBase, IControlHurtbox
{
    [SerializeField] ItemHurtbox itemHurtbox;
    [SerializeField] float minSpd = 4f;
    [SerializeField] float maxSpd = 8f;
    [SerializeField] AttackStat minAttack;
    [SerializeField] AttackStat maxStat;
    [SerializeField] AudioLoudnessPair onHitSFX;
    [SerializeField] AudioLoudnessPair onHurtSFX;

    public UnityEvent OnHit;
    public UnityEvent<EntityStats> OnHurt;

    public AttackStat GetAttackStat() => maxStat;
    public ItemBase GetOwner() => this;

    private void Start()
    {
        itemHurtbox.Initialize(this);
    }

    public override void OnPickUp()
    {
        base.OnPickUp();

        itemHurtbox.DisableHitbox();
    }

    public override void OnDrop(ItemInventory handler)
    {
        base.OnDrop(handler);

        itemHurtbox.EnableHitbox(0.25f);
    }

    [Server]
    public void HurtBoxHit()
    {
        OnHit?.Invoke();

        RpcPlayOnHit();
    }

    [Server]
    public void HurtBoxHurt(EntityStats target)
    {
        float hitSpd = rb.linearVelocity.magnitude;
        if (hitSpd < minSpd) return;

        OnHurt?.Invoke(target);

        PlayerData source = TryGetValidPlayer();

        float t = Mathf.InverseLerp(minSpd, maxSpd, hitSpd);

        AttackStat atkStat = new(0,
            Mathf.Lerp(minAttack.AttackKnock, maxStat.AttackKnock, t),
            Mathf.Lerp(minAttack.AttackForce, maxStat.AttackForce, t),
            Mathf.Lerp(minAttack.AttackDamage, maxStat.AttackDamage, t),
            0);

        target.ReceiveAttack(AttackEvent.From(source, target, atkStat));

        RpcPlayOnHurt();
    }

    [ClientRpc]
    void RpcPlayOnHit()
    {
        if (itemAudioSrc == null || onHitSFX.sfx == null) return;
        AudioManager.Instance.PlayOneShot(itemAudioSrc, onHitSFX.sfx, TryGetValidPlayerGameObject(), onHitSFX.sfxLoudness);
    }

    [ClientRpc]
    void RpcPlayOnHurt()
    {
        if (itemAudioSrc == null) return;

        AudioLoudnessPair? sfx = onHurtSFX.sfx != null ? onHurtSFX : onHitSFX.sfx != null ? onHitSFX : null;
        if (sfx == null) return;

        AudioManager.Instance.PlayOneShot(itemAudioSrc, sfx.Value.sfx, TryGetValidPlayerGameObject(), sfx.Value.sfxLoudness);
    }
}
