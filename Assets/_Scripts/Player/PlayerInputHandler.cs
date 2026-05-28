using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using LethalLive;

public class PlayerInputHandler : NetworkBehaviour
{
    [SerializeField] PlayerData pData;
    bool mWasFree;

    IPlayerController activeController;
    IPlayerController defaultController;

    [SyncVar]
    public bool IsDefaultController = true;

    public Item_Cart GetActiveCart()
    {
        if (activeController is CartPlayerController cartController)
            return cartController.Cart;
        return null;
    }

    void Start()
    {
        if (!isLocalPlayer) return;
        defaultController = new DefaultPlayerController(pData.Player_Movement);
        activeController = defaultController;
    }

    public void RequestReturnMainMenu() => GameManager.Instance.playMod.RequestReturnToMainMenu();

    public void OnPlayerPressEscape(InputAction.CallbackContext context)
    {
        if (!IsDefaultController) return;
        if (!context.started) return;
        if (!isLocalPlayer || GameManager.Instance.lobbyManagerScreen.playerOnLMS == pData.Index) return;

        if (pData.TabletManager.TrySwitchState())
        {
            Cmd_SwitchTabletState(pData.TabletManager.IsActive);
        }
    }

    public void OnForceMouseFreeState(InputAction.CallbackContext context)
    {
        if (context.started)
        {
            if (Cursor.lockState == CursorLockMode.None)
            {
                mWasFree = true;
                return;
            }
            SettingsManager.Instance.SetMouseLockState(false);
        }

        if (context.canceled)
        {
            if (mWasFree) return;
            SettingsManager.Instance.SetMouseLockState(true);
        }
    }

    [Command(requiresAuthority = false)]
    private void Cmd_SwitchTabletState(bool open)
    {
        if (!IsDefaultController) return;

        pData.SetLockPlayer(open);
        pData.Skin_Data.CharacterAnimator.SetBool("Tablet", open);

        if (open)
        {
            pData.Skin_Data.Rigging_Manager.RpcEnableLeftHandChainRig(true);
            pData.Skin_Data.Rigging_Manager.RpcEnableLookAtTabletRig(true);
        }
        else
        {
            pData.Skin_Data.Rigging_Manager.RpcEnableLeftHandChainRig(false);
            pData.Skin_Data.Rigging_Manager.RpcEnableLookAtTabletRig(false);
        }
    }

    [Command]
    public void CmdOnMove(Vector2 input)
    {
        if (pData.Player_Stats.dead || pData.Player_Stats.knocked) return;
        activeController?.OnMove(input);
    }

    [Command]
    public void CmdOnJump()
    {
        if (pData.Player_Stats.dead || pData.Player_Stats.knocked) return;
        activeController?.OnJump();
    }

    [Command]
    public void CmdOnJumpCanceled()
    {
        if (pData.Player_Stats.dead || pData.Player_Stats.knocked) return;
        activeController?.OnJumpCanceled();
    }

    [Command]
    public void CmdOnSprintStart()
    {
        if (pData.Player_Stats.dead || pData.Player_Stats.knocked) return;
        activeController?.OnSprintStart();
    }

    [Command]
    public void CmdOnSprintStop()
    {
        if (pData.Player_Stats.dead || pData.Player_Stats.knocked) return;
        activeController?.OnSprintStop();
    }

    [Command]
    public void CmdOnStartCrouch()
    {
        if (pData.Player_Stats.dead || pData.Player_Stats.knocked) return;
        activeController?.OnCrouchStart();
    }

    [Command]
    public void CmdOnStopCrouch()
    {
        if (pData.Player_Stats.dead || pData.Player_Stats.knocked) return;
        activeController?.OnCrouchStop();
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        if (!isLocalPlayer) return;
        CmdOnMove(context.ReadValue<Vector2>());
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (!isLocalPlayer) return;
        if (context.started) CmdOnJump();
        else if (context.canceled) CmdOnJumpCanceled();
    }

    public void OnStartSprint(InputAction.CallbackContext context)
    {
        if (!isLocalPlayer) return;
        if (context.started) CmdOnSprintStart();
        else if (context.canceled) CmdOnSprintStop();
    }

    public void OnStartCrouch(InputAction.CallbackContext context)
    {
        if (!isLocalPlayer) return;
        if (context.started) CmdOnStartCrouch();
        else if (context.canceled) CmdOnStopCrouch();
    }

    public void SetController(IPlayerController controller)
    {
        if (controller == null) return;
        if (!isServer && !isLocalPlayer) return;

        activeController?.OnLoseControl(pData);
        activeController = controller;
        activeController.OnGainControl(pData);

        IsDefaultController = false;
    }

    public void ReleaseController(IPlayerController controller)
    {
        if (activeController != controller) return;

        activeController.OnLoseControl(pData);
        activeController = defaultController;
        activeController.OnGainControl(pData);

        IsDefaultController = true;
    }

    public void ReleaseToDefault()
    {
        if (IsDefaultController) return;
        if (!isServer && !isLocalPlayer) return;

        pData.Character_Controller.enabled = false;
        Vector3 rot = pData.transform.rotation.eulerAngles;
        rot.x = 0;
        rot.z = 0;
        pData.transform.rotation = Quaternion.Euler(rot);
        pData.Character_Controller.enabled = true;

        activeController?.OnLoseControl(pData);
        activeController = defaultController;
        activeController.OnGainControl(pData);

        IsDefaultController = true;
    }
}
