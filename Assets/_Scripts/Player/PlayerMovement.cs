using Mirror;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using static FootstepSurfacesSO;

public class PlayerMovement : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] PlayerData pData;

    [Header("Ground Detection")]
    [SerializeField] Transform feet;
    [SerializeField] float groundRadius;
    [SerializeField] float raycastLenght = 0.2f;

    [Header("Movement")]
    [SerializeField] float gravity;
    [SerializeField] float groundedGravity = -5f;
    [SerializeField] float coyoteTime = 0.2f;
    [SerializeField] float walkSpeed = 5f;
    [SerializeField] float sprintSpeed = 9f;
    [SerializeField] float crouchSpeed = 2f;
    [SerializeField] float movSpeed = 5f;
    [SerializeField] float rotationSpeed = 10f;
    [SerializeField] float animTransitionSpeed = 1f;
    [SerializeField] float speedMultiplier = 1f;
    [SerializeField] float friction = 3f;
    [SerializeField] float staminaConsuption_Sprint = 10f;
    [SerializeField] float staminaConsuption_Jump = 20f;
    [SerializeField] float airRotationSpd = 10f;
    [SerializeField] float airDecelerationMultiplier = 0.98f;
    Dictionary<int, float> speedModifiers = new();
    int nextModifierId = 0;
    Vector3 externalMomentum = Vector3.zero;

    [Header("Crouching")]
    [SerializeField] float crouchHeight = 1f;
    [SerializeField] Vector3 crouchCenter = new(0f, 0.5f, 0f);
    [SerializeField] float normalHeight = 2f;
    [SerializeField] Vector3 normalCenter = new(0f, 1f, 0f);

    [SerializeField] float normalCamTarget = 1.1f;
    [SerializeField] float crouchCamTarget = 0.5f;

    [Header("Jumping")]
    [SerializeField] float jumpForce;

    [Header("Audio")]
    [SerializeField] PlayerFootstepHandler footstepHandler;
    [SerializeField] float walkDelay;
    [SerializeField] float sprintDelay;
    [SerializeField] float timer;

    [Header("Flags")]
    [SerializeField] float groundedTime;
    [SerializeField] float jumpTime;
    [SerializeField] bool _isGrounded;
    [SerializeField] bool _tryJump;
    [SerializeField] bool _isSprinting = false;
    [SerializeField] bool _isFalling;
    [SerializeField] bool _wantsToCrouch = false;
    [SerializeField] bool _wantsToSprint = false;
    [SerializeField] bool _wantsToUncrouch = false;

    [SerializeField] bool debug = false;

    public bool IsCrouching { get; private set; } = false;
    public bool IsGrounded_ => _isGrounded;

    [Header("Network Variables")]
    [SyncVar(hook = nameof(OnStandMovBlendChanged))]
    float standMovBlend;
    [SyncVar(hook = nameof(OnCrouchMovBlendChanged))]
    float crouchMovBlend;
    [SyncVar(hook = nameof(OnStandCrouchChanged))]
    float standCrouchBlend;

    [SerializeField] Collider[] res;
    Vector2 movementInput;
    Vector3 velocity;
    Vector3 airborneVelocity;
    bool wasGroundedLastFrame;
    Vector3 forcedAimDirection;
    readonly float forcedAimSpeed = 20f;

    public UnityEvent<PlayerData> OnPlayerLanded;

    public Vector3 GetVelocity() => new(
        velocity.x,
        velocity.y + Mathf.Max(0f, externalMomentum.y),
        velocity.z);

    public Vector3 GetVelocityClean()
    {
        if (IsGrounded_)
            return new(
                velocity.x,
                velocity.y - groundedGravity + Mathf.Max(0f, externalMomentum.y),
                velocity.z);

        return GetVelocity();
    }
    bool IsGrounded()
    {
        res = Physics.OverlapSphere(feet.position, groundRadius, pData.GroundMask);
        for (int i = 0; i < res.Length; i++)
        {
            if (res[i] == pData.PlayerCollider) continue;
            footstepHandler.SetSurface(res[i].tag);
            return true;
        }

        if (Physics.Raycast(feet.position + Vector3.down * groundRadius, Vector3.down,
                out RaycastHit hit, raycastLenght, pData.PlayerMask))
        {
            footstepHandler.SetSurface(hit.collider.tag);
            return true;
        }

        return false;
    }

    bool IsSomethingAbove() => Physics.CheckSphere(pData.Head.position, groundRadius, pData.IgnorePlayer);

    private void Start()
    {
        normalCenter = pData.Character_Controller.center;
        normalHeight = pData.Character_Controller.height;

        crouchCenter = normalCenter * 0.5f;
        crouchHeight = normalHeight * 0.5f;

        movSpeed = walkSpeed;

        pData.CameraTarget.localPosition = new Vector3(0, normalCamTarget, 0);

        if (isServer)
        {
            groundedTime = IsGrounded() ? coyoteTime : 0f;
            _isGrounded = groundedTime > 0f;
            _isFalling = !_isGrounded;
            wasGroundedLastFrame = _isGrounded;
        }
    }

    #region SyncVar Hooks
    void OnStandMovBlendChanged(float oldValue, float newValue)
    {
        if (pData.Skin_Data.CharacterAnimator != null)
            pData.Skin_Data.CharacterAnimator.SetFloat("StandMov", newValue);
    }

    void OnCrouchMovBlendChanged(float oldValue, float newValue)
    {
        if (pData.Skin_Data.CharacterAnimator != null)
            pData.Skin_Data.CharacterAnimator.SetFloat("CrouchMov", newValue);
    }

    void OnStandCrouchChanged(float oldValue, float newValue)
    {
        if (pData.Skin_Data.CharacterAnimator != null)
            pData.Skin_Data.CharacterAnimator.SetFloat("StandCrouch", newValue);
    }
    #endregion

    #region Commands
    [Command]
    public void CmdSendMovementInput(Vector2 input) => movementInput = input;

    [Command]
    public void CmdSendJumpInput()
    {
        if (pData._LockPlayer) return;
        jumpTime = 0.2f;
    }

    [Command]
    public void CmdStartSprint()
    {
        if (pData._LockPlayer) return;

        if (IsCrouching)
        {
            if (IsSomethingAbove())
            {
                _wantsToSprint = true;
                return;
            }
            else
            {
                ServerStopCrouch();
                _wantsToCrouch = true;
            }
        }

        if (Mathf.Approximately(pData.Player_Stats.currentStamina, 0))
            return;

        _isSprinting = true;
        movSpeed = sprintSpeed;
    }

    [Command]
    public void CmdStopSprint()
    {
        if (IsCrouching && IsSomethingAbove())
        {
            _wantsToSprint = false;
            return;
        }

        _isSprinting = false;
        movSpeed = walkSpeed;

        if (_wantsToCrouch)
        {
            ServerStartCrouch();
            _wantsToCrouch = false;
        }
    }

    [Command]
    public void CmdStartCrouchAction() => ServerStartCrouch();

    [Command]
    public void CmdStopCrouch() => ServerStopCrouch();
    #endregion

    #region Helpers
    void ServerStartCrouch()
    {
        if (_isSprinting)
        {
            _wantsToCrouch = true;
            return;
        }

        StartCrouch();
        RpcStartCrouch();
    }

    void ServerStopCrouch()
    {
        if (_isSprinting)
        {
            _wantsToCrouch = false;
            return;
        }

        if (IsSomethingAbove())
        {
            _wantsToUncrouch = true;
            return;
        }

        StopCrouch();
        RpcStopCrouch();
    }

    [ClientRpc]
    void RpcStartCrouch() => StartCrouch();

    [ClientRpc]
    void RpcStopCrouch() => StopCrouch();

    void StartCrouch()
    {
        IsCrouching = true;
        pData.Skin_Data.CharacterAnimator.SetBool("Crouch", IsCrouching);
        pData.Character_Controller.height = crouchHeight;
        pData.Character_Controller.center = crouchCenter;
        pData.CameraTarget.localPosition = new Vector3(0, crouchCamTarget, 0);
    }

    void StopCrouch()
    {
        IsCrouching = false;
        pData.Skin_Data.CharacterAnimator.SetBool("Crouch", IsCrouching);
        pData.Character_Controller.height = normalHeight;
        pData.Character_Controller.center = normalCenter;
        pData.CameraTarget.localPosition = new Vector3(0, normalCamTarget - 0.1f, 0);
    }
    #endregion

    #region Debuff/Buff
    public int AddSpeedModifier(float multiplier)
    {
        int id = nextModifierId++;
        speedModifiers[id] = multiplier;
        RecalculateSpeedMultiplier();
        return id;
    }

    public void RemoveSpeedModifier(int id)
    {
        if (speedModifiers.Remove(id))
            RecalculateSpeedMultiplier();
    }

    void RecalculateSpeedMultiplier()
    {
        float final = 1f;
        foreach (var mod in speedModifiers.Values)
            final *= mod;

        speedMultiplier = Mathf.Max(0f, final);
    }
    #endregion

    void Update()
    {
        if (!isServer) return;
        if (pData.Skin_Data.Ragdoll_Manager.IsKnocked ||
            pData.CameraPivot == null ||
            !pData.Character_Controller.enabled)
        {
            jumpTime = 0f;
            _tryJump = false;
            return;
        }

        if (!pData.InputHandler.IsDefaultController)
        {
            groundedTime = IsGrounded() ? coyoteTime : groundedTime > 0 ? groundedTime - Time.deltaTime : 0;
            _isGrounded = groundedTime > 0f;
            return;
        }

        groundedTime = IsGrounded() ? coyoteTime : groundedTime > 0 ? groundedTime - Time.deltaTime : 0;
        _isGrounded = groundedTime > 0f;

        jumpTime = jumpTime > 0 ? jumpTime - Time.deltaTime : 0;
        _tryJump = jumpTime > 0f;

        Vector3 move = Vector3.zero;

        if (!pData._LockPlayer)
        {
            if (_isSprinting && Mathf.Approximately(pData.Player_Stats.currentStamina, 0))
                CmdStopSprint();

            UpdateMoveSpeed();

            bool somethingAbove = false;
            if (_wantsToUncrouch || _wantsToSprint || (_isGrounded && _tryJump))
                somethingAbove = IsSomethingAbove();

            if (_wantsToUncrouch || _wantsToSprint)
            {
                if (!somethingAbove)
                {
                    ServerStopCrouch();
                    if (_wantsToSprint)
                    {
                        _isSprinting = true;
                        movSpeed = sprintSpeed;
                    }

                    _wantsToSprint = false;
                    _wantsToUncrouch = false;
                }
            }

            bool jpd = false;
            if (_isGrounded && _tryJump && !somethingAbove)
            {
                jpd = true;
                externalMomentum.y = 0f;

                velocity.y = Mathf.Approximately(pData.Player_Stats.currentStamina, 0)
                    ? jumpForce * 0.75f
                    : jumpForce;

                jumpTime = 0f; _tryJump = false;
                groundedTime = 0f; _isGrounded = false;
                pData.Player_Stats.ModifyStamina(-staminaConsuption_Jump);

                pData.Skin_Data.CharacterAnimator.SetBool("Jump", true);

                RPC_PlayFootstep(GetIndexOfAudioSource(pData.Modest_AS), 1, SoundLoudness.Moderate, FootstepClipType.Jump);
            }

            Vector3 camForward = pData.CameraPivot.forward;
            Vector3 camRight = pData.CameraPivot.right;
            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            move = (camForward * movementInput.y + camRight * movementInput.x).normalized;
            if (pData._IsPlayerAimLocked)
            {
                Quaternion targetRotation = Quaternion.LookRotation(forcedAimDirection);
                transform.rotation = Quaternion.Lerp(
                    transform.rotation,
                    targetRotation,
                    forcedAimSpeed * Time.deltaTime
                );
            }
            else if (!Mathf.Approximately(move.magnitude, 0) && !pData._IsPlayerAimLocked)
            {
                Quaternion targetRotation = Quaternion.LookRotation(move);
                transform.rotation = Quaternion.Lerp(
                    transform.rotation,
                    targetRotation,
                    rotationSpeed * Time.deltaTime
                );

                pData.EmoteManager.PlayerMoves();
            }

            move *= movSpeed * speedMultiplier * (pData.Player_Stats.speed / 100f);

            if (jpd)
                airborneVelocity = new Vector3(move.x + externalMomentum.x, 0f, move.z + externalMomentum.z);

            if (_isSprinting && movementInput.magnitude > 0.1f && _isGrounded)
                pData.Player_Stats.ModifyStamina(-staminaConsuption_Sprint * Time.deltaTime);

            FootstepUpdate();
        }

        if (_isGrounded && velocity.y <= 0 && externalMomentum.y <= 0)
            velocity.y = groundedGravity;
        else
            velocity.y += gravity * Time.deltaTime;

        if (wasGroundedLastFrame && !_isGrounded)
            airborneVelocity = new Vector3(move.x + externalMomentum.x, 0f, move.z + externalMomentum.z);

        if (!wasGroundedLastFrame && _isGrounded)
        {
            externalMomentum.y = 0f;
            OnPlayerLanded?.Invoke(pData);

            float verticalVelocity = Mathf.Abs(velocity.y);
            SoundLoudness landLoudness;
            if (verticalVelocity < 2)
                landLoudness = SoundLoudness.Quiet;
            else if (verticalVelocity < 4)
                landLoudness = SoundLoudness.Moderate;
            else if (verticalVelocity < 8)
                landLoudness = SoundLoudness.Average;
            else
                landLoudness = SoundLoudness.Loud;

            float volMult = landLoudness switch
            {
                SoundLoudness.Quiet => 0.2f,
                SoundLoudness.Moderate => 0.4f,
                SoundLoudness.Average => 0.6f,
                SoundLoudness.Loud => 1f,
                _ => 1f
            };

            RPC_PlayFootstep(GetIndexOfAudioSource(pData.Loud_AS), volMult, landLoudness, FootstepClipType.Land);
        }

        wasGroundedLastFrame = _isGrounded;

        if (_isGrounded)
        {
            velocity.x = move.x + externalMomentum.x;
            velocity.z = move.z + externalMomentum.z;
        }
        else
        {
            Vector3 horizontalAirVel = new(airborneVelocity.x, 0f, airborneVelocity.z);

            if (move.sqrMagnitude > 0.001f)
            {
                Vector3 desiredDir = move.normalized;
                Vector3 currentDir = horizontalAirVel.normalized;
                float currentSpeed = horizontalAirVel.magnitude;

                float minAirSpeed = crouchSpeed * speedMultiplier;
                if (currentSpeed < minAirSpeed)
                    currentSpeed = Mathf.MoveTowards(currentSpeed, minAirSpeed, movSpeed * Time.deltaTime * 2f);

                float dot = Vector3.Dot(currentDir, desiredDir);
                if (dot < -0.3f)
                    currentSpeed *= airDecelerationMultiplier;

                Vector3 newDir = Vector3.RotateTowards(
                    currentDir.sqrMagnitude < 0.001f ? desiredDir : currentDir,
                    desiredDir,
                    airRotationSpd * Mathf.Deg2Rad * Time.deltaTime,
                    0f
                );

                horizontalAirVel = newDir * currentSpeed;
                airborneVelocity = horizontalAirVel;
            }

            velocity.x = airborneVelocity.x + externalMomentum.x;
            velocity.z = airborneVelocity.z + externalMomentum.z;
        }

        if (externalMomentum.y > 0)
            externalMomentum.y += gravity * Time.deltaTime;
        else
            externalMomentum.y = 0f;

        Vector3 finalVelocity = new(
            velocity.x,
            velocity.y + Mathf.Max(0f, externalMomentum.y),
            velocity.z
        );
        pData.Character_Controller.Move(finalVelocity * Time.deltaTime);

        externalMomentum.x = Mathf.Lerp(externalMomentum.x, 0f, Time.deltaTime * friction);
        externalMomentum.z = Mathf.Lerp(externalMomentum.z, 0f, Time.deltaTime * friction);

        if (pData.Skin_Data.CharacterAnimator == null) return;

        if (!_isGrounded)
        {
            pData.Skin_Data.CharacterAnimator.SetBool("Falling", true);
        }
        else
        {
            pData.Skin_Data.CharacterAnimator.SetBool("Falling", false);
            pData.Skin_Data.CharacterAnimator.SetBool("Jump", false);
        }

        _isFalling = !_isGrounded;

        float speed = new Vector2(velocity.x, velocity.z).magnitude;
        if (IsCrouching)
        {
            standCrouchBlend = Mathf.MoveTowards(
                pData.Skin_Data.CharacterAnimator.GetFloat("StandCrouch"), 1f, Time.deltaTime * animTransitionSpeed);
            float targetBlend = Mathf.Approximately(speed, 0f) ? 0f : speed;
            crouchMovBlend = Mathf.MoveTowards(crouchMovBlend, targetBlend, Time.deltaTime * animTransitionSpeed);
        }
        else
        {
            standCrouchBlend = Mathf.MoveTowards(
                pData.Skin_Data.CharacterAnimator.GetFloat("StandCrouch"), 0f, Time.deltaTime * animTransitionSpeed);
            float targetBlend = Mathf.Approximately(speed, 0f) ? 0f : speed;
            standMovBlend = Mathf.MoveTowards(standMovBlend, targetBlend, Time.deltaTime * animTransitionSpeed);
        }

        pData.Skin_Data.CharacterAnimator.SetFloat("StandMov", standMovBlend);
        pData.Skin_Data.CharacterAnimator.SetFloat("CrouchMov", crouchMovBlend);
        pData.Skin_Data.CharacterAnimator.SetFloat("StandCrouch", standCrouchBlend);
    }

    [Server]
    public void ServerUpdateAnimationState(float horizontalSpeed)
    {
        if (pData.Skin_Data.CharacterAnimator == null) return;

        pData.Skin_Data.CharacterAnimator.SetBool("Falling", !_isGrounded);
        if (_isGrounded) pData.Skin_Data.CharacterAnimator.SetBool("Jump", false);
        _isFalling = !_isGrounded;

        float targetBlend = Mathf.Approximately(horizontalSpeed, 0f) ? 0f : horizontalSpeed;

        if (IsCrouching)
        {
            standCrouchBlend = Mathf.MoveTowards(standCrouchBlend, 1f, Time.deltaTime * animTransitionSpeed);
            crouchMovBlend = Mathf.MoveTowards(crouchMovBlend, targetBlend, Time.deltaTime * animTransitionSpeed);
        }
        else
        {
            standCrouchBlend = Mathf.MoveTowards(standCrouchBlend, 0f, Time.deltaTime * animTransitionSpeed);
            standMovBlend = Mathf.MoveTowards(standMovBlend, targetBlend, Time.deltaTime * animTransitionSpeed);
        }

        pData.Skin_Data.CharacterAnimator.SetFloat("StandMov", standMovBlend);
        pData.Skin_Data.CharacterAnimator.SetFloat("CrouchMov", crouchMovBlend);
        pData.Skin_Data.CharacterAnimator.SetFloat("StandCrouch", standCrouchBlend);
    }

    void UpdateMoveSpeed()
    {
        if (IsCrouching)
            movSpeed = crouchSpeed;
        else if (_isSprinting)
            movSpeed = sprintSpeed;
        else
            movSpeed = walkSpeed;
    }

    public void AddMomentum(Vector3 force)
    {
        if (force.y > 0)
        {
            jumpTime = 0f;
            _tryJump = false;
            groundedTime = 0f;
            _isGrounded = false;
        }

        externalMomentum += force;
    }

    public Vector3 KillMomentum()
    {
        Vector3 momentum = externalMomentum;
        externalMomentum = Vector3.zero;
        return momentum;
    }

    public void ForceJump(float force)
    {
        groundedTime = 0f; 
        _isGrounded = false;
        wasGroundedLastFrame = false;

        jumpTime = 0f; 
        _tryJump = false;

        velocity.y = force;
    }

    void FootstepUpdate()
    {
        if (!_isGrounded || movementInput.magnitude < 0.1f)
        {
            timer = _isSprinting ? sprintDelay : walkDelay;
            return;
        }

        timer -= Time.deltaTime;
        if (timer > 0f) return;

        FootstepClipType type;
        AudioSource src;
        SoundLoudness loudness;
        float multiplier = 1f;

        if (IsCrouching)
        {
            type = FootstepClipType.Walk;
            src = pData.Quiet_AS;
            loudness = SoundLoudness.NoSound;
            multiplier = 0.4f;
        }
        else if (_isSprinting)
        {
            type = FootstepClipType.Sprint;
            src = pData.Loud_AS;
            loudness = SoundLoudness.Average;
        }
        else
        {
            type = FootstepClipType.Walk;
            src = pData.Modest_AS;
            loudness = SoundLoudness.Moderate;
        }

        RPC_PlayFootstep(GetIndexOfAudioSource(src), multiplier, loudness, type);

        timer = IsCrouching ? walkDelay * 1.5f : (_isSprinting ? sprintDelay : walkDelay);
    }

    [ClientRpc]
    void RPC_PlayFootstep(int srcIndex, float multiplier, SoundLoudness loudness, FootstepClipType type)
    {
        AudioManager.Instance.PlayOneShot(GetAudioSourceByIndex(srcIndex),
            footstepHandler.GetNextClip(type), multiplier, gameObject, loudness);
    }

    [Server]
    public void ServerForceAimAt(Vector3 worldPosition)
    {
        Vector3 dir = worldPosition - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        pData._IsPlayerAimLocked = true;
        forcedAimDirection = dir.normalized;

        RpcForceAimAt(dir.normalized);
    }

    [Server]
    public void ServerClearForcedAim()
    {
        pData._IsPlayerAimLocked = false;
        forcedAimDirection = Vector3.zero;
        RpcClearForcedAim();
    }

    [TargetRpc]
    void RpcForceAimAt(Vector3 direction)
    {
        pData.Camera_Movement.ForcePlayerToAimDirection(direction);
    }

    [TargetRpc]
    void RpcClearForcedAim()
    {
        pData.Camera_Movement.ServerClearForcedAim();
    }

    AudioSource GetAudioSourceByIndex(int index)
    {
        if (index == 0) return pData.Quiet_AS;
        if (index == 1) return pData.Modest_AS;
        if (index == 2) return pData.Loud_AS;
        return pData.Modest_AS;
    }

    int GetIndexOfAudioSource(AudioSource src)
    {
        if (src == pData.Quiet_AS) return 0;
        if (src == pData.Modest_AS) return 1;
        if (src == pData.Loud_AS) return 2;
        return 1;
    }

    private void OnDrawGizmosSelected()
    {
        if (!debug) return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(feet.transform.position, groundRadius);
        Gizmos.DrawWireSphere(pData.Head.transform.position, groundRadius);
        Gizmos.DrawLine(feet.position + Vector3.down * groundRadius,
                        feet.position + Vector3.down * groundRadius + Vector3.down * raycastLenght);
    }
}
