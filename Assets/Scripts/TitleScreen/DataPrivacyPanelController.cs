using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using System.Collections;
using TMPro;

public class DataPrivacyPanelController : MonoBehaviour
{
    [Header("UI References")]
    public GameObject dataPrivacyPanel;
    public Toggle agreeToggle;
    public Button agreeButton;
    public Button closeButton;
    public CanvasGroup panelGroup;
    public VideoPlayer panelVideo;

    [Header("Bot Settings (single image)")]
    public GameObject botContainer;
    public Image botImage;
    public Sprite idleSprite;
    public Sprite talkingSprite;
    public AudioSource botAudio;
    public float entranceDuration = 0.5f;
    public float exitDuration = 0.4f;
    public float volumeThreshold = 0.02f;

    [Header("Entrance Animation")]
    public float entranceStartZ = -90f;
    public float entranceEndZ = 0f;
    public float entranceStartScale = 0.3f;
    public float entranceEndScale = 1f;

    [Header("Floating Animation")]
    public float floatAmplitude = 10f;
    public float floatSpeed = 1.5f;

    private bool isFading = false;
    private Coroutine botCoroutine = null;
    private Coroutine floatCoroutine = null;
    private bool isPanelOpen = false;

    [Header("Scene Transition")]
    public CanvasGroup fadeOverlay;
    public float fadeDuration = 1f;
    private bool isTransitioning = false;

    [System.Serializable]
    public class SubtitleEntry
    {
        [Tooltip("Start time in seconds into the bot audio when this subtitle should appear.")]
        public float startTime = 0f;
        [Tooltip("How long (in seconds) the subtitle stays visible. If 0 or less it will stay until the next subtitle.")]
        public float duration = 2f;
        [TextArea]
        public string text;
    }

    public SubtitleEntry[] subtitles;

    [Header("Subtitles")]
    [Tooltip("UI Text (leave empty if using TMP)")]
    public Text subtitleText;
    [Tooltip("TextMeshPro text (leave empty if using Unity UI Text)")]
    public TMP_Text subtitleTMP;
    [Tooltip("CanvasGroup that contains the subtitle text and background (recommended)")]
    public CanvasGroup subtitleGroup;
    public float subtitleFadeDuration = 0.08f;

    [Header("Subtitle Background")]
    [Tooltip("Optional Image used as a dark rectangular background behind the subtitle text. You can size this in the inspector or use a layout.")]
    public RawImage subtitleBackground;
    [Tooltip("Background color (alpha controls darkness). If subtitleGroup is provided, its alpha will also affect this background.")]
    public Color subtitleBgColor = new Color(0f, 0f, 0f, 0.9f);

    private Coroutine subtitleCoroutine = null;

    void Start()
    {
        dataPrivacyPanel.SetActive(false);
        agreeButton.interactable = false;

        agreeToggle.onValueChanged.AddListener(OnToggleChanged);
        agreeButton.onClick.AddListener(OnAgreeClicked);
        closeButton.onClick.AddListener(OnCloseClicked);

        if (botContainer != null)
        {
            botContainer.SetActive(false);
            RectTransform rt = botContainer.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.localEulerAngles = new Vector3(0f, 0f, entranceStartZ);
                rt.localScale = Vector3.one * entranceStartScale;
            }
        }

        if (botImage != null && idleSprite != null)
            botImage.sprite = idleSprite;

        // Ensure subtitle group is hidden at start
        if (subtitleGroup != null)
            subtitleGroup.alpha = 0f;

        if (subtitleText != null)
            subtitleText.text = string.Empty;
        if (subtitleTMP != null)
            subtitleTMP.text = string.Empty;

        // Setup subtitle background if provided
        if (subtitleBackground != null)
        {
            subtitleBackground.color = new Color(subtitleBgColor.r, subtitleBgColor.g, subtitleBgColor.b, subtitleBgColor.a);
            // If there's no group, hide background at start
            if (subtitleGroup == null)
                subtitleBackground.gameObject.SetActive(false);
        }
    }

    public void ShowPanel()
    {
        dataPrivacyPanel.SetActive(true);
        isPanelOpen = true;

        if (panelVideo != null)
            panelVideo.Play();

        if (panelGroup != null)
            StartCoroutine(FadeCanvas(panelGroup, 0f, 1f, 0.3f));

        if (botCoroutine != null) StopCoroutine(botCoroutine);
        botCoroutine = StartCoroutine(ShowBotDelayed(1f));
    }

    private IEnumerator ShowBotDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        yield return StartCoroutine(BotSequence());
        botCoroutine = null;
    }

    private IEnumerator BotSequence()
    {
        if (botContainer == null || botImage == null || botAudio == null) yield break;

        botContainer.SetActive(true);
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        CanvasGroup botGroup = botContainer.GetComponent<CanvasGroup>();
        if (botGroup == null)
            botGroup = botContainer.AddComponent<CanvasGroup>();

        botGroup.alpha = 0f;
        rt.localEulerAngles = new Vector3(0f, 0f, entranceStartZ);
        rt.localScale = Vector3.one * entranceStartScale;
        botImage.sprite = idleSprite;

        yield return StartCoroutine(EntranceAnimation(rt, botGroup));

        if (floatCoroutine != null) StopCoroutine(floatCoroutine);
        floatCoroutine = StartCoroutine(FloatingMotion(rt));

        // Start playing audio and subtitles
        botAudio.Stop();
        botAudio.Play();

        if (subtitleCoroutine != null) StopCoroutine(subtitleCoroutine);
        subtitleCoroutine = StartCoroutine(SubtitleSequence());

        float[] samples = new float[512];
        while (botAudio.isPlaying && isPanelOpen)
        {
            botAudio.GetOutputData(samples, 0);
            float sum = 0f;
            for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
            float rms = Mathf.Sqrt(sum / samples.Length);

            botImage.sprite = (rms > volumeThreshold && talkingSprite != null) ? talkingSprite : idleSprite;
            yield return null;
        }

        // ensure subtitle coroutine stops
        if (subtitleCoroutine != null)
        {
            StopCoroutine(subtitleCoroutine);
            subtitleCoroutine = null;
        }

        botImage.sprite = idleSprite;

        if (floatCoroutine != null) StopCoroutine(floatCoroutine);

        yield return StartCoroutine(ExitAnimation(rt, botGroup));

        botContainer.SetActive(false);

        // hide subtitle when done
        HideSubtitleImmediate();
    }

    private IEnumerator SubtitleSequence()
    {
        if (subtitles == null || subtitles.Length == 0 || botAudio == null) yield break;

        int index = 0;
        // Keep running while audio is playing and panel is open
        while (botAudio.isPlaying && isPanelOpen && index < subtitles.Length)
        {
            SubtitleEntry entry = subtitles[index];

            // wait until we reach the entry start time
            while (botAudio.isPlaying && isPanelOpen && botAudio.time < entry.startTime - 0.01f)
                yield return null;

            if (!botAudio.isPlaying || !isPanelOpen) break;

            // show subtitle
            ShowSubtitle(entry.text);

            // Wait for duration (if duration <= 0, wait until next subtitle start)
            if (entry.duration > 0f)
            {
                float target = entry.startTime + entry.duration;
                while (botAudio.isPlaying && isPanelOpen && botAudio.time < target - 0.01f)
                    yield return null;
            }
            else
            {
                // wait until next subtitle start time or audio end
                float waitUntil = (index + 1 < subtitles.Length) ? subtitles[index + 1].startTime : botAudio.clip.length;
                while (botAudio.isPlaying && isPanelOpen && botAudio.time < waitUntil - 0.01f)
                    yield return null;
            }

            // hide subtitle and continue
            HideSubtitle();
            index++;
        }

        // make sure hidden when finished
        HideSubtitleImmediate();
        subtitleCoroutine = null;
    }

    private void ShowSubtitle(string text)
    {
        if (subtitleText != null)
            subtitleText.text = text;
        if (subtitleTMP != null)
            subtitleTMP.text = text;

        // If there's a background image but no group, make it visible
        if (subtitleBackground != null && subtitleGroup == null)
        {
            subtitleBackground.gameObject.SetActive(true);
            subtitleBackground.color = subtitleBgColor;
        }

        if (subtitleGroup != null)
        {
            StopCoroutineIfRunning(ref subtitleFadeCoroutines.show);
            StopCoroutineIfRunning(ref subtitleFadeCoroutines.hide);
            subtitleFadeCoroutines.show = StartCoroutine(FadeGroup(subtitleGroup, subtitleGroup.alpha, 1f, subtitleFadeDuration));
        }
    }

    private void HideSubtitle()
    {
        if (subtitleGroup != null)
        {
            StopCoroutineIfRunning(ref subtitleFadeCoroutines.show);
            StopCoroutineIfRunning(ref subtitleFadeCoroutines.hide);
            subtitleFadeCoroutines.hide = StartCoroutine(FadeGroup(subtitleGroup, subtitleGroup.alpha, 0f, subtitleFadeDuration));
        }
        else
        {
            // clear immediately if no group provided
            if (subtitleText != null) subtitleText.text = string.Empty;
            if (subtitleTMP != null) subtitleTMP.text = string.Empty;

            if (subtitleBackground != null)
                subtitleBackground.gameObject.SetActive(false);
        }
    }

    private void HideSubtitleImmediate()
    {
        if (subtitleGroup != null)
        {
            StopCoroutineIfRunning(ref subtitleFadeCoroutines.show);
            StopCoroutineIfRunning(ref subtitleFadeCoroutines.hide);
            subtitleGroup.alpha = 0f;
        }
        if (subtitleText != null) subtitleText.text = string.Empty;
        if (subtitleTMP != null) subtitleTMP.text = string.Empty;

        if (subtitleBackground != null)
        {
            if (subtitleGroup == null)
                subtitleBackground.gameObject.SetActive(false);
            else
                subtitleBackground.color = new Color(subtitleBgColor.r, subtitleBgColor.g, subtitleBgColor.b, subtitleBgColor.a);
        }
    }

    // small helper container to keep track of running fade coroutines so we can stop them safely
    private (Coroutine show, Coroutine hide) subtitleFadeCoroutines = (null, null);

    private void StopCoroutineIfRunning(ref Coroutine c)
    {
        if (c != null)
        {
            StopCoroutine(c);
            c = null;
        }
    }

    private IEnumerator FadeGroup(CanvasGroup g, float from, float to, float duration)
    {
        if (g == null) yield break;
        float elapsed = 0f;
        g.alpha = from;
        while (elapsed < duration)
        {
            g.alpha = Mathf.Lerp(from, to, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        g.alpha = to;
    }

    private IEnumerator EntranceAnimation(RectTransform rt, CanvasGroup group)
    {
        float elapsed = 0f;
        Vector2 startPos = rt.anchoredPosition;
        float startY = startPos.y;
        float endY = startY;

        while (elapsed < entranceDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / entranceDuration);
            float z = Mathf.Lerp(entranceStartZ, entranceEndZ, t);
            float scale = Mathf.Lerp(entranceStartScale, entranceEndScale, t);
            float alpha = Mathf.Lerp(0f, 1f, t);

            rt.localEulerAngles = new Vector3(0f, 0f, z);
            rt.localScale = Vector3.one * scale;
            group.alpha = alpha;

            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, Mathf.Lerp(startY - 5f, endY, t));

            elapsed += Time.deltaTime;
            yield return null;
        }

        rt.localEulerAngles = new Vector3(0f, 0f, entranceEndZ);
        rt.localScale = Vector3.one * entranceEndScale;
        group.alpha = 1f;
    }


    private IEnumerator ExitAnimation(RectTransform rt, CanvasGroup group)
    {
        float elapsed = 0f;
        while (elapsed < exitDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / exitDuration);
            float z = Mathf.Lerp(entranceEndZ, entranceStartZ, t);
            float scale = Mathf.Lerp(entranceEndScale, entranceStartScale, t);
            float alpha = Mathf.Lerp(1f, 0f, t);

            rt.localEulerAngles = new Vector3(0f, 0f, z);
            rt.localScale = Vector3.one * scale;
            group.alpha = alpha;

            elapsed += Time.deltaTime;
            yield return null;
        }

        rt.localEulerAngles = new Vector3(0f, 0f, entranceStartZ);
        rt.localScale = Vector3.one * entranceStartScale;
        group.alpha = 0f;
    }

    private IEnumerator FloatingMotion(RectTransform rt)
    {
        Vector2 startPos = rt.anchoredPosition;
        float startTime = Time.time;
        while (isPanelOpen)
        {
            float offset = Mathf.Sin((Time.time - startTime) * floatSpeed) * floatAmplitude;
            rt.anchoredPosition = new Vector2(startPos.x, startPos.y + offset);
            yield return null;
        }
    }


    private void OnToggleChanged(bool isOn)
    {
        agreeButton.interactable = isOn;
    }

    public void OnAgreeClicked()
    {
        if (isTransitioning) return;
        isTransitioning = true;

        StopBotIfRunning();
        StartCoroutine(FadeAndLoadScene("IntroScene"));
    }

    private IEnumerator FadeAndLoadScene(string sceneName)
    {
        fadeOverlay.gameObject.SetActive(true);
        fadeOverlay.alpha = 0f;

        // Optional: fade out background music
        var bgm = FindObjectOfType<BGMManager>();
        if (bgm != null)
            bgm.FadeOut(1.0f);

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            fadeOverlay.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        fadeOverlay.alpha = 1f;
        yield return new WaitForSeconds(0.2f);

        SceneLoader.LoadSceneWithLoading(sceneName);
    }


    private void OnCloseClicked()
    {
        if (panelVideo != null) panelVideo.Stop();
        StopBotIfRunning();

        if (panelGroup != null)
            StartCoroutine(FadeCanvas(panelGroup, 1f, 0f, 0.3f, () =>
            {
                dataPrivacyPanel.SetActive(false);
                isPanelOpen = false;
            }));

        agreeToggle.isOn = false;
        agreeButton.interactable = false;
    }

    private void StopBotIfRunning()
    {
        isPanelOpen = false;

        if (botCoroutine != null)
        {
            StopCoroutine(botCoroutine);
            botCoroutine = null;
        }

        if (floatCoroutine != null)
        {
            StopCoroutine(floatCoroutine);
            floatCoroutine = null;
        }

        if (subtitleCoroutine != null)
        {
            StopCoroutine(subtitleCoroutine);
            subtitleCoroutine = null;
        }

        if (botAudio != null && botAudio.isPlaying)
            botAudio.Stop();

        if (botContainer != null)
            botContainer.SetActive(false);

        if (botImage != null && idleSprite != null)
            botImage.sprite = idleSprite;

        HideSubtitleImmediate();
    }

    private IEnumerator FadeCanvas(CanvasGroup group, float from, float to, float duration, System.Action onComplete = null)
    {
        if (isFading) yield break;
        isFading = true;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            group.alpha = Mathf.Lerp(from, to, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        group.alpha = to;
        isFading = false;
        onComplete?.Invoke();
    }
}