using Mirror;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class EmoteWheelManager : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] PlayerData pData;
    [SerializeField] NetworkAnimator networkAnimator;
    [SerializeField] GameObject wheelObj;
    [SerializeField] RectTransform wheelRect;
    [SerializeField] EmoteWheelPiece[] pieces;
    [SerializeField] Emote[] emotes;
    [SerializeField] float speed = 5f;
    [SerializeField] float rotationSpeed;
    [SerializeField] float delayForEach = 0.2f;

    Coroutine[] activeCoroutines;
    Coroutine activeRotateCoroutine;

    [SerializeField] Emote[] currentEmotes;
    Emote currentLoopEmote;
    bool _playedEmote = false;

    [SyncVar(hook = nameof(OnPageChanged))]
    int emotePage = 0;

    private void Awake()
    {
        wheelObj.SetActive(false);
        activeCoroutines = new Coroutine[pieces.Length];

        for (int i = 0; i < pieces.Length; i++)
        {
            pieces[i].pivot.localScale = Vector3.zero;
            pieces[i].canvasGroup.alpha = 0f;
        }

        currentEmotes = new Emote[6];
        SetEmotesByPage(0);
        wheelRect.localRotation = Quaternion.Euler(0, 0, 135f);
    }

    #region Client wheel
    public void OnPressEmoteWheel(InputAction.CallbackContext context)
    {
        if (!pData.isLocalPlayer || pData._LockPlayer) return;

        if (context.started) OpenWheel();
        else if (context.canceled) CloseWheel();
    }

    void OpenWheel()
    {
        if (pData.HUDManager.OpenedWindows > 0) return;
        pData.HUDManager.OpenWindow(wheelObj);
        pData.Camera_Movement.PauseCamera();
        wheelObj.SetActive(true);

        if (activeRotateCoroutine != null) StopCoroutine(activeRotateCoroutine);
        activeRotateCoroutine = StartCoroutine(AnimateWheel(0f));

        for (int i = 0; i < pieces.Length; i++)
        {
            if (activeCoroutines[i] != null) StopCoroutine(activeCoroutines[i]);
            activeCoroutines[i] = StartCoroutine(AnimateButton(i, Vector3.one, 1f, i * delayForEach));
        }
    }

    void CloseWheel()
    {
        pData.HUDManager.CloseWindow(wheelObj);
        pData.Camera_Movement.ResumeCamera();

        if (activeRotateCoroutine != null) StopCoroutine(activeRotateCoroutine);
        activeRotateCoroutine = StartCoroutine(AnimateWheel(135f));

        for (int i = pieces.Length - 1; i >= 0; i--)
        {
            if (activeCoroutines[i] != null) StopCoroutine(activeCoroutines[i]);
            activeCoroutines[i] = StartCoroutine(AnimateButton(i, Vector3.zero, 0f, (pieces.Length - 1 - i) * delayForEach));
        }

        if (_playedEmote)
            _playedEmote = false;
        else
            CancelEmote();
    }

    IEnumerator AnimateButton(int index, Vector3 targetScale, float targetAlpha, float delay)
    {
        RectTransform pivot = pieces[index].pivot;
        CanvasGroup group = pieces[index].canvasGroup;

        yield return new WaitForSeconds(delay);

        while (Vector3.Distance(pivot.localScale, targetScale) > 0.01f ||
               Mathf.Abs(group.alpha - targetAlpha) > 0.01f)
        {
            pivot.localScale = Vector3.MoveTowards(pivot.localScale, targetScale, Time.deltaTime * speed);
            group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, Time.deltaTime * speed);
            yield return null;
        }

        pivot.localScale = targetScale;
        group.alpha = targetAlpha;
        activeCoroutines[index] = null;

        bool allClosed = true;
        for (int i = 0; i < pieces.Length; i++)
        {
            if (!Mathf.Approximately(pieces[i].canvasGroup.alpha, 0f))
            {
                allClosed = false;
                break;
            }
        }

        if (allClosed) wheelObj.SetActive(false);
    }

    IEnumerator AnimateWheel(float targetRotation)
    {
        while (Mathf.Abs(wheelRect.localRotation.eulerAngles.z - targetRotation) > 0.01f)
        {
            wheelRect.localRotation = Quaternion.Euler(0, 0,
                Mathf.MoveTowards(wheelRect.localRotation.eulerAngles.z, targetRotation, Time.deltaTime * rotationSpeed));
            yield return null;
        }
    }
    #endregion

    #region Page Change
    public void RequestChangePage(int delta)
    {
        if (!isLocalPlayer) return;
        CmdChangePage(delta);
    }

    [Command]
    void CmdChangePage(int delta)
    {
        int newPage = emotePage + delta;
        int startIndex = newPage * 6;

        if (newPage < 0 || startIndex >= emotes.Length) return;

        bool hasEmote = false;
        for (int i = 0; i < 6; i++)
        {
            if (startIndex + i < emotes.Length) { hasEmote = true; break; }
        }

        if (hasEmote) emotePage = newPage;
    }

    void OnPageChanged(int oldPage, int newPage)
    {
        SetEmotesByPage(newPage);
        RefreshUI();
    }

    void SetEmotesByPage(int page)
    {
        emotePage = page;
        for (int i = 0; i < 6; i++)
        {
            int index = i + 6 * page;
            currentEmotes[i] = index < emotes.Length ? emotes[index] : null;
        }

        RefreshUI();
    }

    void RefreshUI()
    {
        for (int i = 0; i < pieces.Length; i++)
            pieces[i].UpdatePiece(currentEmotes[i]);
    }
    #endregion

    #region Emote Logic
    public void PlayerMoves()
    {
        if (!isLocalPlayer) return;
        CmdStopLoopEmote();
    }

    [Command]
    void CmdStopLoopEmote()
    {
        if (currentLoopEmote == null || currentLoopEmote.dynamic) return;
        ServerClearLoopEmote();
    }

    public void PlayEmote(int index)
    {
        if (!isLocalPlayer) return;
        if (pData.PlayerInventory.IsItemInUse) return;

        _playedEmote = true;
        CmdPlayEmote(index);
    }

    [Command]
    void CmdPlayEmote(int index)
    {
        if (index < 0 || index >= currentEmotes.Length) return;
        if (currentEmotes[index] == null) return;
        if (pData.PlayerInventory.IsItemInUse) return;

        Emote emote = currentEmotes[index];

        if (currentLoopEmote != null)
        {
            networkAnimator.animator.SetBool(currentLoopEmote.animatorTrigger, false);

            if (currentLoopEmote == emote)
            {
                currentLoopEmote = null;
                pData.Skin_Data.Rigging_Manager.RpcSetEmoteIK(false);
                return;
            }

            currentLoopEmote = null;
        }

        if (emote.loop)
        {
            currentLoopEmote = emote;
            networkAnimator.animator.SetBool(emote.animatorTrigger, true);
        }
        else
        {
            networkAnimator.SetTrigger(emote.animatorTrigger);
        }

        pData.Skin_Data.Rigging_Manager.RpcSetEmoteIK(emote.disableIK);
    }

    public void CancelEmote()
    {
        if (!isLocalPlayer) return;
        CmdCancelEmote();
    }

    [Command]
    void CmdCancelEmote()
    {
        ServerClearLoopEmote();
    }

    public void ServerClearLoopEmote()
    {
        if (currentLoopEmote == null) return;
        networkAnimator.animator.SetBool(currentLoopEmote.animatorTrigger, false);
        currentLoopEmote = null;
        pData.Skin_Data.Rigging_Manager.RpcSetEmoteIK(false);
    }
    #endregion
}
