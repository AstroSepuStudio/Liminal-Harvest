using Mirror;
using Steamworks;
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using WS_ProceduralGeneration;
using static GameManager;

public class LobbyManagerScreen : UIManagerNetwork
{
    public Sprite defaultIcon;

    [Header("References")]
    [SerializeField] NetworkIdentity identity;
    [SerializeField] DNG_MapModule mapModule;
    [SerializeField] Store store;
    [SerializeField] Canvas worldCanvas;
    [SerializeField] RectTransform refRectTransform;
    [SerializeField] Transform cameraPosition;
    [SerializeField] GameObject loadingWindow;
    [SerializeField] InteractableObject interactableCanvas;
    [SerializeField] Transform loadingThing;
    [SerializeField] Transform playerTargetPos;
    public Transform rightHCIKTarget;

    [SerializeField] TextMeshProUGUI dayText;
    [SerializeField] TextMeshProUGUI timeText;
    [SerializeField] TextMeshProUGUI levelText;
    [SerializeField] TeamBalancePair teamWhiteBalance;
    [SerializeField] TeamBalancePair teamRedBalance;
    [SerializeField] TeamBalancePair teamBlueBalance;
    [SerializeField] TeamBalancePair teamYellowBalance;
    [SerializeField] TeamBalancePair teamGreenBalance;
    [SerializeField] TeamBalancePair teamPinkBalance;
    [SerializeField] TextMeshProUGUI totalBalanceText;

    [Header("Settings")]
    [SerializeField] Vector2 rhcikTargetLimit;
    [SerializeField] float loadThingRotSpd;

    [SerializeField] TMP_Dropdown lobbyType_DP;
    [SerializeField] TMP_InputField mapSize_IP;
    [SerializeField] Toggle teamDamage_Toggle;
    [SerializeField] Toggle teamKnock_Toggle;
    [SerializeField] Toggle setSeed_Toggle;

    [SyncVar] public int playerOnLMS = -1;
    [SyncVar] bool open = false;

    PlayerData currentPlayer;

    [Serializable]
    struct TeamBalancePair
    {
        public GameObject teamObj;
        public TextMeshProUGUI balanceText;
    }

    protected override void Start()
    {
        base.Start();

        LobbySettings.Instance.OnLobbySettingsChanged.AddListener(RefreshLobbySettings);
        GameTick.OnSecond += OnSecond;

        mapSize_IP.SetTextWithoutNotify(LobbySettings.Instance.MapSize.ToString());
        setSeed_Toggle.SetIsOnWithoutNotify(LobbySettings.Instance.UseSetSeed);

        Instance.dngMod.OnThemeChangedEv.AddListener(RefreshLevelName);
        Instance.dayMod.OnDayStarted.AddListener(RefreshDay);
        Instance.dayMod.OnDayEnded.AddListener(RefreshDay);
        Instance.ecoMod.OnTeamBalanceChangedEv.AddListener(RefreshTeamBalances);

        InitialRefresh();
    }

    private void OnDestroy()
    {
        LobbySettings.Instance.OnLobbySettingsChanged.RemoveListener(RefreshLobbySettings);
        GameTick.OnSecond -= OnSecond;

        Instance.dngMod.OnThemeChangedEv.RemoveListener(RefreshLevelName);
        Instance.dayMod.OnDayStarted.RemoveListener(RefreshDay);
        Instance.dayMod.OnDayEnded.RemoveListener(RefreshDay);
        Instance.ecoMod.OnTeamBalanceChangedEv.RemoveListener(RefreshTeamBalances);
    }

    #region State Change
    public void OnEscapePressed(InputAction.CallbackContext context)
    {
        if (!context.started) return;

        CloseLMS();
    }

    public void CloseLMS()
    {
        if (isLocalPlayer && !open) return;

        CmdCloseLMS(Instance.playMod.LocalPlayer.Index);
    }

    public void OnInteract(PlayerData playerData)
    {
        if (open) return;

        playerData.Player_Stats.OnPlayerKnocked.AddListener(OnPlayerKnocked);

        open = true;
        playerOnLMS = playerData.Index;
        playerData._LockPlayer = true;
        currentPlayer = playerData;

        playerData.Teleport(playerTargetPos.position);
        playerData.Player_Movement.ServerForceAimAt(playerTargetPos.position + playerTargetPos.forward);
        playerData.Skin_Data.Rigging_Manager.RpcEnableRightHandChainRig();

        interactableCanvas.DisableInteractable();

        store.SetCurrentPlayer(playerData);

        RpcOpenLMS(playerData.Index);
    }

    [Server]
    private void OnPlayerKnocked()
    {
        Debug.Log("[LMScreen] Player knocked");
        CmdCloseLMS(currentPlayer.Index);
    }

    [ClientRpc]
    void RpcOpenLMS(int index)
    {
        if (Instance.playMod.LocalPlayer.Index != index) return;

        worldCanvas.worldCamera = Instance.playMod.LocalPlayer.PlayerCamera;

        Instance.playMod.LocalPlayer.Player_Input.actions["Esc"].started += OnEscapePressed;
        Instance.playMod.LocalPlayer.PlayerCanvas.SetActive(false);
        Instance.playMod.LocalPlayer.TakeCameraControl(cameraPosition);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        RefreshLevelName(Instance.dngMod.selectedTheme);
        RefreshDay(Instance.dayMod.currentDay);
        RefreshDayTime();
        RefreshLobbySettings();
        RefreshTeamBalances();

        StartCoroutine(RHCIKTPosHandler());
    }

    [Command(requiresAuthority = false)]
    void CmdCloseLMS(int index)
    {
        if (!open || index != playerOnLMS) return;

        currentPlayer.Player_Stats.OnPlayerKnocked.RemoveListener(OnPlayerKnocked);

        currentPlayer.Skin_Data.Rigging_Manager.RpcDisableRightHandChainRig();
        currentPlayer.Player_Movement.ServerClearForcedAim();

        currentPlayer._LockPlayer = false;
        currentPlayer = null;

        open = false;
        playerOnLMS = -1;

        interactableCanvas.EnableInteractable();

        store.ClearCurrentPlayer();

        RpcCloseLMS(index);
    }

    [ClientRpc]
    public void RpcCloseLMS(int index)
    {
        if (Instance.playMod.LocalPlayer.Index != index) return;

        Instance.playMod.LocalPlayer.Player_Input.actions["Esc"].started -= OnEscapePressed;
        Instance.playMod.LocalPlayer.PlayerCanvas.SetActive(true);
        Instance.playMod.LocalPlayer.DropCameraControl();
        mapModule.CmdSnapshotMapAnchor(Instance.playMod.LocalPlayer.Index, mapModule.MapAnchorPosition);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
    #endregion

    #region Screen Refresh
    private void InitialRefresh()
    {
        RefreshLevelName(Instance.dngMod.selectedTheme);
        RefreshDay(Instance.dayMod.currentDay);
        RefreshLobbySettings();
        RefreshTeamBalances(PlayerTeam.White, 0);
    }

    private void OnSecond() => RefreshDayTime();

    public void RefreshLevelName(int index) => levelText.SetText(Instance.dngMod.ThemeDatas[index].levelName);

    public void RefreshDay(int day) => dayText.SetText($"Day {day}");

    public void RefreshDayTime() => timeText.SetText(Instance.dayMod.GetFormatedTime());

    public void RefreshLobbySettings()
    {
        int lobbyIndex = LobbySettings.Instance.Lobby_Type switch
        {
            ELobbyType.k_ELobbyTypeFriendsOnly => 0,
            ELobbyType.k_ELobbyTypePublic => 1,
            ELobbyType.k_ELobbyTypePrivate => 2,
            _ => 0,
        };

        lobbyType_DP.SetValueWithoutNotify(lobbyIndex);
        mapSize_IP.SetTextWithoutNotify(LobbySettings.Instance.MapSize.ToString());
        teamDamage_Toggle.SetIsOnWithoutNotify(LobbySettings.Instance.TeamDamage);
        teamKnock_Toggle.SetIsOnWithoutNotify(LobbySettings.Instance.TeamKnock);
    }

    public void RefreshTeamBalances(PlayerTeam t, float v)
    {
        SetTeamBalance(Instance.ecoMod.teamsBalance[PlayerTeam.White], teamWhiteBalance);
        SetTeamBalance(Instance.ecoMod.teamsBalance[PlayerTeam.Red], teamRedBalance);
        SetTeamBalance(Instance.ecoMod.teamsBalance[PlayerTeam.Blue], teamBlueBalance);
        SetTeamBalance(Instance.ecoMod.teamsBalance[PlayerTeam.Yellow], teamYellowBalance);
        SetTeamBalance(Instance.ecoMod.teamsBalance[PlayerTeam.Green], teamGreenBalance);
        SetTeamBalance(Instance.ecoMod.teamsBalance[PlayerTeam.Pink], teamPinkBalance);

        totalBalanceText.SetText($"{Instance.ecoMod.TotalBalance}");
    }

    public void RefreshTeamBalances()
    {
        SetTeamBalance(Instance.ecoMod.teamsBalance[PlayerTeam.White], teamWhiteBalance);
        SetTeamBalance(Instance.ecoMod.teamsBalance[PlayerTeam.Red], teamRedBalance);
        SetTeamBalance(Instance.ecoMod.teamsBalance[PlayerTeam.Blue], teamBlueBalance);
        SetTeamBalance(Instance.ecoMod.teamsBalance[PlayerTeam.Yellow], teamYellowBalance);
        SetTeamBalance(Instance.ecoMod.teamsBalance[PlayerTeam.Green], teamGreenBalance);
        SetTeamBalance(Instance.ecoMod.teamsBalance[PlayerTeam.Pink], teamPinkBalance);

        totalBalanceText.SetText($"{Instance.ecoMod.TotalBalance}");
    }

    void SetTeamBalance(float balance, TeamBalancePair pair)
    {
        if (balance > 0)
        {
            pair.teamObj.SetActive(true);
            pair.balanceText.SetText($"{balance}");
        }
        else pair.teamObj.SetActive(false);
    }
    #endregion

    #region Settings
    public void RequestTeamSwap(int index)
    {
        PlayerTeam team = (PlayerTeam)index;

        Instance.playMod.CmdRequestTeamChange(Instance.playMod.LocalPlayer.Index, team);
    }

    public void SetLobbyType(int index)
    {
        if (!identity.isServer) return;

        ELobbyType type = lobbyType_DP.options[index].text switch
        {
            "Friends Only" => ELobbyType.k_ELobbyTypeFriendsOnly,
            "Public" => ELobbyType.k_ELobbyTypePublic,
            "Private" => ELobbyType.k_ELobbyTypePrivate,
            _ => ELobbyType.k_ELobbyTypeFriendsOnly,
        };

        LobbySettings.Instance.SetLobbyType(type);
    }

    public void SetMapSize(string value)
    {
        if (!identity.isServer) return;

        if (int.TryParse(value, out int size))
            LobbySettings.Instance.SetMapSize(Mathf.Max(1, size));
    }

    public void TeamDamage(bool value)
    {
        if (!identity.isServer) return;

        LobbySettings.Instance.SetTeamDamage(value);
    }

    public void TeamKnock(bool value)
    {
        if (!identity.isServer) return;

        LobbySettings.Instance.SetTeamKnock(value);
    }

    public void SetSeed(string value)
    {
        if (!identity.isServer) return;

        if (int.TryParse(value, out int seed))
            Instance.SetSeed(seed);
    }

    public void SetUseSetSeed(bool useSetSeed)
    {
        if (!identity.isServer) return;

        LobbySettings.Instance.SetUseSetSeed(useSetSeed);
    }

    public void SetOverrideMapSize(bool overrideMapSize)
    {
        if (!identity.isServer) return;

        LobbySettings.Instance.SetOverrideMapSize(overrideMapSize);
    }

    public void SetTeamsShareBalance(bool teamsShareBalance)
    {
        if (!identity.isServer) return;

        LobbySettings.Instance.SetTeamsShareBalance(teamsShareBalance);
    }
    #endregion

    #region Logic
    public void StartGame()
    {
        if (!identity.isServer) return;

        Instance.StartGame();
    }

    public void StartDay()
    {
        Instance.dayMod.StartDay();
    }

    public void RequestThemeSelection(int index)
    {
        Instance.dngMod.RequestTheme(index);
    }
    #endregion

    #region IK
    IEnumerator RHCIKTPosHandler()
    {
        float w8timer = 0;
        float timer = 0;
        float syncDelay = 0.01f;
        while (open || w8timer < 0.1f)
        {
            while (timer < syncDelay)
            {
                w8timer += Time.deltaTime;
                timer += Time.deltaTime;
                yield return null;
            }

            timer = 0;

            Vector2 mousePos = Mouse.current.position.ReadValue();
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle (
                refRectTransform, mousePos, worldCanvas.worldCamera, out Vector2 localPoint))
            {
                CmdRequestRHCIKTPosChange(localPoint);
            }
            else
                CmdRequestRHCIKTPosChange(Vector2.zero);

            yield return null;
        }
    }

    [Command(requiresAuthority = false)]
    public void CmdRequestRHCIKTPosChange(Vector2 pos)
    {
        RpcSetRHCIKTPos(pos);
    }

    [ClientRpc]
    void RpcSetRHCIKTPos(Vector2 pos)
    {
        pos.x = Mathf.Clamp(pos.x, -rhcikTargetLimit.x, rhcikTargetLimit.x);
        pos.y = Mathf.Clamp(pos.y, -rhcikTargetLimit.y, rhcikTargetLimit.y);

        rightHCIKTarget.localPosition = pos;
    }
    #endregion
}
