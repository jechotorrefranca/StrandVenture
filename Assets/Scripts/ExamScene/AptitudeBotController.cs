using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

public class AptitudeBotController : MonoBehaviour
{
    [Header("Bot Settings")]
    public GameObject botContainer;
    public Image botImage;
    public Sprite idleSprite;
    public Sprite talkingSprite;
    public AudioSource botAudio;

    [Tooltip("Use this (AudioWithSubtitles) if you need multiple subtitle segments for the main dialogue.")]
    public AudioWithSubtitles botDialogueAudio;

    public float entranceDuration = 1f;
    public float floatAmplitude = 10f;
    public float floatSpeed = 1.5f;
    public float volumeThreshold = 0.02f;

    [Header("Outro Settings")]
    public AudioWithSubtitles examCompleteAudio; // exam-complete audio + subtitles
    public GameObject fadeOverlay;
    public string nextSceneName = "AptitudeResultScene";

    [Header("Exam UI")]
    public GameObject examCanvas;
    public GameObject startPanel;
    public Button startButton;

    [Header("Skip UI (only for botDialogueAudio & examCompleteAudio)")]
    public GameObject skipUIPanel;       // small panel showing "Hold Space to Skip"
    public Image skipFillImage;         // radial fill image
    public float skipHoldDuration = 1.2f;
    [Tooltip("Delay before skip UI appears (and it's applied only to eligible audios).")]
    public float skipVisibleDelay = 2.5f; // 2-3 seconds default

    private CanvasGroup skipCanvasGroup;
    private float skipHoldTimer = 0f;
    private bool skipActive = false; // only true when skip UI is visible/active for current eligible audio
    private Coroutine skipShowCoroutine;

    [Header("Subtitle UI")]
    public GameObject subtitlePanel;    // panel containing subtitle UI
    public Image subtitleBackground;    // background image whose color can change per segment
    public TMP_Text subtitleText;       // subtitle text

    private Vector2 botOriginalPos;
    private Coroutine floatCoroutine;
    private Coroutine botTalkCoroutine;
    private Coroutine subtitleCoroutine;
    private Coroutine currentVoiceCoroutine;

    // ---------------------
    // Serializable helpers
    // ---------------------
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

    [System.Serializable]
    public class AudioWithSubtitles
    {
        public AudioClip clip;
        public SubtitleSegment[] segments;
    }
    // ---------------------

    void Start()
    {
        botContainer.SetActive(false);
        examCanvas.SetActive(false);

        botOriginalPos = botContainer.GetComponent<RectTransform>().anchoredPosition;
        var rt = botContainer.GetComponent<RectTransform>();
        rt.anchoredPosition = botOriginalPos + new Vector2(0, -600);

        // setup skip UI: initially hidden; we will show only for eligible audios
        if (skipUIPanel != null)
        {
            skipCanvasGroup = skipUIPanel.GetComponent<CanvasGroup>();
            if (skipCanvasGroup == null) skipCanvasGroup = skipUIPanel.AddComponent<CanvasGroup>();
            skipCanvasGroup.alpha = 0f;
            skipUIPanel.SetActive(true); // keep active so alpha controls visibility
            if (skipFillImage != null) skipFillImage.fillAmount = 0f;
        }

        // subtitle UI hidden initially
        if (subtitlePanel != null) subtitlePanel.SetActive(false);
        if (subtitleBackground != null) { subtitleBackground.enabled = false; subtitleBackground.gameObject.SetActive(false); }

        StartCoroutine(SceneSequence());
    }

    void Update()
    {
        // only accept skip input while skip is active for eligible audio
        if (!skipActive) return;

        if (IsSpacePressed())
        {
            skipHoldTimer += Time.deltaTime;
            if (skipFillImage != null)
                skipFillImage.fillAmount = Mathf.Clamp01(skipHoldTimer / skipHoldDuration);

            if (skipHoldTimer >= skipHoldDuration)
            {
                // perform skip (only valid because skipActive == true)
                SkipNow();
                skipHoldTimer = 0f;
                if (skipFillImage != null) skipFillImage.fillAmount = 0f;
            }
        }
        else if (IsSpaceReleasedThisFrame())
        {
            // reset if released early
            skipHoldTimer = 0f;
            if (skipFillImage != null) skipFillImage.fillAmount = 0f;
        }
    }

    // Input wrappers so this compiles and runs with both Input System and legacy
    private bool IsSpacePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.spaceKey.isPressed;
#else
        return Input.GetKey(KeyCode.Space);
#endif
    }

    private bool IsSpaceReleasedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.spaceKey.wasReleasedThisFrame;
#else
        return Input.GetKeyUp(KeyCode.Space);
#endif
    }

    IEnumerator SceneSequence()
    {
        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(FadeOverlayEntrance());

        yield return new WaitForSeconds(0.5f);

        botContainer.SetActive(true);
        yield return StartCoroutine(BotEntranceAnimation());

        // Play main dialogue: prefer AudioWithSubtitles if provided
        if (botDialogueAudio != null && botDialogueAudio.clip != null)
        {
            yield return StartCoroutine(PlayAudioWithSubtitles(botDialogueAudio, showSkipForThisAudio: true));
        }
        else
        {
            // fallback: if no audio assigned, just wait a bit
            Debug.LogWarning("No main dialogue (botDialogueAudio) assigned.");
            yield return new WaitForSeconds(0.5f);
        }

        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(BotExitUpward());

        yield return new WaitForSeconds(0.3f);
        yield return StartCoroutine(ShowExamCanvas());

        floatCoroutine = StartCoroutine(BotFloatingMotion());
    }

    private IEnumerator FadeOverlayEntrance()
    {
        if (fadeOverlay == null) yield break;
        fadeOverlay.SetActive(true);
        Image overlayImage = fadeOverlay.GetComponent<Image>();
        Color color = overlayImage.color;

        float duration = 1.5f;
        float elapsed = 0f;

        color.a = 1f;
        overlayImage.color = color;

        while (elapsed < duration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            color.a = 1f - t;
            overlayImage.color = color;
            elapsed += Time.deltaTime;
            yield return null;
        }

        color.a = 0f;
        overlayImage.color = color;
        fadeOverlay.SetActive(false);
    }

    private IEnumerator FadeOverlayAndLoadScene()
    {
        if (fadeOverlay == null)
        {
            Debug.LogWarning("Missing fadeOverlay! Scene will not transition.");
            yield break;
        }

        fadeOverlay.SetActive(true);
        Image overlayImage = fadeOverlay.GetComponent<Image>();
        Color color = overlayImage.color;

        float duration = 1.5f;
        float elapsed = 0f;

        color.a = 0f;
        overlayImage.color = color;

        while (elapsed < duration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            color.a = t;
            overlayImage.color = color;
            elapsed += Time.deltaTime;
            yield return null;
        }

        color.a = 1f;
        overlayImage.color = color;

        Debug.Log("Exam Finished — loading result scene...");
        SceneLoader.LoadSceneWithLoading(nextSceneName);
    }

    // --- BOT ANIMATIONS ---

    private IEnumerator BotEntranceAnimation()
    {
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        Vector2 startPos = botOriginalPos + new Vector2(0, -360);
        Vector2 endPos = botOriginalPos;

        float elapsed = 0f;
        while (elapsed < entranceDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / entranceDuration);
            rt.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
            rt.localEulerAngles = new Vector3(0, 0, Mathf.Lerp(0f, 360f, t));
            rt.localScale = Vector3.one * Mathf.Lerp(0f, 1f, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        rt.localEulerAngles = Vector3.zero;
        rt.localScale = Vector3.one;
    }

    private IEnumerator BotTalkAnimation()
    {
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        Vector2 basePos = botOriginalPos;
        float[] samples = new float[512];
        float floatTime = 0f;

        while (botAudio != null && botAudio.isPlaying)
        {
            botAudio.GetOutputData(samples, 0);
            float sum = 0f;
            for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
            float rms = Mathf.Sqrt(sum / samples.Length);

            botImage.sprite = (rms > volumeThreshold) ? talkingSprite : idleSprite;
            float offsetY = Mathf.Sin(floatTime * 2f) * 6f;
            rt.anchoredPosition = basePos + new Vector2(0, offsetY);

            floatTime += Time.deltaTime;
            yield return null;
        }

        botImage.sprite = idleSprite;
        rt.anchoredPosition = basePos;
    }

    private IEnumerator BotExitUpward()
    {
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        Vector2 startPos = rt.anchoredPosition;
        Vector2 endPos = startPos + new Vector2(0, 800);
        float duration = 0.8f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            rt.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        botContainer.SetActive(false);
    }

    private IEnumerator BotFloatingMotion()
    {
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        Vector2 startPos = rt.anchoredPosition;
        float startTime = Time.time;

        while (true)
        {
            float offset = Mathf.Sin((Time.time - startTime) * floatSpeed) * floatAmplitude;
            rt.anchoredPosition = new Vector2(startPos.x, startPos.y + offset);
            yield return null;
        }
    }

    private IEnumerator ShowExamCanvas()
    {
        examCanvas.SetActive(true);
        CanvasGroup group = examCanvas.GetComponent<CanvasGroup>();
        RectTransform rt = examCanvas.GetComponent<RectTransform>();

        if (group == null)
            group = examCanvas.AddComponent<CanvasGroup>();

        group.alpha = 0f;
        rt.localScale = Vector3.one * 0.8f;

        float duration = 0.8f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            group.alpha = t;
            rt.localScale = Vector3.Lerp(Vector3.one * 0.8f, Vector3.one, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        group.alpha = 1f;
        rt.localScale = Vector3.one;
    }

    public IEnumerator PlayExamCompleteSequence()
    {
        CanvasGroup examGroup = examCanvas.GetComponent<CanvasGroup>();
        if (examGroup == null)
            examGroup = examCanvas.AddComponent<CanvasGroup>();

        float fadeDuration = 0.8f;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / fadeDuration);
            examGroup.alpha = 1f - t;
            elapsed += Time.deltaTime;
            yield return null;
        }

        examCanvas.SetActive(false);

        if (floatCoroutine != null)
        {
            StopCoroutine(floatCoroutine);
            floatCoroutine = null;
        }

        botContainer.SetActive(true);
        yield return StartCoroutine(BotEntranceAnimation());

        // prefer examCompleteAudio (with subtitles) if provided
        if (examCompleteAudio != null && examCompleteAudio.clip != null)
        {
            yield return StartCoroutine(PlayAudioWithSubtitles(examCompleteAudio, showSkipForThisAudio: true));
        }
        else
        {
            Debug.LogWarning("Missing examCompleteAudio!");
            yield return new WaitForSeconds(2f);
        }

        if (fadeOverlay != null)
            yield return StartCoroutine(FadeOverlayAndLoadScene());
    }

    // ---------------------
    // Play audio with subtitle segments
    // showSkipForThisAudio: if true, skip UI may appear (after skipVisibleDelay) and skip will work
    // ---------------------
    private IEnumerator PlayAudioWithSubtitles(AudioWithSubtitles aws, bool showSkipForThisAudio = false)
    {
        if (aws == null || aws.clip == null)
            yield break;

        // cancel any existing voice coroutine
        if (currentVoiceCoroutine != null)
        {
            StopCoroutine(currentVoiceCoroutine);
            currentVoiceCoroutine = null;
        }

        // stop existing audio
        if (botAudio.isPlaying) botAudio.Stop();

        // clear previous subtitle coroutine
        StopSubtitleSequence();

        // if skip is enabled for this audio, start the delayed show coroutine
        if (showSkipForThisAudio && skipCanvasGroup != null)
        {
            // ensure hidden first
            skipCanvasGroup.alpha = 0f;
            skipActive = false;
            if (skipShowCoroutine != null) StopCoroutine(skipShowCoroutine);
            skipShowCoroutine = StartCoroutine(ShowSkipAfterDelay(skipVisibleDelay));
        }

        // play clip
        botAudio.clip = aws.clip;
        botAudio.Play();

        // start bot talk animation
        if (botTalkCoroutine != null) StopCoroutine(botTalkCoroutine);
        botTalkCoroutine = StartCoroutine(BotTalkAnimation());

        // start subtitle segments
        if (aws.segments != null && aws.segments.Length > 0)
        {
            subtitleCoroutine = StartCoroutine(SubtitleSequenceCoroutine(aws.clip, aws.segments));
        }

        // wait for end or skip
        while (botAudio != null && botAudio.isPlaying && !(skipActive && skipHoldTimer >= skipHoldDuration && false)) // keep waiting; SkipNow called by Update
            yield return null;

        // if audio still playing and we requested skip via SkipNow, SkipNow already stopped it.
        if (botAudio != null && botAudio.isPlaying)
            botAudio.Stop();

        // cleanup subtitles and animation
        StopSubtitleSequence();

        if (botTalkCoroutine != null)
        {
            StopCoroutine(botTalkCoroutine);
            botTalkCoroutine = null;
        }

        // ensure skip UI hidden after this audio (if it was enabled)
        if (skipShowCoroutine != null)
        {
            StopCoroutine(skipShowCoroutine);
            skipShowCoroutine = null;
        }
        if (skipCanvasGroup != null)
        {
            StartCoroutine(FadeCanvasGroup(skipCanvasGroup, skipCanvasGroup.alpha, 0f, 0.2f));
            skipActive = false;
            skipHoldTimer = 0f;
            if (skipFillImage != null) skipFillImage.fillAmount = 0f;
        }
    }

    private IEnumerator SubtitleSequenceCoroutine(AudioClip clip, SubtitleSegment[] segments)
    {
        if (clip == null || segments == null || segments.Length == 0) yield break;

        System.Array.Sort(segments, (a, b) => a.timestamp.CompareTo(b.timestamp));

        int idx = 0;
        float clipLength = clip.length;

        while (idx < segments.Length && botAudio != null && botAudio.isPlaying)
        {
            float currentTime = botAudio.time;
            SubtitleSegment seg = segments[idx];

            if (currentTime + 0.0001f >= seg.timestamp)
            {
                float segDuration = seg.duration;
                if (segDuration <= 0f)
                {
                    if (idx + 1 < segments.Length) segDuration = Mathf.Max(0.02f, segments[idx + 1].timestamp - seg.timestamp);
                    else segDuration = Mathf.Max(0.02f, clipLength - seg.timestamp);
                }

                // show
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
                while (waited < segDuration && botAudio != null && botAudio.isPlaying)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                // hide between segments
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

        // ensure hidden
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

    // ---------------------
    // Skip UI helpers
    // ---------------------
    private IEnumerator ShowSkipAfterDelay(float delay)
    {
        if (skipCanvasGroup == null) yield break;

        // wait first
        yield return new WaitForSeconds(delay);

        // fade in
        yield return StartCoroutine(FadeCanvasGroup(skipCanvasGroup, skipCanvasGroup.alpha, 1f, 0.4f));
        skipActive = true;
        skipHoldTimer = 0f;
        if (skipFillImage != null) skipFillImage.fillAmount = 0f;
        skipShowCoroutine = null;
    }

    private void SkipNow()
    {
        // only skip if skip is active (visible)
        if (!skipActive) return;

        // mark skip requested
        skipActive = false;

        // stop audio immediately
        if (botAudio != null && botAudio.isPlaying)
            botAudio.Stop();

        // stop bot talk animation
        if (botTalkCoroutine != null)
        {
            StopCoroutine(botTalkCoroutine);
            botTalkCoroutine = null;
        }

        // stop subtitles
        StopSubtitleSequence();

        // visually hide skip UI immediately
        if (skipCanvasGroup != null)
            StartCoroutine(FadeCanvasGroup(skipCanvasGroup, skipCanvasGroup.alpha, 0f, 0.2f));

        if (skipFillImage != null) skipFillImage.fillAmount = 0f;
        skipHoldTimer = 0f;
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
    {
        if (cg == null) yield break;
        float elapsed = 0f;
        cg.alpha = from;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            cg.alpha = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        cg.alpha = to;
    }

    void OnDestroy()
    {
        if (floatCoroutine != null) StopCoroutine(floatCoroutine);
        if (botTalkCoroutine != null) StopCoroutine(botTalkCoroutine);
        if (subtitleCoroutine != null) StopCoroutine(subtitleCoroutine);
        if (currentVoiceCoroutine != null) StopCoroutine(currentVoiceCoroutine);
        if (skipShowCoroutine != null) StopCoroutine(skipShowCoroutine);
    }
}
