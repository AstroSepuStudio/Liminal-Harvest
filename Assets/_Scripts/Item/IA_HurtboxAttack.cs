using Mirror;
using UnityEngine;
using UnityEngine.Events;

public class IA_HurtboxAttack : ItemAction, IControlHurtbox
{
    [Header("Hurtbox Attack Settings")]
    [SerializeField] protected ItemHurtbox hurtbox;
    [SerializeField] protected string animationTrigger = "Atk_Default";
    [SerializeField] protected bool forceAim = true;
    [SerializeField] protected AttackStat attackStat;

    [SerializeField] float swingDelay = 0.1f;
    [SerializeField] AudioSFX swingSFX;
    [SerializeField] SoundLoudness swingLoudness;
    [SerializeField] AudioSFX onHitSFX;
    [SerializeField] SoundLoudness onHitLoudness;
    [SerializeField] AudioSFX OnHurtSFX;
    [SerializeField] SoundLoudness onHurtLoudness;

    public UnityEvent OnHit;
    public UnityEvent<EntityStats> OnHurt;

    public AttackStat AttackStat => attackStat;

    public override void Initialize(ItemBase owner)
    {
        base.Initialize(owner);

        hurtbox.Initialize(this);
    }

    public override bool IsOnCooldown() => attackStat.OnCooldown;

    public override void EnterOnCooldown() => StartCoroutine(attackStat.CountdownCooldown());

    public override void Execute()
    {
        if (isServer) item.InUse = true;

        ItemActionType type = item.GetActionType(this);
        if (type == ItemActionType.None) return;

        if (forceAim && isServer)
            item.PData.Player_Movement.ServerForceAimAt(item.PData.LookCameraTarget.position);

        if (type == ItemActionType.Primary)
            item.AnimationModule.PlayPrimary(this, item.PData, animationTrigger);
        else if (type == ItemActionType.Secondary)
            item.AnimationModule.PlaySecondary(this, item.PData, animationTrigger);

        RpcPlayOnSwing();
    }

    public override void Cancel() { }

    public override void OnAnimationTrigger()
    {
        hurtbox.EnableHitbox();
    }

    public override void OnAnimationFinish()
    {
        if (isServer) item.InUse = false;
        hurtbox.DisableHitbox();

        if (forceAim && isServer)
            item.PData.Player_Movement.ServerClearForcedAim();
    }

    public ItemBase GetOwner() => item;
    public AttackStat GetAttackStat() => attackStat;

    [Server]
    public void HurtBoxHit()
    {
        OnHit?.Invoke();

        RpcPlayOnHit();
    }

    [Server]
    public void HurtBoxHurt(EntityStats target)
    {
        OnHurt?.Invoke(target);

        RpcPlayOnHurt();
    }

    [ClientRpc]
    void RpcPlayOnSwing()
    {
        if (audioSource == null || swingSFX == null) return;
        AudioManager.Instance.PlayOneShotWithDelay(audioSource, swingSFX, swingDelay, item.PData.gameObject, swingLoudness);
    }

    [ClientRpc]
    void RpcPlayOnHit()
    {
        if (audioSource == null || onHitSFX == null) return;
        AudioManager.Instance.PlayOneShot(audioSource, onHitSFX, item.PData.gameObject, onHitLoudness);
    }

    [ClientRpc]
    void RpcPlayOnHurt()
    {
        if (audioSource == null) return;

        AudioSFX sfx = OnHurtSFX != null ? OnHurtSFX : onHitSFX != null ? onHitSFX : null;
        if (sfx == null) return;

        AudioManager.Instance.PlayOneShot(audioSource, sfx, item.PData.gameObject, onHurtLoudness);
    }    
}
