using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Item_Cart : ItemBase
{
    [Header("Cart")]
    [SerializeField] CharacterController cartCC;
    [SerializeField] Transform parent;
    [SerializeField] Transform wheelPosition;
    [SerializeField] Transform handlePosition;
    [SerializeField] Transform itemStoragePosition;
    [SerializeField] Transform rHandPos;
    [SerializeField] Transform lHandPos;
    [SerializeField] Transform wheelPivot;
    [SerializeField] Vector3 grabRotationOffsetEuler;

    [SerializeField] float collideDelay = 0.2f;
    [SerializeField] AudioSource audioSrc;
    [SerializeField] AudioSFX collideSFX;
    [SerializeField] SoundLoudness collideLoudness;
    [SerializeField] AudioSource wheelSrc;
    [SerializeField] AudioSFX rollingSFX;
    [SerializeField] SoundLoudness rollingLoudness;

    [SerializeField] float breakDistance = 2f;
    [SerializeField] float moveSpeed = 4f;
    [SerializeField] float turnSpeed = 60f;
    [SerializeField] float movementThreshold = 0.1f;
    [SerializeField] float wheelRadius = 0.15f;

    [Header("Raycast Collision")]
    [SerializeField] Transform[] raycastTs;
    [SerializeField] float wallPushStrength = 8f;
    [SerializeField] float raycastLength = 0.55f;
    [SerializeField] LayerMask cartBlockingLayers;

    readonly HashSet<ItemBase> storedItems = new();

    PlayerData currentDriver = null;
    CartPlayerController cartController = null;

    Quaternion grabRotationOffset;
    float cartVerticalVelocity = 0f;
    float playerVerticalVelocity = 0f;
    float lastForwardSign = 1f;
    float collideTimer = 0f;
    int currentRayIndex = 0;
    Vector3 accumulatedPush = Vector3.zero;

    private void Awake() => grabRotationOffset = Quaternion.Euler(grabRotationOffsetEuler);

    [Server]
    void Update()
    {
        if (currentDriver == null) return;
        Vector3 offset = new Vector3(0, 0.5f, 0);
        if (Vector3.Distance(handlePosition.position, currentDriver.transform.position) > breakDistance ||
            Physics.Linecast(transform.position + offset, currentDriver.transform.position + offset, cartBlockingLayers))
        {
            ServerReleaseDriver();
            return;
        }

        TickCartMovement();
    }

    [Server]
    void TickCartMovement()
    {
        Vector2 input = cartController?.CurrentInput ?? Vector2.zero;

        float forward = input.y;
        float turn = input.x;

        if (Mathf.Abs(forward) > movementThreshold)
            lastForwardSign = Mathf.Sign(forward);

        if (Mathf.Abs(forward) > movementThreshold)
        {
            float yRot = turn * turnSpeed * Time.deltaTime * Mathf.Sign(forward);
            RotateAroundHandle(yRot);
        }
        else if (Mathf.Abs(turn) > movementThreshold)
        {
            float yRot = turn * turnSpeed * Time.deltaTime * 0.3f * lastForwardSign;
            RotateAroundHandle(yRot);
        }

        float speed = Mathf.Abs(forward) > movementThreshold ? lastForwardSign * moveSpeed : 0f;
        Vector3 horizontalDelta = speed * Time.deltaTime * parent.forward;
        bool isMoving = Mathf.Abs(speed) > movementThreshold;

        TickOneRaycast();
        horizontalDelta += accumulatedPush * Time.deltaTime;
        accumulatedPush = Vector3.Lerp(accumulatedPush, Vector3.zero, Time.deltaTime * wallPushStrength);

        if (cartCC.isGrounded)
            cartVerticalVelocity = -2f;
        else
            cartVerticalVelocity += Physics.gravity.y * Time.deltaTime;

        if (currentDriver.Player_Movement.IsGrounded_)
            playerVerticalVelocity = -2f;
        else
            playerVerticalVelocity += Physics.gravity.y * Time.deltaTime;

        Vector3 cartMove = horizontalDelta;
        cartMove.y = cartVerticalVelocity * Time.deltaTime;
        cartCC.Move(cartMove);

        Vector3 playerMove = horizontalDelta;
        playerMove.y = playerVerticalVelocity * Time.deltaTime;
        currentDriver.Character_Controller.Move(playerMove);

        float distanceMoved = new Vector3(horizontalDelta.x, 0f, horizontalDelta.z).magnitude;
        float wheelDegrees = distanceMoved / (2f * Mathf.PI * wheelRadius) * 360f;
        wheelPivot.Rotate(0f, wheelDegrees * lastForwardSign, 0f, Space.Self);

        currentDriver.Player_Movement.ServerUpdateAnimationState(Mathf.Abs(speed));

        rb.isKinematic = true;

        float xRot = Quaternion.LookRotation(wheelPosition.position - currentDriver.transform.position).eulerAngles.x;
        parent.SetPositionAndRotation(
            cartCC.transform.position,
            Quaternion.Euler(xRot, parent.eulerAngles.y, 0f));

        currentDriver.transform.rotation = Quaternion.Euler(0f, parent.eulerAngles.y, 0f);

        Vector3 handleWorldPos = handlePosition.position;

        currentDriver.Character_Controller.enabled = false;
        currentDriver.transform.position = new Vector3(
            handleWorldPos.x,
            currentDriver.transform.position.y,
            handleWorldPos.z);
        currentDriver.Character_Controller.enabled = true;

        RpcSetRollingAudio(isMoving);
    }

    [Server]
    void TickOneRaycast()
    {
        if (raycastTs == null || raycastTs.Length == 0) return;

        currentRayIndex = (currentRayIndex + 1) % raycastTs.Length;
        Transform ray = raycastTs[currentRayIndex];
        if (ray == null) return;

        if (Physics.Raycast(ray.position, ray.forward, out RaycastHit hit, raycastLength,
                cartBlockingLayers, QueryTriggerInteraction.Ignore))
        {
            float penetration = raycastLength - hit.distance;
            Vector3 push = penetration * wallPushStrength * -ray.forward;
            push.y = 0f;
            accumulatedPush += push;

            if (collideTimer <= 0f)
            {
                collideTimer = collideDelay;
                RpcPlayCollideSFX();
            }
        }

        if (collideTimer > 0f)
            collideTimer -= Time.deltaTime;
    }

    void RotateAroundHandle(float yDegrees)
    {
        Vector3 pivot = handlePosition.position;
        pivot.y = parent.position.y;

        Quaternion rot = Quaternion.Euler(0f, yDegrees, 0f);

        Vector3 offset = parent.position - pivot;
        float xRot = Quaternion.LookRotation(wheelPosition.position - currentDriver.transform.position).eulerAngles.x;
        parent.position = pivot + rot * offset;
        parent.rotation = rot * parent.rotation;
        parent.rotation = Quaternion.Euler(xRot, parent.eulerAngles.y, 0f);

        cartCC.enabled = false;
        cartCC.transform.position = parent.position;
        cartCC.enabled = true;
    }

    [Server]
    public override void OnInteract(PlayerData sourceData)
    {
        StartCoroutine(CheckForHold(sourceData));
    }

    public override void OnTapInteract(PlayerData sourceData)
    {
        if (sourceData.PlayerInventory.ItemEquipped)
        {
            TryStoreItem(sourceData);
            return;
        }
    }

    [Server]
    public override void OnHoldInteract(PlayerData sourceData)
    {
        if (currentDriver != null)
        {
            if (currentDriver == sourceData) ReleaseDriver();
            return;
        }
        AssignDriver(sourceData);
    }

    [Server]
    void TryStoreItem(PlayerData sourceData)
    {
        var item = sourceData.PlayerInventory.EquippedItem;
        if (item == null) return;

        sourceData.PlayerInventory.RemoveCurrentItem();
        item.transform.position = itemStoragePosition.position;
        item.AddForce(Vector3.zero, ForceMode.VelocityChange);
    }

    [Server]
    void AssignDriver(PlayerData player)
    {
        if (player.PlayerInventory.HasTwoHandedEquipped) return;

        currentDriver = player;
        cartController = new CartPlayerController(this);
        cartVerticalVelocity = 0f;
        playerVerticalVelocity = 0f;

        Vector3 toPlayer = player.transform.position - parent.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(-toPlayer.normalized);
            parent.rotation = Quaternion.Euler(
                parent.eulerAngles.x,
                targetRot.eulerAngles.y,
                parent.eulerAngles.z);

            cartCC.enabled = false;
            cartCC.transform.rotation = parent.rotation;
            cartCC.enabled = true;
        }

        parent.rotation *= grabRotationOffset;

        rb.isKinematic = true;
        rb.linearVelocity = Vector3.zero;
        cartCC.enabled = true;

        player.InputHandler.SetController(cartController);
        currentDriver.InteractionDetection_.ServerSetTargetOnly(netId, true);
        player.Character_Controller.enabled = false;
        player.transform.SetPositionAndRotation(handlePosition.position, handlePosition.rotation);
        player.Character_Controller.enabled = true;

        RpcSetupDriverIK(player.netId);
    }

    public void ReleaseDriver()
    {
        if (currentDriver == null) return;
        ServerReleaseDriver();
    }

    [Server]
    void ServerReleaseDriver()
    {
        if (currentDriver == null) return;

        rb.isKinematic = false;
        cartCC.enabled = false;
        cartVerticalVelocity = 0f;
        playerVerticalVelocity = 0f;

        PlayerData released = currentDriver;
        currentDriver.InteractionDetection_.ServerSetTargetOnly(netId, false);
        currentDriver = null;
        cartController = null;

        released.InputHandler.ReleaseToDefault();

        RpcReleaseDriverIK(released.netId);
        RpcReleaseDriver(released.netId);
        RpcStopRollingAudio();
    }

    [ClientRpc]
    void RpcSetupDriverIK(uint playerNetId)
    {
        NetworkClient.spawned.TryGetValue(playerNetId, out var ni);
        if (ni == null) return;

        if (!ni.TryGetComponent<PlayerData>(out var player)) return;

        var rigging = player.Skin_Data.Rigging_Manager;

        rigging.SetRightHandTarget(rHandPos, false);
        rigging.SetLeftHandTarget(lHandPos);
    }

    [ClientRpc]
    void RpcReleaseDriverIK(uint playerNetId)
    {
        NetworkClient.spawned.TryGetValue(playerNetId, out var ni);
        if (ni == null) return;

        if (!ni.TryGetComponent<PlayerData>(out var player)) return;

        var rigging = player.Skin_Data.Rigging_Manager;

        rigging.SetRightHandTarget(null, false);
        rigging.SetLeftHandTarget(null);
    }

    public void OnPlayerReleaseControl(PlayerData player)
    {
        if (isServer && currentDriver != null)
            ServerReleaseDriver();
    }

    [ClientRpc]
    void RpcReleaseDriver(uint playerNetId)
    {
        NetworkClient.spawned.TryGetValue(playerNetId, out var ni);
        if (ni == null) return;

        var player = ni.GetComponent<PlayerData>();
        if (player == null || !player.isLocalPlayer) return;

        player.InputHandler.ReleaseToDefault();
    }

    public void OnDriverJump()
    {
        if (currentDriver == null) return;

        currentDriver.Player_Movement.CmdSendJumpInput();
        ReleaseDriver();
    }

    public override void OnPickUp()
    {

    }

    [Server]
    public void TeleportWithDriver(Vector3 position)
    {
        if (currentDriver == null) return;

        cartVerticalVelocity = 0f;
        playerVerticalVelocity = 0f;

        cartCC.enabled = false;
        parent.position = position;
        cartCC.enabled = true;

        Vector3 offset = position - parent.position;
        foreach (var item in storedItems)
        {
            if (item == null) continue;
            item.transform.position += offset;
        }

        currentDriver.Character_Controller.enabled = false;
        currentDriver.transform.position = handlePosition.position;
        currentDriver.Character_Controller.enabled = true;
    }

    [ClientRpc]
    void RpcPlayCollideSFX()
    {
        AudioManager.Instance.PlayOneShot(audioSrc, collideSFX, gameObject, collideLoudness);
    }

    [ClientRpc]
    void RpcSetRollingAudio(bool isMoving)
    {
        if (wheelSrc == null || rollingSFX == null) return;
        if (!isMoving && !wheelSrc.isPlaying) return;

        AudioManager.Instance.PlayControllerSFX(
            wheelSrc,
            rollingSFX,
            isMoving ? AudioManager.PlayState.Play : AudioManager.PlayState.Pause,
            gameObject,
            rollingLoudness);
    }

    [ClientRpc]
    void RpcStopRollingAudio()
    {
        if (wheelSrc == null) return;
        wheelSrc.Stop();
        AudioManager.Instance.StopControlledSFX(wheelSrc);
    }

    public void OnTriggerEnterEvent(Collider other)
    {
        if (!isServer || !other.CompareTag("Item")) return;
        var item = other.GetComponent<ItemBase>();
        if (item == null || item.HasOwner) return;
        item.transform.SetParent(transform);
        storedItems.Add(item);
        Debug.Log("[Cart] Trigger enter");
    }

    public void OnTriggerExitEvent(Collider other)
    {
        if (!isServer) return;
        if (other.TryGetComponent<ItemBase>(out var item))
        {
            if (item.transform.parent == transform)
                item.transform.parent = null;
            storedItems.Remove(item);
        }
    }

    public void OnDeath(AttackEvent attackEvent)
    {
        if (!isServer) return;

        if (currentDriver != null)
            ServerReleaseDriver();

        foreach (var item in storedItems)
        {
            if (item == null) continue;
            item.transform.SetParent(null);
            item.HasOwner = false;
        }
        storedItems.Clear();

        RpcOnCartDeath();

        StartCoroutine(DelayedDestroy());
    }

    [ClientRpc]
    void RpcOnCartDeath()
    {
        if (wheelSrc != null)
        {
            wheelSrc.Stop();
            AudioManager.Instance.StopControlledSFX(wheelSrc);
        }

        if (audioSrc != null)
            audioSrc.Stop();
    }

    IEnumerator DelayedDestroy()
    {
        yield return new WaitForSeconds(0.2f);
        NetworkServer.Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        if (handlePosition != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(handlePosition.position, 0.2f);
            Gizmos.DrawLine(transform.position, handlePosition.position);
        }

        if (raycastTs == null) return;

        Gizmos.color = new Color(1f, 0.4f, 0f, 0.8f);
        foreach (var t in raycastTs)
        {
            if (t == null) continue;
            Gizmos.DrawRay(t.position, t.forward * raycastLength);
        }
    }
}
