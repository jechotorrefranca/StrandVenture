using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public class InteractionManager : MonoBehaviour
{
    [Header("Core refs")]
    public TimedBotSequence botSequence;
    public FirstPersonCameraMovement playerController;
    public Camera playerCamera;

    [Header("Interactables")]
    public InteractableItem[] interactables;
    public LayerMask interactableLayer = -1;

    [Header("Gaze")]
    [Tooltip("Maximum raycast distance for looking at items")]
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

    [Header("Final")]
    public AnimationClip finalBotAnimation;
    public AudioClip finalBotAudio;
    public Transform finalBotPosition;
    public string nextSceneName;

    // internal
    private InteractableItem currentLooked = null;
    private bool isActive = false;
    private int completedCount = 0;
    private bool isInspecting = false;

    // Track current space sequence to allow interruption
    private Coroutine currentSpaceCoroutine = null;
    private InteractableItem currentSpaceItem = null;

    // player restore states
    private Vector3 playerSavedPos;
    private Quaternion playerSavedRot;
    private CharacterController playerCC;

    private GameObject currentInfoUI;

    // track if currentInfoUI is a scene object (not an instantiated prefab)
    private bool currentInfoUIIsSceneObject = false;
    private Transform currentInfoUIOriginalParent = null;
    private int currentInfoUIOriginalSibling = 0;
    private bool currentInfoUIOriginalActiveState = false;

    // prompt coroutine
    private Coroutine promptCoroutine;

    void Start()
    {
        if (playerCamera == null && Camera.main != null)
            playerCamera = Camera.main;

        if (playerController != null)
            playerCC = playerController.GetComponent<CharacterController>();

        if (promptCanvasGroup != null)
            promptCanvasGroup.alpha = 0f;

        // Lock cursor for gameplay
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

        // auto-find UI root if not set (get RectTransform of first Canvas)
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

        Ray r = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        if (Physics.Raycast(r, out RaycastHit hit, gazeMaxDistance, interactableLayer))
        {
            // Get the InteractableItem from the hit collider or its parents
            var item = hit.collider.GetComponent<InteractableItem>();
            if (item == null)
                item = hit.collider.GetComponentInParent<InteractableItem>();

            if (item != null)
            {
                if (currentLooked != item)
                {
                    currentLooked = item;
                    UpdatePromptForItem(item);
                }

                if (Keyboard.current != null)
                {
                    // FIX: Allow space spam - will interrupt current sequence
                    if (Keyboard.current.spaceKey.wasPressedThisFrame)
                    {
                        // Stop current space sequence if any
                        if (currentSpaceCoroutine != null)
                        {
                            StopCoroutine(currentSpaceCoroutine);
                            currentSpaceCoroutine = null;
                        }

                        currentSpaceCoroutine = StartCoroutine(HandleSpaceForItem(item));
                    }
                    // FIX: Only allow E if spaceDone AND item not yet inspected
                    else if (Keyboard.current.eKey.wasPressedThisFrame && item.spaceDone && !item.inspected)
                    {
                        StartCoroutine(HandleEForItem(item));
                    }
                }
                return;
            }
        }

        currentLooked = null;
        HidePrompt();
    }

    void UpdatePromptForItem(InteractableItem item)
    {
        // FIX: Don't show prompts for inspected items (but allow replaying bot sequence)
        if (item.inspected)
        {
            // After inspection, only show space prompt
            ShowPrompt("Press <Space> to replay bot");
            return;
        }

        if (item.spaceDone)
            ShowPrompt("Press <E> to inspect\n(Press <Space> again to replay bot)");
        else
            ShowPrompt("Press <Space> to call bot");
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

        currentSpaceItem = item;
        HidePrompt();

        TimedBotAction action = new TimedBotAction
        {
            timestamp = 0f,
            animationClip = item.interactionAnimation,
            audioClip = item.interactionAudio,
            botPosition = item.botTargetPosition,
            moveDuration = item.interactionAnimation != null ? item.interactionAnimation.length : 0f,
            startAudioAfterAnimation = true,
            lockRotationDuringMove = true
        };

        if (botSequence != null)
        {
            // Wait for the bot sequence to complete its move/animation
            yield return StartCoroutine(botSequence.TriggerImmediateActionAndWait(action));

            // Wait for audio to complete if it exists
            if (item.interactionAudio != null && item.interactionAudio.length > 0f)
            {
                float waitLen = Mathf.Clamp(item.interactionAudio.length, 0f, 60f);
                yield return new WaitForSeconds(waitLen);
            }
        }

        // Mark that space sequence is done for this item
        item.spaceDone = true;

        // FIX: Update prompt for ALL items that might be looking at this one
        // Not just if currentLooked == item, since player might look away during audio
        if (!item.inspected)
        {
            // Force update the prompt if player is looking at any item
            if (currentLooked != null)
            {
                UpdatePromptForItem(currentLooked);
            }
        }

        currentSpaceCoroutine = null;
        currentSpaceItem = null;
    }

    IEnumerator HandleEForItem(InteractableItem item)
    {
        // don't open another panel if one is already active
        if (isInspecting) yield break;

        // FIX: Check if item is already inspected to prevent duplicate processing
        if (item == null || !item.spaceDone || item.inspected) yield break;

        isInspecting = true;
        HidePrompt();

        // Unlock cursor for UI interaction
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Save player transform and disable movement
        Transform playerT = playerController.transform;
        playerSavedPos = playerT.position;
        playerSavedRot = playerT.rotation;

        if (playerCC != null) playerCC.enabled = false;
        playerController.SetCanLookAround(false);
        playerController.SetCanMove(false);

        // Move player to view transform
        if (item.playerViewTransform != null)
        {
            playerT.position = item.playerViewTransform.position;
            playerT.rotation = item.playerViewTransform.rotation;
        }

        // Instantiate or enable the panel under the UI root.
        GameObject prefabOrSceneObj = item.interactionPanelPrefab != null ? item.interactionPanelPrefab : infoUIPrefab;

        if (prefabOrSceneObj != null)
        {
            // Reset any previous state trackers
            currentInfoUIIsSceneObject = false;
            currentInfoUIOriginalParent = null;

            // Try to ensure uiRoot is set (fallback)
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

                var tmp = currentInfoUI.GetComponentInChildren<TMP_Text>();
                if (tmp != null) tmp.text = item.infoText;

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

        // Lock cursor back for gameplay
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Restore player
        Transform playerT = playerController.transform;
        if (playerCC != null) playerCC.enabled = true;
        playerT.position = playerSavedPos;
        playerT.rotation = playerSavedRot;
        playerController.SetCanLookAround(true);
        playerController.SetCanMove(true);

        isInspecting = false;

        // FIX: Only mark inspected and increment counter if not already inspected
        if (item != null && !item.inspected)
        {
            item.inspected = true;
            item.StopHighlight(); // Stop the glow after inspection
            completedCount++;

            // Check if all items are now inspected
            if (interactables != null && completedCount >= interactables.Length)
            {
                DisableAllInteractables();
                StartCoroutine(WaitThenPlayFinal());
            }
        }
    }

    void DisableAllInteractables()
    {
        isActive = false;

        if (interactables == null) return;

        foreach (var it in interactables)
        {
            if (it == null) continue;

            it.SetHighlight(false);

            var cols = it.GetComponentsInChildren<Collider>(true);
            foreach (var c in cols)
            {
                c.enabled = false;
            }
        }
    }

    IEnumerator WaitThenPlayFinal()
    {
        while (isInspecting || currentInfoUI != null) yield return null;

        yield return new WaitForSeconds(2f);

        yield return StartCoroutine(AllInteractionsCompleteRoutine());
    }

    IEnumerator AllInteractionsCompleteRoutine()
    {
        TimedBotAction finalAction = new TimedBotAction()
        {
            timestamp = 0f,
            animationClip = finalBotAnimation,
            audioClip = finalBotAudio,
            botPosition = finalBotPosition,
            moveDuration = finalBotAnimation != null ? finalBotAnimation.length : 0f,
            startAudioAfterAnimation = true,
            lockRotationDuringMove = true
        };

        if (botSequence != null)
        {
            yield return StartCoroutine(botSequence.TriggerImmediateActionAndWait(finalAction));
            yield return StartCoroutine(botSequence.FadeToBlackAndLoad(nextSceneName));
        }
        else
        {
            yield return new WaitForSeconds(1f);
            if (!string.IsNullOrEmpty(nextSceneName))
                SceneManager.LoadScene(nextSceneName);
        }
    }
}