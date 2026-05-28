using Mirror;
using System.Linq;
using UnityEngine;
using WS_ProceduralGeneration;

public class VoidDestroyer : MonoBehaviour
{
    [SerializeField] Transform targetTransform;

    [SerializeField] string playerTag = "Player";
    [SerializeField] string[] tagsToDestroy = new string[]{
            "Item",
            "Furniture"
        };

    private void Awake()
    {
        if (targetTransform == null) 
            targetTransform = transform;
    }

    private void Start()
    {
        DungeonGenerator.Instance.OnDungeonSetUp.RemoveListener(SetScale);
        DungeonGenerator.Instance.OnDungeonSetUp.AddListener(SetScale);
    }

    private void OnDestroy()
    {
        
    }

    public void SetScale(float size, float seed)
    {
        targetTransform.localScale = new Vector3(size + 2, 1, size + 2);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!NetworkServer.active) return;

        if (tagsToDestroy.Contains(other.gameObject.tag))
            NetworkServer.Destroy(other.gameObject);

        if (other.gameObject.CompareTag(playerTag))
        {
            if (other.TryGetComponent(out PlayerStats pStats))
                pStats.ExecutePlayer();
        }
    }
}
