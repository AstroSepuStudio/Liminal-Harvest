using Mirror;
using System;
using UnityEngine;

public abstract class ItemAction : NetworkBehaviour
{
    [SerializeField] protected AudioSource audioSource;

    protected ItemBase item;

    public ItemBase Item => item;

    public virtual void Initialize(ItemBase owner)
    {
        item = owner;
    }

    public virtual bool IsOnCooldown() => false;
    public virtual void EnterOnCooldown() { }

    public abstract void Execute();
    public abstract void Cancel();

    public virtual void OnAnimationTrigger() { }
    public virtual void OnAnimationFinish() { }

    public float GetAnimationTriggerTime()
    {
        if (item.AnimationModule == null) return 0;

        ItemActionType type = item.GetActionType(this);
        if (type == ItemActionType.None) return 0;

        if (type == ItemActionType.Primary)
            return item.AnimationModule.PrimaryTriggerTime;
        else
            return item.AnimationModule.SecondaryTriggerTime;
    }

    public float GetAnimationFinishTime()
    {
        if (item.AnimationModule == null) return 0;

        ItemActionType type = item.GetActionType(this);
        if (type == ItemActionType.None) return 0;

        if (type == ItemActionType.Primary)
            return item.AnimationModule.PrimaryFinishTime;
        else
            return item.AnimationModule.SecondaryFinishTime;
    }

    protected virtual void TryPlaySFX(AnimationTrigger animationTrigger, ActionTiming timing)
    {
        if (animationTrigger.SFXs == null || animationTrigger.SFXs.Length <= 0) return;
        foreach (var sfx in animationTrigger.SFXs)
        {
            if (sfx.Timing == timing)
            {
                if (sfx.SFX == null) return;
                AudioManager.Instance.PlayOneShot(audioSource, sfx.SFX, gameObject, sfx.Loudness);
                return;
            }
        }
    }
}

public enum ActionTiming { Start, Cancel, Trigger, Finish, Extra1, Extra2, Extra3 }

[Serializable]
public class AnimationTrigger
{
    [Serializable]
    public struct ActionSFX
    {
        public ActionTiming Timing;
        public AudioSFX SFX;
        public SoundLoudness Loudness;
    }

    public string Trigger;
    public ActionSFX[] SFXs;

    [Header("Timing Overrides (-1 for default behaviour)")]
    public float primTrigTimeOverride = -1;
    public float primFinTimeOverride = -1;
    public float secTrigTimeOverride = -1;
    public float secFinTimeOverride = -1;
}