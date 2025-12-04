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

/* * InteractableNPC.cs (with subtitles)
 * ------------------ 
 * - Each NPC has its own AudioSource
 * - Intro uses manual SubtitleSegment timeline
 * - Groq + Piper replies use auto-generated subtitles
 * - Background and subtitle text appear together
 */

public class InteractableNPC : MonoBehaviour
{
    [Serializable]
    public class SubtitleSegment
    {
        [Tooltip("Seconds from clip start when this subtitle appears")]
        public float timestamp;

        [TextArea(1, 4)]
        public string text;

        [Tooltip("Duration in seconds (0 = auto until next segment or clip end)")]
        public float duration;

        [Tooltip("Background color for this subtitle segment (RGBA)")]
        public Color backgroundColor = new Color(0f, 0f, 0f, 0.85f);
    }

    [Header("Interaction")]
    public float interactDistance = 3f;
    public LayerMask obstructionMask = ~0; // used for LOS check
    public LayerMask gazeLayer = ~0;      // which layers the gaze raycast should hit
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

    [Header("Camera")]
    public Camera playerCamera;

    [Header("Subtitle UI")]
    public GameObject subtitlePanel;     // parent panel for subtitle
    public Image subtitleBackground;     // background image
    public TMP_Text subtitleText;        // subtitle text

    [Header("Intro Subtitles (manual)")]
    [Tooltip("Timeline for the greetingClip audio")]
    public SubtitleSegment[] introSubtitles;

    [Header("AI Subtitles (Groq + Piper)")]
    [Tooltip("Background color for AI-generated reply subtitles")]
    public Color aiSubtitleBackgroundColor = new Color(0f, 0f, 0f, 0.85f);

    [Tooltip("Max words per subtitle line for AI replies")]
    public int aiWordsPerSubtitle = 7;

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

    // Prompt coroutine (scale+fade)
    private Coroutine promptCoroutine;

    // Subtitle coroutine
    private Coroutine subtitleCoroutine;

    public static bool GlobalInteractionLocked = false;

    void Start()
    {
        // Ensure each NPC has its own AudioSource
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
            npcAudioSource.playOnAwake = false;
            Debug.Log($"[{gameObject.name}] Using assigned AudioSource");
        }

        // camera resolution
        if (playerCamera == null && Camera.main != null)
            playerCamera = Camera.main;

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

        // Prompt UI initial state
        if (promptCanvasGroup != null)
        {
            promptCanvasGroup.alpha = 0f;
            promptCanvasGroup.transform.localScale = Vector3.one * 0.9f;
            promptCanvasGroup.gameObject.SetActive(false);
        }

        // Chat panel initial state
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

        // Subtitle UI initial state
        if (subtitlePanel != null)
            subtitlePanel.SetActive(false);
        if (subtitleBackground != null)
        {
            subtitleBackground.enabled = false;
            subtitleBackground.gameObject.SetActive(false);
        }
        if (subtitleText != null)
            subtitleText.text = "";

        LoadGroqApiKey();
        InitializePiperPaths();

        // Store initial rotation
        initialRotation = transform.rotation;

        // Ensure Animation component exists if clips are used
        if (npcAnimation == null && (idleAnimationClip != null || conversationAnimationClip != null))
        {
            npcAnimation = GetComponent<Animation>();
            if (npcAnimation == null)
            {
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

        // Start idle animation
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
        // Player detection and rotation (always, unless in conversation)
        if (!isInConversation)
        {
            HandlePlayerTracking();

            // Ensure idle animation keeps playing while not in conversation
            if (npcAnimation != null && idleAnimationClip != null)
            {
                if (!npcAnimation.IsPlaying(idleAnimationClip.name))
                {
                    PlayAnimation(idleAnimationClip, true);
                }
            }
        }

        // If the chat panel is open, conversation logic takes over
        if (panelOpen)
        {
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            {
                StartCoroutine(CloseConversation());
            }

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

        // Global lock from the bot intro
        if (GlobalInteractionLocked)
        {
            if (playerLooking)
            {
                playerLooking = false;
                HidePrompt();
            }
            return;
        }

        // Don't interfere with bot interactions
        if (ExpoBotInteraction.BotIsTalkingGlobal)
        {
            if (playerLooking)
            {
                playerLooking = false;
                HidePrompt();
            }
            return;
        }

        // Raycast from camera forward
        if (playerCamera == null) return;

        Ray r = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        RaycastHit hit;

        bool didHit = Physics.Raycast(r, out hit, gazeMaxDistance, gazeLayer);

        if (didHit && hit.collider != null)
        {
            bool hitThis = (hit.collider.gameObject == this.gameObject) || hit.collider.transform.IsChildOf(this.transform);

            if (hitThis)
            {
                // LOS check
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
                    }

                    // Show prompt while looking
                    ShowPrompt("Press <E> to talk");

                    if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                    {
                        StartCoroutine(BeginConversation());
                    }

                    return;
                }
            }
        }

        // Not looking at NPC
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
        if (promptCanvasGroup == null) return;

        UpdatePromptText(text);

        promptCanvasGroup.gameObject.SetActive(true);
        promptCanvasGroup.alpha = 1f;
        promptCanvasGroup.transform.localScale = promptTargetScale;
        promptCanvasGroup.interactable = true;
        promptCanvasGroup.blocksRaycasts = true;
    }

    void HidePrompt()
    {
        if (promptCanvasGroup == null) return;

        promptCanvasGroup.alpha = 0f;
        promptCanvasGroup.transform.localScale = Vector3.one * 0.9f;
        promptCanvasGroup.gameObject.SetActive(false);
        promptCanvasGroup.interactable = false;
        promptCanvasGroup.blocksRaycasts = false;
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
            directionToPlayer.y = 0;

            if (directionToPlayer.sqrMagnitude > 0.001f)
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
            if (npcAnimation.GetClip(clip.name) == null)
                npcAnimation.AddClip(clip, clip.name);

            var state = npcAnimation[clip.name];
            state.wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
            npcAnimation.CrossFade(clip.name, 0.12f);
        }
    }

    private IEnumerator ConversationAnimationInterval()
    {
        while (isInConversation)
        {
            if (conversationAnimationClip != null)
            {
                PlayAnimation(conversationAnimationClip, false);
            }

            yield return new WaitForSeconds(conversationIdleInterval);

            if (idleAnimationClip != null)
            {
                PlayAnimation(idleAnimationClip, true);
            }

            yield return new WaitForSeconds(1f);
        }
    }

    private void ForcePlayIdle()
    {
        if (npcAnimation == null || idleAnimationClip == null) return;
        try
        {
            PlayAnimation(idleAnimationClip, true);
        }
        catch (Exception)
        {
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

        // Play the prepared greeting AudioClip with manual subtitles
        yield return StartCoroutine(PlayGreetingClip());

        // if the player cancelled during the greeting, don't continue to show the panel
        if (!isInConversation)
        {
            Debug.Log($"[{gameObject.name}] Conversation cancelled during greeting");
            yield break;
        }

        // Ensure EventSystem exists
        EnsureEventSystemExists();

        // Show chat panel only AFTER greeting finished
        if (panelRoot != null) panelRoot.SetActive(true);

        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.interactable = true;
            panelCanvasGroup.blocksRaycasts = true;
            panelCanvasGroup.gameObject.SetActive(true);
            yield return StartCoroutine(FadeCanvasGroup(panelCanvasGroup, 1f, 0.2f));
        }

        // Unlock cursor
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

        if (statusText != null) statusText.text = "";
    }

    private IEnumerator PlayGreetingClip()
    {
        if (npcAudioSource == null || greetingClip == null) yield break;

        // Stop any existing audio & subtitles
        if (npcAudioSource.isPlaying)
        {
            Debug.Log($"[{gameObject.name}] Stopping existing audio before greeting");
            npcAudioSource.Stop();
        }
        StopSubtitleSequence();

        Debug.Log($"[{gameObject.name}] Playing greeting clip");
        npcAudioSource.clip = greetingClip;
        npcAudioSource.Play();

        // Start manual subtitles if configured
        if (introSubtitles != null && introSubtitles.Length > 0)
        {
            subtitleCoroutine = StartCoroutine(PlayManualSubtitleSequence(npcAudioSource, greetingClip, introSubtitles));
        }

        while (npcAudioSource.isPlaying)
        {
            if (!isInConversation)
            {
                Debug.Log($"[{gameObject.name}] Greeting cancelled");
                npcAudioSource.Stop();
                break;
            }
            yield return null;
        }

        Debug.Log($"[{gameObject.name}] Greeting finished");

        StopSubtitleSequence();
    }

    private IEnumerator CloseConversation()
    {
        Debug.Log($"[{gameObject.name}] Closing conversation");

        // Stop any playing audio
        if (npcAudioSource != null && npcAudioSource.isPlaying)
        {
            Debug.Log($"[{gameObject.name}] Stopping audio on conversation close");
            npcAudioSource.Stop();
        }

        // Stop subtitles
        StopSubtitleSequence();

        // Cancel conversation state
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

        // Hide / disable panel UI
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
            yield return StartCoroutine(FadeCanvasGroup(panelCanvasGroup, 0f, 0.2f));
        }
        if (panelRoot != null) panelRoot.SetActive(false);

        panelOpen = false;

        // Deselect UI
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

    #region Send / Receive (Groq + Piper)

    private void OnInputSubmit(string text)
    {
        OnSendClicked();
    }

    private void OnSendClicked()
    {
        if (playerInputField == null) return;

        string msg = playerInputField.text?.Trim();
        if (string.IsNullOrEmpty(msg)) return;

        playerInputField.interactable = false;
        if (sendButton != null) sendButton.interactable = false;
        if (statusText != null) statusText.text = "Thinking...";

        StartCoroutine(ProcessPlayerMessage(msg));
    }

    private IEnumerator ProcessPlayerMessage(string message)
    {
        if (!isInConversation)
        {
            Debug.Log($"[{gameObject.name}] Not in conversation, ignoring message");
            yield break;
        }

        Debug.Log($"[{gameObject.name}] Processing message: {message}");

        string system = $"You are a professional {profession}, act like you are being interviewed. Answer the player's question directly and concisely in 1-2 short sentences. If the question is not appropriate, reply appropriately.";

        string reply = null;
        yield return StartCoroutine(SendGroqChat(system, message, r => reply = r));

        if (string.IsNullOrEmpty(reply))
            reply = "There is something with the code.";

        // Play reply via Piper with auto subtitles
        yield return StartCoroutine(PlayPiperTTS(reply));

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

        // Stop any existing audio
        if (npcAudioSource.isPlaying)
        {
            Debug.Log($"[{gameObject.name}] Stopping existing audio before TTS");
            npcAudioSource.Stop();
        }

        // Unique output per call
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

        if (!isInConversation)
        {
            Debug.Log($"[{gameObject.name}] Conversation ended before audio loaded, cleaning up");
            TryDeleteSafe(outputPath);
            yield break;
        }

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

            if (!isInConversation)
            {
                Debug.Log($"[{gameObject.name}] Conversation ended before playback, cleaning up");
                TryDeleteSafe(outputPath);
                yield break;
            }

            // 🔻 HIDE CHAT PANEL WHILE NPC IS TALKING 🔻
            bool hadPanel = panelCanvasGroup != null && panelCanvasGroup.gameObject.activeSelf;
            if (hadPanel && panelCanvasGroup != null)
            {
                // fade out quickly so subtitle area is clean
                panelCanvasGroup.interactable = false;
                panelCanvasGroup.blocksRaycasts = false;
                yield return StartCoroutine(FadeCanvasGroup(panelCanvasGroup, 0f, 0.15f));
            }

            Debug.Log($"[{gameObject.name}] Playing TTS audio");
            npcAudioSource.clip = clip;
            npcAudioSource.Play();

            // Start auto subtitles for AI reply
            StopSubtitleSequence();
            subtitleCoroutine = StartCoroutine(PlayAutoSubtitleSequence(npcAudioSource, clip, text));

            while (npcAudioSource.isPlaying && isInConversation)
            {
                yield return null;
            }

            if (!isInConversation && npcAudioSource.isPlaying)
            {
                Debug.Log($"[{gameObject.name}] Conversation ended during playback, stopping");
                npcAudioSource.Stop();
            }

            Debug.Log($"[{gameObject.name}] TTS playback finished");

            // stop subtitles first so they don't overlap the UI reappearing
            StopSubtitleSequence();

            // 🔺 SHOW CHAT PANEL BACK AFTER SPEECH (if convo still active) 🔺
            if (isInConversation && hadPanel && panelCanvasGroup != null)
            {
                // Ensure root is active again, then fade in
                panelCanvasGroup.gameObject.SetActive(true);
                yield return StartCoroutine(FadeCanvasGroup(panelCanvasGroup, 1f, 0.15f));
                panelCanvasGroup.interactable = true;
                panelCanvasGroup.blocksRaycasts = true;
            }

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

    #region Subtitle Logic

    private void StopSubtitleSequence()
    {
        if (subtitleCoroutine != null)
        {
            StopCoroutine(subtitleCoroutine);
            subtitleCoroutine = null;
        }

        if (subtitlePanel != null)
            subtitlePanel.SetActive(false);

        if (subtitleBackground != null)
        {
            subtitleBackground.enabled = false;
            subtitleBackground.gameObject.SetActive(false);
        }

        if (subtitleText != null)
            subtitleText.text = "";
    }

    private IEnumerator PlayManualSubtitleSequence(AudioSource source, AudioClip clip, SubtitleSegment[] segments)
    {
        if (source == null || clip == null || segments == null || segments.Length == 0)
            yield break;

        SubtitleSegment[] sorted = (SubtitleSegment[])segments.Clone();
        Array.Sort(sorted, (a, b) => a.timestamp.CompareTo(b.timestamp));

        int idx = 0;
        float clipLength = clip.length;

        while (idx < sorted.Length && source != null && source.isPlaying)
        {
            SubtitleSegment seg = sorted[idx];

            // wait until it's time for this segment
            while (source != null && source.isPlaying && source.time < seg.timestamp)
            {
                yield return null;
            }

            if (source == null || !source.isPlaying)
                break;

            float segDuration = seg.duration;
            if (segDuration <= 0f)
            {
                if (idx + 1 < sorted.Length)
                    segDuration = Mathf.Max(0.02f, sorted[idx + 1].timestamp - seg.timestamp);
                else
                    segDuration = Mathf.Max(0.02f, clipLength - seg.timestamp);
            }

            // Show subtitle + background
            if (subtitlePanel != null) subtitlePanel.SetActive(true);
            if (subtitleText != null) subtitleText.text = seg.text ?? "";

            if (subtitleBackground != null)
            {
                subtitleBackground.gameObject.SetActive(true);
                subtitleBackground.enabled = true;
                Color c = seg.backgroundColor;
                if (c.a <= 0.01f) c.a = 0.85f;
                subtitleBackground.color = c;
            }

            float elapsed = 0f;
            while (elapsed < segDuration && source != null && source.isPlaying)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // Hide between segments
            if (subtitlePanel != null) subtitlePanel.SetActive(false);
            if (subtitleBackground != null)
            {
                subtitleBackground.enabled = false;
                subtitleBackground.gameObject.SetActive(false);
            }

            idx++;
        }

        StopSubtitleSequence();
    }

    private IEnumerator PlayAutoSubtitleSequence(AudioSource source, AudioClip clip, string fullText)
    {
        if (source == null || clip == null || string.IsNullOrWhiteSpace(fullText))
            yield break;

        var words = fullText.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        int totalWords = words.Length;
        if (totalWords == 0) yield break;

        float totalDuration = clip.length;
        float secondsPerWord = totalDuration / Mathf.Max(1, totalWords);

        int wordIndex = 0;

        while (wordIndex < totalWords && source != null && source.isPlaying && isInConversation)
        {
            int count = Mathf.Min(aiWordsPerSubtitle, totalWords - wordIndex);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(" ");
                sb.Append(words[wordIndex + i]);
            }
            string segText = sb.ToString();

            float segDuration = count * secondsPerWord;

            // Show subtitle + background
            if (subtitlePanel != null) subtitlePanel.SetActive(true);
            if (subtitleText != null) subtitleText.text = segText;

            if (subtitleBackground != null)
            {
                subtitleBackground.gameObject.SetActive(true);
                subtitleBackground.enabled = true;
                Color c = aiSubtitleBackgroundColor;
                if (c.a <= 0.01f) c.a = 0.85f;
                subtitleBackground.color = c;
            }

            float elapsed = 0f;
            while (elapsed < segDuration && source != null && source.isPlaying && isInConversation)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            wordIndex += count;
        }

        StopSubtitleSequence();
    }

    #endregion

    #region Groq Chat

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

    #region Piper TTS engine invocation

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
