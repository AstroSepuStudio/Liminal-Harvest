using LethalLive;
using UnityEngine;

namespace WS_ProceduralGeneration
{
    [RequireComponent(typeof(Light))]
    public class LightLOD : MonoBehaviour
    {
        [SerializeField] private Light _light;
        [SerializeField] private float disDisMult = 1.1f;
        [SerializeField] private float shaDisMult = 0.8f;

        private void Start()
        {
            GameTick.OnSecond += OnSecond;
        }

        private void OnDestroy()
        {
            GameTick.OnSecond -= OnSecond;
        }

        void OnSecond()
        {
            if (GameManager.Instance.playMod.LocalPlayer == null) return;

            float distance = Vector3.Distance(transform.position, GameManager.Instance.playMod.LocalPlayer.transform.position);
            float renderDistance = SettingsManager.Instance.UserSettings.GetRenderDistance() * DungeonGenerator.Instance.CellSize;

            if (distance >= renderDistance * disDisMult)
                _light.enabled = false;
            else if (distance >= renderDistance * shaDisMult)
                _light.shadows = LightShadows.None;
            else
            {
                _light.enabled = true;
                _light.shadows = LightShadows.Hard;
            }
        }
    }
}
