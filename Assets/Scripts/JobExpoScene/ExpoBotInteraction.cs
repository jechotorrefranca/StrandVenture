using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

[System.Serializable]
public class QuestionsRoot { public Section[] sections; }
[System.Serializable]
public class Section { public string title; public Question[] questions; }
[System.Serializable]
public class Question { public string id; public string text; public Option[] options; }
[System.Serializable]
public class Option { public string choice; public int weight; }

public class ExpoBotInteraction : MonoBehaviour
{
    [Header("Bot References")]
    public GameObject botModel;
    public Animation botAnimation;
    public AudioSource botAudioSource;
    public Collider botCollider;

    [Header("Player References")]
    public Camera playerCamera;
    public Transform playerTransform;
    public FirstPersonCameraMovement playerMovement;

    [Header("Bot Animations / Mouth Meshes")]
    public AnimationClip idleFloatAnimation;
    public AnimationClip mouthOpenAnimation;
    public AnimationClip mouthClosedAnimation;
    public AnimationClip finalMoveAnimation;
    public GameObject mouthOpenMesh;
    public GameObject mouthClosedMesh;

    [Header("Bot Audio")]
    public AudioClip talkingAudioClip;
    public AudioClip interactionTalkingClip;
    public AudioClip finalDialogueClip;

    [Range(0f, 0.5f)]
    public float mouthOpenThreshold = 0.01f;
    private float[] audioSamples = new float[256];

    [Header("Floating Settings")]
    public float floatAmplitude = 0.2f;
    public float floatSpeed = 1f;

    [Header("Following Settings")]
    public Vector3 followOffset = new Vector3(0f, 0.5f, 2f);
    public float followSmoothTime = 0.5f;
    public float lookAtSpeed = 3f;
    public Vector3 lookAtRotationOffset = new Vector3(0f, 180f, 0f);

    [Header("Final Position")]
    public Transform finalPosition;
    public float moveDuration = 2f;
    public Vector3 finalRotationOffset = Vector3.zero;

    [Header("Interaction")]
    public float gazeMaxDistance = 8f;
    public LayerMask gazeLayer = -1;

    [Header("UI - Fade Overlay")]
    public CanvasGroup fadeOverlayCanvasGroup;
    public float fadeDuration = 1f;
    [Range(0f, 1f)]
    public float interactionOverlayAlpha = 0.6f;

    [Header("UI - Interaction Prompt")]
    public CanvasGroup promptCanvasGroup;
    public TMP_Text promptText;
    public float promptFadeDuration = 0.2f;

    [Header("UI - Survey Panel (Drag & Drop)")]
    [Tooltip("The panel GameObject containing all survey UI elements")]
    public GameObject surveyPanel;
    [Tooltip("The TextMeshPro component that displays the question")]
    public TMP_Text questionText;
    [Tooltip("Button A")]
    public Button buttonA;
    [Tooltip("Button B")]
    public Button buttonB;
    [Tooltip("Button C")]
    public Button buttonC;
    [Tooltip("Button D")]
    public Button buttonD;
    public float panelAnimDuration = 0.3f;
    [Tooltip("Delay before advancing to next question after selection")]
    public float questionTransitionDelay = 0.5f;

    [Header("Questions JSON (Resources)")]
    public string questionsResourcePath = "Questions/filename";

    [Header("Scene Transition")]
    public string nextSceneName = "ExploreMoreScene";

    // Internal state
    private bool isTalking = false;
    private bool canInteract = false;
    private bool isInteracting = false;
    private bool isMovingToFinal = false;
    private bool followPosition = true;
    private bool followRotation = true;
    private Vector3 followVelocity;
    private Vector3 basePosition;
    private float floatTimer;
    private Coroutine promptCoroutine;

    // Survey state
    private QuestionsRoot questionsData;
    private List<Question> flatQuestions = new List<Question>();
    private int currentQuestionIndex = 0;
    private Button[] optionButtons;
    private int totalScore = 0;
    private bool isTransitioning = false;

    void Start()
    {
        if (playerCamera == null) playerCamera = Camera.main;
        if (playerTransform == null && playerCamera != null)
            playerTransform = playerCamera.transform.parent != null ? playerCamera.transform.parent : playerCamera.transform;

        if (botAnimation == null && botModel != null)
            botAnimation = botModel.GetComponentInChildren<Animation>();

        // If overlay is assigned, set it inactive initially (alpha 0)
        if (fadeOverlayCanvasGroup != null)
        {
            fadeOverlayCanvasGroup.gameObject.SetActive(false);
            fadeOverlayCanvasGroup.alpha = 0f;
        }

        SetupAnimations();

        if (promptCanvasGroup != null)
        {
            promptCanvasGroup.alpha = 0f;
            promptCanvasGroup.gameObject.SetActive(false);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (botCollider != null) botCollider.enabled = false;

        SetMouth(false);

        followPosition = true;
        followRotation = true;

        // Initialize option buttons array
        optionButtons = new Button[] { buttonA, buttonB, buttonC, buttonD };

        // Hide survey panel initially
        if (surveyPanel != null)
        {
            surveyPanel.SetActive(false);
        }

        LoadQuestionsFromResources();

        StartCoroutine(MainSequence());
    }

    void Update()
    {
        UpdateFloating();

        if (followPosition && !isMovingToFinal) UpdateFollowingPosition();

        // Rotate to face player only when followRotation is true and not currently moving to final.
        // This prevents Update() from overriding coroutine-controlled rotation during MoveToFinalPosition.
        if (followRotation && !isMovingToFinal)
        {
            UpdateRotationToFacePlayer();
        }

        UpdateMouthFromAudio();

        if (canInteract && !isInteracting) HandleInteractionRaycast();
    }

    private void LoadQuestionsFromResources()
    {
        if (string.IsNullOrEmpty(questionsResourcePath))
        {
            Debug.LogWarning("questionsResourcePath is empty.");
            return;
        }

        TextAsset ta = Resources.Load<TextAsset>(questionsResourcePath);
        if (ta == null)
        {
            Debug.LogWarning("Could not load Questions JSON at Resources/" + questionsResourcePath + ".json");
            return;
        }

        try
        {
            questionsData = JsonUtility.FromJson<QuestionsRoot>(ta.text);
            flatQuestions.Clear();
            if (questionsData != null && questionsData.sections != null)
            {
                foreach (var s in questionsData.sections)
                {
                    if (s.questions == null) continue;
                    foreach (var q in s.questions) flatQuestions.Add(q);
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed parsing Questions JSON: " + ex);
        }
    }

    private void SetupAnimations()
    {
        if (botAnimation == null) return;
        botAnimation.cullingType = AnimationCullingType.AlwaysAnimate;
        if (idleFloatAnimation != null) { botAnimation.AddClip(idleFloatAnimation, "IdleFloat"); botAnimation["IdleFloat"].wrapMode = WrapMode.Loop; }
        if (mouthClosedAnimation != null) { botAnimation.AddClip(mouthClosedAnimation, "MouthClosed"); botAnimation["MouthClosed"].wrapMode = WrapMode.Loop; }
        if (mouthOpenAnimation != null) { botAnimation.AddClip(mouthOpenAnimation, "MouthOpen"); botAnimation["MouthOpen"].wrapMode = WrapMode.Loop; }
        if (finalMoveAnimation != null) botAnimation.AddClip(finalMoveAnimation, "FinalMove");
        if (idleFloatAnimation != null) botAnimation.Play("IdleFloat");
    }

    private IEnumerator MainSequence()
    {
        // Ensure overlay is hidden at start
        if (fadeOverlayCanvasGroup != null) yield return StartCoroutine(FadeOverlay(false));

        if (botModel != null && playerTransform != null)
        {
            Vector3 targetPos = playerTransform.position + playerTransform.forward * followOffset.z +
                                playerTransform.right * followOffset.x +
                                Vector3.up * followOffset.y;
            botModel.transform.position = targetPos;
            basePosition = targetPos;
        }

        followPosition = true;
        followRotation = true;

        yield return new WaitForSeconds(0.5f);

        if (talkingAudioClip != null && botAudioSource != null)
        {
            isTalking = true;
            botAudioSource.clip = talkingAudioClip;
            botAudioSource.Play();
            while (botAudioSource.isPlaying) yield return null;
            isTalking = false;
            SetMouth(false);
            if (botAnimation != null && mouthClosedAnimation != null) botAnimation.CrossFade("MouthClosed", 0.2f);
        }

        yield return new WaitForSeconds(0.5f);

        // Move to final position and stop both position and rotation following
        followPosition = false;
        followRotation = false; // FIXED: Keep this false permanently after moving to final position
        yield return StartCoroutine(MoveToFinalPosition());

        // Keep both followPosition and followRotation false (bot stays at final spot with final rotation)
        followPosition = false;
        followRotation = false; // FIXED: Ensure rotation following stays disabled

        EnableInteraction();
    }

    private void UpdateFloating()
    {
        if (botModel == null) return;
        floatTimer += Time.deltaTime * floatSpeed;
        float yOffset = Mathf.Sin(floatTimer) * floatAmplitude;
        Vector3 targetPos = basePosition + new Vector3(0f, yOffset, 0f);
        botModel.transform.position = targetPos;
    }

    private void UpdateFollowingPosition()
    {
        if (botModel == null || playerTransform == null) return;
        Vector3 targetPos = playerTransform.position + playerTransform.forward * followOffset.z +
                            playerTransform.right * followOffset.x + Vector3.up * followOffset.y;
        basePosition = Vector3.SmoothDamp(basePosition, targetPos, ref followVelocity, followSmoothTime);
    }

    private void UpdateRotationToFacePlayer()
    {
        if (botModel == null || playerCamera == null) return;
        Vector3 lookDir = playerCamera.transform.position - botModel.transform.position;
        if (lookDir.sqrMagnitude > 0.01f)
        {
            // Optionally keep only Y rotation (uncomment if you want the bot not to tilt up/down)
            // lookDir.y = 0f;

            Quaternion targetRotation = Quaternion.LookRotation(lookDir);
            Quaternion offsetRotation = Quaternion.Euler(lookAtRotationOffset);
            targetRotation = targetRotation * offsetRotation;

            // Smoothly slerp from current rotation (which may be the finalPosition rotation) to the look-at rotation.
            botModel.transform.rotation = Quaternion.Slerp(botModel.transform.rotation, targetRotation, Time.deltaTime * lookAtSpeed);
        }
    }

    private void UpdateMouthFromAudio()
    {
        if (botAudioSource == null) return;
        if (botAudioSource.isPlaying && isTalking && botAudioSource.clip != null)
        {
            botAudioSource.GetOutputData(audioSamples, 0);
            float sumSq = 0f;
            for (int i = 0; i < audioSamples.Length; i++) sumSq += audioSamples[i] * audioSamples[i];
            float rms = Mathf.Sqrt(sumSq / audioSamples.Length);
            bool shouldOpen = rms > mouthOpenThreshold;
            SetMouth(shouldOpen);
            if (botAnimation != null)
            {
                if (shouldOpen && mouthOpenAnimation != null) botAnimation.CrossFade("MouthOpen", 0.05f);
                else if (!shouldOpen && mouthClosedAnimation != null) botAnimation.CrossFade("MouthClosed", 0.05f);
            }
        }
        else SetMouth(false);
    }

    private void SetMouth(bool open)
    {
        if (mouthOpenMesh != null) mouthOpenMesh.SetActive(open);
        if (mouthClosedMesh != null) mouthClosedMesh.SetActive(!open);
    }

    private IEnumerator MoveToFinalPosition()
    {
        if (botModel == null || finalPosition == null) yield break;
        isMovingToFinal = true;

        if (botAnimation != null && finalMoveAnimation != null) botAnimation.CrossFade("FinalMove", 0.2f);

        Vector3 startPos = basePosition;
        Quaternion startRot = botModel.transform.rotation;
        Vector3 targetPos = finalPosition.position;

        // Move: lerp position
        float elapsed = 0f;
        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / moveDuration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            basePosition = Vector3.Lerp(startPos, targetPos, smoothT);

            // During the move we also interpolate rotation toward the finalPosition.rotation (without extra offset)
            Quaternion targetRotDuring = finalPosition.rotation;
            botModel.transform.rotation = Quaternion.Slerp(startRot, targetRotDuring, smoothT);

            yield return null;
        }

        // Snap to final values at end of move:
        basePosition = targetPos;

        // Use the finalPosition rotation as the current rotation (do NOT apply finalRotationOffset here
        // to ensure the finalPosition's rotation is respected exactly as requested).
        botModel.transform.rotation = finalPosition.rotation;

        // If you still want to apply a small local offset relative to the final rotation, uncomment:
        // botModel.transform.rotation = finalPosition.rotation * Quaternion.Euler(finalRotationOffset);

        if (botAnimation != null && idleFloatAnimation != null) botAnimation.CrossFade("IdleFloat", 0.3f);
        isMovingToFinal = false;
    }

    private IEnumerator FadeOverlay(bool fadeToBlack)
    {
        if (fadeOverlayCanvasGroup == null) yield break;
        fadeOverlayCanvasGroup.gameObject.SetActive(true);
        float startAlpha = fadeOverlayCanvasGroup.alpha;
        float endAlpha = fadeToBlack ? 1f : 0f;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;
            fadeOverlayCanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, t);
            yield return null;
        }
        fadeOverlayCanvasGroup.alpha = endAlpha;
        if (endAlpha <= 0f) fadeOverlayCanvasGroup.gameObject.SetActive(false);
    }

    // NOTE: Interaction will no longer show overlay. The overlay is only used for scene transitions.
    private IEnumerator FadeOverlayTo(float targetAlpha)
    {
        if (fadeOverlayCanvasGroup == null) yield break;
        fadeOverlayCanvasGroup.gameObject.SetActive(true);
        float startAlpha = fadeOverlayCanvasGroup.alpha;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;
            fadeOverlayCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }
        fadeOverlayCanvasGroup.alpha = targetAlpha;
        if (targetAlpha <= 0f) fadeOverlayCanvasGroup.gameObject.SetActive(false);
    }

    private void EnableInteraction()
    {
        canInteract = true;
        if (botCollider != null) botCollider.enabled = true;
    }

    private void HandleInteractionRaycast()
    {
        if (playerCamera == null) return;
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, gazeMaxDistance, gazeLayer))
        {
            bool hitBot = false;
            if (botCollider != null) hitBot = hit.collider == botCollider || hit.collider.transform.IsChildOf(botCollider.transform);
            else if (botModel != null) hitBot = hit.collider.transform.IsChildOf(botModel.transform) || hit.collider.gameObject == botModel.gameObject;

            if (hitBot)
            {
                ShowPrompt("Press <E> to interact");
                if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) StartCoroutine(InteractionTalkThenShowPanel());
                return;
            }
        }
        HidePrompt();
    }

    private void ShowPrompt(string text)
    {
        if (promptText != null) promptText.text = text;
        if (promptCanvasGroup == null) return;
        if (promptCoroutine != null) StopCoroutine(promptCoroutine);
        promptCoroutine = StartCoroutine(FadePrompt(1f));
    }

    private void HidePrompt()
    {
        if (promptCanvasGroup == null) return;
        if (promptCoroutine != null) StopCoroutine(promptCoroutine);
        promptCoroutine = StartCoroutine(FadePrompt(0f));
    }

    private IEnumerator FadePrompt(float targetAlpha)
    {
        if (promptCanvasGroup == null) yield break;
        float startAlpha = promptCanvasGroup.alpha;
        float elapsed = 0f;
        promptCanvasGroup.gameObject.SetActive(true);
        while (elapsed < promptFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / promptFadeDuration;
            promptCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }
        promptCanvasGroup.alpha = targetAlpha;
        if (targetAlpha <= 0f) promptCanvasGroup.gameObject.SetActive(false);
    }

    private IEnumerator InteractionTalkThenShowPanel()
    {
        if (isInteracting || (surveyPanel != null && surveyPanel.activeSelf)) yield break;
        isInteracting = true;
        HidePrompt();

        // NOTE: Removed overlay activation here. The survey UI will open without showing the overlay.

        if (playerMovement != null)
        {
            playerMovement.SetCanMove(false);
            playerMovement.SetCanLookAround(false);
            playerMovement.uiIsOpen = true;
        }

        // Bot talks first before showing panel
        AudioClip clipToPlay = interactionTalkingClip != null ? interactionTalkingClip : talkingAudioClip;
        if (clipToPlay != null && botAudioSource != null)
        {
            isTalking = true;
            botAudioSource.clip = clipToPlay;
            botAudioSource.Play();
            while (botAudioSource.isPlaying) yield return null;
            isTalking = false;
            SetMouth(false);
        }

        // Then show the survey panel (no overlay)
        yield return StartCoroutine(ShowSurveyPanel());

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private IEnumerator ShowSurveyPanel()
    {
        if (surveyPanel == null)
        {
            Debug.LogError("Survey Panel is not assigned in the Inspector!");
            yield break;
        }

        // Validate all required UI elements
        if (questionText == null || buttonA == null || buttonB == null ||
            buttonC == null || buttonD == null)
        {
            Debug.LogError("One or more UI elements are not assigned in the Inspector!");
            yield break;
        }

        // Reset survey state
        currentQuestionIndex = 0;
        totalScore = 0;
        isTransitioning = false;

        // Setup option buttons
        for (int i = 0; i < optionButtons.Length; i++)
        {
            if (optionButtons[i] != null)
            {
                optionButtons[i].onClick.RemoveAllListeners();
            }
        }

        // Show the panel
        surveyPanel.SetActive(true);

        // Get or add CanvasGroup for animation
        var canvasGroup = surveyPanel.GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = surveyPanel.AddComponent<CanvasGroup>();

        // Animate panel in
        yield return StartCoroutine(AnimatePanelIn(canvasGroup, surveyPanel.transform));

        EnsureEventSystem();

        // Show first question
        ShowQuestion();
    }

    private void ShowQuestion()
    {
        isTransitioning = false;

        if (currentQuestionIndex < 0 || currentQuestionIndex >= flatQuestions.Count)
        {
            if (questionText != null) questionText.text = "No more questions.";
            return;
        }

        var q = flatQuestions[currentQuestionIndex];

        // Set question text
        if (questionText != null)
        {
            questionText.text = q.text;
        }

        // Reset all button colors
        ResetButtonColors();

        // Setup each option button
        for (int i = 0; i < optionButtons.Length && i < q.options.Length; i++)
        {
            if (optionButtons[i] != null)
            {
                // Get the text component from the button
                TMP_Text buttonLabel = optionButtons[i].GetComponentInChildren<TMP_Text>();
                if (buttonLabel != null)
                {
                    buttonLabel.text = q.options[i].choice;
                }

                // Setup click listener
                int weight = q.options[i].weight; // Capture for closure
                Button button = optionButtons[i]; // Capture for closure
                string questionId = q.id; // Capture for closure

                optionButtons[i].onClick.RemoveAllListeners();
                optionButtons[i].onClick.AddListener(() => OnOptionSelected(weight, button, questionId));

                optionButtons[i].gameObject.SetActive(true);
                optionButtons[i].interactable = true;
            }
        }

        // Hide unused buttons if there are fewer than 4 options
        for (int i = q.options.Length; i < optionButtons.Length; i++)
        {
            if (optionButtons[i] != null)
            {
                optionButtons[i].gameObject.SetActive(false);
            }
        }
    }

    private void OnOptionSelected(int weight, Button button, string questionId)
    {
        // Prevent multiple clicks during transition
        if (isTransitioning) return;

        isTransitioning = true;

        // Visual highlight: reset others and highlight selected
        ResetButtonColors();

        var colors = button.colors;
        colors.normalColor = new Color(0.6f, 0.8f, 1f); // Light blue highlight
        button.colors = colors;

        // Disable all buttons to prevent multiple selections
        foreach (var btn in optionButtons)
        {
            if (btn != null) btn.interactable = false;
        }

        // Save selection to PlayerPrefs
        if (!string.IsNullOrEmpty(questionId))
        {
            PlayerPrefs.SetInt(questionId, weight);
            PlayerPrefs.Save();
            Debug.Log($"Saved PlayerPref: {questionId} = {weight}");
        }

        // Add score
        totalScore += weight;

        // Automatically advance to next question after delay
        StartCoroutine(AdvanceToNextQuestion());
    }

    private IEnumerator AdvanceToNextQuestion()
    {
        // Wait for transition delay
        yield return new WaitForSeconds(questionTransitionDelay);

        currentQuestionIndex++;

        if (currentQuestionIndex < flatQuestions.Count)
        {
            ShowQuestion();
        }
        else
        {
            Debug.Log("Survey finished. Total score: " + totalScore);

            // Hide the survey panel
            if (surveyPanel != null)
            {
                surveyPanel.SetActive(false);
            }

            StartCoroutine(PlayFinalDialogueThenLoadNextScene());
        }
    }

    private void ResetButtonColors()
    {
        foreach (var btn in optionButtons)
        {
            if (btn != null)
            {
                var colors = btn.colors;
                colors.normalColor = Color.white;
                btn.colors = colors;
            }
        }
    }

    private IEnumerator PlayFinalDialogueThenLoadNextScene()
    {
        if (finalDialogueClip != null && botAudioSource != null)
        {
            isTalking = true;
            botAudioSource.clip = finalDialogueClip;
            botAudioSource.Play();
            while (botAudioSource.isPlaying) yield return null;
            isTalking = false;
            SetMouth(false);
        }

        if (fadeOverlayCanvasGroup != null) yield return StartCoroutine(FadeOverlay(true));

        if (!string.IsNullOrEmpty(nextSceneName)) SceneLoader.LoadSceneWithLoading(nextSceneName);
        else Debug.LogWarning("nextSceneName not set.");
    }

    private IEnumerator AnimatePanelIn(CanvasGroup canvasGroup, Transform panelTransform)
    {
        canvasGroup.alpha = 0f;
        panelTransform.localScale = Vector3.one * 0.7f;

        float elapsed = 0f;
        while (elapsed < panelAnimDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / panelAnimDuration;
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            panelTransform.localScale = Vector3.Lerp(Vector3.one * 0.7f, Vector3.one, smoothT);
            canvasGroup.alpha = t;
            yield return null;
        }

        panelTransform.localScale = Vector3.one;
        canvasGroup.alpha = 1f;
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }
    }
}