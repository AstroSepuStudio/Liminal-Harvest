using Mirror;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class PlayerStats : EntityStats
{
    [Header("Player")]
    public PlayerData pData;

    [SerializeField, Range(0, 100)] float lowHPEffectsThreshold = 40f;
    [SerializeField, Range(0, 100)] float lowHPThreshold = 20f;
    [SerializeField] float healthRecoveryDelay = 10f;
    [SerializeField] float healthRecoveryRate = 1f;
    [SerializeField] float staminaRecoveryDelay = 0.5f;
    [SerializeField] float staminaRecoveryRate = 10f;

    [Header("SFX")]
    [SerializeField] AudioSource audioSrc;
    [SerializeField] AudioSFX[] heartbeatSFXs;

    [Header("FX")]
    [SerializeField] GameObject takeDamageGO;
    [SerializeField] GameObject lowHPGO;

    float healthRecoveryTimer = 0;
    float staminaRecoveryTimer = 0;
    TakeDamageEffect takeDamageFX;
    LowHPEffect lowHPFX;

    [SyncVar] public float maxStamina = 100f;
    [SyncVar(hook = nameof(OnStaminaChanged))] public float currentStamina;

    public UnityEvent OnPlayerKnocked;
    
    float LowHPThreshold => maxHP * (lowHPThreshold / 100f);
    float LowHPFXThreshold => maxHP * (lowHPEffectsThreshold / 100f);

    float heartbeatTimer = 0f;
    float tookDamageTimer = 0;

    Coroutine tookDmgCor;

    #region Lifecycle

    public override void OnStartServer() => ResetStats();

    private void Start()
    {
        if (!pData.isLocalPlayer) return;

        if (takeDamageGO.TryGetComponent(out takeDamageFX))
            GameManager.Instance.ppController.RegisterEffect(takeDamageFX);

        if (lowHPGO.TryGetComponent(out lowHPFX))
            GameManager.Instance.ppController.RegisterEffect(lowHPFX);
    }

    [Server]
    public void ResetStats()
    {
        currentHP = maxHP;
        currentStamina = maxStamina;
        currentKnock = 0f;
        dead = false;
        knocked = false;

        RpcUpdateLowHPEffect(1, false);
    }

    protected override void Update()
    {
        if (dead) return;
        TickHeartbeat();

        if (!isServer) return;

        TickHealthRecovery();
        TickStaminaRecovery();
        TickKnockRecovery();

        if (currentKnock <= ragdollRecoveryValue && pData.Skin_Data.Ragdoll_Manager.IsKnocked)
        {
            pData.Skin_Data.Ragdoll_Manager.DisableRagdoll();
            knocked = false;
        }
    }

    void TickHeartbeat()
    {
        if (!pData.isLocalPlayer || heartbeatSFXs == null || heartbeatSFXs.Length == 0) return;

        float lowHP = LowHPThreshold;
        if (currentHP >= lowHP)
        {
            heartbeatTimer = 0f;
            return;
        }

        float danger = 1f - Mathf.Clamp01(currentHP / lowHP);

        float interval = Mathf.Lerp(1.5f, 0.3f, danger);
        heartbeatTimer -= Time.deltaTime;

        if (heartbeatTimer > 0f) return;

        heartbeatTimer = interval;

        int index = Mathf.Clamp(Mathf.FloorToInt(danger * heartbeatSFXs.Length), 0, heartbeatSFXs.Length - 1);
        var sfx = heartbeatSFXs[index];

        float volumeMultiplier = Mathf.Lerp(0.3f, 1f, danger);
        AudioManager.Instance.PlayOneShot(audioSrc, sfx, volumeMultiplier);
    }

    #endregion

    #region Attack

    [Server]
    public override void ReceiveAttack(AttackEvent source)
    {
        bool sameTeam = source.TeamID != -1 && source.TeamID == (int)pData.Team;

        if (sameTeam)
        {
            if (LobbySettings.Instance.TeamDamage) ApplyDamage(source);
            if (LobbySettings.Instance.TeamKnock) ApplyKnock(source);
        }
        else
        {
            PlaySFX(SFXEvent.TakeDamage);
            ApplyDamage(source);
            ApplyKnock(source);
        }
    }

    #endregion

    #region HP

    private void TickHealthRecovery()
    {
        if (currentHP >= LowHPThreshold) return;
        healthRecoveryTimer = Mathf.Clamp(healthRecoveryTimer + Time.deltaTime, 0f, healthRecoveryDelay);
        if (!Mathf.Approximately(healthRecoveryTimer, healthRecoveryDelay)) return;
        currentHP = Mathf.Clamp(currentHP + Time.deltaTime * healthRecoveryRate, 0f, LowHPThreshold);
        UpdateLowHPState();
    }

    public override void RestoreHealth(float amount)
    {
        base.RestoreHealth(amount);
        if (currentHP >= LowHPThreshold)
            pData.Skin_Data.CharacterAnimator.SetBool("Hurt", false);
        UpdateLowHPState();
    }

    void UpdateLowHPState()
    {
        float lowHPFXThreshold = LowHPFXThreshold;
        bool isLow = currentHP < lowHPFXThreshold;
        float normHP = isLow ? currentHP / lowHPFXThreshold : 1f;

        if (!isLow)
        {
            pData.Skin_Data.CharacterAnimator.SetBool("Hurt", false);
            pData.Skin_Data.CharacterAnimator.SetBool("LowHP", false);
        }
        else
        {
            pData.Skin_Data.CharacterAnimator.SetBool("LowHP", true);
        }

        RpcUpdateLowHPEffect(normHP, isLow);
    }

    [Server]
    public override void ApplyDamage(AttackEvent source)
    {
        if (!GameManager.Instance.gameStarted || !GameManager.Instance.dayMod.dayStarted)
            return;

        if (dead) return;

        if (tookDmgCor == null)
            tookDmgCor = StartCoroutine(TookDamageCoroutine());
        else
            tookDamageTimer = 1;

        float normDmg = source.AttackStat_.AttackDamage / 100f;
        RpcTriggerDamageEffect(normDmg);
        base.ApplyDamage(source);

        float lowHP = LowHPFXThreshold;
        bool isLow = currentHP < lowHP;
        float normHP = isLow ? currentHP / lowHP : 1f;

        pData.Skin_Data.CharacterAnimator.SetBool("LowHP", isLow);
        RpcUpdateLowHPEffect(normHP, isLow);
    }

    [ClientRpc]
    void RpcTriggerDamageEffect(float normDmg)
    {
        if (!pData.isLocalPlayer || takeDamageFX == null) return;
        takeDamageFX.Trigger(normDmg);
    }

    [ClientRpc]
    void RpcUpdateLowHPEffect(float normHP, bool isLow)
    {
        if (!pData.isLocalPlayer || lowHPFX == null) return;
        lowHPFX.SetHealthNormalized(isLow ? normHP : 1f);
    }

    protected override void OnHPChanged(float oldVal, float newVal)
        => pData.HUDmanager.UpdateHUD();

    [Server]
    protected override void HandleDeath(AttackEvent source)
    {
        if (dead) return;
        float multiplier = Random.Range(1f, 2f);
        Vector3 momentum = source.SourceStats != null
            ? CalculateMomentum(source.Position, source.AttackStat_.AttackForce, multiplier)
            : Vector3.zero;

        dead = true;
        pData.OnPlayerDeath(source.AttackStat_, momentum, false);
    }

    [Server]
    protected void HandleDeath(AttackEvent source, bool executed)
    {
        if (dead) return;
        float multiplier = Random.Range(1f, 2f);
        Vector3 momentum = source.SourceStats != null
            ? CalculateMomentum(source.Position, source.AttackStat_.AttackForce, multiplier)
            : Vector3.zero;

        dead = true;
        pData.OnPlayerDeath(source.AttackStat_, momentum, executed);
    }

    [Server]
    public void ExecutePlayer(bool silent = false)
    {
        if (silent)
        {
            currentHP = 0f;
            HandleDeath(default, true);
        }
        else
        {
            pData.ExplodeComp.TriggerExplosion(true);
        }
    }

    IEnumerator TookDamageCoroutine()
    {
        pData.Skin_Data.CharacterAnimator.SetBool("Hurt", true);

        tookDamageTimer = 1;
        while (tookDamageTimer > 0)
        {
            tookDamageTimer -= Time.deltaTime;
            yield return null;
        }

        if (currentHP >= LowHPThreshold)
            pData.Skin_Data.CharacterAnimator.SetBool("Hurt", false);

        tookDmgCor = null;
    }

    #endregion

    #region Stamina

    [Server]
    public void ModifyStamina(float amount)
    {
        staminaRecoveryTimer = 0f;
        currentStamina = Mathf.Clamp(currentStamina + amount, 0f, maxStamina);
    }

    void OnStaminaChanged(float oldVal, float newVal) => pData.HUDmanager.UpdateHUD();

    void TickStaminaRecovery()
    {
        staminaRecoveryTimer = Mathf.Clamp(staminaRecoveryTimer + Time.deltaTime, 0f, staminaRecoveryDelay);
        if (!Mathf.Approximately(staminaRecoveryTimer, staminaRecoveryDelay)) return;
        currentStamina = Mathf.Clamp(currentStamina + Time.deltaTime * staminaRecoveryRate, 0f, maxStamina);
    }

    #endregion

    #region Knock

    protected override void OnKnockChanged(float oldVal, float newVal)
        => pData.HUDmanager.UpdateHUD();

    [Server]
    public override void AddKnock(float amount, Vector3 momentum)
    {
        if (knocked) return;
        knockRecoveryTimer = 0f;
        currentKnock = Mathf.Clamp(currentKnock + amount, 0f, maxKnock);

        if (currentKnock >= maxKnock) HandleKnocked(momentum);
        else pData.Player_Movement.AddMomentum(momentum);
    }

    [Server]
    protected override void HandleKnocked(Vector3 momentum)
    {
        if (knocked) return;
        knocked = true;
        pData.PlayerInventory.DropEverything();
        pData.Skin_Data.Ragdoll_Manager.EnableRagdoll(momentum);
        OnPlayerKnocked?.Invoke();
    }

    #endregion
}
