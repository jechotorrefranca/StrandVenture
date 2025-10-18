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
    public AudioClip botDialogueClip;
    public float entranceDuration = 1f;
    public float floatAmplitude = 10f;
    public float floatSpeed = 1.5f;
    public float volumeThreshold = 0.02f;

    [Header("Outro Settings")]
    public AudioClip examCompleteClip;
    public GameObject fadeOverlay;
    public string nextSceneName = "AptitudeResultScene";

    [Header("Exam UI")]
    public GameObject examCanvas;
    public GameObject startPanel;
    public Button startButton;

    private Vector2 botOriginalPos;
    private Coroutine floatCoroutine;

    void Start()
    {
        botContainer.SetActive(false);
        examCanvas.SetActive(false);

        botOriginalPos = botContainer.GetComponent<RectTransform>().anchoredPosition;
        var rt = botContainer.GetComponent<RectTransform>();
        rt.anchoredPosition = botOriginalPos + new Vector2(0, -600);

        StartCoroutine(SceneSequence());
    }

    private IEnumerator SceneSequence()
    {
        yield return new WaitForSeconds(0.5f);

        botContainer.SetActive(true);
        yield return StartCoroutine(BotEntranceAnimation());

        if (botDialogueClip != null)
        {
            botAudio.clip = botDialogueClip;
            botAudio.Play();
            yield return StartCoroutine(BotTalkAnimation());
        }
        else
        {
            Debug.LogWarning("⚠️ Missing botDialogueClip! Please assign your MP3 file in the Inspector.");
        }

        yield return new WaitForSeconds(0.5f);

        yield return StartCoroutine(BotExitUpward());

        yield return new WaitForSeconds(0.3f);
        yield return StartCoroutine(ShowExamCanvas());

        floatCoroutine = StartCoroutine(BotFloatingMotion());
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

        if (examCompleteClip != null)
        {
            botAudio.clip = examCompleteClip;
            botAudio.Play();
            yield return StartCoroutine(BotTalkAnimation());
        }
        else
        {
            Debug.LogWarning("⚠️ Missing examCompleteClip!");
            yield return new WaitForSeconds(2f);
        }

        if (fadeOverlay != null)
            yield return StartCoroutine(FadeOverlayAndLoadScene());
        else
            Debug.LogWarning("⚠️ Missing fadeOverlay! Scene will not transition.");
    }

    private IEnumerator FadeOverlayAndLoadScene()
    {
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
        SceneManager.LoadScene(nextSceneName);
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

        while (botAudio.isPlaying)
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
}