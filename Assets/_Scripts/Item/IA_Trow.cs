using UnityEngine;
using UnityEngine.Events;

public class IA_Trow : ItemAction
{
    [SerializeField] AnimationTrigger animTrigger;
    [SerializeField] float trowForce = 10f;

    public UnityEvent OnTrow;

    public override void Cancel()
    {

    }

    public override void Execute()
    {
        if (!isServer) return;
        item.PData.Player_Movement.ServerForceAimAt(item.PData.LookCameraTarget.position);
        item.AnimationModule.PlayPrimary(this, item.PData, animTrigger.Trigger);
    }

    public override void OnAnimationTrigger()
    {
        TryPlaySFX(animTrigger, ActionTiming.Trigger);
        if (!isServer) return;

        item.PData.PlayerInventory.RemoveCurrentItem();

        item.transform.SetParent(null);
        item.RigidBody.isKinematic = false;
        item.SetCollider(true);

        item.AddForce(item.PData.PlayerCamera.transform.forward * trowForce + 
            item.PData.Player_Movement.GetVelocityClean() * 
            Mathf.Clamp(item.RigidBody.mass, 1, 20), ForceMode.Impulse);

        OnTrow?.Invoke();
    }

    public override void OnAnimationFinish()
    {
        base.OnAnimationFinish();
        if (isServer)
            item.LastPlayer.Player_Movement.ServerClearForcedAim();
    }
}
