using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using UnityEngine.Video;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System;

[System.Serializable]
public class PieSlice
{
    public string strandName;
    public Image sliceImage;
    public TMP_Text percentageText;
    [HideInInspector] public float targetFill;   // 0–1, used for the pie fill (share)
    [HideInInspector] public float rawPercent;   // original percent from PlayerPrefs
}

[System.Serializable]
public class TimedSubtitle
{
    public float startTime;   // seconds from start of audio
    public float duration;    // how long to show this line
    [TextArea] public string text;
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
    public AudioClip introAudioClip;
    public AudioClip ttsErrorFallbackClip;
    private string apiKey;

    [Header("Piper TTS Settings")]
    public string piperVoiceName = "en_US-hfc_male-medium";
    [Range(0f, 1f)]
    public float piperVolume = 0.5f; // Adjustable volume for Piper TTS
    private string piperPath;
    private string voicesDir;

    [Header("Bot Button (Insights)")]
    public GameObject botButton;
    public GameObject insightsPanel;
    public TMP_Text insightsContentText;
    public float buttonFloatAmplitude = 10f;
    public float buttonFloatSpeed = 1.5f;
    public float insightsPanelDelay = 1f;

    [Header("Bot Animation Settings")]
    public float botEntranceDuration = 0.5f;
    public float botOutroDuration = 0.5f;
    public float botEntranceRotation = 360f;
    public float volumeThreshold = 0.02f;
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

    [Header("Pie Graph Highlight")]
    public Color topStrandGlowColor = Color.white;
    public float topStrandGlowDistance = 3f;

    [Header("Subtitles")]
    public TMP_Text subtitleText;
    public GameObject subtitleBackground;       // background panel for subtitles
    public float subtitleClearExtraDelay = 0.5f;
    public TimedSubtitle[] introSubtitles;      // manual timeline for intro audio
    [Tooltip("Max words shown at once for AI TTS subtitles")]
    public int maxWordsPerSubtitleChunk = 10;

    private Vector2 botFinalPos;
    private Vector2 buttonOriginalPos;
    private Coroutine buttonFloatCoroutine;
    private bool isBotAnimating = false;
    private bool isPanelAnimating = false;
    private bool isClosingPanel = false;
    private Coroutine currentBotSequence;

    private string cachedInsightsText = "";
    private string cachedSummaryText = "";
    private AudioClip cachedTTSClip = null;
    private bool isInsightsLoaded = false;
    private bool isInsightsLoading = false;

    // track best strands (supports ties)
    private List<string> topStrands = new List<string>();
    private float topStrandPercent = 0f;
    private Coroutine subtitleCoroutine;
    private Coroutine subtitleTimelineCoroutine;

    void Start()
    {
        string topCareer = PlayerPrefs.GetString("RIASEC_TopCareer", "Unknown");
        string careerlist = PlayerPrefs.GetString("RIASEC_CareerRecommendations", "Unknown");
        Debug.Log($"Top Career: {topCareer}");
        Debug.Log($"Career Recommendations: {careerlist}");

        LoadGroqApiKey();
        InitializePiper();

        // Fade in
        fadeOverlay.alpha = 1f;
        fadeOverlay.blocksRaycasts = true;
        StartCoroutine(FadeCanvas(fadeOverlay, 1f, 0f, 1f));

        if (backgroundVideo != null) backgroundVideo.Play();

        // Bot setup
        RectTransform botRect = botContainer.GetComponent<RectTransform>();
        botFinalPos = botRect.anchoredPosition;
        botRect.localScale = Vector3.zero;
        botContainer.SetActive(true);
        botImage.sprite = idleSprite;

        // Insights panel setup
        if (insightsPanel != null)
        {
            insightsPanel.SetActive(false);
            insightsPanel.GetComponent<RectTransform>().localScale = Vector3.zero;
        }

        // Bot button setup
        buttonOriginalPos = botButton.GetComponent<RectTransform>().anchoredPosition;
        Button btnComponent = botButton.GetComponent<Button>();
        if (btnComponent == null)
        {
            btnComponent = botButton.AddComponent<Button>();
        }
        btnComponent.onClick.AddListener(OnBotButtonClicked);
        buttonFloatCoroutine = StartCoroutine(ButtonFloatingMotion());

        // Load pie graph data first so we can know top strands (supporting ties)
        LoadPieGraphData();

        // Compute top filled percent (0–100%)
        float topFilledPercent = 0f;
        foreach (var slice in pieSlices)
        {
            if (topStrands.Contains(slice.strandName))
            {
                float filledPercent = slice.targetFill * 100f;
                if (filledPercent > topFilledPercent)
                    topFilledPercent = filledPercent;
            }
        }

        // Best strand label (supports multiple top strands)
        if (topStrands.Count == 0 || topFilledPercent <= 0f)
        {
            bestStrandText.text = "Your best strand could not be determined.";
        }
        else if (topStrands.Count == 1)
        {
            bestStrandText.text = $"Your best strand is: {topStrands[0]} ({topFilledPercent:F1}%)";
        }
        else
        {
            string joined = string.Join(", ", topStrands);
            bestStrandText.text = $"Your best strands are: {joined} ({topFilledPercent:F1}%)";
        }

        // Apply glow to all top strands
        ApplyTopStrandGlow();

        // Animate pie graph
        StartCoroutine(AnimatePieSlices());

        // Continue button
        continueButton.onClick.AddListener(OnContinueClicked);

        // Bot intro and AI preloading
        StartCoroutine(BotIntroSequence());
        StartCoroutine(PreloadAIInsights());

        // Init subtitle (hidden)
        if (subtitleText != null)
        {
            subtitleText.text = "";
            subtitleText.gameObject.SetActive(false);
        }
        if (subtitleBackground != null)
        {
            subtitleBackground.SetActive(false);
        }
    }

    // ---------- SUBTITLE HELPERS ----------

    private void SetSubtitle(string text, float autoClearAfterSeconds = -1f)
    {
        if (subtitleText == null) return;

        // We are overriding any timeline subtitles
        StopSubtitleTimeline();

        if (subtitleCoroutine != null)
        {
            StopCoroutine(subtitleCoroutine);
            subtitleCoroutine = null;
        }

        if (string.IsNullOrEmpty(text))
        {
            HideSubtitleUI();
            return;
        }

        ShowSubtitleUI(text);

        if (autoClearAfterSeconds > 0f)
        {
            subtitleCoroutine = StartCoroutine(ClearSubtitleAfterDelay(autoClearAfterSeconds));
        }
    }

    private IEnumerator ClearSubtitleAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HideSubtitleUI();
    }

    private void ShowSubtitleUI(string text)
    {
        if (subtitleText == null) return;

        subtitleText.text = text;
        subtitleText.gameObject.SetActive(true);
        if (subtitleBackground != null)
            subtitleBackground.SetActive(true);
    }

    private void HideSubtitleUI()
    {
        if (subtitleText != null)
        {
            subtitleText.text = "";
            subtitleText.gameObject.SetActive(false);
        }
        if (subtitleBackground != null)
            subtitleBackground.SetActive(false);
    }

    private void StartSubtitleTimeline(TimedSubtitle[] timeline, AudioSource source)
    {
        if (timeline == null || timeline.Length == 0 || source == null) return;

        if (subtitleTimelineCoroutine != null)
        {
            StopCoroutine(subtitleTimelineCoroutine);
        }
        subtitleTimelineCoroutine = StartCoroutine(SubtitleTimelineRoutine(timeline, source));
    }

    private void StopSubtitleTimeline()
    {
        if (subtitleTimelineCoroutine != null)
        {
            StopCoroutine(subtitleTimelineCoroutine);
            subtitleTimelineCoroutine = null;
        }
        HideSubtitleUI();
    }

    private IEnumerator SubtitleTimelineRoutine(TimedSubtitle[] timeline, AudioSource source)
    {
        if (timeline == null || timeline.Length == 0 || source == null || source.clip == null)
            yield break;

        // wait until audio starts playing
        while (source.clip != null && !source.isPlaying)
            yield return null;

        while (source.isPlaying)
        {
            float t = source.time;
            bool hasSubtitle = false;

            for (int i = 0; i < timeline.Length; i++)
            {
                float start = Mathf.Max(0f, timeline[i].startTime);
                float duration = Mathf.Max(0f, timeline[i].duration);
                float end = start + duration;

                if (t >= start && t < end)
                {
                    ShowSubtitleUI(timeline[i].text);
                    hasSubtitle = true;
                    break;
                }
            }

            if (!hasSubtitle)
            {
                // no subtitle for this moment
                HideSubtitleUI();
            }

            yield return null;
        }

        HideSubtitleUI();
        subtitleTimelineCoroutine = null;
    }

    // Build auto subtitles for AI TTS: split text into chunks of up to N words
    // and distribute them across the clip length.
    private TimedSubtitle[] BuildAutoSubtitleTimeline(string text, float clipLength)
    {
        if (string.IsNullOrWhiteSpace(text) || clipLength <= 0.1f)
            return null;

        string trimmed = text.Trim();

        // First split into sentences (roughly)
        string[] rawSentences = System.Text.RegularExpressions.Regex.Split(
            trimmed,
            @"(?<=[\.!\?])\s+"
        );

        // Then split each sentence into smaller word chunks
        List<string> chunks = new List<string>();

        int maxWords = Mathf.Max(3, maxWordsPerSubtitleChunk); // at least 3 words to avoid super tiny chunks

        foreach (var raw in rawSentences)
        {
            string sentence = raw.Trim();
            if (string.IsNullOrEmpty(sentence))
                continue;

            // Replace newlines with spaces to avoid weird splits
            sentence = sentence.Replace("\n", " ").Replace("\r", " ");

            string[] words = sentence.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                continue;

            int index = 0;
            while (index < words.Length)
            {
                int remaining = words.Length - index;

                // Take up to maxWords words for this chunk
                int take = Mathf.Min(maxWords, remaining);

                // If last bit is small (e.g. 2–3 words), just include all of them
                if (remaining <= maxWords + 2)
                {
                    take = remaining;
                }

                string chunkText = string.Join(" ", new ArraySegment<string>(words, index, take));
                chunks.Add(chunkText);

                index += take;
            }
        }

        // Fallback: if splitting somehow produced nothing, use the whole text as one chunk
        if (chunks.Count == 0)
        {
            chunks.Add(trimmed);
        }

        // Now compute timing based on chunk lengths (char count)
        int totalChars = 0;
        foreach (var c in chunks)
            totalChars += c.Length;

        if (totalChars <= 0)
            totalChars = chunks.Count;

        TimedSubtitle[] result = new TimedSubtitle[chunks.Count];
        float currentStart = 0f;

        for (int i = 0; i < chunks.Count; i++)
        {
            string chunk = chunks[i];
            float proportion = chunk.Length / (float)totalChars;
            float duration = proportion * clipLength;

            result[i] = new TimedSubtitle
            {
                startTime = currentStart,
                duration = duration,
                text = chunk
            };

            currentStart += duration;
        }

        // Ensure the last chunk reaches (almost) the clip end
        if (result.Length > 0)
        {
            float lastStart = result[result.Length - 1].startTime;
            result[result.Length - 1].duration = Mathf.Max(0.2f, clipLength - lastStart);
        }

        return result;
    }

    // ---------- PIPER / GROQ CONFIG ----------

    private void InitializePiper()
    {
        piperPath = Path.Combine(Application.streamingAssetsPath, "piper/piper.exe");
        voicesDir = Path.Combine(Application.streamingAssetsPath, "piper/voices");

        if (!File.Exists(piperPath))
        {
            Debug.LogError($"❌ Piper executable not found at: {piperPath}");
        }

        string modelPath = Path.Combine(voicesDir, piperVoiceName + ".onnx");
        if (!File.Exists(modelPath))
        {
            Debug.LogError($"❌ Piper voice model not found: {piperVoiceName}");
        }
        else
        {
            Debug.Log($"✅ Piper TTS initialized with voice: {piperVoiceName}");
        }
    }

    private void LoadGroqApiKey()
    {
        string path;

#if UNITY_EDITOR
        path = Path.Combine(Application.dataPath, "../groq_config.json");
#else
        path = Path.Combine(Application.streamingAssetsPath, "groq_config.json");
#endif

        Debug.Log("Looking for config at: " + path);

        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            GroqConfig config = JsonUtility.FromJson<GroqConfig>(json);
            apiKey = config.api_key;
            Debug.Log("API Key loaded successfully!");
        }
        else
        {
            Debug.LogError("groq_config.json not found at: " + path);
            Debug.LogError("Current Application.dataPath: " + Application.dataPath);
            Debug.LogError("Current Application.streamingAssetsPath: " + Application.streamingAssetsPath);
        }
    }

    #region AI Preloading

    private IEnumerator PreloadAIInsights()
    {
        if (isInsightsLoaded || isInsightsLoading) yield break;

        isInsightsLoading = true;
        Debug.Log("🔄 Preloading AI insights in background...");

        yield return new WaitForSeconds(2f);

        // Generate main insights text
        yield return StartCoroutine(GenerateGroqInsights());

        if (string.IsNullOrEmpty(cachedInsightsText))
        {
            Debug.LogWarning("⚠️ Failed to preload insights");
            isInsightsLoading = false;
            yield break;
        }

        // Summarize for TTS
        bool summaryComplete = false;
        yield return StartCoroutine(SummarizeInsightsForTTS(cachedInsightsText, summary =>
        {
            cachedSummaryText = summary;
            summaryComplete = true;
        }));

        if (!summaryComplete || string.IsNullOrEmpty(cachedSummaryText))
        {
            Debug.LogWarning("⚠️ Failed to preload summary");
            cachedSummaryText = cachedInsightsText;
        }

        // Generate TTS
        bool ttsComplete = false;
        yield return StartCoroutine(GeneratePiperTTS(cachedSummaryText, clip =>
        {
            cachedTTSClip = clip;
            ttsComplete = true;
        }));

        if (!ttsComplete || cachedTTSClip == null)
        {
            Debug.LogWarning("⚠️ Failed to preload TTS audio - will use fallback sound");
            cachedTTSClip = ttsErrorFallbackClip;
        }

        isInsightsLoaded = true;
        isInsightsLoading = false;
        Debug.Log("✅ AI insights preloaded successfully!");
    }

    #endregion

    #region Bot Animation Sequences

    private IEnumerator BotIntroSequence()
    {
        isBotAnimating = true;

        yield return StartCoroutine(BotEntranceAnimation());

        string topStrandList = (topStrands.Count > 0) ? string.Join(", ", topStrands) : "Unknown";
        string introText = $"Welcome! Your results are in. Your best fit is {topStrandList}. Let me explain your scores!";

        Debug.Log($"Bot Intro Text: {introText}");

        // Play intro with manual subtitle timeline (if provided), otherwise single line
        yield return StartCoroutine(PlayBotSpeech(introAudioClip, introSubtitles, introText));

        yield return StartCoroutine(BotOutroAnimation());

        isBotAnimating = false;
    }

    private IEnumerator BotInsightsSequence()
    {
        if (isBotAnimating)
        {
            RectTransform rt = botContainer.GetComponent<RectTransform>();
            rt.localScale = Vector3.zero;
            rt.anchoredPosition = botFinalPos;

            if (botAudio != null && botAudio.isPlaying)
            {
                botAudio.Stop();
            }

            botImage.sprite = idleSprite;
        }

        isBotAnimating = true;
        isClosingPanel = false;

        if (insightsPanel != null)
        {
            insightsPanel.SetActive(true);

            if (isInsightsLoading)
            {
                insightsContentText.text = "Analyzing your results... This might take a while.";
            }
            else if (isInsightsLoaded)
            {
                insightsContentText.text = cachedInsightsText;
            }
            else
            {
                insightsContentText.text = "Loading insights...";
            }

            yield return StartCoroutine(PanelEntranceAnimation());
        }

        if (isClosingPanel) yield break;

        while (isInsightsLoading)
        {
            yield return new WaitForSeconds(0.1f);
            if (isClosingPanel) yield break;
        }

        if (!isInsightsLoaded)
        {
            Debug.Log("⚠️ Insights not preloaded, loading now...");
            yield return StartCoroutine(PreloadAIInsights());
        }

        if (insightsPanel != null && !string.IsNullOrEmpty(cachedInsightsText))
        {
            insightsContentText.text = cachedInsightsText;
        }

        if (isClosingPanel) yield break;

        yield return new WaitForSeconds(insightsPanelDelay);

        if (isClosingPanel) yield break;

        yield return StartCoroutine(BotEntranceAnimation());

        if (isClosingPanel) yield break;

        if (cachedTTSClip != null)
        {
            string subtitleSource = !string.IsNullOrEmpty(cachedSummaryText) ? cachedSummaryText : cachedInsightsText;
            TimedSubtitle[] autoTimeline = BuildAutoSubtitleTimeline(subtitleSource, cachedTTSClip.length);

            // Use auto-timed subtitles based on TTS clip length
            yield return StartCoroutine(PlayBotSpeech(cachedTTSClip, autoTimeline, subtitleSource));
        }
        else
        {
            Debug.LogWarning("⚠️ No TTS clip or fallback available, using animation only");
            SetSubtitle("I have some insights about your strand results, but audio is unavailable right now.", 3f);
            yield return StartCoroutine(BotTalkAnimation(3f));
        }

        if (isClosingPanel) yield break;

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

            rt.localScale = Vector3.one * curveValue;
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

            rt.localScale = Vector3.one * (1f - curveValue);
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
        // First pass: read all percents, compute total, and determine top strands (supports ties)
        float totalPercent = 0f;
        topStrands.Clear();
        topStrandPercent = 0f;

        foreach (var slice in pieSlices)
        {
            float percent = Mathf.Max(0f, PlayerPrefs.GetFloat($"Strand_{slice.strandName}_Percent", 0f));
            slice.rawPercent = percent;
            totalPercent += percent;

            if (percent > topStrandPercent + 0.01f)
            {
                topStrandPercent = percent;
                topStrands.Clear();
                topStrands.Add(slice.strandName);
            }
            else if (Mathf.Approximately(percent, topStrandPercent) && percent > 0f)
            {
                if (!topStrands.Contains(slice.strandName))
                    topStrands.Add(slice.strandName);
            }
        }

        // Second pass: normalize to 0–1 for the pie, set rotation & text
        float currentFillOffset = 0f;

        foreach (var slice in pieSlices)
        {
            float fillAmount = 0f;

            if (totalPercent > 0f)
            {
                // share of the circle (0–1)
                fillAmount = slice.rawPercent / totalPercent;
            }

            slice.targetFill = fillAmount;
            slice.sliceImage.fillAmount = 0f; // start from 0 for animation

            slice.sliceImage.fillOrigin = 2;  // radial 360
            RectTransform rt = slice.sliceImage.GetComponent<RectTransform>();
            rt.localEulerAngles = new Vector3(0, 0, -currentFillOffset * 360f);

            currentFillOffset += fillAmount;

            if (slice.percentageText != null)
            {
                // Start at 0% for animation, but show PIE SHARE % (not raw)
                slice.percentageText.text = $"{slice.strandName}: 0%";
                slice.percentageText.color = slice.sliceImage.color;
            }

            Debug.Log($"{slice.strandName} raw percent: {slice.rawPercent:F1}%, pie share: {fillAmount * 100f:F1}%");
        }
    }

    IEnumerator AnimatePieSlices()
    {
        for (int i = 0; i < pieSlices.Length; i++)
        {
            PieSlice slice = pieSlices[i];
            float elapsed = 0f;

            while (elapsed < fillDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fillDuration);
                float curveValue = fillCurve.Evaluate(t);

                // Animate the pie fill (0 → normalized targetFill)
                float newFill = Mathf.Lerp(0f, slice.targetFill, curveValue);
                slice.sliceImage.fillAmount = newFill;

                // Animate the label from 0 → pie share %
                if (slice.percentageText != null)
                {
                    float displayedPercent = Mathf.Lerp(0f, slice.targetFill * 100f, curveValue);
                    slice.percentageText.text = $"{slice.strandName}: {Mathf.RoundToInt(displayedPercent)}%";
                }

                yield return null;
            }

            // Snap to final values
            slice.sliceImage.fillAmount = slice.targetFill;
            if (slice.percentageText != null)
            {
                slice.percentageText.text = $"{slice.strandName}: {Mathf.RoundToInt(slice.targetFill * 100f)}%";
            }
        }
    }

    public void RefreshPieGraph()
    {
        StopCoroutine(nameof(AnimatePieSlices));
        LoadPieGraphData();
        ApplyTopStrandGlow();
        StartCoroutine(AnimatePieSlices());
    }

    // apply glow/outline to all top strands (supports ties)
    private void ApplyTopStrandGlow()
    {
        foreach (var slice in pieSlices)
        {
            bool isTop = topStrands.Contains(slice.strandName);
            Outline outline = slice.sliceImage.GetComponent<Outline>();

            if (isTop)
            {
                if (outline == null)
                {
                    outline = slice.sliceImage.gameObject.AddComponent<Outline>();
                }
                outline.effectColor = topStrandGlowColor;
                outline.effectDistance = new Vector2(topStrandGlowDistance, topStrandGlowDistance);
                outline.enabled = true;
            }
            else
            {
                if (outline != null)
                {
                    outline.enabled = false;
                }
            }
        }
    }

    #endregion

    #region Groq AI Methods

    private IEnumerator GenerateGroqInsights()
    {
        string url = "https://api.groq.com/openai/v1/chat/completions";

        string resultsData = "Student Results (RIASEC percentages):\n";

        // Use strand percents
        foreach (var slice in pieSlices)
        {
            float filledPercent = slice.targetFill * 100f;
            resultsData += $"- {slice.strandName}: {filledPercent:F1}%\n";
            Debug.Log($"Strand {slice.strandName} filled percent (for AI): {filledPercent:F1}% (targetFill={slice.targetFill:F3})");
        }

        // Compute highest filled percent among the top strands
        float topFilledPercent = 0f;
        foreach (var slice in pieSlices)
        {
            if (topStrands.Contains(slice.strandName))
            {
                float filledPercent = slice.targetFill * 100f;
                if (filledPercent > topFilledPercent)
                    topFilledPercent = filledPercent;
            }
        }

        string topStrandList = (topStrands.Count > 0) ? string.Join(", ", topStrands) : "Unknown";
        resultsData += $"\nHighest Score: {topStrandList} ({topFilledPercent:F1}%)";

        string prompt = $"{resultsData}\n\nBased on these results, give your AI opinion on which track the student is best suited for and explain why in simple short sentences and terms, keep it short and not too long.";

        ChatRequest chatRequest = new ChatRequest
        {
            model = "openai/gpt-oss-120b",
            messages = new List<Message>
            {
                new Message { role = "system", content = "You are an educational guidance AI. Provide clear, simple, and encouraging insights about the student's strengths and aptitudes. If the results indicate potential fit for other strands, mention them as additional recommendations. Keep responses concise, supportive, short and easy to understand. Do not use emojis."  },
                new Message { role = "user", content = prompt }
            }
        };

        string requestBody = JsonUtility.ToJson(chatRequest);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Groq API Error: " + request.error);
                cachedInsightsText = "I'm having trouble analyzing your results right now. Please try again later.";
            }
            else
            {
                Debug.Log("Groq Insights Raw Response: " + request.downloadHandler.text);

                ChatResponse response = JsonUtility.FromJson<ChatResponse>(request.downloadHandler.text);
                if (response != null && response.choices.Length > 0)
                {
                    cachedInsightsText = CleanAIText(response.choices[0].message.content);
                    Debug.Log("Generated Insights: " + cachedInsightsText);
                }
            }
        }
    }

    private string CleanAIText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        text = text.Replace("**", "");
        text = text.Replace("__", "");
        text = text.Replace("*", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?<=\s)_|_(?=\s)|^_|_$", "");
        text = text.Replace("###", "");
        text = text.Replace("##", "");
        text = text.Replace("#", "");

        return text.Trim();
    }

    private IEnumerator SummarizeInsightsForTTS(string fullInsights, System.Action<string> onComplete)
    {
        string url = "https://api.groq.com/openai/v1/chat/completions";

        string prompt = $"Summarize this educational insight into a short, natural-sounding speech (3-4 sentences max) that can be spoken aloud:\n\n{fullInsights}";

        ChatRequest chatRequest = new ChatRequest
        {
            model = "openai/gpt-oss-120b",
            messages = new List<Message>
            {
                new Message { role = "system", content = "You are a text summarizer. Create brief, conversational summaries perfect for text-to-speech. Keep it friendly and concise." },
                new Message { role = "user", content = prompt }
            }
        };

        string requestBody = JsonUtility.ToJson(chatRequest);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Groq Summarization Error: " + request.error);
                string[] sentences = fullInsights.Split('.');
                string fallbackSummary = sentences.Length > 2
                    ? $"{sentences[0]}. {sentences[1]}."
                    : fullInsights;
                onComplete?.Invoke(fallbackSummary);
            }
            else
            {
                Debug.Log("Groq Summary Raw Response: " + request.downloadHandler.text);

                ChatResponse response = JsonUtility.FromJson<ChatResponse>(request.downloadHandler.text);
                if (response != null && response.choices.Length > 0)
                {
                    string summary = response.choices[0].message.content.Trim();
                    Debug.Log("Summarized for TTS: " + summary);
                    onComplete?.Invoke(summary);
                }
                else
                {
                    onComplete?.Invoke(fullInsights);
                }
            }
        }
    }

    #endregion

    #region Piper TTS Methods

    private IEnumerator GeneratePiperTTS(string text, System.Action<AudioClip> onComplete)
    {
        string outputPath = Path.Combine(Application.persistentDataPath, "piper_output.wav");
        string modelPath = Path.Combine(voicesDir, piperVoiceName + ".onnx");

        if (!File.Exists(modelPath))
        {
            Debug.LogError($"❌ Piper voice not found: {piperVoiceName}");
            onComplete?.Invoke(null);
            yield break;
        }

        int maxRetries = 3;              // how many times to retry the whole TTS process
        float baseTimeout = 15f;         // base timeout in seconds (will increase with each retry)
        AudioClip finalClip = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            Debug.Log($"🔊 Piper TTS - attempt {attempt}/{maxRetries}");

            // delete previous file if present
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); }
                catch (System.Exception e) { Debug.LogWarning($"⚠️ Could not delete old WAV file before attempt: {e.Message}"); }
            }

            var psi = new ProcessStartInfo
            {
                FileName = piperPath,
                Arguments = $"--model \"{modelPath}\" --output_file \"{outputPath}\" --output_format wav",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(piperPath)
            };

            Process process = null;
            bool processStarted = false;
            string startError = "";

            try
            {
                process = Process.Start(psi);
                processStarted = process != null;
            }
            catch (System.Exception e)
            {
                startError = e.Message;
                processStarted = false;
            }

            if (!processStarted || process == null)
            {
                Debug.LogError($"❌ Failed to start Piper process on attempt {attempt}: {startError}");
                if (attempt < maxRetries)
                {
                    yield return new WaitForSeconds(0.5f);
                    continue; // retry
                }
                else
                {
                    onComplete?.Invoke(null);
                    yield break;
                }
            }

            // provide text input
            try
            {
                process.StandardInput.WriteLine(text);
                process.StandardInput.Close();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"⚠️ Failed writing to Piper stdin: {e.Message}");
            }

            // wait for process to exit with a timeout that increases per attempt
            float timeout = baseTimeout * attempt; // e.g. 15s, 30s, 45s
            float elapsed = 0f;
            while (!process.HasExited && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!process.HasExited)
            {
                Debug.LogWarning($"❌ Piper process timed out on attempt {attempt} (timeout {timeout}s). Killing process and retrying if possible.");
                try { process.Kill(); } catch { }
                process.Dispose();

                if (attempt < maxRetries)
                {
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }
                else
                {
                    onComplete?.Invoke(null);
                    yield break;
                }
            }

            // read pipes for logging
            string stderr = "";
            string stdout = "";
            try
            {
                stderr = process.StandardError.ReadToEnd();
                stdout = process.StandardOutput.ReadToEnd();
            }
            catch { /* ignore */ }

            if (!string.IsNullOrEmpty(stderr))
                Debug.LogWarning($"[Piper STDERR] {stderr}");
            if (!string.IsNullOrEmpty(stdout))
                Debug.Log($"[Piper STDOUT] {stdout}");

            try { process.Dispose(); } catch { }

            // wait a short while for the file to appear
            float fileWait = 0f;
            float fileWaitTimeout = 10f;
            while (!File.Exists(outputPath) && fileWait < fileWaitTimeout)
            {
                fileWait += Time.deltaTime;
                yield return null;
            }

            if (!File.Exists(outputPath))
            {
                Debug.LogWarning($"❌ No WAV file generated by Piper on attempt {attempt}.");
                if (attempt < maxRetries)
                {
                    yield return new WaitForSeconds(0.3f);
                    continue; // retry
                }
                else
                {
                    onComplete?.Invoke(null);
                    yield break;
                }
            }

            // load the audio file using UnityWebRequestMultimedia
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + outputPath, AudioType.WAV))
            {
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"⚠️ Failed to load Piper WAV file on attempt {attempt}: {www.error}");
                    if (attempt < maxRetries)
                    {
                        yield return new WaitForSeconds(0.3f);
                        continue;
                    }
                    else
                    {
                        onComplete?.Invoke(null);
                        yield break;
                    }
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                if (clip == null)
                {
                    Debug.LogWarning($"⚠️ Loaded clip was null on attempt {attempt}.");
                    if (attempt < maxRetries)
                    {
                        yield return new WaitForSeconds(0.3f);
                        continue;
                    }
                    else
                    {
                        onComplete?.Invoke(null);
                        yield break;
                    }
                }

                // adjust volume if needed and finish
                finalClip = AdjustAudioClipVolume(clip, piperVolume);
            }

            // if we reached here, we have a successful clip
            break;
        } // end attempts loop

        if (finalClip == null)
        {
            Debug.LogError("❌ Piper TTS failed after all retries.");
            onComplete?.Invoke(null);
        }
        else
        {
            Debug.Log("✅ Piper TTS succeeded and clip is ready.");
            onComplete?.Invoke(finalClip);
        }
    }

    private AudioClip AdjustAudioClipVolume(AudioClip originalClip, float volumeMultiplier)
    {
        if (originalClip == null || volumeMultiplier >= 0.99f)
            return originalClip;

        float[] samples = new float[originalClip.samples * originalClip.channels];
        originalClip.GetData(samples, 0);

        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] *= volumeMultiplier;
        }

        AudioClip adjustedClip = AudioClip.Create(
            originalClip.name + "_Adjusted",
            originalClip.samples,
            originalClip.channels,
            originalClip.frequency,
            false
        );
        adjustedClip.SetData(samples, 0);

        return adjustedClip;
    }

    #endregion

    #region Bot Methods

    private void OnBotButtonClicked()
    {
        if (isPanelAnimating) return;

        if (insightsPanel != null && insightsPanel.activeSelf)
        {
            isClosingPanel = true;
            if (currentBotSequence != null)
            {
                StopCoroutine(currentBotSequence);
                currentBotSequence = null;
            }

            StartCoroutine(CloseInsightsPanelSequence());
        }
        else
        {
            if (currentBotSequence != null)
            {
                StopCoroutine(currentBotSequence);
            }
            currentBotSequence = StartCoroutine(BotInsightsSequence());
        }
    }

    private IEnumerator CloseInsightsPanelSequence()
    {
        if (isBotAnimating || botContainer.GetComponent<RectTransform>().localScale != Vector3.zero)
        {
            if (botAudio != null && botAudio.isPlaying)
            {
                botAudio.Stop();
            }

            botImage.sprite = idleSprite;
            RectTransform botRect = botContainer.GetComponent<RectTransform>();
            botRect.anchoredPosition = botFinalPos;

            yield return StartCoroutine(BotOutroAnimation());

            isBotAnimating = false;
        }

        yield return StartCoroutine(PanelOutroAnimation());

        // hide subtitle when panel closes
        SetSubtitle("");

        isClosingPanel = false;
    }

    // can take timeline + fallback subtitle text
    private IEnumerator PlayBotSpeech(AudioClip audioClip, TimedSubtitle[] timeline = null, string fallbackSubtitle = null)
    {
        if (botAudio != null && audioClip != null)
        {
            botAudio.clip = audioClip;
            botAudio.Play();
            Debug.Log($"Playing audio: {audioClip.name}");

            if (timeline != null && timeline.Length > 0)
            {
                StartSubtitleTimeline(timeline, botAudio);
            }
            else if (!string.IsNullOrEmpty(fallbackSubtitle))
            {
                SetSubtitle(fallbackSubtitle, audioClip.length + subtitleClearExtraDelay);
            }

            yield return StartCoroutine(BotTalkAnimationWithAudio());

            // When audio is done, stop any timeline and hide subtitles
            StopSubtitleTimeline();
        }
        else
        {
            Debug.LogWarning("No audio clip assigned or AudioSource missing!");
            if (!string.IsNullOrEmpty(fallbackSubtitle))
            {
                SetSubtitle(fallbackSubtitle, 2f + subtitleClearExtraDelay);
            }

            float duration = 2f;
            yield return StartCoroutine(BotTalkAnimation(duration));
            HideSubtitleUI();
        }
    }

    private IEnumerator BotTalkAnimationWithAudio()
    {
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        float[] samples = new float[256];

        while (botAudio.isPlaying)
        {
            botAudio.GetOutputData(samples, 0);

            float averageVolume = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                averageVolume += Mathf.Abs(samples[i]);
            }
            averageVolume /= samples.Length;

            if (averageVolume > volumeThreshold)
            {
                botImage.sprite = talkingSprite;
            }
            else
            {
                botImage.sprite = idleSprite;
            }

            float time = Time.time;
            float offsetY = Mathf.Sin(time * 3f) * 5f;
            rt.anchoredPosition = botFinalPos + new Vector2(0, offsetY);

            yield return null;
        }

        botImage.sprite = idleSprite;
        rt.anchoredPosition = botFinalPos;
    }

    private IEnumerator BotTalkAnimation(float duration)
    {
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        float time = 0f;

        while (time < duration)
        {
            botImage.sprite = (Mathf.Sin(time * 20f) > 0) ? talkingSprite : idleSprite;

            float offsetY = Mathf.Sin(time * 3f) * 5f;
            rt.anchoredPosition = botFinalPos + new Vector2(0, offsetY);

            time += Time.deltaTime;
            yield return null;
        }

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

    #region JSON Serialization Classes

    [System.Serializable]
    public class Message
    {
        public string role;
        public string content;
    }

    [System.Serializable]
    public class Choice
    {
        public Message message;
    }

    [System.Serializable]
    public class ChatResponse
    {
        public Choice[] choices;
    }

    [System.Serializable]
    public class ChatRequest
    {
        public string model;
        public List<Message> messages;
    }

    [System.Serializable]
    public class GroqConfig
    {
        public string api_key;
    }

    #endregion
}
