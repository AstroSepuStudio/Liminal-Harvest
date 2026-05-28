using Mirror;
using Steamworks;
using UnityEngine;
using WS_ProceduralGeneration;

public class AmbientSoundPlayer : NetworkBehaviour
{
    [SerializeField] AudioSource src;

    [Header("Timing")]
    [SerializeField] float minInterval = 10f;
    [SerializeField] float maxInterval = 60f;
    [SerializeField] float playChance = 10f;

    [Header("Positioning")]
    [SerializeField] float edgeOffset = 100f;

    float currentInterval = 0;
    float timer = 0;

    public override void OnStartServer()
    {
        base.OnStartServer();

        timer = 0;
        currentInterval = Random.Range(minInterval, maxInterval);

        LobbyManager.Instance.OnLobbyLeaveEvent.AddListener(StopAmbience);
        LobbyManager.Instance.OnLobbyKickedEvent.AddListener(OnLobbyKicked);

        GameTick.OnSecond += OnSecond;
    }

    private void OnLobbyKicked(LobbyKicked_t arg0) => StopAmbience();

    private void StopAmbience()
    {
        GameTick.OnSecond -= OnSecond;
    }

    [Server]
    private void OnSecond()
    {
        if (src.isPlaying) return;

        timer++;
        if (timer < currentInterval) return;

        currentInterval = Random.Range(minInterval, maxInterval);
        timer = 0;

        float roll = Random.value * 100;
        if (roll > playChance) return;
        Debug.Log("Playing ambience sfx", src.gameObject);

        var theme = DungeonGenerator.Instance.Theme;
        if (theme == null || theme.eerySFX == null || theme.eerySFX.Length == 0) return;

        int index = Random.Range(0, theme.eerySFX.Length);
        Vector3 origin = DungeonGenerator.Instance.StartRoomPos;
        float radius = DungeonGenerator.Instance.MaxDistance + edgeOffset;
        Vector3 edgePos = origin + Random.onUnitSphere * radius;
        edgePos.y = origin.y;

        RpcPlayEerySFX(edgePos, index);
    }

    [ClientRpc]
    void RpcPlayEerySFX(Vector3 position, int index)
    {
        var theme = DungeonGenerator.Instance?.Theme;
        if (theme == null || theme.eerySFX == null || theme.eerySFX.Length == 0) return;

        AudioManager.Instance.PlayOneShot(src, theme.eerySFX[index], position);
    }
}
