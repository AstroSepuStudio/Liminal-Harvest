using UnityEngine;
using WS_ProceduralGeneration;

public class AIS_SkulkDisengage : AIState
{
    [SerializeField] float fleeDistance = 18f;
    [SerializeField] float fleeRoomSteps = 2;
    [SerializeField] float recalculateInterval = 0.5f;
    [SerializeField] float fleeDuration = 8f;

    float fleeTimer;
    float recalcTimer;
    Vector3 fleeTarget;

    public override void OnEnterState(AIBrain brain)
    {
        brain.ResumeAgentMovement();
        brain.SetIdleState(false);
        brain.Animator_.SetBool("Walk", true);
        fleeTimer = fleeDuration;
        recalcTimer = 0f;

        fleeTarget = PickFleePosition(brain);
        brain.MoveAgent(fleeTarget);
    }

    public override void OnUpdateState(AIBrain brain)
    {
        fleeTimer -= Time.deltaTime;
        recalcTimer -= Time.deltaTime;

        if (recalcTimer <= 0f)
        {
            recalcTimer = recalculateInterval;
            brain.MoveAgent(fleeTarget);
        }

        if (fleeTimer <= 0f || !brain.IsAgentInMovement())
            ((SkulkParasiteAI)brain).ResumeWander();

        brain.Animator_.SetBool("Walk", brain.IsAgentInMovement());
    }

    public override void OnExitState(AIBrain brain)
    {
        brain.Animator_.SetBool("Walk", false);
        brain.SetIdleState(true);
    }

    Vector3 PickFleePosition(AIBrain brain)
    {
        var gen = DungeonGenerator.Instance;
        if (gen == null) return brain.transform.position;

        RoomData currentRoom = gen.GetRoomDataAtPosition(brain.transform.position);
        if (currentRoom == null) return brain.transform.position;

        int currentId = currentRoom.PlacedRoom.id;

        for (int i = 0; i < fleeRoomSteps; i++)
        {
            if (!gen.RoomAdjacency.TryGetValue(currentId, out var neighbors) || neighbors.Count == 0)
                break;

            var list = new System.Collections.Generic.List<int>(neighbors);
            currentId = list[Random.Range(0, list.Count)];
        }

        if (!gen.SpawnedRooms.TryGetValue(currentId, out var targetRoom) || targetRoom == null)
            return brain.transform.position;

        return targetRoom.GetRandomPositionInRoom();
    }
}
