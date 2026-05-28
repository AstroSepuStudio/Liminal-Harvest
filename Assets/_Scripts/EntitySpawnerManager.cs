using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WS_ProceduralGeneration;

public class EntitySpawnerManager : NetworkBehaviour
{
    public static EntitySpawnerManager Instance;

    [SerializeField] Transform entityParent;

    [SerializeField] float spawnDelay = 20f;
    [SerializeField] float spawnChance = 0.1f;
    [SerializeField] float spawnCooldown = 10f;

    [SerializeField] List<EntityStats> aliveEntities = new();
    [SerializeField] List<EntityStats> deadEntities = new();

    int TotalQ => aliveEntities.Count + deadEntities.Count;
    int MaxEntities => GameManager.Instance.progressionMod.CurrentEntityCap;

    public readonly SyncList<uint> EntityNetIds = new();

    Transform[] spawnerPositions;
    Coroutine enemySpawningCoroutine;
    System.Random rng;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (isServer)
        {
            GameManager.Instance.dngMod.OnDungeonOpens.AddListener(OnDungeonOpens);
            GameManager.Instance.dngMod.OnDungeonCloses.AddListener(OnDungeonCloses);
        }
    }

    private void OnDestroy()
    {
        if (isServer)
        {
            GameManager.Instance.dngMod.OnDungeonOpens.RemoveListener(OnDungeonOpens);
            GameManager.Instance.dngMod.OnDungeonCloses.RemoveListener(OnDungeonCloses);
        }
    }

    private void OnDungeonCloses()
    {
        foreach (var e in aliveEntities)
        {
            if (e == null) continue;
            NetworkServer.Destroy(e.gameObject);
        }

        aliveEntities.Clear();

        foreach (var e in deadEntities)
        {
            if (e == null) continue;
            NetworkServer.Destroy(e.gameObject);
        }

        deadEntities.Clear();

        StopCoroutine(enemySpawningCoroutine);
        if (isServer) EntityNetIds.Clear();
    }

    private void OnDungeonOpens()
    {
        aliveEntities.Clear();
        deadEntities.Clear();

        rng = DungeonGenerator.Instance.RNG;
        enemySpawningCoroutine = StartCoroutine(EnemySpawning());
    }

    [Server]
    public void SetSpawnerPositions(List<Transform> positions)
    {
        spawnerPositions = positions.ToArray();
    }

    [Server]
    IEnumerator EnemySpawning()
    {
        float difMultiplier = GameManager.Instance.progressionMod.CurrentDifficultyMultiplier;

        float timer = 0f;
        while (timer < spawnDelay)
        {
            timer += Time.deltaTime * Mathf.Clamp(difMultiplier, 1, 3);
            yield return null;
        }

        timer = 0f;
        float cooldownTimer = 0f;
        while (true)
        {
            while (cooldownTimer > 0)
            {
                cooldownTimer -= Time.deltaTime;
                yield return null;
            }

            while (aliveEntities.Count >= MaxEntities)
            {
                yield return null;
            }

            if (timer >= 5f)
            {
                rng ??= DungeonGenerator.Instance.RNG;
                float rand = (float)(rng.NextDouble() * 100f);

                if (rand * difMultiplier <= spawnChance)
                {
                    if (TrySpawnEnemy())
                        cooldownTimer = spawnCooldown;
                }

                timer = 0f;
            }
            
            timer += Time.deltaTime * difMultiplier;
            yield return null;
        }
    }

    [Server]
    bool TrySpawnEnemy()
    {
        if (aliveEntities.Count >= MaxEntities) return false;

        if (spawnerPositions == null || spawnerPositions.Length <= 0)
        {
            Debug.LogWarning(spawnerPositions == null
                ? "Spawner positions array is null"
                : "Spawner positions array is empty");
            return false;
        }

        int positionIndex = (int)(rng.NextDouble() * spawnerPositions.Length);
        Transform position = spawnerPositions[positionIndex];
        if (position == null) return false;

        ThemeDataSO theme = GameManager.Instance.dngMod.ThemeDatas[GameManager.Instance.dngMod.selectedTheme];
        var prog = GameManager.Instance.progressionMod;

        ThemeDataSO.EntitySpawn spawn = theme.GetWeightedEntitySpawn(
            position.position, rng,
            prog.CurrentMinEntityTier,
            prog.CurrentMaxEntityTier);

        GameObject entityObj = Instantiate(spawn.EntityData.entityPrefab, position.position + position.forward, position.rotation, entityParent);
        entityObj.name = $"{spawn.EntityData.entityPrefab.name} (ID: {TotalQ})";
        NetworkServer.Spawn(entityObj);

        EntityStats stats = entityObj.GetComponentInChildren<EntityStats>();
        stats.OnDeath.AddListener(OnEntityDeath);
        aliveEntities.Add(stats);

        if (entityObj.TryGetComponent<NetworkIdentity>(out var ni))
            EntityNetIds.Add(ni.netId);

        return true;
    }

    [Server]
    void OnEntityDeath(AttackEvent source)
    {
        if (!aliveEntities.Contains(source.TargetStats)) return;
        
        source.TargetStats.OnDeath.RemoveListener(OnEntityDeath);
        aliveEntities.Remove(source.TargetStats);
        deadEntities.Add(source.TargetStats);

        if (source.TargetStats.TryGetComponent<NetworkIdentity>(out var ni))
            EntityNetIds.Remove(ni.netId);
    }
}
