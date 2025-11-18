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

/* * InteractableNPC.cs (Fixed - v2)
 * ------------------ 
 * Changes from previous version:
 * - Uses an explicit Camera reference (playerCamera) like your InteractionManager
 * - Uses a gaze layer mask and gazeMaxDistance for raycasts
 * - Accepts child colliders (GetComponentInParent / IsChildOf)
 * - Adds a Fade+Scale prompt routine copied from your InteractionManager
 * - Adds an optional LOS (obstruction) check using obstructionMask
 * - Ensures idle animation remains playing when not interacting
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
    public string voiceName = "en_US-hfc_male-medium";

    [Header("Groq/Piper config")]
    public string groqConfigFilename = "groq_config.json";
    public string piperRelativePath = "piper/piper.exe";
    public string voicesRelativeDir = "piper/voices";

    [Header("Player refs")]
    public Transform playerHead;
    public GameObject playerRoot;
    public FirstPersonCameraMovement fpsController;

    [Header("Animation Settings")]
    public Animator npcAnimator;
    public AnimationClip idleAnimationClip;
    public AnimationClip conversationAnimationClip;
    public float conversationIdleInterval = 3f; // seconds between switching to idle during conversation
    public float playerDetectionRange = 5f; // range to start following player
    public float rotationSpeed = 2f; // speed of Y-axis rotation

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

        // Start idle animation (ensure it stays active when not interacting)
        if (npcAnimator != null && idleAnimationClip != null)
        {
            ForcePlayIdle();
        }
    }

    void Update()
    {
        // Handle player detection and rotation (always, unless in conversation)
        if (!isInConversation)
        {
            HandlePlayerTracking();

            // Ensure idle animation keeps playing while not in conversation
            if (npcAnimator != null && idleAnimationClip != null)
            {
                var state = npcAnimator.GetCurrentAnimatorStateInfo(0);
                if (!state.IsName(idleAnimationClip.name))
                {
                    npcAnimator.Play(idleAnimationClip.name);
                }
            }
        }

        if (panelOpen)
        {
            // Allow quick exit with Q
            if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame)
            {
                StartCoroutine(CloseConversation());
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
        if (npcAnimator != null && clip != null)
        {
            npcAnimator.Play(clip.name);
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
        if (npcAnimator == null || idleAnimationClip == null) return;
        try
        {
            npcAnimator.Play(idleAnimationClip.name);
        }
        catch (Exception)
        {
            // If Play by name fails (Animator states don't match clip names), silently ignore —
            // the user should ensure the Animator Controller has states named after the clips.
        }
    }

    #endregion

    #region Conversation Flow

    private IEnumerator BeginConversation()
    {
        panelOpen = true;
        isInConversation = true;

        // Hide the prompt immediately when interaction starts
        if (playerLooking)
        {
            playerLooking = false;
            HidePrompt();
        }

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

        // Play a short local greeting via TTS then open panel after audio finishes
        string greeting = $"Hello, I am a {profession}. Ask me a question.";
        yield return StartCoroutine(PlayPiperTTS(greeting)); // PlayPiperTTS already yields until audio finishes

        // Ensure EventSystem exists (so selection/focus works)
        EnsureEventSystemExists();

        // Show chat panel only AFTER TTS finished
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
            EventSystem.current.SetSelectedGameObject(playerInputField.gameObject);
            playerInputField.ActivateInputField();
        }

        if (sendButton != null) sendButton.interactable = true;

        // Clear any status text
        if (statusText != null) statusText.text = "";
    }


    private IEnumerator CloseConversation()
    {
        // Stop conversation animation
        isInConversation = false;
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

        if (fpsController != null) fpsController.SetCanMove(true);
        if (fpsController != null) fpsController.SetCanLookAround(true);
    }

    private void EnsureEventSystemExists()
    {
        if (EventSystem.current != null) return;

        // Create a minimal EventSystem so UI selection and clicks work
        var go = new GameObject("EventSystem", new System.Type[] { typeof(EventSystem), typeof(StandaloneInputModule) });
        // Optionally configure the module here. This works with the old UI input module
        // If you're using the new Input System, you may prefer InputSystemUIInputModule (install package).
        // For most projects StandaloneInputModule is fine.
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

    #region Send / Receive (unchanged)

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
        // Build concise system prompt
        string system = $"You are a professional {profession}. Answer the player's question directly and concisely in 1-2 short sentences. If the question is not answerable, reply exactly: I can't answer that.";

        string reply = null;
        yield return StartCoroutine(SendGroqChat(system, message, r => reply = r));

        if (string.IsNullOrEmpty(reply))
            reply = "I'm sorry, I couldn't get an answer.";

        // Play reply via Piper
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

    #endregion

    #region Groq Chat Method (unchanged)

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

    #region Piper TTS (unchanged)

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

    [Serializable]
    private class SerializableKey
    {
        public string api_key;
    }

    private void InitializePiperPaths()
    {
        piperPath = Path.Combine(Application.streamingAssetsPath, piperRelativePath);
        voicesDir = Path.Combine(Application.streamingAssetsPath, voicesRelativeDir);
    }

    private IEnumerator PlayPiperTTS(string text)
    {
        if (npcAudioSource == null) yield break;

        string outputPath = Path.Combine(Application.persistentDataPath, "piper_output.wav");
        bool ok = false;

        Task<bool> gen = Task.Run(() => GeneratePiperAudio(text, outputPath));
        while (!gen.IsCompleted) yield return null;
        ok = gen.Result;

        if (!ok)
        {
            Debug.LogError("Piper generation failed");
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
                Debug.LogError("Audio load error: " + www.error);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
            if (clip == null)
            {
                Debug.LogError("Failed to get audio clip");
                yield break;
            }

            npcAudioSource.clip = clip;
            npcAudioSource.Play();

            while (npcAudioSource.isPlaying)
                yield return null;
        }
    }

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
            while (!File.Exists(outputPath) && attempts < 50)
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
