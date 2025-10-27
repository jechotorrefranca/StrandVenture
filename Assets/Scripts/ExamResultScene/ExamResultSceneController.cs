using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Video;
using UnityEngine.SceneManagement;
using System.Collections;

[System.Serializable]
public class PieSlice
{
    public string strandName;
    public Image sliceImage;
    public TMP_Text percentageText; // optional
    [HideInInspector] public float targetFill;
}

public class ExamResultSceneController : MonoBehaviour
{
    [Header("Scene Elements")]
    public VideoPlayer backgroundVideo;
    public CanvasGroup fadeOverlay;

    [Header("Bot Settings")]
    public GameObject botContainer;
    public Image botImage;
    public Sprite idleSprite;
    public Sprite talkingSprite;
    public AudioSource botAudio;
    public AudioClip introAudioClip; // Audio for initial scene start
    public AudioClip insightsAudioClip; // Audio for AI insights button

    [Header("Bot Button (Insights)")]
    public GameObject botButton; // Empty object with image and text that floats
    public GameObject insightsPanel; // GameObject that appears when insights button is clicked
    public float buttonFloatAmplitude = 10f;
    public float buttonFloatSpeed = 1.5f;
    public float insightsPanelDelay = 1f; // Delay before bot speaks after panel appears

    [Header("Bot Animation Settings")]
    public float botEntranceDuration = 0.5f;
    public float botOutroDuration = 0.5f;
    public float botEntranceRotation = 360f; // Full rotation during entrance
    public float volumeThreshold = 0.02f; // Audio threshold for talking detection
    public AnimationCurve botScaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Insights Panel Animation Settings")]
    public float panelEntranceDuration = 0.5f;
    public float panelOutroDuration = 0.5f;
    public AnimationCurve panelScaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Result UI")]
    public TMP_Text bestStrandText;
    public Button continueButton;

    [Header("Pie Graph Settings")]
    public PieSlice[] pieSlices;
    public float fillDuration = 1.5f;
    public AnimationCurve fillCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Vector2 botFinalPos;
    private Vector2 buttonOriginalPos;
    private Coroutine buttonFloatCoroutine;
    private bool isBotAnimating = false;
    private bool isPanelAnimating = false;
    private bool isClosingPanel = false;
    private Coroutine currentBotSequence;

    void Start()
    {
        // Fade in overlay
        fadeOverlay.alpha = 1f;
        fadeOverlay.blocksRaycasts = true;
        StartCoroutine(FadeCanvas(fadeOverlay, 1f, 0f, 1f));

        // Start video background
        if (backgroundVideo != null) backgroundVideo.Play();

        // Store bot final position and set initial scale to 0
        RectTransform botRect = botContainer.GetComponent<RectTransform>();
        botFinalPos = botRect.anchoredPosition;
        botRect.localScale = Vector3.zero;
        botContainer.SetActive(true);
        botImage.sprite = idleSprite;

        // Hide insights panel initially and set scale to 0
        if (insightsPanel != null)
        {
            insightsPanel.SetActive(false);
            insightsPanel.GetComponent<RectTransform>().localScale = Vector3.zero;
        }

        // Store button position and start floating
        buttonOriginalPos = botButton.GetComponent<RectTransform>().anchoredPosition;
        Button btnComponent = botButton.GetComponent<Button>();
        if (btnComponent == null)
        {
            btnComponent = botButton.AddComponent<Button>();
        }
        btnComponent.onClick.AddListener(OnBotButtonClicked);
        buttonFloatCoroutine = StartCoroutine(ButtonFloatingMotion());

        // Display best strand
        string bestStrand = PlayerPrefs.GetString("BestStrand", "Unknown");
        float bestScore = PlayerPrefs.GetFloat("BestScore", 0f);
        bestStrandText.text = $"Your best strand is: {bestStrand} ({bestScore:F1}%)";

        // Initialize and animate pie graph
        LoadPieGraphData();
        StartCoroutine(AnimatePieSlices());

        // Continue button
        continueButton.onClick.AddListener(OnContinueClicked);

        // Start intro sequence: entrance -> talk -> outro
        StartCoroutine(BotIntroSequence());
    }

    #region Bot Animation Sequences

    private IEnumerator BotIntroSequence()
    {
        isBotAnimating = true;

        // Entrance animation
        yield return StartCoroutine(BotEntranceAnimation());

        // Get intro speech
        string bestStrand = PlayerPrefs.GetString("BestStrand", "Unknown");
        string introText = $"Welcome! Your results are in. Your best fit is {bestStrand}. Let me explain your scores!";

        // Talk animation
        yield return StartCoroutine(PlayBotSpeech(introAudioClip));

        // Outro animation
        yield return StartCoroutine(BotOutroAnimation());

        isBotAnimating = false;
    }

    private IEnumerator BotInsightsSequence()
    {
        // Force immediate outro if bot is currently visible
        if (isBotAnimating)
        {
            // Immediately hide the bot without animation
            RectTransform rt = botContainer.GetComponent<RectTransform>();
            rt.localScale = Vector3.zero;
            rt.anchoredPosition = botFinalPos;

            // Stop audio
            if (botAudio != null && botAudio.isPlaying)
            {
                botAudio.Stop();
            }

            // Reset sprite
            botImage.sprite = idleSprite;
        }

        isBotAnimating = true;
        isClosingPanel = false;

        // Show insights panel with entrance animation
        if (insightsPanel != null)
        {
            insightsPanel.SetActive(true);
            yield return StartCoroutine(PanelEntranceAnimation());
        }

        // Check if closing was requested during panel entrance
        if (isClosingPanel) yield break;

        // Wait for specified delay before bot appears and speaks
        yield return new WaitForSeconds(insightsPanelDelay);

        // Check if closing was requested during delay
        if (isClosingPanel) yield break;

        // Entrance animation
        yield return StartCoroutine(BotEntranceAnimation());

        // Check if closing was requested during bot entrance
        if (isClosingPanel) yield break;

        // Get insights
        string bestStrand = PlayerPrefs.GetString("BestStrand", "Unknown");
        string stats = PlayerPrefs.GetString($"{bestStrand}_Stats", "No detailed data available for this strand.");

        // Talk animation
        yield return StartCoroutine(PlayBotSpeech(insightsAudioClip));

        // Check if closing was requested during speech
        if (isClosingPanel) yield break;

        // Outro animation
        yield return StartCoroutine(BotOutroAnimation());

        isBotAnimating = false;
    }

    private IEnumerator BotEntranceAnimation()
    {
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        float elapsed = 0f;

        while (elapsed < botEntranceDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / botEntranceDuration);
            float curveValue = botScaleCurve.Evaluate(t);

            // Scale from 0 to 1
            rt.localScale = Vector3.one * curveValue;

            // Rotate during entrance
            float rotation = Mathf.Lerp(botEntranceRotation, 0f, curveValue);
            rt.localEulerAngles = new Vector3(0, 0, rotation);

            yield return null;
        }

        rt.localScale = Vector3.one;
        rt.localEulerAngles = Vector3.zero;
    }

    private IEnumerator BotOutroAnimation()
    {
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        float elapsed = 0f;

        while (elapsed < botOutroDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / botOutroDuration);
            float curveValue = botScaleCurve.Evaluate(t);

            // Scale from 1 to 0
            rt.localScale = Vector3.one * (1f - curveValue);

            // Rotate during outro
            float rotation = Mathf.Lerp(0f, botEntranceRotation, curveValue);
            rt.localEulerAngles = new Vector3(0, 0, rotation);

            yield return null;
        }

        rt.localScale = Vector3.zero;
        rt.localEulerAngles = Vector3.zero;
    }

    #endregion

    #region Panel Animation Methods

    private IEnumerator PanelEntranceAnimation()
    {
        isPanelAnimating = true;
        RectTransform rt = insightsPanel.GetComponent<RectTransform>();
        float elapsed = 0f;

        while (elapsed < panelEntranceDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / panelEntranceDuration);
            float curveValue = panelScaleCurve.Evaluate(t);

            // Scale from 0 to 1
            rt.localScale = Vector3.one * curveValue;

            yield return null;
        }

        rt.localScale = Vector3.one;
        isPanelAnimating = false;
    }

    private IEnumerator PanelOutroAnimation()
    {
        isPanelAnimating = true;
        RectTransform rt = insightsPanel.GetComponent<RectTransform>();
        float elapsed = 0f;

        while (elapsed < panelOutroDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / panelOutroDuration);
            float curveValue = panelScaleCurve.Evaluate(t);

            // Scale from 1 to 0
            rt.localScale = Vector3.one * (1f - curveValue);

            yield return null;
        }

        rt.localScale = Vector3.zero;
        insightsPanel.SetActive(false);
        isPanelAnimating = false;
    }

    #endregion

    #region Pie Graph Methods

    void LoadPieGraphData()
    {
        float totalScore = 0f;

        // First pass: calculate total score
        foreach (var slice in pieSlices)
        {
            float score = PlayerPrefs.GetFloat($"{slice.strandName}_Score", 0f);
            totalScore += score;
        }

        // Second pass: set up slices with proper fill amounts and rotations
        float currentFillOffset = 0f;

        foreach (var slice in pieSlices)
        {
            float score = PlayerPrefs.GetFloat($"{slice.strandName}_Score", 0f);
            Debug.Log($"{slice.strandName}_Score: {score}");

            // Calculate this slice's portion of the whole pie
            float fillAmount = totalScore > 0 ? (score / totalScore) : 0f;
            slice.targetFill = fillAmount;
            slice.sliceImage.fillAmount = 0f;

            // Set the fill origin rotation so this slice starts where the last one ended
            slice.sliceImage.fillOrigin = 2; // Top origin
            RectTransform rt = slice.sliceImage.GetComponent<RectTransform>();
            rt.localEulerAngles = new Vector3(0, 0, -currentFillOffset * 360f);

            currentFillOffset += fillAmount;

            if (slice.percentageText != null)
                slice.percentageText.text = "0%";
        }
    }

    IEnumerator AnimatePieSlices()
    {
        // Animate each slice one by one, sequentially
        for (int i = 0; i < pieSlices.Length; i++)
        {
            PieSlice slice = pieSlices[i];
            float elapsed = 0f;

            while (elapsed < fillDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fillDuration);
                float curveValue = fillCurve.Evaluate(t);

                // Animate only this slice
                float newFill = Mathf.Lerp(0f, slice.targetFill, curveValue);
                slice.sliceImage.fillAmount = newFill;

                if (slice.percentageText != null)
                {
                    float actualPercentage = newFill * 100f;
                    slice.percentageText.text = Mathf.RoundToInt(actualPercentage) + "%";
                }

                yield return null;
            }

            // Ensure final value is set before moving to next slice
            slice.sliceImage.fillAmount = slice.targetFill;
            if (slice.percentageText != null)
            {
                float actualPercentage = slice.targetFill * 100f;
                slice.percentageText.text = Mathf.RoundToInt(actualPercentage) + "%";
            }
        }
    }

    public void RefreshPieGraph()
    {
        StopCoroutine(nameof(AnimatePieSlices));
        LoadPieGraphData();
        StartCoroutine(AnimatePieSlices());
    }

    #endregion

    #region Bot Methods

    private void OnBotButtonClicked()
    {
        // Don't allow clicks while panel is animating
        if (isPanelAnimating) return;

        // Toggle insights panel
        if (insightsPanel != null && insightsPanel.activeSelf)
        {
            // Signal that we're closing and stop the current sequence
            isClosingPanel = true;
            if (currentBotSequence != null)
            {
                StopCoroutine(currentBotSequence);
                currentBotSequence = null;
            }

            // Close panel with animation and force bot outro
            StartCoroutine(CloseInsightsPanelSequence());
        }
        else
        {
            // Start insights sequence (entrance -> talk -> outro)
            if (currentBotSequence != null)
            {
                StopCoroutine(currentBotSequence);
            }
            currentBotSequence = StartCoroutine(BotInsightsSequence());
        }
    }

    private IEnumerator CloseInsightsPanelSequence()
    {
        // If bot is talking or animating, force immediate outro
        if (isBotAnimating)
        {
            // Stop audio immediately
            if (botAudio != null && botAudio.isPlaying)
            {
                botAudio.Stop();
            }

            // Reset sprite and position
            botImage.sprite = idleSprite;
            RectTransform botRect = botContainer.GetComponent<RectTransform>();
            botRect.anchoredPosition = botFinalPos;

            // Play bot outro animation
            yield return StartCoroutine(BotOutroAnimation());

            isBotAnimating = false;
        }

        // Play panel outro animation
        yield return StartCoroutine(PanelOutroAnimation());

        // Reset closing flag
        isClosingPanel = false;
    }

    private IEnumerator PlayBotSpeech(AudioClip audioClip)
    {
        // Play audio if available
        if (botAudio != null && audioClip != null)
        {
            botAudio.clip = audioClip;
            botAudio.Play();
            Debug.Log($"Playing audio: {audioClip.name}");
            yield return StartCoroutine(BotTalkAnimationWithAudio());
        }
        else
        {
            Debug.LogWarning("No audio clip assigned or AudioSource missing!");
            // Fallback: default duration if no audio
            float duration = 2f;
            yield return StartCoroutine(BotTalkAnimation(duration));
        }
    }

    private IEnumerator BotTalkAnimationWithAudio()
    {
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        float[] samples = new float[256];

        while (botAudio.isPlaying)
        {
            // Get audio spectrum data
            botAudio.GetOutputData(samples, 0);

            // Calculate average volume
            float averageVolume = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                averageVolume += Mathf.Abs(samples[i]);
            }
            averageVolume /= samples.Length;

            // Switch sprite based on volume threshold
            if (averageVolume > volumeThreshold)
            {
                botImage.sprite = talkingSprite;
            }
            else
            {
                botImage.sprite = idleSprite;
            }

            // Add slight vertical movement while talking
            float time = Time.time;
            float offsetY = Mathf.Sin(time * 3f) * 5f;
            rt.anchoredPosition = botFinalPos + new Vector2(0, offsetY);

            yield return null;
        }

        // Return to idle state
        botImage.sprite = idleSprite;
        rt.anchoredPosition = botFinalPos;
    }

    private IEnumerator BotTalkAnimation(float duration)
    {
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        float time = 0f;

        while (time < duration)
        {
            // Swap between idle and talking sprites (fallback when no audio)
            botImage.sprite = (Mathf.Sin(time * 20f) > 0) ? talkingSprite : idleSprite;

            // Add slight vertical movement while talking
            float offsetY = Mathf.Sin(time * 3f) * 5f;
            rt.anchoredPosition = botFinalPos + new Vector2(0, offsetY);

            time += Time.deltaTime;
            yield return null;
        }

        // Return to idle state
        botImage.sprite = idleSprite;
        rt.anchoredPosition = botFinalPos;
    }

    private IEnumerator ButtonFloatingMotion()
    {
        RectTransform rt = botButton.GetComponent<RectTransform>();
        float startTime = Time.time;

        while (true)
        {
            float offset = Mathf.Sin((Time.time - startTime) * buttonFloatSpeed) * buttonFloatAmplitude;
            rt.anchoredPosition = new Vector2(buttonOriginalPos.x, buttonOriginalPos.y + offset);
            yield return null;
        }
    }

    #endregion

    #region Scene Transition Methods

    private void OnContinueClicked()
    {
        StartCoroutine(FadeAndLoadNextScene("ChooseStrand"));
    }

    private IEnumerator FadeAndLoadNextScene(string sceneName)
    {
        yield return StartCoroutine(FadeCanvas(fadeOverlay, 0f, 1f, 1f));
        SceneLoader.LoadSceneWithLoading(sceneName);
    }

    private IEnumerator FadeCanvas(CanvasGroup group, float from, float to, float duration)
    {
        group.blocksRaycasts = true;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            group.alpha = Mathf.Lerp(from, to, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        group.alpha = to;

        if (group.alpha <= 0.01f)
            group.blocksRaycasts = false;
    }

    #endregion
}