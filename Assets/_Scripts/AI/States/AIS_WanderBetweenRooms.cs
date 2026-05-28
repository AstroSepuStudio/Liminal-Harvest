using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using WS_ProceduralGeneration;

public class AIS_WanderBetweenRooms : AIState
{
    [SerializeField] int minRooms = 1;
    [SerializeField] int maxRooms = 5;

    [SerializeField] float minSleep = 3f;
    [SerializeField] float maxSleep = 10f;

    float sleepTimer = 0;

    public UnityEvent OnWanderStart;
    public UnityEvent OnWanderCompleted;

    bool moving = false;

    public override void OnEnterState(AIBrain brain)
    {
        brain.SetIdleState(false);
        MoveAgent(brain);
    }

    public override void OnExitState(AIBrain brain)
    {
        brain.SetIdleState(true);
        brain.Animator_.SetBool("Walk", false);
    }

    public override void OnUpdateState(AIBrain brain)
    {
        bool mov = brain.IsAgentInMovement();

        brain.Animator_.SetBool("Walk", mov);
        if (mov) return;

        if (moving)
        {
            brain.SetIdleState(true);
            moving = false;
            OnWanderCompleted?.Invoke();
        }

        sleepTimer -= Time.deltaTime;

        if (sleepTimer <= 0)
            MoveAgent(brain);
    }

    private Vector3 GetRandomRoomPosition(Vector3 origin, int maxRoomDist, int minRoomDist = 0)
    {
        var gen = DungeonGenerator.Instance;
        if (gen == null) return origin;

        RoomData startRd = gen.GetRoomDataAtPosition(origin);
        if (startRd == null) return origin;

        int startId = startRd.PlacedRoom.id;
        int targetSteps = Random.Range(minRoomDist, maxRoomDist + 1);

        int currentId = startId;
        for (int i = 0; i < targetSteps; i++)
        {
            if (!gen.RoomAdjacency.TryGetValue(currentId, out var neighbors) || neighbors.Count == 0)
                break;

            var candidates = new List<int>(neighbors);
            if (candidates.Count > 1) candidates.Remove(startId);

            currentId = candidates[Random.Range(0, candidates.Count)];
        }

        if (!gen.SpawnedRooms.TryGetValue(currentId, out var targetRd) || targetRd == null)
            return origin;

        return targetRd.GetRandomPositionInRoom();
    }

    private void MoveAgent(AIBrain brain)
    {
        if (sleepTimer > 0) return;
        brain.SetIdleState(false);

        brain.MoveAgent(GetRandomRoomPosition(transform.position, maxRooms, minRooms));
        sleepTimer = Random.Range(minSleep, maxSleep);

        moving = true;
        OnWanderStart?.Invoke();
    }
}
