using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using System.Collections;

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

        botAudio.Stop();
        botAudio.Play();

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

        botImage.sprite = idleSprite;

        if (floatCoroutine != null) StopCoroutine(floatCoroutine);

        yield return StartCoroutine(ExitAnimation(rt, botGroup));

        botContainer.SetActive(false);
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

    private void OnAgreeClicked()
    {
        StopBotIfRunning();
        StartCoroutine(FadeAndLoadScene("UserInfoScene"));
    }

    private IEnumerator FadeAndLoadScene(string sceneName)
    {
        fadeOverlay.gameObject.SetActive(true);
        fadeOverlay.alpha = 0f;

        // Optional: fade out the music slightly before transition
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

        SceneManager.LoadScene(sceneName);
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

        if (botAudio != null && botAudio.isPlaying)
            botAudio.Stop();

        if (botContainer != null)
            botContainer.SetActive(false);

        if (botImage != null && idleSprite != null)
            botImage.sprite = idleSprite;
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
