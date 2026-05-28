using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using WS_ProceduralGeneration;

public class AIS_Wander : AIState
{
    [Header("Between Rooms")]
    [SerializeField] int minRooms = 1;
    [SerializeField] int maxRooms = 3;

    [Header("In-Room Cycles")]
    [SerializeField] int minRoomWanders = 1;
    [SerializeField] int maxRoomWanders = 4;

    [Header("Timing")]
    [SerializeField] float minSleep = 3f;
    [SerializeField] float maxSleep = 10f;

    enum Phase { BetweenRooms, AroundRoom }
    Phase currentPhase = Phase.BetweenRooms;

    int targetRoomWanders = 0;
    int roomWanderCount = 0;

    float sleepTimer = 0f;
    bool moving = false;

    public UnityEvent OnWanderStart;
    public UnityEvent OnWanderCompleted;

    public override void OnEnterState(AIBrain brain)
    {
        brain.SetIdleState(false);
        StartBetweenRooms(brain);
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
            OnMoveCompleted(brain);
        }

        sleepTimer -= Time.deltaTime;

        if (sleepTimer <= 0f)
            MoveAgent(brain, GetDestination());
    }

    void OnMoveCompleted(AIBrain brain)
    {
        if (currentPhase == Phase.BetweenRooms)
        {
            currentPhase = Phase.AroundRoom;
            targetRoomWanders = Random.Range(minRoomWanders, maxRoomWanders + 1);
            roomWanderCount = 0;
            return;
        }

        roomWanderCount++;
        if (roomWanderCount >= targetRoomWanders)
            StartBetweenRooms(brain);
    }

    void StartBetweenRooms(AIBrain brain)
    {
        currentPhase = Phase.BetweenRooms;
        MoveAgent(brain, GetBetweenRoomsDestination());
    }

    void MoveAgent(AIBrain brain, Vector3 destination)
    {
        if (sleepTimer > 0f) return;

        brain.SetIdleState(false);
        brain.MoveAgent(destination);
        sleepTimer = Random.Range(minSleep, maxSleep);
        moving = true;
        OnWanderStart?.Invoke();
    }

    Vector3 GetDestination() => currentPhase == Phase.BetweenRooms
        ? GetBetweenRoomsDestination()
        : GetAroundRoomDestination();

    Vector3 GetAroundRoomDestination()
    {
        var gen = DungeonGenerator.Instance;
        if (gen == null) return transform.position;

        RoomData room = gen.GetRoomDataAtPosition(transform.position);
        return room != null ? room.GetRandomPositionInRoom() : transform.position;
    }

    Vector3 GetBetweenRoomsDestination()
    {
        var gen = DungeonGenerator.Instance;
        if (gen == null) return transform.position;

        RoomData startRoom = gen.GetRoomDataAtPosition(transform.position);
        if (startRoom == null) return transform.position;

        int startId = startRoom.PlacedRoom.id;
        int currentId = startId;
        int steps = Random.Range(minRooms, maxRooms + 1);

        for (int i = 0; i < steps; i++)
        {
            if (!gen.RoomAdjacency.TryGetValue(currentId, out var neighbors) || neighbors.Count == 0)
                break;

            var candidates = new List<int>(neighbors);
            if (candidates.Count > 1) candidates.Remove(startId);
            currentId = candidates[Random.Range(0, candidates.Count)];
        }

        if (!gen.SpawnedRooms.TryGetValue(currentId, out var targetRoom) || targetRoom == null)
            return transform.position;

        return targetRoom.GetRandomPositionInRoom();
    }
}
