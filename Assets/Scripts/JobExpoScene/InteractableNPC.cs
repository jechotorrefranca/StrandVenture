using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using UnityEngine.EventSystems;

/* * InteractableNPC.cs (Fixed - v5)
 * ------------------ 
 * Fix: Ensure ONLY the active NPC plays audio by:
 * 1. Stopping all audio on the AudioSource before playing new clips
 * 2. Checking isInConversation flag before playing audio
 * 3. Creating AudioSource dynamically if not assigned to ensure each NPC has its own
 */
public class InteractableNPC : MonoBehaviour
{
    [Header("Interaction")]
    public float interactDistance = 3f;
    public LayerMask obstructionMask = ~0; // used for LOS check
    public LayerMask gazeLayer = ~0;      // which layers the gaze raycast should hit (like InteractionManager.interactableLayer)
    public float gazeMaxDistance = 3f;
    public string profession = "doctor";

    [Header("Camera Focus")]
    public Transform cameraFocusPoint;
    public float cameraMoveDuration = 0.8f;

    [Header("UI - Prompt")]
    public CanvasGroup promptCanvasGroup;
    public TMP_Text promptText;
    public float promptFadeDuration = 0.2f;
    public Vector3 promptTargetScale = Vector3.one;

    [Header("UI - Chat Panel")]
    public CanvasGroup panelCanvasGroup;
    public GameObject panelRoot;
    public TMP_InputField playerInputField;
    public Button sendButton;
    public TMP_Text statusText;

    [Header("Audio / TTS")]
    public AudioSource npcAudioSource;
    public AudioClip greetingClip; // assign your prepared WAV here for the intro greeting
    public string voiceName = "en_US-hfc_male-medium"; // used for Piper replies

    [Header("Groq/Piper config")]
    public string groqConfigFilename = "groq_config.json";
    public string piperRelativePath = "piper/piper.exe";
    public string voicesRelativeDir = "piper/voices";

    [Header("Player refs")]
    public Transform playerHead;
    public GameObject playerRoot;
    public FirstPersonCameraMovement fpsController;

    [Header("Animation Settings - legacy Animation")]
    [Tooltip("Assign an Animation component (legacy) that contains the clips, or leave null and the script will add clips at runtime.")]
    public Animation npcAnimation; // legacy animation component
    public AnimationClip idleAnimationClip;
    public AnimationClip conversationAnimationClip;
    public float conversationIdleInterval = 3f; // seconds between switching to idle during conversation
    public float playerDetectionRange = 5f; // range to start following player
    public float rotationSpeed = 2f; // speed of Y-axis rotation

    [Header("Crosshair")]
    public GameObject crosshair; // will be hidden when interacting

    // Allow explicit camera assignment (matches your InteractionManager)
    [Header("Camera")]
    public Camera playerCamera;

    // internal state
    private bool playerLooking = false;
    private bool panelOpen = false;

    // saved camera state
    private Transform savedCameraParent;
    private Vector3 savedCameraLocalPos;
    private Quaternion savedCameraLocalRot;

    // API keys and paths
    private string groqApiKey;
    private string piperPath;
    private string voicesDir;
    private const string groqUrl = "https://api.groq.com/openai/v1/chat/completions";

    // Animation tracking
    private Quaternion initialRotation;
    private bool isInConversation = false;
    private Coroutine animationIntervalCoroutine;
    private Coroutine fadeCoroutine;

    // Prompt coroutine (scale+fade)
    private Coroutine promptCoroutine;

    void Start()
    {
        // CRITICAL FIX: Ensure each NPC has its own AudioSource
        if (npcAudioSource == null)
        {
            npcAudioSource = gameObject.AddComponent<AudioSource>();
            npcAudioSource.playOnAwake = false;
            npcAudioSource.spatialBlend = 1f; // 3D sound
            npcAudioSource.minDistance = 1f;
            npcAudioSource.maxDistance = 10f;
            Debug.Log($"[{gameObject.name}] Created AudioSource dynamically");
        }
        else
        {
            // Ensure existing AudioSource is configured correctly
            npcAudioSource.playOnAwake = false;
            Debug.Log($"[{gameObject.name}] Using assigned AudioSource");
        }

        // camera resolution: prefer explicit playerCamera (like your InteractionManager), fallback to Camera.main
        if (playerCamera == null && Camera.main != null)
            playerCamera = Camera.main;

        // If playerCamera still null, try finding a Camera in playerHead or playerRoot
        if (playerCamera == null && playerHead != null)
        {
            var cam = playerHead.GetComponentInChildren<Camera>();
            if (cam != null) playerCamera = cam;
        }

        if (playerCamera == null && playerRoot != null)
        {
            var cam = playerRoot.GetComponentInChildren<Camera>();
            if (cam != null) playerCamera = cam;
        }

        if (fpsController == null && playerRoot != null)
        {
            fpsController = playerRoot.GetComponent<FirstPersonCameraMovement>();
        }

        // Setup UI initial states
        if (promptCanvasGroup != null)
        {
            promptCanvasGroup.alpha = 0f;
            promptCanvasGroup.transform.localScale = Vector3.one * 0.9f;
            promptCanvasGroup.gameObject.SetActive(false);
        }

        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        // Wire send button
        if (sendButton != null)
        {
            sendButton.onClick.AddListener(OnSendClicked);
        }

        // Wire Enter key for input field
        if (playerInputField != null)
        {
            playerInputField.onSubmit.AddListener(OnInputSubmit);
        }

        LoadGroqApiKey();
        InitializePiperPaths();

        // Store initial rotation
        initialRotation = transform.rotation;

        // Ensure Animation component exists if clips are used
        if (npcAnimation == null && (idleAnimationClip != null || conversationAnimationClip != null))
        {
            // Try to get one from the same GameObject
            npcAnimation = GetComponent<Animation>();
            if (npcAnimation == null)
            {
                // create one so we can play legacy clips
                npcAnimation = gameObject.AddComponent<Animation>();
            }
        }

        // Add clips to the legacy Animation component so they can be played by name
        if (npcAnimation != null)
        {
            if (idleAnimationClip != null && npcAnimation.GetClip(idleAnimationClip.name) == null)
                npcAnimation.AddClip(idleAnimationClip, idleAnimationClip.name);
            if (conversationAnimationClip != null && npcAnimation.GetClip(conversationAnimationClip.name) == null)
                npcAnimation.AddClip(conversationAnimationClip, conversationAnimationClip.name);
        }

        // Start idle animation (ensure it stays active when not interacting)
        if (npcAnimation != null && idleAnimationClip != null)
        {
            ForcePlayIdle();
        }
    }

    private void LoadGroqApiKey()
    {
        string path = Path.Combine(Application.streamingAssetsPath, groqConfigFilename);
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                var obj = JsonUtility.FromJson<SerializableKey>(json);
                groqApiKey = obj.api_key;
                Debug.Log("Loaded Groq key");
            }
            catch (Exception e)
            {
                Debug.LogError("Failed load Groq key: " + e.Message);
            }
        }
        else
            Debug.LogWarning("groq_config.json not found at " + path);
    }
    private void InitializePiperPaths()
    {
        piperPath = Path.Combine(Application.streamingAssetsPath, piperRelativePath);
        voicesDir = Path.Combine(Application.streamingAssetsPath, voicesRelativeDir);
    }


    void Update()
    {
        // Handle player detection and rotation (always, unless in conversation)
        if (!isInConversation)
        {
            HandlePlayerTracking();

            // Ensure idle animation keeps playing while not in conversation (legacy Animation)
            if (npcAnimation != null && idleAnimationClip != null)
            {
                if (!npcAnimation.IsPlaying(idleAnimationClip.name))
                {
                    PlayAnimation(idleAnimationClip, true);
                }
            }
        }

        if (panelOpen)
        {
            // Allow quick exit with Q
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            {
                StartCoroutine(CloseConversation());
            }

            // If the panel is open and the player clicks the mouse, ensure the input field is selected
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (playerInputField != null && EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(playerInputField.gameObject);
                    playerInputField.ActivateInputField();
                }
            }

            return;
        }

        // Raycast from camera forward — use playerCamera (like your InteractionManager)
        if (playerCamera == null) return;

        Ray r = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        RaycastHit hit;

        bool didHit = Physics.Raycast(r, out hit, gazeMaxDistance, gazeLayer);

        if (didHit && hit.collider != null)
        {
            // Accept root or child colliders (covers imported models with colliders on children)
            bool hitThis = (hit.collider.gameObject == this.gameObject) || hit.collider.transform.IsChildOf(this.transform);

            if (hitThis)
            {
                // Optional LOS (line-of-sight) check: make sure nothing else is between camera and NPC center
                bool blocked = false;
                Vector3 dirToNpc = (transform.position - playerCamera.transform.position);
                float distToNpc = dirToNpc.magnitude;
                dirToNpc.Normalize();

                if (Physics.Raycast(playerCamera.transform.position, dirToNpc, out RaycastHit hit2, distToNpc, obstructionMask))
                {
                    if (!(hit2.collider.transform.IsChildOf(this.transform)))
                    {
                        blocked = true;
                    }
                }

                if (!blocked)
                {
                    if (!playerLooking)
                    {
                        playerLooking = true;
                        UpdatePromptText("Press <E> to talk");
                        ShowPrompt("Press <E> to talk");
                    }

                    if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                    {
                        StartCoroutine(BeginConversation());
                    }

                    return; // early out so we don't immediately clear the prompt below
                }
            }
        }

        // if we get here, we are not looking at the NPC (or LOS blocked)
        if (playerLooking)
        {
            playerLooking = false;
            HidePrompt();
        }
    }

    void UpdatePromptText(string text)
    {
        if (promptText != null) promptText.text = text;
    }

    void ShowPrompt(string text)
    {
        UpdatePromptText(text);
        if (promptCanvasGroup == null) return;

        if (promptCoroutine != null) StopCoroutine(promptCoroutine);
        promptCoroutine = StartCoroutine(FadeAndScalePrompt(1f, promptFadeDuration, promptTargetScale));
    }

    void HidePrompt()
    {
        if (promptCanvasGroup == null) return;

        if (promptCoroutine != null) StopCoroutine(promptCoroutine);
        promptCoroutine = StartCoroutine(FadeAndScalePrompt(0f, promptFadeDuration, Vector3.one * 0.9f));
    }

    IEnumerator FadeAndScalePrompt(float targetAlpha, float duration, Vector3 targetScale)
    {
        if (promptCanvasGroup == null) yield break;

        Transform t = promptCanvasGroup.transform;
        Vector3 startScale = t.localScale;
        Vector3 endScale = targetScale;
        float startAlpha = promptCanvasGroup.alpha;
        float elapsed = 0f;

        promptCanvasGroup.gameObject.SetActive(true);

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

        if (promptCanvasGroup.alpha <= 0f)
            promptCanvasGroup.gameObject.SetActive(false);
    }

    #region Animation & Player Tracking

    private void HandlePlayerTracking()
    {
        if (playerHead == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, playerHead.position);

        if (distanceToPlayer <= playerDetectionRange)
        {
            // Look at player (Y-axis only)
            Vector3 directionToPlayer = playerHead.position - transform.position;
            directionToPlayer.y = 0; // Keep on horizontal plane

            if (directionToPlayer.sqrMagnitude > 0.001f) // Use sqrMagnitude for better performance
            {
                Quaternion targetRotation = Quaternion.LookRotation(directionToPlayer);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
        }
        else
        {
            // Reset to initial rotation
            transform.rotation = Quaternion.Slerp(transform.rotation, initialRotation, rotationSpeed * Time.deltaTime);
        }
    }

    private void PlayAnimation(AnimationClip clip, bool loop)
    {
        if (npcAnimation != null && clip != null)
        {
            // ensure clip exists on the Animation component
            if (npcAnimation.GetClip(clip.name) == null)
                npcAnimation.AddClip(clip, clip.name);

            var state = npcAnimation[clip.name];
            state.wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
            // Use CrossFade for smoother transitions and to ensure the clip actually starts
            npcAnimation.CrossFade(clip.name, 0.12f);
        }
    }

    private IEnumerator ConversationAnimationInterval()
    {
        while (isInConversation)
        {
            // Play conversation animation
            if (conversationAnimationClip != null)
            {
                PlayAnimation(conversationAnimationClip, false);
            }

            // Wait for the interval
            yield return new WaitForSeconds(conversationIdleInterval);

            // Play idle animation briefly
            if (idleAnimationClip != null)
            {
                PlayAnimation(idleAnimationClip, true);
            }

            // Short idle duration before switching back
            yield return new WaitForSeconds(1f);
        }
    }

    private void ForcePlayIdle()
    {
        // Helper to force the idle animation on start / when restoring
        if (npcAnimation == null || idleAnimationClip == null) return;
        try
        {
            PlayAnimation(idleAnimationClip, true);
        }
        catch (Exception)
        {
            // If Play by name fails, ignore silently
        }
    }

    #endregion

    #region Conversation Flow

    private IEnumerator BeginConversation()
    {
        panelOpen = true;
        isInConversation = true;

        Debug.Log($"[{gameObject.name}] Starting conversation");

        // Hide the prompt immediately when interaction starts
        if (playerLooking)
        {
            playerLooking = false;
            HidePrompt();
        }

        // Hide crosshair
        if (crosshair != null) crosshair.SetActive(false);

        // Disable player movement/look
        if (fpsController != null) fpsController.SetCanMove(false);
        if (fpsController != null) fpsController.SetCanLookAround(false);

        // Start conversation animation interval
        if (animationIntervalCoroutine != null)
        {
            StopCoroutine(animationIntervalCoroutine);
        }
        animationIntervalCoroutine = StartCoroutine(ConversationAnimationInterval());

        // Move camera to focus point smoothly
        yield return StartCoroutine(MoveCameraToFocus());

        // Play the prepared greeting AudioClip (user-assigned wav) instead of Piper TTS
        yield return StartCoroutine(PlayGreetingClip());

        // if the player cancelled during the greeting, don't continue to show the panel
        if (!isInConversation)
        {
            Debug.Log($"[{gameObject.name}] Conversation cancelled during greeting");
            yield break;
        }

        // Ensure EventSystem exists (so selection/focus works)
        EnsureEventSystemExists();

        // Show chat panel only AFTER greeting finished
        if (panelRoot != null) panelRoot.SetActive(true);

        if (panelCanvasGroup != null)
        {
            // Make sure the canvas group will accept clicks
            panelCanvasGroup.interactable = true;
            panelCanvasGroup.blocksRaycasts = true;
            panelCanvasGroup.gameObject.SetActive(true);
            yield return StartCoroutine(FadeCanvasGroup(panelCanvasGroup, 1f, 0.2f));
        }

        // Unlock the cursor so the player can click the input field
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Enable and focus the input field
        if (playerInputField != null)
        {
            playerInputField.interactable = true;
            playerInputField.text = "";
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(playerInputField.gameObject);
            }
            playerInputField.ActivateInputField();
        }

        if (sendButton != null) sendButton.interactable = true;

        // Clear any status text
        if (statusText != null) statusText.text = "";
    }

    private IEnumerator PlayGreetingClip()
    {
        if (npcAudioSource == null || greetingClip == null) yield break;

        // CRITICAL FIX: Stop any existing audio before playing
        if (npcAudioSource.isPlaying)
        {
            Debug.Log($"[{gameObject.name}] Stopping existing audio before greeting");
            npcAudioSource.Stop();
        }

        Debug.Log($"[{gameObject.name}] Playing greeting clip");
        npcAudioSource.clip = greetingClip;
        npcAudioSource.Play();

        while (npcAudioSource.isPlaying)
        {
            // allow cancellation while greeting plays
            if (!isInConversation)
            {
                Debug.Log($"[{gameObject.name}] Greeting cancelled");
                npcAudioSource.Stop();
                yield break;
            }
            yield return null;
        }

        Debug.Log($"[{gameObject.name}] Greeting finished");
    }

    private IEnumerator CloseConversation()
    {
        Debug.Log($"[{gameObject.name}] Closing conversation");

        // CRITICAL FIX: Stop any playing audio immediately
        if (npcAudioSource != null && npcAudioSource.isPlaying)
        {
            Debug.Log($"[{gameObject.name}] Stopping audio on conversation close");
            npcAudioSource.Stop();
        }

        // If panel not yet presented but greeting playing, cancel conversation state so BeginConversation exits early
        isInConversation = false;

        // Stop conversation animation
        if (animationIntervalCoroutine != null)
        {
            StopCoroutine(animationIntervalCoroutine);
            animationIntervalCoroutine = null;
        }

        // Return to idle animation
        if (idleAnimationClip != null)
        {
            PlayAnimation(idleAnimationClip, true);
        }

        // Hide / disable panel UI so it doesn't receive clicks
        if (panelCanvasGroup != null)
        {
            // disable interaction immediately, then fade out
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
            yield return StartCoroutine(FadeCanvasGroup(panelCanvasGroup, 0f, 0.2f));
        }
        if (panelRoot != null) panelRoot.SetActive(false);

        panelOpen = false;

        // Deselect UI (clear EventSystem selection)
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        // Restore camera
        yield return StartCoroutine(RestoreCamera());

        // Re-lock/hide cursor and re-enable player
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Show crosshair again
        if (crosshair != null) crosshair.SetActive(true);

        if (fpsController != null) fpsController.SetCanMove(true);
        if (fpsController != null) fpsController.SetCanLookAround(true);
    }

    private void EnsureEventSystemExists()
    {
        if (EventSystem.current != null) return;

        // Create a minimal EventSystem so UI selection and clicks work
        var go = new GameObject("EventSystem", new System.Type[] { typeof(EventSystem), typeof(StandaloneInputModule) });
    }

    #endregion

    #region Camera movement

    private IEnumerator MoveCameraToFocus()
    {
        if (playerCamera == null) yield break;

        // Save
        savedCameraParent = playerCamera.transform.parent;
        savedCameraLocalPos = playerCamera.transform.localPosition;
        savedCameraLocalRot = playerCamera.transform.localRotation;

        // Unparent camera
        playerCamera.transform.SetParent(null, true);

        // Determine target
        Vector3 targetPos;
        Quaternion targetRot;

        if (cameraFocusPoint != null)
        {
            targetPos = cameraFocusPoint.position;
            targetRot = cameraFocusPoint.rotation;
        }
        else
        {
            Vector3 dir = (playerCamera.transform.position - transform.position).normalized;
            targetPos = transform.position + transform.forward * 1.2f + Vector3.up * 1.4f;
            targetRot = Quaternion.LookRotation(transform.position - targetPos);
        }

        float t = 0f;
        Vector3 startPos = playerCamera.transform.position;
        Quaternion startRot = playerCamera.transform.rotation;

        while (t < cameraMoveDuration)
        {
            t += Time.deltaTime;
            float f = Mathf.SmoothStep(0f, 1f, t / cameraMoveDuration);
            playerCamera.transform.position = Vector3.Lerp(startPos, targetPos, f);
            playerCamera.transform.rotation = Quaternion.Slerp(startRot, targetRot, f);
            yield return null;
        }

        playerCamera.transform.position = targetPos;
        playerCamera.transform.rotation = targetRot;
    }

    private IEnumerator RestoreCamera()
    {
        if (playerCamera == null) yield break;

        float t = 0f;
        float dur = cameraMoveDuration;
        Vector3 startPos = playerCamera.transform.position;
        Quaternion startRot = playerCamera.transform.rotation;

        Vector3 targetPos;
        Quaternion targetRot;

        if (savedCameraParent != null)
        {
            targetPos = savedCameraParent.TransformPoint(savedCameraLocalPos);
            targetRot = savedCameraParent.rotation * savedCameraLocalRot;
        }
        else
        {
            targetPos = startPos;
            targetRot = startRot;
        }

        while (t < dur)
        {
            t += Time.deltaTime;
            float f = Mathf.SmoothStep(0f, 1f, t / dur);
            playerCamera.transform.position = Vector3.Lerp(startPos, targetPos, f);
            playerCamera.transform.rotation = Quaternion.Slerp(startRot, targetRot, f);
            yield return null;
        }

        playerCamera.transform.SetParent(savedCameraParent, true);
        playerCamera.transform.localPosition = savedCameraLocalPos;
        playerCamera.transform.localRotation = savedCameraLocalRot;
    }

    #endregion

    #region UI Helpers

    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float target, float duration)
    {
        if (cg == null) yield break;

        cg.gameObject.SetActive(true);
        float start = cg.alpha;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            cg.alpha = Mathf.Lerp(start, target, t / duration);
            yield return null;
        }

        cg.alpha = target;
        if (cg.alpha <= 0f) cg.gameObject.SetActive(false);
    }

    #endregion

    #region Send / Receive (modified to use unique temp files)

    private void OnInputSubmit(string text)
    {
        // Trigger send when Enter is pressed
        OnSendClicked();
    }

    private void OnSendClicked()
    {
        if (playerInputField == null) return;

        string msg = playerInputField.text?.Trim();
        if (string.IsNullOrEmpty(msg)) return;

        // Disable input while processing
        playerInputField.interactable = false;
        if (sendButton != null) sendButton.interactable = false;
        if (statusText != null) statusText.text = "Thinking...";

        StartCoroutine(ProcessPlayerMessage(msg));
    }

    private IEnumerator ProcessPlayerMessage(string message)
    {
        // CRITICAL: Check if still in conversation before processing
        if (!isInConversation)
        {
            Debug.Log($"[{gameObject.name}] Not in conversation, ignoring message");
            yield break;
        }

        Debug.Log($"[{gameObject.name}] Processing message: {message}");

        // Build concise system prompt
        string system = $"You are a professional {profession}, act like you are being interviewed. Answer the player's question directly and concisely in 1-2 short sentences. If the question is not appropriate, reply appropriately.";

        string reply = null;
        yield return StartCoroutine(SendGroqChat(system, message, r => reply = r));

        if (string.IsNullOrEmpty(reply))
            reply = "There is something with the code.";

        // Play reply via Piper (this uses the Piper toolchain). Use a unique temporary filename per NPC/call to avoid cross-talk
        yield return StartCoroutine(PlayPiperTTS(reply));

        // Re-enable input and UI
        if (statusText != null) statusText.text = "";
        if (playerInputField != null)
        {
            playerInputField.text = "";
            playerInputField.interactable = true;
            playerInputField.ActivateInputField();
        }
        if (sendButton != null) sendButton.interactable = true;
    }

    private IEnumerator PlayPiperTTS(string text)
    {
        // CRITICAL: Double-check we're still in conversation
        if (!isInConversation)
        {
            Debug.Log($"[{gameObject.name}] Not in conversation, skipping TTS playback");
            yield break;
        }

        if (npcAudioSource == null)
        {
            Debug.LogWarning($"[{gameObject.name}] No AudioSource assigned!");
            yield break;
        }

        // CRITICAL FIX: Stop any existing audio before playing new clip
        if (npcAudioSource.isPlaying)
        {
            Debug.Log($"[{gameObject.name}] Stopping existing audio before TTS");
            npcAudioSource.Stop();
        }

        // Create a unique output path per call so multiple NPCs don't overwrite the same file
        string filename = $"piper_output_{GetInstanceID()}_{DateTime.UtcNow.Ticks}.wav";
        string outputPath = Path.Combine(Application.persistentDataPath, filename);

        Debug.Log($"[{gameObject.name}] Generating TTS to: {outputPath}");

        bool ok = false;

        Task<bool> gen = Task.Run(() => GeneratePiperAudio(text, outputPath));
        while (!gen.IsCompleted) yield return null;
        ok = gen.Result;

        if (!ok)
        {
            Debug.LogError($"[{gameObject.name}] Piper generation failed");
            yield break;
        }

        // CRITICAL: Check again if still in conversation before loading/playing
        if (!isInConversation)
        {
            Debug.Log($"[{gameObject.name}] Conversation ended before audio loaded, cleaning up");
            TryDeleteSafe(outputPath);
            yield break;
        }

        // Load audio
        string uri = "file://" + outputPath;
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.WAV))
        {
            yield return www.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (www.result != UnityWebRequest.Result.Success)
#else
            if (www.isNetworkError || www.isHttpError)
#endif
            {
                Debug.LogError($"[{gameObject.name}] Audio load error: " + www.error);
                TryDeleteSafe(outputPath);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
            if (clip == null)
            {
                Debug.LogError($"[{gameObject.name}] Failed to get audio clip");
                TryDeleteSafe(outputPath);
                yield break;
            }

            // CRITICAL: Final check before playing
            if (!isInConversation)
            {
                Debug.Log($"[{gameObject.name}] Conversation ended before playback, cleaning up");
                TryDeleteSafe(outputPath);
                yield break;
            }

            Debug.Log($"[{gameObject.name}] Playing TTS audio");
            npcAudioSource.clip = clip;
            npcAudioSource.Play();

            while (npcAudioSource.isPlaying && isInConversation)
            {
                yield return null;
            }

            // If conversation ended while playing, stop the audio
            if (!isInConversation && npcAudioSource.isPlaying)
            {
                Debug.Log($"[{gameObject.name}] Conversation ended during playback, stopping");
                npcAudioSource.Stop();
            }

            Debug.Log($"[{gameObject.name}] TTS playback finished");

            // optionally delete the temp file after playback to avoid disk piling up
            TryDeleteSafe(outputPath);
        }
    }

    private void TryDeleteSafe(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[{gameObject.name}] Failed delete temp piper file: " + e.Message);
        }
    }

    #endregion

    #region Groq Chat Method (unchanged)

    private class SerializableKey
    {
        public string api_key;
    }

    [Serializable]
    private class GMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    private class GChoice
    {
        public GMessage message;
    }

    [Serializable]
    private class GResponse
    {
        public GChoice[] choices;
    }

    [Serializable]
    private class GRequest
    {
        public string model;
        public List<GMessage> messages;
    }

    private IEnumerator SendGroqChat(string systemPrompt, string userMessage, Action<string> onComplete)
    {
        if (string.IsNullOrEmpty(groqApiKey))
        {
            Debug.LogError("Groq API key missing");
            onComplete?.Invoke(null);
            yield break;
        }

        string url = groqUrl;
        var req = new GRequest
        {
            model = "openai/gpt-oss-120b",
            messages = new List<GMessage>
            {
                new GMessage { role = "system", content = systemPrompt },
                new GMessage { role = "user", content = userMessage }
            }
        };

        string json = JsonUtility.ToJson(req);
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest uw = new UnityWebRequest(url, "POST"))
        {
            uw.uploadHandler = new UploadHandlerRaw(body);
            uw.downloadHandler = new DownloadHandlerBuffer();
            uw.SetRequestHeader("Content-Type", "application/json");
            uw.SetRequestHeader("Authorization", "Bearer " + groqApiKey);

            yield return uw.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (uw.result != UnityWebRequest.Result.Success)
#else
            if (uw.isNetworkError || uw.isHttpError)
#endif
            {
                Debug.LogError("Groq request error: " + uw.error + " Response: " + uw.downloadHandler.text);
                onComplete?.Invoke(null);
                yield break;
            }

            string respText = uw.downloadHandler.text;

            try
            {
                GResponse resp = JsonUtility.FromJson<GResponse>(respText);
                if (resp != null && resp.choices != null && resp.choices.Length > 0 && resp.choices[0].message != null)
                {
                    string content = resp.choices[0].message.content.Trim();
                    onComplete?.Invoke(content);
                    yield break;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Failed parsing Groq response: " + e.Message);
            }

            onComplete?.Invoke(null);
        }
    }

    #endregion

    #region Piper TTS engine invocation (unchanged - writes to provided outputPath)

    private bool GeneratePiperAudio(string text, string outputPath)
    {
        try
        {
            string modelPath = Path.Combine(voicesDir, voiceName + ".onnx");
            if (!File.Exists(modelPath))
            {
                Debug.LogError("Voice model missing: " + modelPath);
                return false;
            }

            if (File.Exists(outputPath))
                File.Delete(outputPath);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = piperPath,
                Arguments = $"--model \"{modelPath}\" --output_file \"{outputPath}\" --output_format wav",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(piperPath)
            };

            using (var proc = System.Diagnostics.Process.Start(psi))
            {
                proc.StandardInput.WriteLine(text);
                proc.StandardInput.Close();
                proc.WaitForExit();

                string stderr = proc.StandardError.ReadToEnd();
                if (!string.IsNullOrEmpty(stderr))
                    Debug.LogWarning("[Piper] " + stderr);
            }

            int attempts = 0;
            while (!File.Exists(outputPath) && attempts < 150)
            {
                System.Threading.Thread.Sleep(100);
                attempts++;
            }

            return File.Exists(outputPath);
        }
        catch (Exception e)
        {
            Debug.LogError("Piper generation error: " + e.Message);
            return false;
        }
    }

    #endregion
}