using System.Collections;
using TMPro;
using UnityEngine;
using WS_ProceduralGeneration;

public class DungeonLoadingUI : MonoBehaviour
{
    [SerializeField] DungeonGenerator generator;
    [SerializeField] GameObject canvas;
    [SerializeField] RectTransform panel;
    [SerializeField] CanvasGroup group;
    [SerializeField] TextMeshProUGUI label;
    [SerializeField] float displayDuration = 5f;

    void OnEnable()
    {
        generator.OnGenerationStarted.AddListener(OnStarted);
        generator.OnGenerationFinished.AddListener(OnFinished);
    }

    void OnDisable()
    {
        generator.OnGenerationStarted.RemoveListener(OnStarted);
        generator.OnGenerationFinished.RemoveListener(OnFinished);
    }

    void OnStarted(int seed)
    {
        StopAllCoroutines();
        StartCoroutine(ShowRoutine(seed));
    }

    void OnFinished(int seed)
    {
        StopAllCoroutines();
        StartCoroutine(HideRoutine(seed));
    }

    IEnumerator ShowRoutine(int seed)
    {
        panel.anchoredPosition = new Vector2(0, -225f);
        group.alpha = 0f;
        canvas.SetActive(true);

        LeanTween.moveY(panel, 0f, 0.5f).setEaseOutSine();

        float dotTimer = 0f;
        int dotCount = 0;
        const float dotInterval = 0.22f;

        float elapsed = 0f;
        while (elapsed < 3f)
        {
            group.alpha = elapsed * 2f;
            elapsed += Time.deltaTime;
            dotTimer += Time.deltaTime;
            if (dotTimer >= dotInterval) { dotTimer = 0f; dotCount = (dotCount + 1) % 4; }
            label.SetText($"Connecting to new location{new string('.', dotCount)}\n({seed})");
            yield return null;
        }

        group.alpha = 1f;
    }

    IEnumerator HideRoutine(int seed)
    {
        label.SetText($"New location found!\n({seed})");
        yield return new WaitForSeconds(displayDuration);

        float t = 0.5f;
        while (t > 0f) { group.alpha = t * 2f; t -= Time.deltaTime; yield return null; }
        group.alpha = 0f;
        canvas.SetActive(false);
    }
}
