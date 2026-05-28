using UnityEngine;
using UnityEngine.Events;
using WS_ProceduralGeneration;

public class AIS_DropItemAtHome : AIState
{
    [SerializeField] float recalculateInterval = 0.5f;
    [SerializeField] float dropRange = 1.5f;
    [SerializeField] float movementGracePeriod = 1f;
    [SerializeField] float stuckTimeout = 3f;

    bool hasDropped;
    float stuckTimer;
    float graceTimer;
    float recalcTimer;
    bool destinationSet;
    Vector3 targetPosition;

    public UnityEvent OnItemDropped = new();
    public UnityEvent OnNoItemToDeliver = new();
    public UnityEvent OnArrivedAtHome = new();

    public override void OnEnterState(AIBrain brain)
    {
        var carrier = brain.GetModule<AIModule_ItemCarrier>();
        var home = brain.GetModule<AIModule_Home>();

        if (carrier == null || !carrier.HasItem)
        {
            destinationSet = false;
            return;
        }

        if (home == null || home.GetEffectiveHome() == null)
        {
            destinationSet = false;
            return;
        }
        hasDropped = false;
        brain.Animator_.SetBool("Walk", true);
        destinationSet = true;
        stuckTimer = stuckTimeout;
        graceTimer = movementGracePeriod;
        recalcTimer = recalculateInterval;
        targetPosition = home.GetEffectiveHome().transform.position + PickOffset();
        brain.MoveAgent(targetPosition);
        brain.SetIdleState(false);
    }

    public override void OnUpdateState(AIBrain brain)
    {
        if (!destinationSet)
        {
            OnNoItemToDeliver?.Invoke();
            return;
        }

        if (graceTimer > 0f) { graceTimer -= Time.deltaTime; return; }

        recalcTimer -= Time.deltaTime;
        if (recalcTimer <= 0f)
        {
            recalcTimer = recalculateInterval;
            brain.MoveAgent(targetPosition);
        }

        float dist = Vector3.Distance(brain.transform.position, targetPosition);
        if (!hasDropped && dist <= dropRange)
        {
            hasDropped = true;
            brain.GetModule<AIModule_ItemCarrier>().DropCarriedItem();
            OnArrivedAtHome?.Invoke();
            OnItemDropped?.Invoke();
            graceTimer = 0f;
        }

        bool mov = brain.IsAgentInMovement();
        brain.Animator_.SetBool("Walk", mov);

        if (mov) 
        { 
            stuckTimer = stuckTimeout; 
            return; 
        }

        stuckTimer -= Time.deltaTime;
        if (stuckTimer <= 0f) 
        { 
            OnItemDropped?.Invoke(); 
            return; 
        }
    }

    public override void OnExitState(AIBrain brain)
    {
        brain.Animator_.SetBool("Walk", false);
        destinationSet = false;
        brain.SetIdleState(true);
    }

    Vector3 PickOffset()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float dist = DungeonGenerator.Instance.CellSize * 0.4f;
        return new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
    }
}
