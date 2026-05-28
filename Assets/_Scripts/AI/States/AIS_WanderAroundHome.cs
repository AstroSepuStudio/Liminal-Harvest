using UnityEngine;
using UnityEngine.Events;
using WS_ProceduralGeneration;

public class AIS_WanderAroundHome : AIState
{
    [SerializeField] float minSleep = 3f;
    [SerializeField] float maxSleep = 8f;
    [SerializeField] int maxWandersBeforeLeaving = 2;

    int wanderCount;
    float sleepTimer;
    bool moving;
    bool arrived;

    public UnityEvent OnWanderStart;
    public UnityEvent OnWanderCompleted;
    public UnityEvent OnHomeVisitComplete;
    public UnityEvent OnArrivedAtHome;

    public override void OnEnterState(AIBrain brain)
    {
        wanderCount = 0;
        sleepTimer = 0f;
        moving = false;
        arrived = false;
        MoveToRandomHomePosition(brain);
        brain.SetIdleState(false);
    }

    public override void OnUpdateState(AIBrain brain)
    {
        bool mov = brain.IsAgentInMovement();
        brain.Animator_.SetBool("Walk", mov);
        if (mov) return;

        if (moving)
        {
            moving = false;
            wanderCount++;
            OnWanderCompleted?.Invoke();
            brain.SetIdleState(true);

            bool isAlpha = brain.TryGetModule<AIModule_Alpha>(out var alpha) && alpha.IsActingAsAlpha;
            if (brain is VortexAI vai) isAlpha = vai.IsAlpha;

            if (!isAlpha && wanderCount >= maxWandersBeforeLeaving)
                OnHomeVisitComplete?.Invoke();
        }

        sleepTimer -= Time.deltaTime;
        if (sleepTimer <= 0f)
        {
            if (!arrived && !moving)
            {
                arrived = true;
                OnArrivedAtHome?.Invoke();
            }

            if (brain.TryGetModule<AIModule_ItemCarrier>(out var carrier) && carrier.HasItem)
            {
                carrier.DropCarriedItem();
                return;
            }

            MoveToRandomHomePosition(brain);
        }
    }

    public override void OnExitState(AIBrain brain)
    {
        brain.Animator_.SetBool("Walk", false);
        brain.SetIdleState(true);
    }

    void MoveToRandomHomePosition(AIBrain brain)
    {
        var home = brain.GetModule<AIModule_Home>();
        RoomData homeRoom = home?.GetEffectiveHome();
        if (homeRoom == null) return;
        brain.SetIdleState(false);

        brain.MoveAgent(homeRoom.GetRandomPositionInRoom());
        brain.Animator_.SetBool("Walk", true);
        sleepTimer = Random.Range(minSleep, maxSleep);
        moving = true;
        OnWanderStart?.Invoke();
    }
}
