using System.Collections;
using UnityEngine;

public class ItemAnimationModule : MonoBehaviour
{
    [SerializeField] Animator animatorOverride;

    [SerializeField] float primaryTriggerTime;
    [SerializeField] float primaryFinishTime;
    [SerializeField] float secondaryTriggerTime;
    [SerializeField] float secondaryFinishTime;

    public float PrimaryTriggerTime => primaryTriggerTime;
    public float PrimaryFinishTime => primaryFinishTime;
    public float SecondaryTriggerTime => secondaryTriggerTime;
    public float SecondaryFinishTime => secondaryFinishTime;

    public void OverridePrimaryTriggerTime(float time) => primaryTrigger = new(time);
    public void OverridePrimaryFinishTime(float time) => primaryFinish = new(time);
    public void OverrideSecondaryTriggerTime(float time) => secondaryTrigger = new(time);
    public void OverrideSecondaryFinishTime(float time) => secondaryFinish = new(time);

    public void ClearOverridePrimaryTriggerTime() => primaryTrigger = new(primaryTriggerTime);
    public void ClearOverridePrimaryFinishTime() => primaryFinish = new(primaryFinishTime);
    public void ClearOverrideSecondaryTriggerTime() => secondaryTrigger = new(secondaryTriggerTime);
    public void ClearOverrideSecondaryFinishTime() => secondaryFinish = new(secondaryFinishTime);
    public void ClearAllOverrides() => SetUpDefaultTimings();

    private void SetUpDefaultTimings()
    {
        primaryTrigger = new(primaryTriggerTime);
        primaryFinish = new(primaryFinishTime);
        secondaryTrigger = new(secondaryTriggerTime);
        secondaryFinish = new(secondaryFinishTime);
    }

    ItemBase item;
    Coroutine activeSequence;

    WaitForSeconds primaryTrigger;
    WaitForSeconds primaryFinish;
    WaitForSeconds secondaryTrigger;
    WaitForSeconds secondaryFinish;

    public void Initialize(ItemBase item)
    {
        this.item = item;

        SetUpDefaultTimings();
    }

    public void PlayPrimary(ItemAction action, PlayerData pData, string animTrigger, bool isBool = false)
        => PlaySequence(action, animTrigger, primaryTrigger, primaryFinish, isBool);

    public void PlaySecondary(ItemAction action, PlayerData pData, string animTrigger, bool isBool = false)
        => PlaySequence(action, animTrigger, secondaryTrigger, secondaryFinish, isBool);

    public void StopSequence(bool isBool = false, string animTrigger = "")
    {
        if (activeSequence != null) StopCoroutine(activeSequence);
        Animator anim = animatorOverride != null ? animatorOverride : item.PData.Skin_Data.CharacterAnimator;
        if (isBool) anim.SetBool(animTrigger, false);

        activeSequence = null;
    }

    void PlaySequence(ItemAction action, string animTrigger, WaitForSeconds triggerDelay, WaitForSeconds finishDelay, bool isBool)
    {
        StopSequence();
        activeSequence = StartCoroutine(Sequence(action, animTrigger, triggerDelay, finishDelay, isBool));
    }

    IEnumerator Sequence(ItemAction action, string animTrigger, WaitForSeconds triggerDelay, WaitForSeconds finishDelay, bool isBool)
    {
        Animator anim = animatorOverride != null ? animatorOverride : item.PData.Skin_Data.CharacterAnimator;

        if (isBool) anim.SetBool(animTrigger, true);
        else anim.SetTrigger(animTrigger);

        yield return triggerDelay;
        action.OnAnimationTrigger();

        yield return finishDelay;
        action.OnAnimationFinish();

        if (isBool) anim.SetBool(animTrigger, false);

        activeSequence = null;
    }
}
