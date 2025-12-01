using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

#region Question Data Classes

[System.Serializable]
public class QuestionsRoot { public Section[] sections; }

[System.Serializable]
public class Section
{
    public string title;
    public Question[] questions;
}

[System.Serializable]
public class Question
{
    public string id;
    public string text;
    public Option[] options;
}

[System.Serializable]
public class Option
{
    public string choice;
    public int weight;
}

#endregion

public class ExpoBotInteraction : MonoBehaviour
{
    // -------------------------
    // Subtitle data types
    // -------------------------
    [System.Serializable]
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

    // -------------------------
    // Bot & Player References
    // -------------------------
    [Header("Bot References")]
    public GameObject botModel;
    public Animation botAnimation;
    public AudioSource botAudioSource;
    public Collider botCollider;

    [Header("Player References")]
    public Camera playerCamera;
    public Transform playerTransform;
    public FirstPersonCameraMovement playerMovement;

    // -------------------------
    // Bot Animations / Mouth
    // -------------------------
    [Header("Bot Animations / Mouth Meshes")]
    public AnimationClip idleFloatAnimation;
    public AnimationClip mouthOpenAnimation;
    public AnimationClip mouthClosedAnimation;
    public AnimationClip finalMoveAnimation;
    public GameObject mouthOpenMesh;
    public GameObject mouthClosedMesh;

    [Range(0f, 0.5f)]
    public float mouthOpenThreshold = 0.01f;
    private float[] audioSamples = new float[256];

    // -------------------------
    // Audio Clips + Subtitles
    // -------------------------
    [Header("Bot Audio")]
    public AudioClip talkingAudioClip;          // intro
    public AudioClip interactionTalkingClip;    // when pressing E
    public AudioClip finalDialogueClip;         // after survey

    [Header("Subtitles (Bot)")]
    [Tooltip("Subtitle timeline for talkingAudioClip (intro)")]
    public SubtitleSegment[] introSubtitles;
    [Tooltip("Subtitle timeline for interactionTalkingClip (E interaction)")]
    public SubtitleSegment[] interactionSubtitles;
    [Tooltip("Subtitle timeline for finalDialogueClip (after survey)")]
    public SubtitleSegment[] finalSubtitles;

    // -------------------------
    // Movement / Following
    // -------------------------
    [Header("Floating Settings")]
    public float floatAmplitude = 0.2f;
    public float floatSpeed = 1f;

    [Header("Following Settings")]
    public Vector3 followOffset = new Vector3(0f, 0.5f, 2f);
    public float followSmoothTime = 0.5f;

    [Header("Rotation Follow")]
    public float lookAtSpeed = 3f;

    [Tooltip("Rotation offset WHILE bot is in intro (before reaching final position).")]
    public Vector3 lookAtRotationOffset = new Vector3(0f, 180f, 0f);

    [Header("Final Position")]
    public Transform finalPosition;
    public float moveDuration = 2f;

    [Tooltip("Rotation offset AFTER intro, when bot is at its final position and following player rotation.")]
    public Vector3 finalRotationOffset = Vector3.zero;

    // -------------------------
    // Interaction / UI
    // -------------------------
    [Header("Interaction")]
    public float gazeMaxDistance = 8f;
    public LayerMask gazeLayer = -1;

    [Tooltip("Max angle in degrees to still consider the player looking at the bot for the interaction prompt.")]
    public float promptViewAngle = 10f;


    [Header("UI - Fade Overlay")]
    public CanvasGroup fadeOverlayCanvasGroup;
    public float fadeDuration = 1f;


    [Header("UI - Interaction Prompt")]
    public CanvasGroup promptCanvasGroup;
    public TMP_Text promptText;
    public float promptFadeDuration = 0.2f;

    [Tooltip("How long the raycast can briefly miss the bot before hiding the prompt")]
    public float promptGraceTime = 0.15f;

    // -------------------------
    // Survey UI
    // -------------------------
    [Header("UI - Survey Panel")]
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

    // -------------------------
    // Subtitle UI
    // -------------------------
    [Header("Subtitle UI (Bot & NPC)")]
    public GameObject subtitlePanel;     // parent panel for subtitle text & bg
    public Image subtitleBackground;     // background image
    public TMP_Text subtitleText;        // actual subtitle text

    // -------------------------
    // Skip UI (Bot Only)
    // -------------------------
    [Header("Skip UI (Bot Only)")]
    public GameObject skipUIPanel;       // small "Hold Space to Skip" UI
    public Image skipFillImage;          // radial image for hold progress
    public float skipHoldDuration = 1.2f;
    [Tooltip("Delay before skip UI appears for a given bot line")]
    public float skipVisibleDelay = 2.5f;

    // -------------------------
    // Internal state
    // -------------------------
    private bool isTalking = false;          // any bot audio currently speaking
    public static bool BotIsTalkingGlobal;   // other NPC scripts can check this

    private bool introFinished = false;      // becomes true only after intro + move done
    private bool canInteract = false;        // gates E for THIS bot
    private bool isInteracting = false;      // survey interaction in progress
    private bool isMovingToFinal = false;
    private bool followPosition = true;
    private bool followRotation = true;
    private bool useFinalRotationOffset = false; // which offset we are currently using

    private Vector3 followVelocity;
    private Vector3 basePosition;
    private float floatTimer;
    private Coroutine promptCoroutine;

    private float lastHitBotTime = -999f;

    // Survey state
    private QuestionsRoot questionsData;
    private List<Question> flatQuestions = new List<Question>();
    private int currentQuestionIndex = 0;
    private Button[] optionButtons;
    private int totalScore = 0;
    private bool isTransitioning = false;

    // Subtitles & skip state
    private Coroutine subtitleCoroutine;
    private CanvasGroup skipCanvasGroup;
    private float skipHoldTimer = 0f;
    private bool skipActive = false;
    private Coroutine skipShowCoroutine;

    [Header("UI - Crosshair")]
    public GameObject crosshair;


    // -------------------------
    // Unity Life-cycle
    // -------------------------
    void Start()
    {
        // Camera / player references
        if (playerCamera == null) playerCamera = Camera.main;
        if (playerTransform == null && playerCamera != null)
        {
            playerTransform = playerCamera.transform.parent != null
                ? playerCamera.transform.parent
                : playerCamera.transform;
        }

        // Bot animation
        if (botAnimation == null && botModel != null)
            botAnimation = botModel.GetComponentInChildren<Animation>();

        // Overlay hidden initially
        if (fadeOverlayCanvasGroup != null)
        {
            fadeOverlayCanvasGroup.gameObject.SetActive(false);
            fadeOverlayCanvasGroup.alpha = 0f;
        }

        SetupAnimations();

        // Prompt hidden initially
        if (promptCanvasGroup != null)
        {
            promptCanvasGroup.alpha = 0f;
            promptCanvasGroup.gameObject.SetActive(false);
        }

        // Subtitle UI hidden initially
        if (subtitlePanel != null) subtitlePanel.SetActive(false);
        if (subtitleBackground != null)
        {
            subtitleBackground.enabled = false;
            subtitleBackground.gameObject.SetActive(false);
        }

        // Skip UI setup (bot only)
        if (skipUIPanel != null)
        {
            skipCanvasGroup = skipUIPanel.GetComponent<CanvasGroup>();
            if (skipCanvasGroup == null)
                skipCanvasGroup = skipUIPanel.AddComponent<CanvasGroup>();

            skipCanvasGroup.alpha = 0f;
            skipUIPanel.SetActive(false);
        }
        skipActive = false;
        skipHoldTimer = 0f;
        if (skipFillImage != null) skipFillImage.fillAmount = 0f;

        // Lock cursor for FPS control
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (botCollider != null) botCollider.enabled = false;

        SetMouth(false);

        followPosition = true;
        followRotation = true;
        useFinalRotationOffset = false;

        // Initialize option buttons
        optionButtons = new Button[] { buttonA, buttonB, buttonC, buttonD };

        // Hide survey panel
        if (surveyPanel != null)
            surveyPanel.SetActive(false);

        // Load questions
        LoadQuestionsFromResources();

        // Start main sequence
        StartCoroutine(MainSequence());
    }

    void Update()
    {
        UpdateFloating();

        if (followPosition && !isMovingToFinal)
            UpdateFollowingPosition();

        if (followRotation && !isMovingToFinal)
            UpdateRotationToFacePlayer();

        UpdateMouthFromAudio();

        // Skip handling for bot audio
        HandleSkipInput();

        // Only handle bot interaction when appropriate
        if (introFinished && canInteract && !isInteracting && !isTalking)
        {
            HandleInteractionRaycast();
        }
        // REMOVED: Don't force-hide prompt when bot isn't interactable
        // This was hiding the NPC prompts
    }

    void OnDestroy()
    {
        if (promptCoroutine != null) StopCoroutine(promptCoroutine);
        if (subtitleCoroutine != null) StopCoroutine(subtitleCoroutine);
        if (skipShowCoroutine != null) StopCoroutine(skipShowCoroutine);
    }

    // -------------------------
    // Question loading
    // -------------------------
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
                    foreach (var q in s.questions)
                        flatQuestions.Add(q);
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed parsing Questions JSON: " + ex);
        }
    }

    // -------------------------
    // Animations / floating
    // -------------------------
    private void SetupAnimations()
    {
        if (botAnimation == null) return;

        botAnimation.cullingType = AnimationCullingType.AlwaysAnimate;

        if (idleFloatAnimation != null)
        {
            botAnimation.AddClip(idleFloatAnimation, "IdleFloat");
            botAnimation["IdleFloat"].wrapMode = WrapMode.Loop;
        }
        if (mouthClosedAnimation != null)
        {
            botAnimation.AddClip(mouthClosedAnimation, "MouthClosed");
            botAnimation["MouthClosed"].wrapMode = WrapMode.Loop;
        }
        if (mouthOpenAnimation != null)
        {
            botAnimation.AddClip(mouthOpenAnimation, "MouthOpen");
            botAnimation["MouthOpen"].wrapMode = WrapMode.Loop;
        }
        if (finalMoveAnimation != null)
        {
            botAnimation.AddClip(finalMoveAnimation, "FinalMove");
        }

        if (idleFloatAnimation != null)
        {
            botAnimation.Play("IdleFloat");
        }
    }

    private IEnumerator MainSequence()
    {
        // Ensure overlay hidden at start
        if (fadeOverlayCanvasGroup != null)
            yield return StartCoroutine(FadeOverlay(false));

        // Place bot at initial offset in front of player
        if (botModel != null && playerTransform != null)
        {
            Vector3 targetPos = playerTransform.position
                                + playerTransform.forward * followOffset.z
                                + playerTransform.right * followOffset.x
                                + Vector3.up * followOffset.y;

            botModel.transform.position = targetPos;
            basePosition = targetPos;
        }

        followPosition = true;
        followRotation = true;
        useFinalRotationOffset = false; // intro uses lookAtRotationOffset

        yield return new WaitForSeconds(0.5f);

        // 🔒 Lock all NPC interactions while the bot intro is playing.
        InteractableNPC.GlobalInteractionLocked = true;

        // Intro bot line with subtitles + SKIP
        if (talkingAudioClip != null && botAudioSource != null)
        {
            yield return StartCoroutine(PlayAudioWithSubtitles(
                talkingAudioClip,
                introSubtitles,
                allowSkip: true
            ));

            if (botAnimation != null && mouthClosedAnimation != null)
            {
                botAnimation.CrossFade("MouthClosed", 0.2f);
            }
        }

        // ✅ Intro finished (or skipped) → unlock NPCs.
        InteractableNPC.GlobalInteractionLocked = false;


        yield return new WaitForSeconds(0.5f);

        // Move to final position
        followPosition = false;  // stop position follow
        followRotation = false;  // disable rotation follow while moving
        yield return StartCoroutine(MoveToFinalPosition());

        // After arriving: keep position fixed, but follow player rotation with FINAL offset
        followPosition = false;
        useFinalRotationOffset = true;
        followRotation = true;

        // NOW allow interaction
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

        Vector3 targetPos = playerTransform.position
                            + playerTransform.forward * followOffset.z
                            + playerTransform.right * followOffset.x
                            + Vector3.up * followOffset.y;

        basePosition = Vector3.SmoothDamp(basePosition, targetPos, ref followVelocity, followSmoothTime);
    }

    private void UpdateRotationToFacePlayer()
    {
        if (botModel == null || playerCamera == null) return;

        Vector3 lookDir = playerCamera.transform.position - botModel.transform.position;
        if (lookDir.sqrMagnitude < 0.01f) return;

        Quaternion targetRotation = Quaternion.LookRotation(lookDir);

        // Use different offset before/after intro
        Vector3 offset = useFinalRotationOffset ? finalRotationOffset : lookAtRotationOffset;
        Quaternion offsetRotation = Quaternion.Euler(offset);
        targetRotation = targetRotation * offsetRotation;

        botModel.transform.rotation = Quaternion.Slerp(
            botModel.transform.rotation,
            targetRotation,
            Time.deltaTime * lookAtSpeed
        );
    }

    private void UpdateMouthFromAudio()
    {
        if (botAudioSource == null) return;

        if (botAudioSource.isPlaying && isTalking && botAudioSource.clip != null)
        {
            botAudioSource.GetOutputData(audioSamples, 0);
            float sumSq = 0f;
            for (int i = 0; i < audioSamples.Length; i++)
                sumSq += audioSamples[i] * audioSamples[i];

            float rms = Mathf.Sqrt(sumSq / audioSamples.Length);
            bool shouldOpen = rms > mouthOpenThreshold;
            SetMouth(shouldOpen);

            if (botAnimation != null)
            {
                if (shouldOpen && mouthOpenAnimation != null)
                    botAnimation.CrossFade("MouthOpen", 0.05f);
                else if (!shouldOpen && mouthClosedAnimation != null)
                    botAnimation.CrossFade("MouthClosed", 0.05f);
            }
        }
        else
        {
            SetMouth(false);
        }
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

        if (botAnimation != null && finalMoveAnimation != null)
            botAnimation.CrossFade("FinalMove", 0.2f);

        Vector3 startPos = basePosition;
        Quaternion startRot = botModel.transform.rotation;
        Vector3 targetPos = finalPosition.position;
        Quaternion targetRot = finalPosition.rotation; // base rotation; offset is applied later in follow

        float elapsed = 0f;
        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / moveDuration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            basePosition = Vector3.Lerp(startPos, targetPos, smoothT);
            botModel.transform.rotation = Quaternion.Slerp(startRot, targetRot, smoothT);

            yield return null;
        }

        basePosition = targetPos;
        botModel.transform.rotation = targetRot;

        if (botAnimation != null && idleFloatAnimation != null)
            botAnimation.CrossFade("IdleFloat", 0.3f);

        isMovingToFinal = false;
    }

    // -------------------------
    // Fade Overlay
    // -------------------------
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

        if (endAlpha <= 0f)
            fadeOverlayCanvasGroup.gameObject.SetActive(false);
    }

    // -------------------------
    // Interaction / Prompt UI
    // -------------------------
    private void EnableInteraction()
    {
        introFinished = true;          // intro + move fully done
        canInteract = true;
        if (botCollider != null) botCollider.enabled = true;
    }

    private void HandleInteractionRaycast()
    {
        if (playerCamera == null) return;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        bool hitBot = false;

        if (Physics.Raycast(ray, out RaycastHit hit, gazeMaxDistance, gazeLayer))
        {
            if (botCollider != null)
            {
                hitBot = hit.collider == botCollider ||
                         hit.collider.transform.IsChildOf(botCollider.transform);
            }
            else if (botModel != null)
            {
                hitBot = hit.collider.transform.IsChildOf(botModel.transform) ||
                         hit.collider.gameObject == botModel.gameObject;
            }
        }

        if (!hitBot && IsLookingAtBot())
        {
            hitBot = true;
        }

        if (hitBot)
        {
            ShowPrompt("Press <E> to interact");

            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                StartCoroutine(InteractionTalkThenShowPanel());
            }
        }
        else
        {
            // CRITICAL: Only hide the prompt if WE (the bot) were showing it
            // Don't hide if an NPC is showing it
            if (promptCanvasGroup != null && promptCanvasGroup.alpha > 0f)
            {
                // Check if the prompt text is ours
                if (promptText != null && promptText.text.Contains("interact"))
                {
                    HidePromptImmediate();
                }
            }
        }
    }


    private bool IsLookingAtBot()
    {
        if (playerCamera == null) return false;

        Transform targetTransform = null;
        if (botModel != null) targetTransform = botModel.transform;
        else if (botCollider != null) targetTransform = botCollider.transform;

        if (targetTransform == null) return false;

        Vector3 toBot = targetTransform.position - playerCamera.transform.position;
        float distance = toBot.magnitude;
        if (distance > gazeMaxDistance) return false;

        float angle = Vector3.Angle(playerCamera.transform.forward, toBot.normalized);
        return angle <= promptViewAngle;
    }



    private void ShowPrompt(string text)
    {
        if (promptText != null)
            promptText.text = text;

        if (promptCanvasGroup == null) return;

        promptCanvasGroup.gameObject.SetActive(true);
        promptCanvasGroup.alpha = 1f;
    }

    private void HidePromptImmediate()
    {
        if (promptCanvasGroup == null) return;

        promptCanvasGroup.alpha = 0f;
        promptCanvasGroup.gameObject.SetActive(false);
    }


    private IEnumerator InteractionTalkThenShowPanel()
    {
        if (isInteracting || (surveyPanel != null && surveyPanel.activeSelf)) yield break;

        isInteracting = true;
        HidePromptImmediate();

        if (playerMovement != null)
        {
            playerMovement.SetCanMove(false);
            playerMovement.SetCanLookAround(false);
            playerMovement.uiIsOpen = true;
        }

        // Hide crosshair while interacting with the bot
        if (crosshair != null)
            crosshair.SetActive(false);

        // Bot line before showing survey (with subtitles + SKIP)
        AudioClip clipToPlay = interactionTalkingClip != null
            ? interactionTalkingClip
            : talkingAudioClip;

        SubtitleSegment[] segs = (clipToPlay == interactionTalkingClip)
            ? interactionSubtitles
            : introSubtitles;

        if (clipToPlay != null && botAudioSource != null)
        {
            yield return StartCoroutine(PlayAudioWithSubtitles(
                clipToPlay,
                segs,
                allowSkip: true
            ));
        }

        // Then show the survey panel
        yield return StartCoroutine(ShowSurveyPanel());

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnSurveyFinished()
    {
        isInteracting = false;

        // Re-enable movement and looking around
        if (playerMovement != null)
        {
            playerMovement.SetCanMove(true);
            playerMovement.SetCanLookAround(true);
            playerMovement.uiIsOpen = false;
        }

        // Lock cursor back to FPS mode
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Show crosshair again
        if (crosshair != null)
            crosshair.SetActive(true);
    }



    // -------------------------
    // Survey Logic
    // -------------------------
    private IEnumerator ShowSurveyPanel()
    {
        if (surveyPanel == null)
        {
            Debug.LogError("Survey Panel is not assigned in the Inspector!");
            yield break;
        }

        if (questionText == null || buttonA == null || buttonB == null || buttonC == null || buttonD == null)
        {
            Debug.LogError("One or more UI elements are not assigned in the Inspector!");
            yield break;
        }

        currentQuestionIndex = 0;
        totalScore = 0;
        isTransitioning = false;

        for (int i = 0; i < optionButtons.Length; i++)
        {
            if (optionButtons[i] != null)
                optionButtons[i].onClick.RemoveAllListeners();
        }

        surveyPanel.SetActive(true);

        var canvasGroup = surveyPanel.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = surveyPanel.AddComponent<CanvasGroup>();

        yield return StartCoroutine(AnimatePanelIn(canvasGroup, surveyPanel.transform));

        EnsureEventSystem();

        ShowQuestion();
    }

    private void ShowQuestion()
    {
        isTransitioning = false;

        if (currentQuestionIndex < 0 || currentQuestionIndex >= flatQuestions.Count)
        {
            if (questionText != null)
                questionText.text = "No more questions.";
            return;
        }

        var q = flatQuestions[currentQuestionIndex];

        if (questionText != null)
            questionText.text = q.text;

        ResetButtonColors();

        for (int i = 0; i < optionButtons.Length && i < q.options.Length; i++)
        {
            if (optionButtons[i] != null)
            {
                TMP_Text buttonLabel = optionButtons[i].GetComponentInChildren<TMP_Text>();
                if (buttonLabel != null)
                    buttonLabel.text = q.options[i].choice;

                int weight = q.options[i].weight;
                Button button = optionButtons[i];
                string questionId = q.id;

                optionButtons[i].onClick.RemoveAllListeners();
                optionButtons[i].onClick.AddListener(() => OnOptionSelected(weight, button, questionId));

                optionButtons[i].gameObject.SetActive(true);
                optionButtons[i].interactable = true;
            }
        }

        // Hide unused buttons
        for (int i = q.options.Length; i < optionButtons.Length; i++)
        {
            if (optionButtons[i] != null)
                optionButtons[i].gameObject.SetActive(false);
        }
    }

    private void OnOptionSelected(int weight, Button button, string questionId)
    {
        if (isTransitioning) return;

        isTransitioning = true;

        ResetButtonColors();

        var colors = button.colors;
        colors.normalColor = new Color(0.6f, 0.8f, 1f); // Light blue highlight
        button.colors = colors;

        foreach (var btn in optionButtons)
        {
            if (btn != null) btn.interactable = false;
        }

        if (!string.IsNullOrEmpty(questionId))
        {
            PlayerPrefs.SetInt(questionId, weight);
            PlayerPrefs.Save();
            Debug.Log($"Saved PlayerPref: {questionId} = {weight}");
        }

        totalScore += weight;

        StartCoroutine(AdvanceToNextQuestion());
    }

    private IEnumerator AdvanceToNextQuestion()
    {
        yield return new WaitForSeconds(questionTransitionDelay);

        currentQuestionIndex++;

        if (currentQuestionIndex < flatQuestions.Count)
        {
            ShowQuestion();
        }
        else
        {
            Debug.Log("Survey finished. Total score: " + totalScore);

            if (surveyPanel != null)
                surveyPanel.SetActive(false);

            // ✅ Survey is done → give control back to the player
            OnSurveyFinished();

            // Continue with final dialogue + scene transition
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
            yield return StartCoroutine(PlayAudioWithSubtitles(
                finalDialogueClip,
                finalSubtitles,
                allowSkip: true
            ));
        }

        if (fadeOverlayCanvasGroup != null)
            yield return StartCoroutine(FadeOverlay(true));

        if (!string.IsNullOrEmpty(nextSceneName))
            SceneLoader.LoadSceneWithLoading(nextSceneName);
        else
            Debug.LogWarning("nextSceneName not set.");
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

    // -------------------------
    // Subtitles + Skip System
    // -------------------------
    private IEnumerator PlayAudioWithSubtitles(AudioClip clip, SubtitleSegment[] segments, bool allowSkip)
    {
        if (clip == null || botAudioSource == null)
            yield break;

        // Stop any existing audio
        if (botAudioSource.isPlaying)
            botAudioSource.Stop();

        // Clear subtitles
        StopSubtitleSequence();

        // Reset skip UI
        if (skipShowCoroutine != null)
        {
            StopCoroutine(skipShowCoroutine);
            skipShowCoroutine = null;
        }
        skipActive = false;
        skipHoldTimer = 0f;
        if (skipFillImage != null) skipFillImage.fillAmount = 0f;

        if (skipCanvasGroup != null && skipUIPanel != null)
        {
            skipCanvasGroup.alpha = 0f;
            skipUIPanel.SetActive(false);
        }

        // Start delayed skip UI only for bot audio when allowed
        if (allowSkip && skipCanvasGroup != null && skipUIPanel != null)
        {
            skipShowCoroutine = StartCoroutine(ShowSkipAfterDelay(skipVisibleDelay));
        }

        // Temporarily disable interaction & collider while bot is speaking
        bool prevCanInteract = canInteract;
        bool prevColliderEnabled = botCollider != null && botCollider.enabled;
        canInteract = false;
        if (botCollider != null) botCollider.enabled = false;

        // Play audio
        isTalking = true;
        BotIsTalkingGlobal = true;
        botAudioSource.clip = clip;
        botAudioSource.Play();

        // Start subtitle timeline
        if (segments != null && segments.Length > 0)
        {
            subtitleCoroutine = StartCoroutine(SubtitleSequenceCoroutine(
                botAudioSource,
                clip,
                segments
            ));
        }

        // Wait until audio finishes or SkipNow() stops it
        while (botAudioSource != null && botAudioSource.isPlaying)
        {
            yield return null;
        }

        isTalking = false;
        BotIsTalkingGlobal = false;
        SetMouth(false);

        // Restore interaction & collider states
        canInteract = prevCanInteract;
        if (botCollider != null) botCollider.enabled = prevColliderEnabled;

        // Clean up subtitles
        StopSubtitleSequence();

        // Hide skip UI
        if (skipShowCoroutine != null)
        {
            StopCoroutine(skipShowCoroutine);
            skipShowCoroutine = null;
        }
        if (skipCanvasGroup != null && skipUIPanel != null)
        {
            StartCoroutine(FadeCanvasGroup(skipCanvasGroup, skipCanvasGroup.alpha, 0f, 0.2f));
        }
        skipActive = false;
        skipHoldTimer = 0f;
        if (skipFillImage != null) skipFillImage.fillAmount = 0f;
    }

    private IEnumerator SubtitleSequenceCoroutine(AudioSource source, AudioClip clip, SubtitleSegment[] segments)
    {
        if (source == null || clip == null || segments == null || segments.Length == 0) yield break;

        System.Array.Sort(segments, (a, b) => a.timestamp.CompareTo(b.timestamp));

        int idx = 0;
        float clipLength = clip.length;

        while (idx < segments.Length && source != null && source.isPlaying)
        {
            float currentTime = source.time;
            SubtitleSegment seg = segments[idx];

            if (currentTime + 0.0001f >= seg.timestamp)
            {
                float segDuration = seg.duration;
                if (segDuration <= 0f)
                {
                    if (idx + 1 < segments.Length)
                        segDuration = Mathf.Max(0.02f, segments[idx + 1].timestamp - seg.timestamp);
                    else
                        segDuration = Mathf.Max(0.02f, clipLength - seg.timestamp);
                }

                // Show subtitle
                if (subtitlePanel != null) subtitlePanel.SetActive(true);
                if (subtitleText != null) subtitleText.text = seg.text ?? "";

                if (subtitleBackground != null)
                {
                    subtitleBackground.enabled = true;
                    subtitleBackground.gameObject.SetActive(true);
                    Color c = seg.backgroundColor;
                    if (c.a <= 0.01f) c.a = 0.85f;
                    subtitleBackground.color = c;
                }

                float waited = 0f;
                while (waited < segDuration && source != null && source.isPlaying)
                {
                    waited += Time.deltaTime;
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
            else
            {
                yield return null;
            }
        }

        // Ensure hidden
        if (subtitlePanel != null) subtitlePanel.SetActive(false);
        if (subtitleBackground != null)
        {
            subtitleBackground.enabled = false;
            subtitleBackground.gameObject.SetActive(false);
        }

        subtitleCoroutine = null;
    }

    private void StopSubtitleSequence()
    {
        if (subtitleCoroutine != null)
        {
            StopCoroutine(subtitleCoroutine);
            subtitleCoroutine = null;
        }

        if (subtitlePanel != null) subtitlePanel.SetActive(false);
        if (subtitleBackground != null)
        {
            subtitleBackground.enabled = false;
            subtitleBackground.gameObject.SetActive(false);
        }
    }

    private IEnumerator ShowSkipAfterDelay(float delay)
    {
        if (skipCanvasGroup == null || skipUIPanel == null) yield break;

        yield return new WaitForSeconds(delay);

        skipUIPanel.SetActive(true);
        yield return StartCoroutine(FadeCanvasGroup(skipCanvasGroup, skipCanvasGroup.alpha, 1f, 0.3f));

        skipActive = true;
        skipHoldTimer = 0f;
        if (skipFillImage != null) skipFillImage.fillAmount = 0f;
        skipShowCoroutine = null;
    }

    private void HandleSkipInput()
    {
        if (!skipActive) return;
        if (Keyboard.current == null) return;

        var spaceKey = Keyboard.current.spaceKey;
        if (spaceKey.isPressed)
        {
            skipHoldTimer += Time.deltaTime;
            if (skipFillImage != null)
            {
                skipFillImage.fillAmount = Mathf.Clamp01(skipHoldTimer / skipHoldDuration);
            }

            if (skipHoldTimer >= skipHoldDuration)
            {
                SkipNow();
            }
        }
        else if (spaceKey.wasReleasedThisFrame)
        {
            skipHoldTimer = 0f;
            if (skipFillImage != null) skipFillImage.fillAmount = 0f;
        }
    }

    private void SkipNow()
    {
        if (!skipActive) return;

        skipActive = false;

        // Stop bot audio only
        if (botAudioSource != null && botAudioSource.isPlaying)
            botAudioSource.Stop();

        isTalking = false;
        BotIsTalkingGlobal = false;
        SetMouth(false);

        // Stop subtitles
        StopSubtitleSequence();

        // Hide skip UI
        if (skipShowCoroutine != null)
        {
            StopCoroutine(skipShowCoroutine);
            skipShowCoroutine = null;
        }

        if (skipCanvasGroup != null && skipUIPanel != null)
        {
            StartCoroutine(FadeCanvasGroup(skipCanvasGroup, skipCanvasGroup.alpha, 0f, 0.2f));
        }

        if (skipFillImage != null) skipFillImage.fillAmount = 0f;
        skipHoldTimer = 0f;
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
    {
        if (cg == null) yield break;

        float elapsed = 0f;
        cg.alpha = from;
        cg.gameObject.SetActive(true);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            cg.alpha = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }

        cg.alpha = to;
        if (to <= 0f)
            cg.gameObject.SetActive(false);
    }
}
