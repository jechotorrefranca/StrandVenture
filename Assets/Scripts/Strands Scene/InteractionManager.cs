using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public class InteractionManager : MonoBehaviour
{
    [Header("Bot Interaction")]
    [Tooltip("Root or collider transform used for looking at the bot.")]
    public Transform botLookTarget;

    [Header("Core refs")]
    public TimedBotSequence botSequence;
    public FirstPersonCameraMovement playerController;
    public Camera playerCamera;

    [Header("Interactables")]
    public InteractableItem[] interactables;
    public LayerMask interactableLayer = -1;

    [Header("Gaze")]
    [Tooltip("Maximum raycast distance for looking at items / bot")]
    public float gazeMaxDistance = 8f;

    [Header("HUD prompt")]
    public CanvasGroup promptCanvasGroup;
    public TMP_Text promptText;
    public float promptScaleInTime = 0.12f;
    public Vector3 promptTargetScale = Vector3.one;

    [Header("Info UI")]
    [Tooltip("Fallback info UI prefab used if InteractableItem doesn't provide one (optional)")]
    public GameObject infoUIPrefab;
    public float uiScaleDuration = 0.18f;

    [Header("UI root (drag your Canvas or parent Panel (RectTransform) here)")]
    public RectTransform uiRoot;

    [Header("Final Bot Dialogue")]
    public AnimationClip finalBotAnimation;
    public AudioClip finalBotAudio;
    public Transform finalBotPosition;
    public string nextSceneName;

    [Tooltip("Subtitles for the FINAL bot dialogue (handled by TimedBotSequence).")]
    public SubtitleSegment[] finalSubtitleSegments;
    [Tooltip("Allow skipping the FINAL bot dialogue via space + radial pie.")]
    public bool finalAllowSkip = true;

    [Header("Final Interaction Panel")]
    [Tooltip("Panel prefab with two buttons: Not Yet / Finish Experience.")]
    public GameObject finalPanelPrefab;
    [Tooltip("Optional: child name of the 'Not Yet' button inside the finalPanelPrefab.")]
    public string finalNotYetButtonName = "notyet";
    [Tooltip("Optional: child name of the 'Finish Experience' button inside the finalPanelPrefab.")]
    public string finalFinishButtonName = "finish";

    [Header("Debug")]
    public bool debugPrompts = false;

    private InteractableItem currentLooked = null;
    private bool isActive = false;
    private int completedCount = 0;
    private bool isInspecting = false;

    private Coroutine currentSpaceCoroutine = null;
    private InteractableItem currentSpaceItem = null;

    private Vector3 playerSavedPos;
    private Quaternion playerSavedRot;
    private CharacterController playerCC;

    private GameObject currentInfoUI;

    private bool currentInfoUIIsSceneObject = false;
    private Transform currentInfoUIOriginalParent = null;
    private int currentInfoUIOriginalSibling = 0;
    private bool currentInfoUIOriginalActiveState = false;

    private Coroutine promptCoroutine;

    // tracking completion & final flow
    private bool allItemsCompleted = false;
    private bool finalActionPlayed = false;

    // final panel
    private GameObject finalPanelInstance;
    private bool finalPanelOpen = false;

    // track if final panel is a scene object or spawned prefab
    private bool finalPanelIsSceneObject = false;
    private Transform finalPanelOriginalParent = null;
    private int finalPanelOriginalSibling = 0;
    private bool finalPanelOriginalActiveState = false;

    void Start()
    {
        if (playerCamera == null && Camera.main != null)
            playerCamera = Camera.main;

        if (playerController != null)
            playerCC = playerController.GetComponent<CharacterController>();

        if (promptCanvasGroup != null)
            promptCanvasGroup.alpha = 0f;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (interactables != null)
        {
            foreach (var it in interactables)
            {
                if (it != null) it.SetHighlight(false);
            }
        }

        if (botSequence != null && botSequence.interactionManager == null)
            botSequence.interactionManager = this;

        if (uiRoot == null)
        {
            var c = FindObjectOfType<Canvas>();
            if (c != null)
                uiRoot = c.GetComponent<RectTransform>();
            else
                Debug.LogWarning("InteractionManager: No Canvas found in scene. Please assign uiRoot in inspector.");
        }
    }

    public void ActivateInteractables()
    {
        isActive = true;
        completedCount = 0;
        allItemsCompleted = false;
        finalActionPlayed = false;

        if (interactables == null) return;

        foreach (var it in interactables)
        {
            if (it != null && !it.inspected)
                it.SetHighlight(true);
        }
    }

    void Update()
    {
        if (!isActive || isInspecting) return;
        if (playerCamera == null) return;

        var kb = Keyboard.current;
        Ray r = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        bool botPromptHandled = false;

        // -----------------------------
        // 1) Final bot interaction (after final dialogue played)
        // -----------------------------
        if (finalActionPlayed && !finalPanelOpen)
        {
            if (Physics.Raycast(r, out RaycastHit botHit, gazeMaxDistance))
            {
                bool lookingAtBot = IsLookingAtBot(botHit.transform);

                if (debugPrompts)
                {
                    Debug.Log($"[BOT RAY] Hit: {botHit.collider.name}, " +
                              $"layer={LayerMask.LayerToName(botHit.collider.gameObject.layer)}, " +
                              $"IsLookingAtBot={lookingAtBot}, finalActionPlayed={finalActionPlayed}");
                }

                if (lookingAtBot)
                {
                    // looking directly at the bot → show bot prompt
                    ShowPrompt("Press <E> to interact");
                    botPromptHandled = true;

                    if (kb != null && kb.eKey.wasPressedThisFrame)
                    {
                        if (debugPrompts) Debug.Log("[BOT] E pressed → opening final panel");
                        StartCoroutine(OpenFinalPanel());
                    }

                    // IMPORTANT: when looking at bot, do NOT process item prompts
                    return;
                }
            }

            // Not looking at the bot AND not hovering any item → clear any stale bot prompt
            if (!botPromptHandled && currentLooked == null)
            {
                HidePrompt();
            }
        }

        // -----------------------------
        // 2) Normal interactables
        //    (works both before and after finalActionPlayed; only blocked while looking at bot)
        // -----------------------------
        if (Physics.Raycast(r, out RaycastHit hit, gazeMaxDistance, interactableLayer))
        {
            var item = hit.collider.GetComponent<InteractableItem>();
            if (item == null)
                item = hit.collider.GetComponentInParent<InteractableItem>();

            if (item != null)
            {
                currentLooked = item;
                UpdatePromptForItem(item);

                if (kb != null)
                {
                    if (kb.qKey.wasPressedThisFrame)
                    {
                        if (currentSpaceCoroutine != null)
                        {
                            StopCoroutine(currentSpaceCoroutine);
                            currentSpaceCoroutine = null;
                            currentSpaceItem = null;
                        }

                        currentSpaceCoroutine = StartCoroutine(HandleSpaceForItem(item));
                    }
                    else if (kb.eKey.wasPressedThisFrame && item.spaceDone && !isInspecting)
                    {
                        StartCoroutine(HandleEForItem(item));
                    }
                }
                return;
            }
        }

        // -----------------------------
        // 3) Nothing hit
        // -----------------------------
        if (currentLooked != null)
        {
            currentLooked = null;
            HidePrompt();
        }
    }

    // Check if gaze hit belongs to the bot model hierarchy
    bool IsLookingAtBot(Transform t)
    {
        // Prefer explicit botLookTarget if set
        Transform targetRoot = null;

        if (botLookTarget != null)
        {
            targetRoot = botLookTarget;
        }
        else if (botSequence != null && botSequence.botModel != null)
        {
            targetRoot = botSequence.botModel.transform;
        }

        if (targetRoot == null) return false;

        // Are we looking directly at this transform or one of its children?
        return t == targetRoot || t.IsChildOf(targetRoot);
    }

    void UpdatePromptForItem(InteractableItem item)
    {
        if (item == null) return;

        string promptMsg = "";

        if (item.inspected)
        {
            if (item.spaceDone)
                promptMsg = "Press <E> to inspect again\nPress <Q> to replay AXEL";
            else
                promptMsg = "Press <E> to inspect again\nPress <Q> to call AXEL";
        }
        else
        {
            if (item.spaceDone)
                promptMsg = "Press <E> to inspect\n(Press <Q> again to replay AXEL)";
            else
                promptMsg = "Press <Q> to call bot";
        }

        if (debugPrompts) Debug.Log($"[UpdatePrompt] {item.name}: inspected={item.inspected}, spaceDone={item.spaceDone} → \"{promptMsg}\"");
        ShowPrompt(promptMsg);
    }

    void ShowPrompt(string text)
    {
        if (promptText != null) promptText.text = text;
        if (promptCanvasGroup == null) return;

        if (promptCoroutine != null) StopCoroutine(promptCoroutine);
        promptCoroutine = StartCoroutine(FadeAndScalePrompt(1f, promptScaleInTime, promptTargetScale));
    }

    void HidePrompt()
    {
        if (promptCanvasGroup == null) return;

        if (promptCoroutine != null) StopCoroutine(promptCoroutine);
        promptCoroutine = StartCoroutine(FadeAndScalePrompt(0f, 0.12f, Vector3.one * 0.9f));
    }

    IEnumerator FadeAndScalePrompt(float targetAlpha, float duration, Vector3 targetScale)
    {
        if (promptCanvasGroup == null) yield break;

        Transform t = promptCanvasGroup.transform;
        Vector3 startScale = t.localScale;
        Vector3 endScale = targetScale;
        float startAlpha = promptCanvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / duration);
            promptCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, k);
            t.localScale = Vector3.Lerp(startScale, endScale, Mathf.SmoothStep(0f, 1f, k));
            yield return null;
        }

        promptCanvasGroup.alpha = targetAlpha;
        t.localScale = endScale;
    }

    IEnumerator HandleSpaceForItem(InteractableItem item)
    {
        if (item == null) yield break;

        if (currentSpaceCoroutine != null && currentSpaceItem != null && currentSpaceItem != item)
        {
            StopCoroutine(currentSpaceCoroutine);
            currentSpaceCoroutine = null;
            currentSpaceItem = null;
        }

        currentSpaceItem = item;
        HidePrompt();

        bool wasAlreadyCompleted = item.spaceDone;

        if (!wasAlreadyCompleted)
        {
            item.spaceDone = false;
            if (debugPrompts) Debug.Log($"[Space] Starting first-time interaction for {item.name}, set spaceDone=false");
        }
        else
        {
            if (debugPrompts) Debug.Log($"[Space] Replaying interaction for {item.name}, keeping spaceDone=true");
        }

        TimedBotAction action = new TimedBotAction
        {
            animationClip = item.interactionAnimation,
            audioClip = item.interactionAudio,
            botPosition = item.botTargetPosition,
            moveDuration = item.interactionAnimation != null ? item.interactionAnimation.length : 0f,
            startAudioAfterAnimation = true,
            lockRotationDuringMove = true,
        };

        if (botSequence != null)
        {
            yield return StartCoroutine(botSequence.TriggerImmediateActionAndWait(action));
        }

        item.spaceDone = true;
        if (debugPrompts) Debug.Log($"[Space] Sequence finished for {item.name}, set spaceDone=true. CurrentLooked={(currentLooked != null ? currentLooked.name : "null")}");

        yield return null;

        if (currentLooked == item)
        {
            if (debugPrompts) Debug.Log($"[Space] Player is looking at {item.name}, forcing prompt update");
            UpdatePromptForItem(item);
        }

        currentSpaceCoroutine = null;
        currentSpaceItem = null;
    }

    IEnumerator HandleEForItem(InteractableItem item)
    {
        if (isInspecting) yield break;
        if (item == null || !item.spaceDone) yield break;

        isInspecting = true;
        HidePrompt();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Transform playerT = playerController.transform;
        playerSavedPos = playerT.position;
        playerSavedRot = playerT.rotation;

        if (playerCC != null) playerCC.enabled = false;
        playerController.SetCanLookAround(false);
        playerController.SetCanMove(false);

        if (item.playerViewTransform != null)
        {
            playerT.position = item.playerViewTransform.position;
            playerT.rotation = item.playerViewTransform.rotation;
        }

        GameObject prefabOrSceneObj = item.interactionPanelPrefab != null ? item.interactionPanelPrefab : infoUIPrefab;

        if (prefabOrSceneObj != null)
        {
            currentInfoUIIsSceneObject = false;
            currentInfoUIOriginalParent = null;

            if (uiRoot == null)
            {
                var c = FindObjectOfType<Canvas>();
                if (c != null) uiRoot = c.GetComponent<RectTransform>();
            }

            bool isSceneObject = prefabOrSceneObj.scene.IsValid();

            if (isSceneObject)
            {
                currentInfoUI = prefabOrSceneObj;
                currentInfoUIOriginalParent = currentInfoUI.transform.parent;
                currentInfoUIOriginalSibling = currentInfoUI.transform.GetSiblingIndex();
                currentInfoUIOriginalActiveState = currentInfoUI.activeSelf;
                currentInfoUI.SetActive(true);
                currentInfoUIIsSceneObject = true;

                if (uiRoot != null && !currentInfoUI.transform.IsChildOf(uiRoot))
                {
                    Debug.LogWarning("InteractionManager: scene panel is not under the chosen uiRoot. Ensure the panel is parented to a Canvas or the intended parent.");
                }
            }
            else
            {
                if (uiRoot != null)
                {
                    currentInfoUI = Instantiate(prefabOrSceneObj, uiRoot, false);
                }
                else
                {
                    currentInfoUI = Instantiate(prefabOrSceneObj);
                }
                currentInfoUIIsSceneObject = false;

                var rt = currentInfoUI.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.localScale = Vector3.one;
                    rt.SetAsLastSibling();
                }
            }

            if (currentInfoUI == null)
            {
                Debug.LogWarning("InteractionManager: failed to create or find info UI.");
            }
            else
            {
                Button closeBtn = null;
                var closeGo = currentInfoUI.transform.Find("CloseButton");
                if (closeGo != null) closeBtn = closeGo.GetComponent<Button>();
                if (closeBtn == null)
                {
                    closeBtn = currentInfoUI.GetComponentInChildren<Button>();
                }

                if (closeBtn != null)
                {
                    closeBtn.onClick.AddListener(() =>
                    {
                        StartCoroutine(CloseInfoUI(item));
                    });
                }
                else
                {
                    Debug.LogWarning("No close button found in UI prefab! Please add a Button or name it 'CloseButton'.");
                }

                // NEW: Let the panel script initialise itself
                var uiController = currentInfoUI.GetComponent<IInteractableUI>();
                if (uiController != null)
                {
                    uiController.Init(item, this);
                }
                else
                {
                    // Fallback: simple text fill (uses itemInfo if set, else infoText)
                    var tmp = currentInfoUI.GetComponentInChildren<TMP_Text>();
                    if (tmp != null)
                    {
                        string textToUse = string.IsNullOrEmpty(item.itemInfo) ? item.infoText : item.itemInfo;
                        tmp.text = textToUse;
                    }
                }

                var cg = currentInfoUI.GetComponent<CanvasGroup>();
                if (cg == null) cg = currentInfoUI.AddComponent<CanvasGroup>();

                cg.alpha = 0f;
                currentInfoUI.transform.localScale = Vector3.one * 0.8f;
                StartCoroutine(AnimateUIIn(cg, currentInfoUI.transform));

                var es = EventSystem.current;
                if (es != null && closeBtn != null)
                {
                    es.SetSelectedGameObject(closeBtn.gameObject);
                }
            }
        }
        else
        {
            yield return new WaitForSeconds(1f);
            StartCoroutine(CloseInfoUI(item));
        }
    }

    IEnumerator AnimateUIIn(CanvasGroup cg, Transform tf)
    {
        float elapsed = 0f;
        float dur = uiScaleDuration;
        Vector3 start = tf.localScale;
        Vector3 target = Vector3.one;
        float startAlpha = cg.alpha;

        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / dur);
            tf.localScale = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, k));
            cg.alpha = Mathf.Lerp(startAlpha, 1f, k);
            yield return null;
        }

        tf.localScale = target;
        cg.alpha = 1f;
    }

    IEnumerator CloseInfoUI(InteractableItem item)
    {
        if (currentInfoUI != null)
        {
            CanvasGroup cg = currentInfoUI.GetComponent<CanvasGroup>();
            Transform tf = currentInfoUI.transform;
            float elapsed = 0f;
            float dur = uiScaleDuration;
            Vector3 start = tf.localScale;
            Vector3 target = Vector3.one * 0.6f;
            float startAlpha = cg != null ? cg.alpha : 1f;

            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / dur);
                tf.localScale = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, k));
                if (cg != null) cg.alpha = Mathf.Lerp(startAlpha, 0f, k);
                yield return null;
            }

            if (currentInfoUIIsSceneObject)
            {
                currentInfoUI.SetActive(currentInfoUIOriginalActiveState);
                if (currentInfoUI.transform.parent != currentInfoUIOriginalParent)
                {
                    currentInfoUI.transform.SetParent(currentInfoUIOriginalParent, false);
                    currentInfoUI.transform.SetSiblingIndex(currentInfoUIOriginalSibling);
                }
            }
            else
            {
                Destroy(currentInfoUI);
            }

            currentInfoUI = null;
            currentInfoUIIsSceneObject = false;
            currentInfoUIOriginalParent = null;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Transform playerT = playerController.transform;
        if (playerCC != null) playerCC.enabled = true;
        playerT.position = playerSavedPos;
        playerT.rotation = playerSavedRot;
        playerController.SetCanLookAround(true);
        playerController.SetCanMove(true);

        isInspecting = false;

        if (item != null && !item.inspected)
        {
            item.inspected = true;
            item.StopHighlight();
            completedCount++;

            if (interactables != null && !allItemsCompleted && completedCount >= interactables.Length)
            {
                allItemsCompleted = true;
                StartCoroutine(WaitThenPlayFinal());
            }
        }

        if (currentLooked != null)
            UpdatePromptForItem(currentLooked);
    }

    IEnumerator WaitThenPlayFinal()
    {
        // wait until no info UI
        while (isInspecting || currentInfoUI != null) yield return null;

        yield return new WaitForSeconds(2f);

        // Disable item interaction + clear any existing HUD prompt
        isActive = false;
        currentLooked = null;
        HidePrompt();

        // Play final bot dialogue (with subtitles/skip handled by TimedBotSequence)
        yield return StartCoroutine(AllInteractionsCompleteRoutine());

        // Re-enable item interaction afterwards
        isActive = true;

        finalActionPlayed = true; // now the bot can be interacted with (E) to finish
        if (debugPrompts) Debug.Log("[Final] Final bot dialogue played. Bot is now interactable with E.");
    }

    IEnumerator AllInteractionsCompleteRoutine()
    {
        TimedBotAction finalAction = new TimedBotAction()
        {
            animationClip = finalBotAnimation,
            audioClip = finalBotAudio,
            botPosition = finalBotPosition,
            moveDuration = finalBotAnimation != null ? finalBotAnimation.length : 0f,
            startAudioAfterAnimation = true,
            lockRotationDuringMove = true,
            subtitleSegments = finalSubtitleSegments,
            allowSkip = finalAllowSkip
        };

        if (botSequence != null)
        {
            // Plays final dialogue with subtitles + skip (handled by TimedBotSequence)
            yield return StartCoroutine(botSequence.TriggerImmediateActionAndWait(finalAction));
        }
        else
        {
            yield return new WaitForSeconds(1f);
        }
    }

    // -------------------------
    // Final panel (Not Yet / Finish Experience)
    // -------------------------
    IEnumerator OpenFinalPanel()
    {
        if (finalPanelOpen) yield break;
        if (finalPanelPrefab == null)
        {
            Debug.LogWarning("InteractionManager: finalPanelPrefab is not assigned.");
            yield break;
        }

        finalPanelOpen = true;
        isInspecting = true;
        HidePrompt();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (uiRoot == null)
        {
            var c = FindObjectOfType<Canvas>();
            if (c != null) uiRoot = c.GetComponent<RectTransform>();
        }

        // Is this a scene object or a prefab asset?
        finalPanelIsSceneObject = finalPanelPrefab.scene.IsValid();

        if (finalPanelIsSceneObject)
        {
            // Use the existing panel in the scene
            finalPanelInstance = finalPanelPrefab;

            finalPanelOriginalParent = finalPanelInstance.transform.parent;
            finalPanelOriginalSibling = finalPanelInstance.transform.GetSiblingIndex();
            finalPanelOriginalActiveState = finalPanelInstance.activeSelf;

            finalPanelInstance.SetActive(true);
        }
        else
        {
            // Instantiate as a normal prefab
            if (uiRoot != null)
                finalPanelInstance = Instantiate(finalPanelPrefab, uiRoot, false);
            else
                finalPanelInstance = Instantiate(finalPanelPrefab);

            var rt = finalPanelInstance.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.localScale = Vector3.one * 0.8f;
                rt.SetAsLastSibling();
            }
        }

        if (finalPanelInstance == null)
        {
            Debug.LogWarning("InteractionManager: failed to create/find final panel instance.");
            yield break;
        }

        CanvasGroup cgPanel = finalPanelInstance.GetComponent<CanvasGroup>();
        if (cgPanel == null) cgPanel = finalPanelInstance.AddComponent<CanvasGroup>();
        cgPanel.alpha = 0f;

        if (finalPanelIsSceneObject)
            finalPanelInstance.transform.localScale = Vector3.one * 0.8f;

        // wire buttons
        Button notYetBtn = null;
        Button finishBtn = null;

        if (!string.IsNullOrEmpty(finalNotYetButtonName))
        {
            var ny = finalPanelInstance.transform.Find(finalNotYetButtonName);
            if (ny != null) notYetBtn = ny.GetComponent<Button>();
        }
        if (!string.IsNullOrEmpty(finalFinishButtonName))
        {
            var fe = finalPanelInstance.transform.Find(finalFinishButtonName);
            if (fe != null) finishBtn = fe.GetComponent<Button>();
        }

        // Fallback: just pick first/second Button if names not found
        if (notYetBtn == null || finishBtn == null)
        {
            var buttons = finalPanelInstance.GetComponentsInChildren<Button>(true);
            if (buttons.Length >= 2)
            {
                if (notYetBtn == null) notYetBtn = buttons[0];
                if (finishBtn == null) finishBtn = buttons[1];
            }
        }

        if (notYetBtn != null)
        {
            notYetBtn.onClick.RemoveAllListeners();
            notYetBtn.onClick.AddListener(() =>
            {
                StartCoroutine(CloseFinalPanel(false));
            });
        }
        else
        {
            Debug.LogWarning("Final panel: 'Not Yet' button not found. Assign names or manually wire.");
        }

        if (finishBtn != null)
        {
            finishBtn.onClick.RemoveAllListeners();
            finishBtn.onClick.AddListener(() =>
            {
                StartCoroutine(CloseFinalPanel(true));
            });
        }
        else
        {
            Debug.LogWarning("Final panel: 'Finish Experience' button not found. Assign names or manually wire.");
        }

        // animate in
        StartCoroutine(AnimateUIIn(cgPanel, finalPanelInstance.transform));

        var es = EventSystem.current;
        if (es != null && notYetBtn != null)
            es.SetSelectedGameObject(notYetBtn.gameObject);

        yield return null;
    }

    IEnumerator CloseFinalPanel(bool finishExperience)
    {
        if (finalPanelInstance != null)
        {
            CanvasGroup cg = finalPanelInstance.GetComponent<CanvasGroup>();
            Transform tf = finalPanelInstance.transform;
            float elapsed = 0f;
            float dur = uiScaleDuration;
            Vector3 start = tf.localScale;
            Vector3 target = Vector3.one * 0.6f;
            float startAlpha = cg != null ? cg.alpha : 1f;

            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / dur);
                tf.localScale = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, k));
                if (cg != null) cg.alpha = Mathf.Lerp(startAlpha, 0f, k);
                yield return null;
            }

            if (finalPanelIsSceneObject)
            {
                finalPanelInstance.SetActive(false);

                if (finalPanelOriginalParent != null &&
                    finalPanelInstance.transform.parent != finalPanelOriginalParent)
                {
                    finalPanelInstance.transform.SetParent(finalPanelOriginalParent, false);
                    finalPanelInstance.transform.SetSiblingIndex(finalPanelOriginalSibling);
                }
            }
            else
            {
                Destroy(finalPanelInstance);
            }

            finalPanelInstance = null;
        }

        finalPanelOpen = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        isInspecting = false;

        if (finishExperience)
        {
            if (botSequence != null)
            {
                yield return StartCoroutine(botSequence.FadeToBlackAndLoad(nextSceneName));
            }
            else if (!string.IsNullOrEmpty(nextSceneName))
            {
                SceneLoader.LoadSceneWithLoading(nextSceneName);
            }
        }
        else
        {
            // "Not yet" → just go back to exploring (all interactables still active)
            if (currentLooked != null)
                UpdatePromptForItem(currentLooked);
        }
    }

    void OnDestroy()
    {
        if (promptCoroutine != null) StopCoroutine(promptCoroutine);
        if (currentSpaceCoroutine != null) StopCoroutine(currentSpaceCoroutine);
    }
}
