using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using WS_ProceduralGeneration;

public class AIS_SearchForItems : AIState
{
    [SerializeField] int maxSearchAttempts = 5;
    [SerializeField] float waitAtPositionDuration = 3f;
    [SerializeField] int maxRoomStepsPerMove = 2;

    int attemptsRemaining;
    float waitTimer;
    bool waitingAtPosition;

    public UnityEvent OnItemFound;
    public UnityEvent OnSearchFailed;

    public override void OnEnterState(AIBrain brain)
    {
        attemptsRemaining = maxSearchAttempts;
        waitingAtPosition = false;
        waitTimer = 0f;
        MoveToNextSearchPosition(brain);
        brain.SetIdleState(false);
    }

    public override void OnUpdateState(AIBrain brain)
    {
        if (brain.TryGetModule<AIModule_ItemCarrier>(out var carrier) && carrier.HasItem)
        {
            OnItemFound?.Invoke();
            return;
        }

        if (waitingAtPosition)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f)
            {
                waitingAtPosition = false;
                if (attemptsRemaining <= 0) { OnSearchFailed?.Invoke(); return; }
                MoveToNextSearchPosition(brain);
            }
            return;
        }

        if (!brain.IsAgentInMovement())
        {
            brain.Animator_.SetBool("Walk", false);
            waitingAtPosition = true;
            waitTimer = waitAtPositionDuration;
            brain.SetIdleState(true);
        }
    }

    public override void OnExitState(AIBrain brain)
    {
        brain.Animator_.SetBool("Walk", false);
        waitingAtPosition = false;
        brain.SetIdleState(true);
    }

    void MoveToNextSearchPosition(AIBrain brain)
    {
        brain.SetIdleState(false);
        attemptsRemaining--;

        var gen = DungeonGenerator.Instance;
        if (gen == null) { OnSearchFailed?.Invoke(); return; }

        RoomData currentRoom = gen.GetRoomDataAtPosition(brain.transform.position);
        if (currentRoom == null) { OnSearchFailed?.Invoke(); return; }

        int currentId = currentRoom.PlacedRoom.id;
        int steps = Random.Range(1, maxRoomStepsPerMove + 1);

        for (int i = 0; i < steps; i++)
        {
            if (!gen.RoomAdjacency.TryGetValue(currentId, out var neighbors) || neighbors.Count == 0)
                break;

            var candidates = new List<int>(neighbors);
            currentId = candidates[Random.Range(0, candidates.Count)];
        }

        if (!gen.SpawnedRooms.TryGetValue(currentId, out var targetRoom) || targetRoom == null)
        {
            OnSearchFailed?.Invoke();
            return;
        }

        brain.MoveAgent(targetRoom.GetRandomPositionInRoom());
        brain.Animator_.SetBool("Walk", true);
    }
}
