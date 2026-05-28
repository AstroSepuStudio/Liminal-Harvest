using Mirror;
using UnityEngine;
using WS_ProceduralGeneration;

public class Int_HomewardBeacon : InteractableObject, IMapFollowTarget
{
    public Transform FollowTransform => transform;

    public bool IsAvailable => true;

    public override bool CanBeInteracted()
    {
        bool interactable = this.interactable && !GameManager.Instance.onDeadTime;
        return interactable;
    }

    public override void OnInteract(PlayerData sourceData)
    {
        base.OnInteract(sourceData);

        sourceData.RpcStartTeleport();
    }

    public override void OnStopInteract(PlayerData sourceData)
    {
        base.OnStopInteract(sourceData);

        sourceData.RpcCancelTeleport();
    }

    [Server]
    public void GoBackToOffice(PlayerData sourceData)
    {
        if (GameManager.Instance.onDeadTime) return;

        sourceData.RpcCompleteTeleport();

        Vector3 destination = GameManager.Instance.Teleporter.transform.position;

        var cart = sourceData.InputHandler.GetActiveCart();
        if (cart != null)
        {
            cart.TeleportWithDriver(destination);
        }
        else
        {
            sourceData.Character_Controller.enabled = false;
            sourceData.Character_Controller.transform.position = destination;
            sourceData.Character_Controller.enabled = true;
        }

        sourceData._PlayerInOffice = true;

        DisableCanvas();
        GameManager.Instance.dngMod.OnReturnOffice(sourceData);
    }
}
