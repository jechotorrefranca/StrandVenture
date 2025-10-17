using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class AptitudeBotController : MonoBehaviour
{
    [Header("Bot Settings")]
    public GameObject botContainer;
    public Image botImage;
    public Sprite idleSprite;
    public Sprite talkingSprite;
    public AudioSource botAudio;
    public AudioClip botDialogueClip; // 🎧 Personalized MP3
    public float entranceDuration = 1f;
    public float floatAmplitude = 10f;
    public float floatSpeed = 1.5f;
    public float volumeThreshold = 0.02f;

    [Header("Exam UI")]
    public GameObject examCanvas;


    private Vector2 botOriginalPos;
    private Coroutine floatCoroutine;

    void Start()
    {
        // Hide bot and exam at start
        botContainer.SetActive(false);
        examCanvas.SetActive(false);


        botOriginalPos = botContainer.GetComponent<RectTransform>().anchoredPosition;
        var rt = botContainer.GetComponent<RectTransform>();
        rt.anchoredPosition = botOriginalPos + new Vector2(0, -600);

        StartCoroutine(SceneSequence());
    }

    private IEnumerator SceneSequence()
    {
        yield return new WaitForSeconds(0.5f); // short delay before showing bot

        // 🟢 Bot Entrance
        botContainer.SetActive(true);
        yield return StartCoroutine(BotEntranceAnimation());

        // 🗣️ Play local MP3 with reactive talk animation
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

        // ☁️ Exit Upward
        yield return StartCoroutine(BotExitUpward());

        // 🎓 Show Exam UI
        // 🎓 Show Exam UI with fade animation
        yield return new WaitForSeconds(0.3f);
        yield return StartCoroutine(ShowExamCanvas());
        floatCoroutine = StartCoroutine(BotFloatingMotion());

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
