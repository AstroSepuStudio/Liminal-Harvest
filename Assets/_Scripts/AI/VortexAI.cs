using System.Collections;
using UnityEngine;
using Mirror;
using System.Linq;
using WS_ProceduralGeneration;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class VortexAI : AIBrain, IHearingListener
{
    [SerializeField] Transform pickUpPos;
    [SerializeField] ParticleSystem deathParticles;
    [SerializeField] AudioSource alphaCallSrc;

    [Header("State Indexes")]
    [SerializeField] int wanderStateIndex = 0;
    [SerializeField] int pickUpStateIndex = 1;
    [SerializeField] int dropAtHomeStateIndex = 2;
    [SerializeField] int wanderHomeStateIndex = 3;
    [SerializeField] int followLeaderStateIndex = 4;
    [SerializeField] int searchStateIndex = 5;
    [SerializeField] int followPlayerStateIndex = 6;
    [SerializeField] int attackFurnitureStateIndex = 7;
    [SerializeField] int backAwayStateIndex = 8;
    [SerializeField] int stareAtPlayerNearItemStateIndex = 9;
    [SerializeField] int attackPlayerStateIndex = 10;

    [Header("Detection")]
    [SerializeField] float playerNearItemDistance = 3f;
    [SerializeField] float itemScanInterval = 1f;
    float itemScanTimer = 0f;

    [Header("Scout Dispatch")]
    [SerializeField] float minDispatchTime = 5f;
    [SerializeField] float maxDispatchTime = 30f;
    float dispatchTimer = 0f;
    bool canDispatch = false;

    [Header("Skull Crusher Fear")]
    [SerializeField] float fearDuration = 12f;
    [SerializeField] float fearImmunityOnTheft = 8f;

    bool _feared = false;
    float _fearTimer = 0f;
    float _fearImmunityTimer = 0f;
    GameObject _skullCrusherSource = null;

#if UNITY_EDITOR
    [Header("Gizmos")]
    [SerializeField] bool showDetectionRadius = true;
    [SerializeField] bool showHomeIndicator = true;
    [SerializeField] bool showStateLabel = true;
    [SerializeField] bool showCarriedItem = true;
    [SerializeField] bool showAlphaStatus = true;
    [SerializeField] bool showPatience = true;
#endif

    AIS_PickUpItem pickUpState;
    AIS_DropItemAtHome dropAtHomeState;
    AIS_SearchForItems searchState;
    AIS_FollowPlayer followPlayerState;
    AIS_AttackFurniture attackFurnitureState;
    AIS_BackAwayFromPlayer backAwayState;
    AIS_StareAtPlayerNearItem stareAtPlayerNearItemState;
    AIS_AttackPlayer attackPlayerState;
    AIS_FollowAlpha followLeaderState;

    VortexScoutGroup currentGroup = null;

    AIModule_Alpha AlphaModule => GetModule<AIModule_Alpha>();
    AIModule_Patience PatienceModule => GetModule<AIModule_Patience>();
    AIModule_ItemCarrier CarrierModule => GetModule<AIModule_ItemCarrier>();
    AIModule_Senses SensesModule => GetModule<AIModule_Senses>();
    AIModule_Grudge GrudgeModule => GetModule<AIModule_Grudge>();

    VortexPack Pack => VortexPack.Instance;

    public float Alpha => AlphaModule.AlphaValue;
    public bool IsAlpha => Pack.IsAlpha(this);
    public ItemBase CarriedItem => CarrierModule.CarriedItem;
    public RoomData HomeRoom => Pack.HomeRoom;
    public float Patience => PatienceModule.Patience;

    public override void PlaySFX(SourceType type, SFXEvent sfxEvent, float pitch,
        bool overrideLoudness = false, SoundLoudness loudnessOverride = SoundLoudness.NoSound)
    {
        pitch *= AlphaModule.GetPitch();
        base.PlaySFX(type, sfxEvent, pitch);
    }

    public void PlayAlphaCall()
    {
        if (!sfxMap.TryGetValue(SFXEvent.AlphaCall, out var group) || group.Clips.Length == 0) return;
        int index = Random.Range(0, group.Clips.Length);
        RpcPlayAlphaCall(index, AlphaModule.GetPitch());
    }

    [ClientRpc]
    void RpcPlayAlphaCall(int clipIndex, float pitch)
    {
        if (alphaCallSrc == null) return;
        if (!sfxMap.TryGetValue(SFXEvent.AlphaCall, out var group) || group.Clips.Length == 0) return;
        alphaCallSrc.pitch = pitch;
        AudioManager.Instance.PlayOneShot(alphaCallSrc, group.Clips[clipIndex], gameObject, group.Loudness);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (AlphaModule == null)
            Initialize();

        lootDropper.OnLootSpawned = (item) =>
        {
            item.MultiplyValue(AlphaModule.GetMultiplier());
            item.SetScale(Vector3.one * Mathf.Lerp(0.6f, 1.2f, Alpha / 100f));
        };
    }

    protected override void Start()
    {
        base.Start();
        CacheStates();
        SubscribeSensesEvents();

        if (!isServer) return;
        Pack.Register(this);
        HearingEventBroadcaster.Instance.AddListener(this);
    }

    public void OnSoundHeard(AudioSoundEvent soundEvent)
    {
        if (!isServer) return;
        if (HomeRoom == null) return;

        if (soundEvent.source.CompareTag("SkullCrusher"))
        {
            OnSkullCrusherHeard(soundEvent);
            return;
        }

        if (soundEvent.source.CompareTag("Player"))
        {
            float homeDist = Vector3.Distance(soundEvent.position, HomeRoom.transform.position);
            float cellSize = DungeonGenerator.Instance.CellSize;
            if (homeDist > cellSize * 2f) return;

            if (!IsInAttackState())
                TriggerReturnHome();
        }
    }

    void OnSkullCrusherHeard(AudioSoundEvent soundEvent)
    {
        if (_fearImmunityTimer > 0f) return;

        _skullCrusherSource = soundEvent.source;
        _fearTimer = fearDuration;

        if (!_feared)
            EnterFear();
        else
            StopAgentMovement();
    }

    protected override void RegisterModules()
    {
        base.RegisterModules();

        var home = GetModule<AIModule_Home>();
        if (home == null) return;

        home.SetOverride(HomeRoom);
    }

    void EnterFear()
    {
        _feared = true;
        stayQuiet = true;
        SetIdleState(false);
        StopAgentMovement();

        if (IsInAttackState())
            SetState(states[wanderStateIndex]);
    }

    void ExitFear()
    {
        _feared = false;
        stayQuiet = false;
        _skullCrusherSource = null;
        SetIdleState(true);
        ResumeAgentMovement();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        UnsubscribeSensesEvents();
        if (isServer) 
        { 
            Pack.Unregister(this);
            HearingEventBroadcaster.Instance.RemoveListener(this);
        }
    }

    void CacheStates()
    {
        if (states == null) return;
        pickUpState = SafeGetState<AIS_PickUpItem>(pickUpStateIndex);
        dropAtHomeState = SafeGetState<AIS_DropItemAtHome>(dropAtHomeStateIndex);
        searchState = SafeGetState<AIS_SearchForItems>(searchStateIndex);
        followPlayerState = SafeGetState<AIS_FollowPlayer>(followPlayerStateIndex);
        attackFurnitureState = SafeGetState<AIS_AttackFurniture>(attackFurnitureStateIndex);
        backAwayState = SafeGetState<AIS_BackAwayFromPlayer>(backAwayStateIndex);
        stareAtPlayerNearItemState = SafeGetState<AIS_StareAtPlayerNearItem>(stareAtPlayerNearItemStateIndex);
        attackPlayerState = SafeGetState<AIS_AttackPlayer>(attackPlayerStateIndex);
        followLeaderState = SafeGetState<AIS_FollowAlpha>(followLeaderStateIndex);
    }

    void SubscribeSensesEvents()
    {
        var senses = SensesModule;
        if (senses == null) return;
        senses.OnPlayerTooClose.AddListener(OnPlayerTooClose);
        senses.OnNewPlayerSpotted.AddListener(OnNewPlayerSpotted);
        senses.OnPlayerSpotted.AddListener(OnPlayerSpotted);
    }

    void UnsubscribeSensesEvents()
    {
        var senses = SensesModule;
        if (senses == null) return;
        senses.OnPlayerTooClose.RemoveListener(OnPlayerTooClose);
        senses.OnNewPlayerSpotted.RemoveListener(OnNewPlayerSpotted);
        senses.OnPlayerSpotted.AddListener(OnPlayerSpotted);
    }

    void OnPlayerTooClose(PlayerData player)
    {
        if (!isServer || IsInAttackState()) return;
        TriggerBackAway();
    }

    void OnNewPlayerSpotted(PlayerData player)
    {
        if (!isServer || IsInAttackState()) return;

        if (GrudgeModule != null && GrudgeModule.HasGrudge(player))
        {
            TriggerAttackThief(player);
            return;
        }

        if (CarriedItem != null) return;
        if (!IsInWanderState()) return;
        TriggerFollowPlayer();
    }

    void OnPlayerSpotted(PlayerData player)
    {
        if (!isServer || IsInAttackState()) return;

        if (GrudgeModule != null && GrudgeModule.HasGrudge(player))
        {
            TriggerAttackThief(player);
            return;
        }
    }

    protected override void Update()
    {
        if (isDying || !isServer) return;
        base.Update();

        TickFear();

        if (canDispatch)
        {
            dispatchTimer -= Time.deltaTime;

            if (dispatchTimer < 0 && IsAlpha)
            {
                Pack.DispatchScouts();
                dispatchTimer = Random.Range(minDispatchTime, maxDispatchTime);
            }
        }

        itemScanTimer -= Time.deltaTime;
        if (itemScanTimer > 0f) return;
        itemScanTimer = itemScanInterval;

        CheckHomeItems();
        ScanForItems();
    }

    void TickFear()
    {
        if (_fearImmunityTimer > 0f)
            _fearImmunityTimer -= Time.deltaTime;

        if (!_feared) return;

        _fearTimer -= Time.deltaTime;
        if (_fearTimer <= 0f)
        {
            ExitFear();
            TryResumeWander();
            return;
        }

        StopAgentMovement();
    }

    void ScanForItems()
    {
        TryScanForStolenItems();

        if (CarriedItem != null)
        {
            if (Pack != null && Pack.IsVortexAtHome(this))
                TriggerDropAtHome();
            return;
        }

        if (IsInAttackState() || IsAlpha) return;

        var senses = SensesModule;
        float radius = senses != null ? senses.DetectionRadius : 10f;

        Collider[] hits = Physics.OverlapSphere(transform.position, radius);

        ItemBase closestItem = null;
        float iDist = float.MaxValue;

        foreach (var hit in hits)
        {
            if (!hit.CompareTag("Item")) continue;
            if (CarrierModule != null && CarrierModule.IsOnDropCooldown) continue;

            var item = hit.GetComponent<ItemBase>();
            if (item == null || !item.ItemData.pickable || item.HasOwner) continue;
            if (Pack != null && Pack.IsItemAtHome(item)) continue;

            float d = Vector3.Distance(transform.position, item.transform.position);
            if (d < iDist && HasLineOfSight(item.transform.position)) { iDist = d; closestItem = item; }
        }

        if (closestItem != null)
            TriggerPickUp(closestItem);
    }

    void CheckHomeItems()
    {
        if (HomeRoom == null) return;
        var senses = SensesModule;
        float radius = senses != null ? senses.DetectionRadius : 10f;
        if (Vector3.Distance(transform.position, HomeRoom.transform.position) > radius) return;
        Pack.CheckStolenItems(this);
    }

    bool IsInWanderState()
    {
        if (CurrentState == null) return false;
        if (wanderHomeStateIndex < states.Length && CurrentState == states[wanderHomeStateIndex]) return true;
        return wanderStateIndex < states.Length && CurrentState == states[wanderStateIndex];
    }

    bool IsInAttackState() =>
        (attackFurnitureState != null && CurrentState == states[attackFurnitureStateIndex]) ||
        (attackPlayerState != null && CurrentState == states[attackPlayerStateIndex]);

    bool IsFollowingLeader() =>
        followLeaderState != null && CurrentState == states[followLeaderStateIndex];

    void TriggerPickUp(ItemBase item)
    {
        if (pickUpState == null) return;
        PlayerData blocker = GetPlayerNearItem(item, playerNearItemDistance);
        if (blocker != null) { TriggerStareAtPlayerNearItem(item, blocker); return; }
        pickUpState.TargetItem = item;
        SetState(states[pickUpStateIndex]);
    }

    void TriggerFollowPlayer()
    {
        if (followPlayerState == null || SensesModule == null) return;
        followPlayerState.WatchedPlayers = SensesModule.WatchedPlayers;
        SetState(states[followPlayerStateIndex]);
    }

    public void TriggerDropAtHome()
    {
        if (CurrentState != dropAtHomeState)
            SetState(states[dropAtHomeStateIndex]);
    }

    public void TriggerReturnHome()
    {
        if (CurrentState == states[wanderHomeStateIndex] ||
            CurrentState == states[dropAtHomeStateIndex]) 
            return;

        if (CarriedItem != null) TriggerDropAtHome();
        else TriggerWanderHome();
    }

    void TryResumeWander()
    {
        if (currentGroup != null)
            if (currentGroup.Leader == this)
                ResumeWander();
            else
                ResumeFollowLeader();
        else
            TriggerReturnHome();
    }

    void ResumeWander() => SetState(states[wanderStateIndex]);
    void TriggerWanderHome() => SetState(states[wanderHomeStateIndex]);
    public void ResumeFollowLeader() => SetState(states[followLeaderStateIndex]);

    void TriggerBackAway()
    {
        if (backAwayState == null) 
        {
            TryResumeWander();
            return; 
        }
        PatienceModule.DrainOnBackAway();
        if (PatienceModule == null || !PatienceModule.IsExhausted)
            SetState(states[backAwayStateIndex]);
    }

    void TriggerStareAtPlayerNearItem(ItemBase item, PlayerData blocker)
    {
        stareAtPlayerNearItemState.WatchedItem = item;
        stareAtPlayerNearItemState.BlockingPlayer = blocker;
        SetState(states[stareAtPlayerNearItemStateIndex]);
    }

    public void TriggerAttackPlayer()
    {
        PatienceModule.Restore(0f);

        PlayerData target = SensesModule.GetClosestSeenPlayer(this);
        if (target == null) { TryResumeWander(); return; }

        ItemBase dropped = CarriedItem;
        CarrierModule.DropCarriedItem();
        attackPlayerState.ItemToRecoverAfter = dropped;
        attackPlayerState.Target = target;
        SetState(states[attackPlayerStateIndex]);
    }

    void TriggerAttackThief(PlayerData thief)
    {
        if (attackPlayerState == null) return;
        CarrierModule.DropCarriedItem();
        PatienceModule.Restore(0f);
        attackPlayerState.Target = thief;
        attackPlayerState.ItemToRecoverAfter = null;
        SetState(states[attackPlayerStateIndex]);
    }

    public void BeginGroupWander() => ResumeWander();

    public void TriggerFollowLeader(VortexAI leader)
    {
        if (followLeaderState == null) return;
        followLeaderState.AlphaTarget = leader;
        SetState(states[followLeaderStateIndex]);
    }

    public void OnAlphaRoleGranted()
    {
        AlphaModule.SetActingAsAlpha(true);
        canDispatch = true;
        dispatchTimer = Random.Range(minDispatchTime, maxDispatchTime);
        TriggerReturnHome();
    }

    public void OnAlphaRoleRevoked()
    {
        AlphaModule.SetActingAsAlpha(false);
        TryResumeWander();
    }

    public override void OnAgentHurt(AttackEvent source)
    {
        if (IsAlpha) PlayAlphaCall();
        PlaySFX(SourceType.Default, SFXEvent.CallForHelp, 1f);

        _fearImmunityTimer = fearImmunityOnTheft;
        if (_feared) ExitFear();

        if (source.SourceStats != null && source.SourceStats.TryGetComponent<PlayerData>(out var attacker))
        {
            float duration = Random.Range(10f, 30f);
            GrudgeModule.AddTimedGrudge(attacker, duration);
        }

        TriggerAttackPlayer();

        if (Pack == null) return;
        foreach (var other in Pack.Members)
        {
            if (other == null || other == this || other.IsInAttackState()) continue;
            float d = Vector3.Distance(transform.position, other.transform.position);
            if (d <= SensesModule.DetectionRadius * 2f) other.RespondToHelpCall(this);
        }
    }

    public void OnItemStolenFromHome(ItemBase item, PlayerData thief, VortexAI reporter)
    {
        if (!isServer) return;

        GrudgeModule.AddGrudge(thief, item);

        _fearImmunityTimer = fearImmunityOnTheft;
        if (_feared) ExitFear();

        bool thiefVisible = SensesModule != null && SensesModule.HasSeenPlayer(thief)
                         || HasLineOfSight(thief.transform.position);

        if (thiefVisible)
            TriggerAttackThief(thief);
        else
            TriggerReturnHome();
    }

    public void RespondToHelpCall(VortexAI caller)
    {
        if (IsInAttackState()) return;
        CarrierModule.DropCarriedItem();
        PlaySFX(SourceType.Default, SFXEvent.CallForHelp, 1f);

        PlayerData target = caller.attackPlayerState.Target != null ? 
            caller.attackPlayerState.Target : caller.SensesModule.GetClosestSeenPlayer(caller);
        if (target == null) return;

        SensesModule.RegisterSeenPlayer(target);

        if (Vector3.Distance(transform.position, target.transform.position) <= SensesModule.DetectionRadius)
        {
            if (attackPlayerState == null) return;
            PatienceModule.Restore(0f);
            attackPlayerState.Target = target;
            attackPlayerState.ItemToRecoverAfter = null;
            SetState(states[attackPlayerStateIndex]);
        }
    }

    public override void SetAggressive(bool aggressive)
    {
        base.SetAggressive(aggressive);
        renderer_.SetBlendShapeWeight(0, aggressive ? 0 : 100);
    }

    public void OnItemPickedUp()
    {
        var item = CarriedItem;
        var grudge = GrudgeModule;

        if (item != null && grudge != null && grudge.StolenItems.Contains(item))
        {
            grudge.RemoveGrudgeForItem(item);
            Pack.OnItemRecovered(item);
            Pack.ClearGrudgeForItem(item);
        }

        TriggerDropAtHome();
    }

    public void OnItemLost() => TryResumeWander();

    public void OnItemDropped()
    {
        if (IsAlpha) { TriggerWanderHome(); return; }
        TryResumeWander();
    }

    public void OnNoItemToDeliver()
    {
        if (IsAlpha) { TriggerWanderHome(); return; }
        TryResumeWander();
    }

    public void OnSearchFailed() => TriggerDropAtHome();
    public void OnSearchItemFound() => TriggerDropAtHome();

    public void OnArrivedAtHome()
    {
        Pack.ScanHomeItems();
        Pack.ShareGrudgesAtHome(this);

        if (IsAlpha)
        {
            dispatchTimer = Random.Range(minDispatchTime, maxDispatchTime);
            canDispatch = true;
            return;
        }

        currentGroup?.NotifyMemberArrived(this);
        currentGroup = null;
    }

    public void SetGroup(VortexScoutGroup group) => currentGroup = group;

    public void OnCuriosityExpired()
    {
        SensesModule.ClearWatched();
        TryResumeWander();
    }

    public void OnFurnitureBlocking()
    {
        if (attackFurnitureState == null) { TryResumeWander(); return; }
        attackFurnitureState.Target = pickUpState.BlockingFurniture;
        attackFurnitureState.ItemToPickUpAfter = pickUpState.TargetItem;
        SetState(states[attackFurnitureStateIndex]);
    }

    public void OnFurnitureDestroyed()
    {
        if (attackFurnitureState.ItemToPickUpAfter != null)
            TriggerPickUp(attackFurnitureState.ItemToPickUpAfter);
        else
            TryResumeWander();
    }

    public void OnFurnitureLost() => TryResumeWander();
    public void OnBackAwaySafe() => TryResumeWander();
    public void OnBackAwayGaveUp() => TryResumeWander();

    public void OnPlayerLeftItem()
    {
        if (stareAtPlayerNearItemState.WatchedItem != null)
            TriggerPickUp(stareAtPlayerNearItemState.WatchedItem);
        else
            TryResumeWander();
    }

    public void OnItemStareGaveUp() => TryResumeWander();
    public void OnAttackPlayerLost() => TryResumeWander();

    public void OnAttackPlayerCalmedDown()
    {
        PatienceModule.Restore(0.3f);
        ItemBase itemToRecover = attackPlayerState.ItemToRecoverAfter;
        if (itemToRecover != null && itemToRecover.ItemData.pickable && !itemToRecover.HasOwner)
        {
            attackPlayerState.ItemToRecoverAfter = null;
            TriggerPickUp(itemToRecover);
            return;
        }
        TryResumeWander();
    }

    public void DrainPatienceOnItemStolen() => PatienceModule.DrainOnItemStolen();
    public void DrainPatienceOnBackAway() => PatienceModule.DrainOnBackAway();

    public override void OnAgentDeath(AttackEvent source)
    {
        base.OnAgentDeath(source);

        if (source.SourceStats != null && source.SourceStats.TryGetComponent<PlayerData>(out var killer))
            Pack.AddPackGrudge(killer);

        Pack.Unregister(this);
        CarrierModule.DropCarriedItem();
        
        StartCoroutine(DespawnVortex());
        RPC_PlayDeathParticles();
    }

    IEnumerator DespawnVortex()
    {
        yield return new WaitForSeconds(8f);
        NetworkServer.Destroy(gameObject);
    }

    [ClientRpc] void RPC_PlayDeathParticles() => StartCoroutine(PlayDeathParticles());

    IEnumerator PlayDeathParticles()
    {
        deathParticles.Play();
        yield return new WaitForSeconds(2f);
        deathParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void TryScanForStolenItems()
    {
        var grudge = GrudgeModule;
        if (grudge == null || grudge.StolenItems.Count == 0) return;

        var senses = SensesModule;

        foreach (var item in grudge.StolenItems)
        {
            if (item == null || item.HasOwner) continue;
            float dist = Vector3.Distance(transform.position, item.transform.position);
            if (dist > senses.DetectionRadius) continue;
            if (!HasLineOfSight(item.transform.position)) continue;

            if (pickUpState != null)
            {
                pickUpState.TargetItem = item;
                SetState(states[pickUpStateIndex]);
                return;
            }
        }
    }

#if UNITY_EDITOR
    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        var senses = SensesModule;

        if (showDetectionRadius && senses != null)
        {
            Handles.color = Color.yellow;
            Handles.DrawWireDisc(transform.position, Vector3.up, senses.DetectionRadius);
        }

        if (showHomeIndicator)
        {
            RoomData home = HomeRoom;
            if (home != null)
            {
                Vector3 homePos = home.transform.position + Vector3.up * 0.1f;
                Color homeColor = new(1f, 0.5f, 0f);
                Handles.color = new Color(homeColor.r, homeColor.g, homeColor.b, 0.35f);
                Handles.DrawSolidDisc(homePos, Vector3.up, 1.5f);
                Handles.color = homeColor;
                Handles.DrawWireDisc(homePos, Vector3.up, 1.5f);
                Handles.DrawDottedLine(transform.position, homePos, 4f);
                GUIStyle hs = new() { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
                hs.normal.textColor = homeColor;
                Handles.Label(home.transform.position + Vector3.up * 2f, "Pack Home", hs);
            }
        }

        GUIStyle style = new() { alignment = TextAnchor.MiddleCenter };
        Vector3 labelPos = transform.position + Vector3.up;
        float labelOffset = 0f;

        if (showStateLabel)
        {
            style.normal.textColor = Color.cyan;
            style.fontSize = 14;
            style.fontStyle = FontStyle.Bold;
            Handles.Label(labelPos, $"a {Alpha:F0}", style);

            if (CurrentState != null)
            {
                style.normal.textColor = Color.white;
                style.fontSize = 11;
                style.fontStyle = FontStyle.Normal;
                Handles.Label(labelPos, CurrentState.GetType().Name, style);
            }

            labelOffset += 0.2f;
        }

        if (showCarriedItem && CarriedItem != null)
        {
            style.normal.textColor = Color.green;
            style.fontSize = 11;
            style.fontStyle = FontStyle.Normal;
            Handles.Label(labelPos + Vector3.up * labelOffset, $"[{CarriedItem.ItemData.itemName}]", style);
            labelOffset += 0.2f;
        }

        if (showAlphaStatus)
        {
            if (IsAlpha)
            {
                style.normal.textColor = new Color(1f, 0.5f, 0f);
                style.fontSize = 11;
                style.fontStyle = FontStyle.Normal;
                Handles.Label(labelPos + Vector3.up * labelOffset, "ALPHA", style);
                labelOffset += 0.2f;
            }
            else if (IsFollowingLeader())
            {
                style.normal.textColor = Color.white;
                style.fontSize = 10;
                style.fontStyle = FontStyle.Normal;
                Handles.Label(labelPos + Vector3.up * labelOffset,
                    $"-> {followLeaderState.AlphaTarget.name ?? "?"}", style);
                labelOffset += 0.2f;
            }
        }

        if (showPatience)
        {
            var pm = PatienceModule;
            if (pm != null)
            {
                style.normal.textColor = Color.Lerp(Color.red, Color.green, pm.Patience / pm.MaxPatience);
                style.fontSize = 10;
                style.fontStyle = FontStyle.Normal;
                Handles.Label(labelPos + Vector3.up * labelOffset,
                    $"{pm.CurrentPersonality} [{pm.Patience:F0}/{pm.MaxPatience:F0}]", style);
            }
        }
    }
#endif
}
