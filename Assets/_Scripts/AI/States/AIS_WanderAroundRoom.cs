using UnityEngine;
using UnityEngine.Events;
using WS_ProceduralGeneration;

public class AIS_WanderAroundRoom : AIState
{
    [SerializeField] float minSleep = 3f;
    [SerializeField] float maxSleep = 10f;

    float sleepTimer = 0;

    public UnityEvent OnWanderStart;
    public UnityEvent OnWanderCompleted;

    bool moving = false;

    public override void OnEnterState(AIBrain brain)
    {
        MoveAgent(brain);
        brain.SetIdleState(false);
    }

    public override void OnExitState(AIBrain brain)
    {
        brain.Animator_.SetBool("Walk", false);
        brain.SetIdleState(true);
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

    private Vector3 GetRandomRoomPosition(Vector3 origin)
    {
        var gen = DungeonGenerator.Instance;
        if (gen == null) return origin;
        
        RoomData rd = gen.GetRoomDataAtPosition(transform.position);
        if (rd == null) return origin;

        return rd.GetRandomPositionInRoom();
    }

    private void MoveAgent(AIBrain brain)
    {
        if (sleepTimer > 0) return;
        brain.SetIdleState(false);

        brain.MoveAgent(GetRandomRoomPosition(transform.position));
        sleepTimer = Random.Range(minSleep, maxSleep);

        moving = true;
        OnWanderStart?.Invoke();
    }
}
