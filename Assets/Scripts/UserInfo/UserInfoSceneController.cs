using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.IO;
using UnityEngine.Video;
using System.Collections;
using TMPro;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;
using System.Diagnostics;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

[System.Serializable]
public class GroqConfig
{
    public string api_key;
}

public class UserInfoSceneController : MonoBehaviour
{
    private string apiKey;

    [Header("Scene Elements")]
    public CanvasGroup fadeOverlay;
    public VideoPlayer backgroundVideo;

    [Header("Bot Settings")]
    public GameObject botContainer;
    public Image botImage;
    public Sprite idleSprite;
    public Sprite talkingSprite;
    public AudioSource botAudio;
    public AudioWithSubtitles botGoodbyeAudio; // optional goodbye audio + subtitles
    public float entranceDuration = 1f;
    public float floatAmplitude = 10f;
    public float floatSpeed = 1.5f;
    public float volumeThreshold = 0.02f;

    [Header("Piper TTS Settings")]
    public string voiceName = "en_US-hfc_male-medium";
    [Range(0f, 1f)]
    public float ttsVolume = 0.5f;
    private string piperPath;
    private string voicesDir;

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

    [Header("Extra Audio Clips (with subtitles)")]
    public AudioWithSubtitles errorAudio;     // audio + subtitle segments for error
    public AudioWithSubtitles getReadyAudio;  // audio + subtitle segments for get-ready line

    // ----------------------------------------------------------------------------
    // Subtitle data types (array per audio, background color per segment)
    [System.Serializable]
    public class SubtitleSegment
    {
        [Tooltip("Timestamp in seconds when this subtitle should appear (relative to its audio clip start)")]
        public float timestamp;

        [TextArea(1, 4)]
        [Tooltip("Subtitle text for this segment")]
        public string text;

        [Tooltip("Duration in seconds for this subtitle. 0 = auto (until next segment or clip end)")]
        public float duration;

        [Tooltip("Background color for this subtitle segment")]
        public Color backgroundColor = new Color(0f, 0f, 0f, 0.8f);
    }

    [System.Serializable]
    public class AudioWithSubtitles
    {
        [Tooltip("Audio clip to play")]
        public AudioClip clip;

        [Tooltip("Subtitle segments for this clip (can be multiple)")]
        public SubtitleSegment[] segments;
    }
    // ----------------------------------------------------------------------------

    [Header("Final Settings")]
    [Tooltip("Duration of fade to black")]
    public float fadeOutDuration = 2f;

    [Tooltip("Duration of video fade in/out")]
    public float videoFadeDuration = 0.5f;

    private Texture2D fadeTexture;
    private float fadeAlpha = 0f;

    private Vector3 botBasePosition;
    private float floatTimer = 0f;

    private Animation botAnimator;

    // single subtitle coroutine handle (used for both segmented and one-shot subtitles)
    private Coroutine subtitleCoroutine = null;
    private Coroutine currentVoiceCoroutine = null;

    // Subtitles UI (shared background used for all segments; color changed per segment)
    [Header("Subtitle UI")]
    public GameObject subtitlePanel;      // panel that contains subtitle UI (assign in Inspector)
    public Image subtitleBackground;      // shared background image for subtitles (assign in Inspector)
    public TMP_Text subtitleText;         // TextMeshPro text for subtitle

    // private helpers
    private Coroutine botTalkCoroutine = null;

    void Start()
    {
        fadeTexture = new Texture2D(1, 1);
        fadeTexture.SetPixel(0, 0, Color.black);
        fadeTexture.Apply();

        // Ensure cursor is visible & unlocked when this scene starts
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        StartCoroutine(DelayedStuff());
        LoadGroqApiKey();
        InitializePiper();

        fadeOverlay.blocksRaycasts = true;

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

        // Ensure subtitle panel hidden at start and background active (so color toggles work)
        if (subtitlePanel != null)
            subtitlePanel.SetActive(false);
        if (subtitleBackground != null)
        {
            subtitleBackground.enabled = false;
            subtitleBackground.gameObject.SetActive(false);
        }
    }

    IEnumerator DelayedStuff()
    {
        yield return new WaitForSeconds(0.5f);
        // Placeholder for other delayed logic
    }

    private void InitializePiper()
    {
        piperPath = Path.Combine(Application.streamingAssetsPath, "piper/piper.exe");
        voicesDir = Path.Combine(Application.streamingAssetsPath, "piper/voices");
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

    private void ValidateForm()
    {
        string playerName = nameInput.text.Trim();
        string section = sectionDropdown.options[sectionDropdown.value].text;

        isNameValid = !string.IsNullOrEmpty(playerName);
        isSectionValid = section != "Select your section";

        saveButton.interactable = isNameValid && isSectionValid;
    }

    // Keep cursor unlocked/visible each frame to counteract previous scene lock
    void Update()
    {
        if (Cursor.lockState != CursorLockMode.None)
            Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible)
            Cursor.visible = true;
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

        // If botGoodbyeAudio is assigned and contains an initial clip, play it with subtitles
        if (botGoodbyeAudio != null && botGoodbyeAudio.clip != null)
        {
            yield return StartCoroutine(PlayAudioWithSubtitles(botGoodbyeAudio));
        }
        else
        {
            // Fallback: if botAudio.clip already assigned, play and animate
            if (botAudio.clip != null)
            {
                botAudio.Play();
                yield return StartCoroutine(BotTalkAnimation());
            }
        }

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

        StartCoroutine(GetGroqNickname(playerName));

        saveButton.interactable = false;

        StartCoroutine(SaveSequenceAfterClick());
    }

    private IEnumerator GetGroqNickname(string fullName)
    {
        string url = "https://api.groq.com/openai/v1/chat/completions";

        string prompt = $"From this full name: '{fullName}', return only the most natural first name or nickname that a friend would use, don't change the name. In case of people having 2 first name like john adrian, return what would be the most common people would call them, like adrian.";

        // Build request body in code (no inspector hierarchy)
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

                // Attempt to parse minimal ChatResponse
                ChatResponse response = JsonUtility.FromJson<ChatResponse>(request.downloadHandler.text);
                if (response != null && response.choices != null && response.choices.Length > 0 && response.choices[0].message != null)
                {
                    string nickname = response.choices[0].message.content.Trim();

                    PlayerPrefs.SetString("PlayerNickname", nickname);
                    PlayerPrefs.Save();

                    Debug.Log("Saved Nickname: " + nickname);
                }
                else
                {
                    Debug.LogWarning("Groq response parsing failed or no choices.");
                }
            }
        }
    }

    private IEnumerator PlayPiperTTS(string text, System.Action<bool> onComplete)
    {
        string outputPath = Path.Combine(Application.persistentDataPath, "piper_output.wav");

        // Generate audio on background thread
        Task<bool> generateTask = Task.Run(() => GeneratePiperAudio(text, outputPath));

        // Wait for generation to complete without blocking
        while (!generateTask.IsCompleted)
        {
            yield return null;
        }

        if (!generateTask.Result)
        {
            Debug.LogError("❌ Failed to generate TTS audio!");
            onComplete?.Invoke(false);
            yield break;
        }

        // Load the audio file
        using (var www = new WWW("file://" + outputPath))
        {
            yield return www;
            var clip = www.GetAudioClip(false, false, AudioType.WAV);
            if (clip != null)
            {
                float originalVolume = botAudio.volume;
                botAudio.volume = ttsVolume;
                botAudio.clip = clip;
                botAudio.Play();

                // show subtitle one-shot for the generated TTS
                // use a neutral background color if you want (here we use semi-opaque black)
                Color bg = new Color(0f, 0f, 0f, 0.8f);
                // stop any existing subtitle coroutine and start one-shot
                if (subtitleCoroutine != null)
                {
                    StopCoroutine(subtitleCoroutine);
                    subtitleCoroutine = null;
                }
                subtitleCoroutine = StartCoroutine(SubtitleOneShotRoutine(text, clip.length, bg));

                Debug.Log($"✅ Playing Piper TTS: {voiceName} at volume {ttsVolume}");
                yield return StartCoroutine(BotTalkAnimation());

                // ensure subtitle coroutine is cleared (in case it still running)
                if (subtitleCoroutine != null)
                {
                    StopCoroutine(subtitleCoroutine);
                    subtitleCoroutine = null;
                    if (subtitlePanel != null) subtitlePanel.SetActive(false);
                    if (subtitleBackground != null) { subtitleBackground.enabled = false; subtitleBackground.gameObject.SetActive(false); }
                }

                botAudio.volume = originalVolume;
                onComplete?.Invoke(true);
            }
            else
            {
                Debug.LogError("❌ Failed to load WAV!");
                onComplete?.Invoke(false);
            }
        }
    }

    private bool GeneratePiperAudio(string text, string outputPath)
    {
        try
        {
            string modelPath = Path.Combine(voicesDir, voiceName + ".onnx");

            if (!File.Exists(modelPath))
            {
                Debug.LogError($"❌ Voice not found: {voiceName}");
                return false;
            }

            if (File.Exists(outputPath)) File.Delete(outputPath);

            var psi = new ProcessStartInfo
            {
                FileName = piperPath,
                Arguments = $"--model \"{modelPath}\" --output_file \"{outputPath}\" --output_format wav",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(piperPath)
            };

            using (var process = Process.Start(psi))
            {
                process.StandardInput.WriteLine(text);
                process.StandardInput.Close();
                process.WaitForExit();

                string stderr = process.StandardError.ReadToEnd();
                if (!string.IsNullOrEmpty(stderr)) Debug.LogWarning($"[Piper] {stderr}");
            }

            // Wait for file to be written
            int maxAttempts = 50;
            int attempts = 0;
            while (!File.Exists(outputPath) && attempts < maxAttempts)
            {
                System.Threading.Thread.Sleep(100);
                attempts++;
            }

            return File.Exists(outputPath);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Piper generation error: {e.Message}");
            return false;
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

        string nickname = PlayerPrefs.GetString("PlayerNickname", "friend");
        bool ttsSuccess = false;

        string ttsLine = $"Awesome name, {nickname}!";
        yield return StartCoroutine(PlayPiperTTS(ttsLine, success => ttsSuccess = success));

        if (!ttsSuccess)
        {
            // play errorAudio (AudioWithSubtitles) if set
            if (errorAudio != null && errorAudio.clip != null)
            {
                yield return StartCoroutine(PlayAudioWithSubtitles(errorAudio));
            }
        }
        else
        {
            yield return new WaitForSeconds(0.3f);
        }

        if (getReadyAudio != null && getReadyAudio.clip != null)
        {
            yield return StartCoroutine(PlayAudioWithSubtitles(getReadyAudio));
        }

        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(FadeCanvas(fadeOverlay, 0f, 1f, 1f));

        SceneLoader.LoadSceneWithLoading("ExamScene");
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
        Vector2 basePos = rt.anchoredPosition;

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

    // ---------------------
    // Play audio (regular clip) with multiple subtitle segments
    // ---------------------
    private IEnumerator PlayAudioWithSubtitles(AudioWithSubtitles aws)
    {
        if (aws == null || aws.clip == null)
            yield break;

        // Stop any existing voice coroutine
        if (currentVoiceCoroutine != null)
        {
            StopCoroutine(currentVoiceCoroutine);
            currentVoiceCoroutine = null;
        }

        // Stop previous audio/subtitle
        if (botAudio.isPlaying)
            botAudio.Stop();

        StopSubtitleSequence();

        botAudio.clip = aws.clip;
        botAudio.Play();

        // Start bot talk animation coroutine
        botTalkCoroutine = StartCoroutine(BotTalkAnimation());

        // Start subtitle segments if any
        if (aws.segments != null && aws.segments.Length > 0)
        {
            if (subtitleCoroutine != null)
            {
                StopCoroutine(subtitleCoroutine);
                subtitleCoroutine = null;
            }
            subtitleCoroutine = StartCoroutine(SubtitleSequenceCoroutine(aws.clip, aws.segments));
        }

        // Wait for clip end
        while (botAudio != null && botAudio.isPlaying)
            yield return null;

        // Ensure we stop subtitle coroutine and bot animation
        StopSubtitleSequence();

        if (botTalkCoroutine != null)
        {
            StopCoroutine(botTalkCoroutine);
            botTalkCoroutine = null;
        }

        botImage.sprite = idleSprite;
    }

    private void StopSubtitleSequence()
    {
        if (subtitleCoroutine != null)
        {
            StopCoroutine(subtitleCoroutine);
            subtitleCoroutine = null;
        }
        if (subtitlePanel != null)
            subtitlePanel.SetActive(false);
        if (subtitleBackground != null)
        {
            subtitleBackground.enabled = false;
            subtitleBackground.gameObject.SetActive(false);
        }
    }

    // Show subtitle segments using timestamps and background color per segment
    private IEnumerator SubtitleSequenceCoroutine(AudioClip clip, SubtitleSegment[] segments)
    {
        if (clip == null || segments == null || segments.Length == 0)
            yield break;

        System.Array.Sort(segments, (a, b) => a.timestamp.CompareTo(b.timestamp));

        int idx = 0;
        float clipLength = clip.length;

        while (idx < segments.Length && botAudio != null && botAudio.isPlaying)
        {
            float currentTime = botAudio.time;
            SubtitleSegment seg = segments[idx];

            if (currentTime + 0.0001f >= seg.timestamp)
            {
                // Determine duration
                float segDuration = seg.duration;
                if (segDuration <= 0f)
                {
                    if (idx + 1 < segments.Length) segDuration = Mathf.Max(0.02f, segments[idx + 1].timestamp - seg.timestamp);
                    else segDuration = Mathf.Max(0.02f, clipLength - seg.timestamp);
                }

                // show subtitle UI and set background color
                if (subtitlePanel != null)
                    subtitlePanel.SetActive(true);

                if (subtitleText != null)
                    subtitleText.text = seg.text ?? "";

                if (subtitleBackground != null)
                {
                    // ensure background is enabled and visible
                    subtitleBackground.enabled = true;
                    subtitleBackground.gameObject.SetActive(true);

                    Color col = seg.backgroundColor;
                    // Avoid completely transparent backgrounds by forcing alpha if user accidentally set it to 0
                    if (col.a <= 0.01f) col.a = 0.85f;
                    subtitleBackground.color = col;
                }

                float waited = 0f;
                while (waited < segDuration && botAudio != null && botAudio.isPlaying)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                // hide subtitle before next
                if (subtitlePanel != null)
                    subtitlePanel.SetActive(false);

                // also disable background to keep consistent state
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
        if (subtitlePanel != null)
            subtitlePanel.SetActive(false);
        if (subtitleBackground != null)
        {
            subtitleBackground.enabled = false;
            subtitleBackground.gameObject.SetActive(false);
        }

        subtitleCoroutine = null;
    }

    // One-shot subtitle routine (used for TTS lines)
    private IEnumerator SubtitleOneShotRoutine(string text, float duration, Color bgColor)
    {
        if (subtitlePanel != null)
            subtitlePanel.SetActive(true);

        if (subtitleText != null)
            subtitleText.text = text ?? "";

        if (subtitleBackground != null)
        {
            subtitleBackground.enabled = true;
            subtitleBackground.gameObject.SetActive(true);
            Color col = bgColor;
            if (col.a <= 0.01f) col.a = 0.85f;
            subtitleBackground.color = col;
        }

        float elapsed = 0f;
        while (elapsed < duration && botAudio != null && botAudio.isPlaying)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (subtitlePanel != null)
            subtitlePanel.SetActive(false);
        if (subtitleBackground != null)
        {
            subtitleBackground.enabled = false;
            subtitleBackground.gameObject.SetActive(false);
        }

        subtitleCoroutine = null;
    }

    void OnDestroy()
    {
        if (botTalkCoroutine != null)
        {
            StopCoroutine(botTalkCoroutine);
            botTalkCoroutine = null;
        }

        StopSubtitleSequence();
    }

    // ---------------------
    // Minimal data types for Groq response parsing (kept at bottom)
    // ---------------------
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
}
