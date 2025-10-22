using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Video;
using UnityEngine.SceneManagement;
using System.Collections;

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
    public Button botButton;
    public float floatAmplitude = 10f;
    public float floatSpeed = 1.5f;
    public float volumeThreshold = 0.02f;

    [Header("Result UI")]
    public TMP_Text bestStrandText;
    public GameObject pieGraph; // Replace with actual PieGraph script or UI container
    public Button continueButton;

    private Vector2 botOriginalPos;
    private Coroutine floatCoroutine;

    void Start()
    {
        // Fade in overlay
        fadeOverlay.alpha = 1f;
        fadeOverlay.blocksRaycasts = true;
        StartCoroutine(FadeCanvas(fadeOverlay, 1f, 0f, 1f));

        // Start video background
        if (backgroundVideo != null) backgroundVideo.Play();

        // Initialize bot
        botOriginalPos = botContainer.GetComponent<RectTransform>().anchoredPosition;
        botContainer.SetActive(true);
        botImage.sprite = idleSprite;

        botButton.onClick.AddListener(OnBotButtonClicked);

        // Display best strand
        string bestStrand = PlayerPrefs.GetString("BestStrand", "Unknown");
        float bestScore = PlayerPrefs.GetFloat("BestScore", 0f);
        bestStrandText.text = $"Your best strand is: {bestStrand} ({bestScore:F1}%)";

        // Initialize pie graph
        SetupPieGraph(bestStrand, bestScore);

        // Continue button
        continueButton.onClick.AddListener(OnContinueClicked);

        // Start floating animation
        floatCoroutine = StartCoroutine(BotFloatingMotion());
    }

    private void SetupPieGraph(string bestStrand, float bestScore)
    {
        // If you are using a pie chart script, you can feed it data here
        // Example pseudo-code:
        // PieChart chart = pieGraph.GetComponent<PieChart>();
        // chart.SetData(new string[] { bestStrand, "Other" }, new float[] { bestScore, 100 - bestScore });
    }

    private void OnBotButtonClicked()
    {
        string bestStrand = PlayerPrefs.GetString("BestStrand", "Unknown");
        string stats = PlayerPrefs.GetString($"{bestStrand}_Stats", "No data available");

        // Example: TTS or display text on bot speech bubble
        StartCoroutine(PlayBotSpeech(stats));
    }

    private IEnumerator PlayBotSpeech(string text)
    {
        // Optionally use TTS here
        // For now, just log
        Debug.Log("Bot says: " + text);

        // Animate talking
        botAudio.Play(); // Optional audio
        yield return StartCoroutine(BotTalkAnimation(2f)); // 2s duration placeholder
    }

    private IEnumerator BotTalkAnimation(float duration)
    {
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        Vector2 basePos = botOriginalPos;
        float time = 0f;

        while (time < duration)
        {
            // Simple idle/talking swap animation
            botImage.sprite = (Mathf.Sin(time * 20f) > 0) ? talkingSprite : idleSprite;

            // Floating
            float offsetY = Mathf.Sin(time * 3f) * 5f;
            rt.anchoredPosition = basePos + new Vector2(0, offsetY);

            time += Time.deltaTime;
            yield return null;
        }

        botImage.sprite = idleSprite;
        rt.anchoredPosition = basePos;
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

    private void OnContinueClicked()
    {
        StartCoroutine(FadeAndLoadNextScene("ChooseStrand")); // Replace with your next scene name
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
}
