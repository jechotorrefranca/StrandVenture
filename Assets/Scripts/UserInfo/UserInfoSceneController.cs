using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using System.Collections;
using TMPro;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;

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
    public AudioClip botGoodbyeClip;
    public float entranceDuration = 1f;
    public float floatAmplitude = 10f;
    public float floatSpeed = 1.5f;
    public float volumeThreshold = 0.02f;

    [Header("Textbox UI")]
    public GameObject textboxContainer;
    public Button continueButton;
    public float textboxMoveDuration = 0.7f;

    [Header("User Info UI")]
    public TMP_InputField nameInput;
    public TMP_Dropdown sectionDropdown;
    public Button saveButton;
    public Color saveButtonPressedColor = new Color(0.2f, 0.2f, 0.2f);
    private Color saveButtonOriginalColor;

    private Vector2 botOriginalPos;
    private Vector2 textboxOriginalPos;
    private Coroutine floatCoroutine;

    private bool isNameValid = false;
    private bool isSectionValid = false;


    void Start()
    {
        fadeOverlay.blocksRaycasts = true; // Default for fade-in


        var bgm = FindObjectOfType<BGMManager>();
        if (bgm != null)
            bgm.FadeIn(1f, 0.055f);

        fadeOverlay.alpha = 1f;
        botContainer.SetActive(false);
        textboxContainer.SetActive(false);

        botOriginalPos = botContainer.GetComponent<RectTransform>().anchoredPosition;
        textboxOriginalPos = textboxContainer.GetComponent<RectTransform>().anchoredPosition;

        var botRT = botContainer.GetComponent<RectTransform>();
        var textRT = textboxContainer.GetComponent<RectTransform>();
        botRT.anchoredPosition = botOriginalPos + new Vector2(0, -600);
        textRT.anchoredPosition = textboxOriginalPos + new Vector2(0, -600);

        StartCoroutine(SceneSequence());

        saveButton.interactable = false;

        nameInput.onValueChanged.AddListener(delegate { ValidateForm(); });
        sectionDropdown.onValueChanged.AddListener(delegate { ValidateForm(); });

        saveButtonOriginalColor = saveButton.image.color;

        saveButton.onClick.AddListener(OnSaveButtonClicked);

    }
    private void ValidateForm()
    {
        string playerName = nameInput.text.Trim();
        string section = sectionDropdown.options[sectionDropdown.value].text;

        isNameValid = !string.IsNullOrEmpty(playerName);
        isSectionValid = section != "Select your section";

        saveButton.interactable = isNameValid && isSectionValid;
    }

    private IEnumerator SceneSequence()
    {

        yield return new WaitForSeconds(1f);

        yield return StartCoroutine(FadeCanvas(fadeOverlay, 1f, 0f, 1f));

        yield return new WaitForSeconds(0.3f);
        if (backgroundVideo != null) backgroundVideo.Play();

        yield return new WaitForSeconds(0.5f);
        botContainer.SetActive(true);
        yield return StartCoroutine(BotEntranceAnimation());

        botAudio.Play();
        yield return StartCoroutine(BotTalkAnimation());

        yield return StartCoroutine(BotExitDownward());

        yield return new WaitForSeconds(0.3f);

        textboxContainer.SetActive(true);
        yield return StartCoroutine(BotAndTextboxRiseTogether());

        floatCoroutine = StartCoroutine(BotFloatingMotion());
    }

    private void OnSaveButtonClicked()
    {
        string playerName = nameInput.text.Trim();
        string section = sectionDropdown.options[sectionDropdown.value].text;

        if (string.IsNullOrEmpty(playerName) || section == "Select your section")
        {
            Debug.LogWarning("Invalid input — cannot continue!");
            return;
        }

        PlayerPrefs.SetString("PlayerName", playerName);
        PlayerPrefs.SetString("PlayerSection", section);
        PlayerPrefs.Save();

        Debug.Log($"Saved: Name={playerName}, Section={section}");

        StartCoroutine(GetGroqNickname(playerName)); // 👈 Fetch nickname using Gro

        saveButton.interactable = false;

        StartCoroutine(SaveSequenceAfterClick());

    }

    private IEnumerator GetGroqNickname(string fullName)
    {
        string apiKey = "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";  // replace with your working key
        string url = "https://api.groq.com/openai/v1/chat/completions";

        string prompt = $"From this full name: '{fullName}', return only the most natural first name or nickname that a friend would use, don't change the name. ";

        ChatRequest chatRequest = new ChatRequest
        {
            model = "openai/gpt-oss-120b",
            messages = new List<Message>
        {
            new Message { role = "system", content = "You are a precise name extractor that outputs only first names or nicknames." },
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
            }
            else
            {
                Debug.Log("Groq Nickname Raw Response: " + request.downloadHandler.text);

                ChatResponse response = JsonUtility.FromJson<ChatResponse>(request.downloadHandler.text);
                if (response != null && response.choices.Length > 0)
                {
                    string nickname = response.choices[0].message.content.Trim();

                    PlayerPrefs.SetString("PlayerNickname", nickname);
                    PlayerPrefs.Save();

                    Debug.Log("Saved Nickname: " + nickname);
                }
            }
        }
    }

    private IEnumerator PlayGroqTTS(string text)
    {
        string apiKey = "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";
        string url = "https://api.groq.com/openai/v1/audio/speech";

        SpeechRequest payload = new SpeechRequest
        {
            model = "playai-tts",
            voice = "Chip-PlayAI",
            input = text,
            response_format = "wav"
        };

        string json = JsonUtility.ToJson(payload);
        Debug.Log("➡️ Sending TTS JSON: " + json);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "audio/wav");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("❌ TTS Error: " + request.error);
                Debug.LogError("Response: " + request.downloadHandler.text);
            }
            else
            {
                Debug.Log("✅ TTS Request Successful! Bytes: " + request.downloadHandler.data.Length);
                byte[] audioData = request.downloadHandler.data;

                if (audioData == null || audioData.Length == 0)
                {
                    Debug.LogError("⚠️ Empty audio response.");
                    yield break;
                }

                AudioClip clip = WavUtility.ToAudioClip(audioData, 0, "GroqTTSClip");
                if (clip != null)
                {
                    botAudio.clip = clip;
                    botAudio.Play();
                    yield return StartCoroutine(BotTalkAnimation());
                }
                else
                {
                    Debug.LogError("Failed to decode audio clip.");
                }
            }
        }
    }


    private IEnumerator SaveSequenceAfterClick()
    {
        yield return StartCoroutine(ButtonClickEffect());

        if (floatCoroutine != null)
        {
            StopCoroutine(floatCoroutine);
            floatCoroutine = null;
        }

        yield return StartCoroutine(BotAndTextboxExitTogether());

        yield return new WaitForSeconds(0.3f);

        RectTransform botRT = botContainer.GetComponent<RectTransform>();

        Vector2 startPos = botRT.anchoredPosition;
        Vector2 endPos = botOriginalPos;
        Vector2 overshootPos = endPos + new Vector2(0, 60);

        float durationUp = 1.2f;
        float durationSettle = 0.4f;
        float elapsed = 0f;

        while (elapsed < durationUp)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / durationUp);
            botRT.anchoredPosition = Vector2.Lerp(startPos, overshootPos, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < durationSettle)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / durationSettle);
            botRT.anchoredPosition = Vector2.Lerp(overshootPos, endPos, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        botRT.anchoredPosition = endPos;

        // 🔊 Goodbye message with nickname (Groq TTS)
        string nickname = PlayerPrefs.GetString("PlayerNickname", "friend");
        yield return StartCoroutine(PlayGroqTTS($"{nickname}! what a nice name, we'll now start your strand venture experience!"));


        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(FadeCanvas(fadeOverlay, 0f, 1f, 1f));

        Debug.Log("Fade complete — ready for next scene transition.");
    }

    private IEnumerator BotAndTextboxExitTogether()
    {
        RectTransform botRT = botContainer.GetComponent<RectTransform>();
        RectTransform textRT = textboxContainer.GetComponent<RectTransform>();

        Vector2 botStart = botRT.anchoredPosition;
        Vector2 textStart = textRT.anchoredPosition;

        Vector2 botEnd = botStart + new Vector2(0, -1100);
        Vector2 textEnd = textStart + new Vector2(0, -1000);

        float duration = 1.0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            botRT.anchoredPosition = Vector2.Lerp(botStart, botEnd, t);
            textRT.anchoredPosition = Vector2.Lerp(textStart, textEnd, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        botRT.anchoredPosition = botEnd;
        textRT.anchoredPosition = textEnd;

        textboxContainer.SetActive(false);
    }

    private IEnumerator ButtonClickEffect()
    {
        saveButton.image.color = saveButtonPressedColor;
        yield return new WaitForSeconds(0.15f);
        saveButton.image.color = saveButtonOriginalColor;
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

            float spin = Mathf.Lerp(0f, 360f, Mathf.SmoothStep(0f, 1f, t));

            rt.localEulerAngles = new Vector3(0, 0, spin);

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
        float talkFloatSpeed = 2f;
        float talkFloatAmplitude = 6f;

        while (botAudio.isPlaying)
        {

            botAudio.GetOutputData(samples, 0);
            float sum = 0f;
            for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
            float rms = Mathf.Sqrt(sum / samples.Length);

            botImage.sprite = (rms > volumeThreshold) ? talkingSprite : idleSprite;

            float offsetY = Mathf.Sin(floatTime * talkFloatSpeed) * talkFloatAmplitude;
            rt.anchoredPosition = basePos + new Vector2(0, offsetY);

            floatTime += Time.deltaTime;
            yield return null;
        }

        rt.anchoredPosition = basePos;
        botImage.sprite = idleSprite;
    }

    private IEnumerator BotExitDownward()
    {
        RectTransform rt = botContainer.GetComponent<RectTransform>();
        Vector2 startPos = rt.anchoredPosition;
        Vector2 endPos = startPos + new Vector2(0, -800);
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
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);

            botRT.anchoredPosition = Vector2.Lerp(botStart, botEnd, t);
            textRT.anchoredPosition = Vector2.Lerp(textStart, textEnd, t);

            float wobble = Mathf.Sin(t * Mathf.PI * 2f) * 2f;
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
    public class SpeechRequest
    {
        public string model;
        public string voice;
        public string input;
        public string response_format;
    }
}