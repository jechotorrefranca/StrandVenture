using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using System.Collections;

public class UserInfoSceneController : MonoBehaviour
{
    [Header("Scene Elements")]
    public CanvasGroup fadeOverlay;
    public VideoPlayer backgroundVideo;

    [Header("Bot Settings")]
    public GameObject botContainer;
    public Image botImage;
    public Sprite idleSprite;
    public Sprite talkingSprite;
    public AudioSource botAudio;
    public float entranceDuration = 1f;
    public float floatAmplitude = 10f;
    public float floatSpeed = 1.5f;
    public float volumeThreshold = 0.02f;

    [Header("Textbox UI")]
    public GameObject textboxContainer;
    public Button continueButton;
    public float textboxMoveDuration = 0.7f;

    private Vector2 botOriginalPos;
    private Vector2 textboxOriginalPos;
    private Coroutine floatCoroutine;

    void Start()
    {

        var bgm = FindObjectOfType<BGMManager>();
        if (bgm != null)
            bgm.FadeIn(1f, 0.055f); // fade in smoothly to 80% volume


        // Initialize
        fadeOverlay.alpha = 1f;
        botContainer.SetActive(false);
        textboxContainer.SetActive(false);

        botOriginalPos = botContainer.GetComponent<RectTransform>().anchoredPosition;
        textboxOriginalPos = textboxContainer.GetComponent<RectTransform>().anchoredPosition;

        // Move them below screen
        var botRT = botContainer.GetComponent<RectTransform>();
        var textRT = textboxContainer.GetComponent<RectTransform>();
        botRT.anchoredPosition = botOriginalPos + new Vector2(0, -600);
        textRT.anchoredPosition = textboxOriginalPos + new Vector2(0, -600);

        // Start sequence
        StartCoroutine(SceneSequence());
    }

    private IEnumerator SceneSequence()
    {
        // Fade from black to scene
        // Hold black screen first
        yield return new WaitForSeconds(1f);

        // Then fade from black to scene
        yield return StartCoroutine(FadeCanvas(fadeOverlay, 1f, 0f, 1f));


        // Play background video
        yield return new WaitForSeconds(0.3f);
        if (backgroundVideo != null) backgroundVideo.Play();

        // Wait then show bot
        yield return new WaitForSeconds(0.5f);
        botContainer.SetActive(true);
        yield return StartCoroutine(BotEntranceAnimation());

        // Bot speaks
        botAudio.Play();
        yield return StartCoroutine(BotTalkAnimation());

        // Bot exits downward
        yield return StartCoroutine(BotExitDownward());

        yield return new WaitForSeconds(0.3f);

        // Wait briefly, then bring bot + textbox together
        textboxContainer.SetActive(true);
        yield return StartCoroutine(BotAndTextboxRiseTogether());

        // Start floating motion
        floatCoroutine = StartCoroutine(BotFloatingMotion());
    }


    private IEnumerator FadeCanvas(CanvasGroup group, float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            group.alpha = Mathf.Lerp(from, to, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        group.alpha = to;
    }

    private IEnumerator BotEntranceAnimation()
    {
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        Vector2 startPos = botOriginalPos + new Vector2(0, -360);
        Vector2 endPos = botOriginalPos;

        float elapsed = 0f;
        while (elapsed < entranceDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / entranceDuration);

            // Smooth position
            rt.anchoredPosition = Vector2.Lerp(startPos, endPos, t);

            // Full 360° spin with easing
            float spin = Mathf.Lerp(0f, 360f, Mathf.SmoothStep(0f, 1f, t));

            rt.localEulerAngles = new Vector3(0, 0, spin);

            // Smooth scale from small to full
            float scale = Mathf.Lerp(0f, 1f, t);
            rt.localScale = new Vector3(scale, scale, 1f);

            elapsed += Time.deltaTime;
            yield return null;
        }

        rt.anchoredPosition = endPos;
        rt.localEulerAngles = Vector3.zero;
        rt.localScale = Vector3.one;
    }


    private IEnumerator BotTalkAnimation()
    {
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        Vector2 basePos = botOriginalPos;

        float[] samples = new float[512];
        float floatTime = 0f;
        float talkFloatSpeed = 2f;   // faster oscillation for talking
        float talkFloatAmplitude = 6f; // small up/down motion

        while (botAudio.isPlaying)
        {
            // Audio-based sprite switching
            botAudio.GetOutputData(samples, 0);
            float sum = 0f;
            for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
            float rms = Mathf.Sqrt(sum / samples.Length);

            botImage.sprite = (rms > volumeThreshold) ? talkingSprite : idleSprite;

            // Add gentle floating while talking
            float offsetY = Mathf.Sin(floatTime * talkFloatSpeed) * talkFloatAmplitude;
            rt.anchoredPosition = basePos + new Vector2(0, offsetY);

            floatTime += Time.deltaTime;
            yield return null;
        }

        // Reset to normal position and sprite after talking
        rt.anchoredPosition = basePos;
        botImage.sprite = idleSprite;
    }


    private IEnumerator BotExitDownward()
    {
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        Vector2 startPos = rt.anchoredPosition;
        Vector2 endPos = startPos + new Vector2(0, -800); // move further down
        float duration = 0.6f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            rt.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
            rt.localEulerAngles = new Vector3(0, 0, Mathf.Lerp(0f, 15f, t));
            elapsed += Time.deltaTime;
            yield return null;
        }

        rt.anchoredPosition = endPos;
    }


    private IEnumerator BotAndTextboxRiseTogether()
    {
        RectTransform botRT = botContainer.GetComponent<RectTransform>();
        RectTransform textRT = textboxContainer.GetComponent<RectTransform>();

        Vector2 botStart = botOriginalPos + new Vector2(0, -800);
        Vector2 botEnd = botOriginalPos + new Vector2(0, 100);
        Vector2 textStart = textboxOriginalPos + new Vector2(0, -800);
        Vector2 textEnd = textboxOriginalPos;

        botRT.anchoredPosition = botStart;
        textRT.anchoredPosition = textStart;

        float duration = 1.1f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            // Smoother easing
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);

            // Gentle rise
            botRT.anchoredPosition = Vector2.Lerp(botStart, botEnd, t);
            textRT.anchoredPosition = Vector2.Lerp(textStart, textEnd, t);

            // Replace jerky rotation with a tiny wobble
            float wobble = Mathf.Sin(t * Mathf.PI * 2f) * 2f; // subtle movement
            botRT.localEulerAngles = new Vector3(0, 0, wobble);

            elapsed += Time.deltaTime;
            yield return null;
        }

        botRT.anchoredPosition = botEnd;
        textRT.anchoredPosition = textEnd;
        botRT.localEulerAngles = Vector3.zero;
    }




    private IEnumerator MoveBotUpSlightly()
    {
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        Vector2 startPos = rt.anchoredPosition;
        Vector2 endPos = startPos + new Vector2(0, 100);
        float elapsed = 0f;

        while (elapsed < 0.8f)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / 0.8f);
            rt.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator SlideUpTextbox()
    {
        RectTransform rt = textboxContainer.GetComponent<RectTransform>();
        Vector2 startPos = textboxOriginalPos + new Vector2(0, -600);
        Vector2 endPos = textboxOriginalPos;
        float elapsed = 0f;

        while (elapsed < textboxMoveDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / textboxMoveDuration);
            rt.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        rt.anchoredPosition = endPos;
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
}
