using System.Collections;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;
using TMPro;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class IntroSceneController : MonoBehaviour
{
    [Header("Scene References")]
    [Tooltip("Drag your Main Camera here")]
    public Camera mainCamera;

    [Tooltip("Reference to the camera controller script")]
    public SimpleFirstPersonCamera cameraController;

    [Tooltip("The spotlight that illuminates the bot")]
    public Light ufoLight;

    [Tooltip("The bot 3D model GameObject")]
    public GameObject botModel;

    [Tooltip("The bot's mesh/child object that has the Animation component (leave empty if animations are on botModel)")]
    public GameObject botMeshChild;

    [Tooltip("AudioSource component on the bot (used for all voice playback so subtitles can sync)")]
    public AudioSource botAudioSource;

    [Tooltip("The Canvas GameObject with Video Player component")]
    public GameObject videoCanvas;

    [Tooltip("Reference to the Video Player component")]
    public VideoPlayer videoPlayer;

    [Tooltip("CanvasGroup for fading video in/out")]
    public CanvasGroup videoCanvasGroup;

    [Tooltip("Light that illuminates scene when video plays")]
    public Light videoLight;

    [Header("Video Light Color Matching")]
    public Light directionalLight;
    public RenderTexture videoRenderTexture;
    [Header("Directional Light Fade Settings")]
    public float dirLightFadeDuration = 1.2f;
    public float dirLightTargetIntensity = 1.2f;

    private float dirLightOriginalIntensity = 0f;

    [Range(0f, 10f)] public float colorLerpSpeed = 5f;

    private float colorSampleTimer = 0f;
    private float colorSampleInterval = 0.1f;

    public GameObject mouthOpenMesh;
    public GameObject mouthClosedMesh;

    [Header("Bot Idle/Talk Animations")]
    [Tooltip("Idle animation when bot is not talking")]
    public AnimationClip idleAnimation;

    [Tooltip("Talking animation when bot is speaking")]
    public AnimationClip talkingAnimation;

    [Tooltip("Audio volume threshold to trigger talking animation (0-1)")]
    [Range(0f, 1f)]
    public float audioThreshold = 0.01f;

    [Header("Animation Settings")]
    [Tooltip("Target light intensity")]
    public float targetLightIntensity = 5f;

    [Tooltip("Bot floating animation settings")]
    public float floatAmplitude = 0.3f;
    public float floatSpeed = 1f;

    [Tooltip("Video light intensity")]
    public float videoLightIntensity = 115f;

    [Header("Light & Sound Effects")]
    [Tooltip("Sound effect when lights turn on (plays with spotlight)")]
    public AudioClip lightOpeningSFX;

    [Tooltip("AudioSource for sound effects (separate from bot voice)")]
    public AudioSource sfxAudioSource;

    // ----------------------------------------------------------------------------
    // New: AudioWithSubtitles — pair audio + subtitle segments so any sequence can have many subtitles
    [System.Serializable]
    public class SubtitleSegment
    {
        [Tooltip("Timestamp in seconds when this subtitle should appear (relative to its audio clip start)")]
        public float timestamp;

        [TextArea(1, 4)]
        [Tooltip("Subtitle text for this segment")]
        public string text;

        [Tooltip("Duration in seconds for this subtitle. 0 = auto (either until next segment or clip end)")]
        public float duration;
    }

    [System.Serializable]
    public class AudioWithSubtitles
    {
        public AudioClip clip;
        public SubtitleSegment[] segments;
    }
    // ----------------------------------------------------------------------------

    [Header("Sequence 1: Initial Bot Talk")]
    [Tooltip("Audio that plays after light opens")]
    public AudioWithSubtitles initialBotAudio;
    public Transform initialBotPosition;
    public AnimationClip initialBotAnimation;

    [Header("Sequence 2: First Video with Timed Audio")]
    [Tooltip("First video to play")]
    public VideoClip firstVideo;
    [Tooltip("Timed audio clips during first video")]
    public TimedAudioWithPosition[] firstVideoAudios;

    [Header("Sequence 3: Second Bot Talk")]
    [Tooltip("Audio after first video")]
    public AudioWithSubtitles secondBotAudio;
    public Transform secondBotPosition;
    public AnimationClip secondBotAnimation;

    [Header("Sequence 4: Third Bot Talk")]
    [Tooltip("Audio before strand sequences")]
    public AudioWithSubtitles thirdBotAudio;
    public Transform thirdBotPosition;
    public AnimationClip thirdBotAnimation;

    [Header("Sequence 5: Strand Sequences (Array)")]
    [Tooltip("Multiple strand sequences with audio and video")]
    public StrandSequence[] strandSequences;

    [Header("Sequence 6: Fourth Bot Talk (No Video)")]
    [Tooltip("Audio after all strands")]
    public AudioWithSubtitles fourthBotAudio;
    public Transform fourthBotPosition;
    public AnimationClip fourthBotAnimation;

    [Header("Sequence 7: Fifth Bot Talk (now with Video + Audio together)")]
    [Tooltip("Audio after fourth bot talk")]
    public AudioWithSubtitles fifthBotAudio;
    public Transform fifthBotPosition;
    public AnimationClip fifthBotAnimation;
    [Tooltip("Video to play concurrently with the fifth bot audio")]
    public VideoClip fifthBotVideo;

    [Header("Sequence 8: Sixth Bot Talk with Video Array")]
    [Tooltip("Multiple audio-video pairs")]
    public BotTalkWithVideo[] sixthSequenceArray;

    [Header("Sequence 9: Final Bot Talk")]
    [Tooltip("Final audio before fade")]
    public AudioWithSubtitles finalBotAudio;
    public Transform finalBotPosition;
    public AnimationClip finalBotAnimation;

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

    private float[] audioSamples = new float[128];
    private bool isCurrentlyTalking = false;
    private Coroutine audioMonitorCoroutine;

    // Subtitles UI (single shared background for all subtitles)
    public GameObject subtitlePanel;      // panel that contains subtitle UI (assign in Inspector)
    public Image subtitleBackground;      // shared background image for subtitles (assign in Inspector)
    public TMP_Text subtitleText;         // TextMeshPro text for subtitle

    // Skip UI
    [Header("Skip UI")]
    public GameObject skipUIPanel;       // assign a small UI panel bottom-right
    public TMP_Text skipUIText;         // "Press Space to Skip"
    public Image skipFillImage;         // Image (type=Filled) - shows pie-fill
    private CanvasGroup skipCanvasGroup;

    // Skip timing config (NEW)
    [Tooltip("How long (seconds) to wait before showing skip UI at start and after each skip. 2-3 recommended.")]
    public float skipDelay = 2.5f;

    // how visible skip UI stays when not flashing
    [Tooltip("Final alpha for the skip UI (0-1) after fade-in (e.g. 0.6)")]
    public float skipVisibleAlpha = 0.6f;

    // Skip state
    private float skipHoldTimer = 0f;
    public float skipHoldDuration = 1.5f; // hold time to skip
    private bool skipRequested = false;
    private Coroutine currentSequenceCoroutine = null;

    // subtitle state
    private Coroutine subtitleCoroutine = null;

    // voice coroutine tracking (so we can cancel immediately)
    private Coroutine currentVoiceCoroutine = null;

    // skip respawn coroutine handle
    private Coroutine skipRespawnCoroutine = null;

    void Start()
    {
        fadeTexture = new Texture2D(1, 1);
        fadeTexture.SetPixel(0, 0, Color.black);
        fadeTexture.Apply();

        if (cameraController != null)
        {
            cameraController.SetCanLookAround(true);
        }

        if (botAudioSource != null)
        {
            botAudioSource.playOnAwake = false;
            AudioReverbFilter reverb = botAudioSource.GetComponent<AudioReverbFilter>();
            if (reverb == null)
            {
                reverb = botAudioSource.gameObject.AddComponent<AudioReverbFilter>();
            }
            reverb.reverbPreset = AudioReverbPreset.Auditorium;
            reverb.dryLevel = 0;
            reverb.room = -1000;
            reverb.roomHF = -100;
            reverb.decayTime = 5.0f;
        }

        if (sfxAudioSource != null)
        {
            AudioReverbFilter reverb = sfxAudioSource.GetComponent<AudioReverbFilter>();
            if (reverb == null)
            {
                reverb = sfxAudioSource.gameObject.AddComponent<AudioReverbFilter>();
            }
            reverb.reverbPreset = AudioReverbPreset.Auditorium;
            reverb.dryLevel = -500;
            reverb.room = -1000;
            reverb.decayTime = 3.0f;
        }

        if (botModel != null)
        {
            if (botMeshChild != null)
                botAnimator = botMeshChild.GetComponent<Animation>();
            else
                botAnimator = botModel.GetComponent<Animation>();

            if (botAnimator == null && botMeshChild != null)
            {
                botAnimator = botMeshChild.AddComponent<Animation>();
            }
            else if (botAnimator == null)
            {
                botAnimator = botModel.AddComponent<Animation>();
            }

            if (botAnimator != null)
            {
                botAnimator.cullingType = AnimationCullingType.AlwaysAnimate;

                if (idleAnimation != null)
                {
                    botAnimator.AddClip(idleAnimation, "Idle");
                    AnimationState idleState = botAnimator["Idle"];
                    if (idleState != null)
                    {
                        idleState.wrapMode = WrapMode.Loop;
                    }
                }

                if (talkingAnimation != null)
                {
                    botAnimator.AddClip(talkingAnimation, "Talking");
                    AnimationState talkState = botAnimator["Talking"];
                    if (talkState != null)
                    {
                        talkState.wrapMode = WrapMode.Loop;
                    }
                }
            }

            if (initialBotPosition != null)
            {
                botModel.transform.position = initialBotPosition.position;
                botModel.transform.rotation = initialBotPosition.rotation;
            }

            botBasePosition = botModel.transform.position;

            botModel.SetActive(false);
        }

        if (videoPlayer != null)
        {
            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.skipOnDrop = true;
            videoPlayer.prepareCompleted += OnVideoPrepared;
            videoPlayer.loopPointReached += OnVideoFinished;
        }

        if (videoCanvasGroup == null && videoCanvas != null)
        {
            videoCanvasGroup = videoCanvas.GetComponent<CanvasGroup>();
            if (videoCanvasGroup == null)
            {
                videoCanvasGroup = videoCanvas.AddComponent<CanvasGroup>();
            }
        }

        if (videoCanvasGroup != null)
        {
            videoCanvasGroup.alpha = 0f;
        }
        if (videoCanvas != null)
        {
            videoCanvas.SetActive(false);
        }

        if (videoLight != null)
        {
            videoLight.enabled = false;
            videoLight.intensity = videoLightIntensity;
        }

        // Setup skip canvas group (for fading)
        if (skipUIPanel != null)
        {
            skipCanvasGroup = skipUIPanel.GetComponent<CanvasGroup>();
            if (skipCanvasGroup == null)
                skipCanvasGroup = skipUIPanel.AddComponent<CanvasGroup>();

            // start hidden; will fade in after skipDelay
            skipCanvasGroup.alpha = 0f;
            skipUIPanel.SetActive(true); // keep active so alpha controls visibility
        }

        // Ensure subtitle panel hidden at start
        if (subtitlePanel != null)
            subtitlePanel.SetActive(false);

        // fade in skip UI at sequence start (after configurable skipDelay)
        StartCoroutine(DelayedStartRoutine());

        StartCoroutine(FloatBot());
    }

    IEnumerator DelayedStartRoutine()
    {
        // Wait the configured skipDelay before showing skip UI AND start the intro.
        yield return new WaitForSeconds(0.5f);

        // fade in skip UI to configured visible alpha
        if (skipCanvasGroup != null)
            StartCoroutine(FadeCanvasGroup(skipCanvasGroup, 0f, skipVisibleAlpha, 0.6f));

        // start main sequence
        StartCoroutine(IntroSequence());
    }

    void Update()
    {
        // color sampling
        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            colorSampleTimer += Time.deltaTime;
            if (colorSampleTimer >= colorSampleInterval)
            {
                colorSampleTimer = 0f;
                SampleVideoColor();
            }
        }

        // SKIP HOLD logic (bottom-right UI)
        // removed instant SetActive(true). skipUIPanel remains active but with alpha control.
        // Use input wrapper that supports both Input System and legacy Input Manager.
        if (IsSpacePressed())
        {
            skipHoldTimer += Time.deltaTime;
            if (skipFillImage != null)
            {
                skipFillImage.fillAmount = Mathf.Clamp01(skipHoldTimer / skipHoldDuration);
            }

            if (skipHoldTimer >= skipHoldDuration)
            {
                RequestSkipCurrentSequence();
                if (skipFillImage != null) skipFillImage.fillAmount = 0f;
                skipHoldTimer = 0f;
            }
        }
        else if (IsSpaceReleasedThisFrame())
        {
            skipHoldTimer = 0f;
            if (skipFillImage != null)
            {
                skipFillImage.fillAmount = 0f;
            }
        }
        else
        {
            if (skipHoldTimer > 0f)
            {
                skipHoldTimer = 0f;
                if (skipFillImage != null) skipFillImage.fillAmount = 0f;
            }
        }
    }

    // Input wrappers - compile-time safe
    private bool IsSpacePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
#else
        return Input.GetKey(KeyCode.Space);
#endif
    }

    private bool IsSpaceReleasedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.spaceKey.wasReleasedThisFrame;
#else
        return Input.GetKeyUp(KeyCode.Space);
#endif
    }

    IEnumerator FadeDirectionalLight(float from, float to, float duration)
    {
        float elapsed = 0f;
        if (directionalLight == null) yield break;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            directionalLight.intensity = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }

        directionalLight.intensity = to;
    }

    void SampleVideoColor()
    {
        if (videoRenderTexture == null || directionalLight == null) return;

        RenderTexture.active = videoRenderTexture;
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(videoRenderTexture.width / 2, videoRenderTexture.height / 2, 1, 1), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        Color sampledColor = tex.GetPixel(0, 0);
        directionalLight.color = Color.Lerp(directionalLight.color, sampledColor, colorLerpSpeed * Time.deltaTime);
        Destroy(tex);
    }

    void OnVideoPrepared(VideoPlayer source)
    {
        Debug.Log("Video prepared and ready to play");
    }

    void OnVideoFinished(VideoPlayer source)
    {
        Debug.Log("Video finished playing");
    }

    IEnumerator FloatBot()
    {
        while (true)
        {
            if (botModel != null && botModel.activeSelf)
            {
                floatTimer += Time.deltaTime * floatSpeed;
                float yOffset = Mathf.Sin(floatTimer) * floatAmplitude;

                Vector3 targetPos = botBasePosition + new Vector3(0, yOffset, 0);
                botModel.transform.position = targetPos;
            }
            yield return null;
        }
    }

    IEnumerator MonitorAudioAndSwitchAnimation()
    {
        isCurrentlyTalking = false;

        while (botAudioSource != null && botAudioSource.isPlaying)
        {
            // get output data for simple mouth/activity
            botAudioSource.GetOutputData(audioSamples, 0);
            float sum = 0f;
            for (int i = 0; i < audioSamples.Length; i++)
                sum += Mathf.Abs(audioSamples[i]);

            float avgVolume = sum / audioSamples.Length;
            bool shouldTalk = avgVolume > audioThreshold;

            if (shouldTalk != isCurrentlyTalking)
            {
                isCurrentlyTalking = shouldTalk;

                if (botAnimator != null)
                {
                    botAnimator.Stop();

                    if (isCurrentlyTalking)
                    {
                        botAnimator.Play("Talking");
                        SetMouthState(true);
                    }
                    else
                    {
                        botAnimator.Play("Idle");
                        SetMouthState(false);
                    }
                }
            }

            yield return null;
        }

        isCurrentlyTalking = false;
        SetMouthState(false);
        if (botAnimator != null) botAnimator.Play("Idle");
    }

    void SetMouthState(bool talking)
    {
        if (mouthOpenMesh != null) mouthOpenMesh.SetActive(talking);
        if (mouthClosedMesh != null) mouthClosedMesh.SetActive(!talking);
    }

    IEnumerator IntroSequence()
    {
        yield return new WaitForSeconds(2f);

        if (ufoLight != null)
        {
            ufoLight.intensity = targetLightIntensity;
        }

        if (lightOpeningSFX != null && sfxAudioSource != null)
        {
            sfxAudioSource.PlayOneShot(lightOpeningSFX);
        }

        if (botModel != null)
        {
            botModel.SetActive(true);
            if (idleAnimation != null && botAnimator != null)
            {
                botAnimator.Play("Idle");
            }
        }

        yield return new WaitForSeconds(0.8f);

        // Sequence 1: initialBotAudio (now AudioWithSubtitles)
        yield return StartCoroutine(RunSequence(PlayBotAudio(initialBotAudio, initialBotPosition, initialBotAnimation)));

        // Sequence 2: video with timed audios (timed audios already carry subtitleSegments)
        yield return StartCoroutine(RunSequence(PlayVideoWithTimedAudio(firstVideo, firstVideoAudios)));

        // Sequence 3
        yield return StartCoroutine(RunSequence(PlayBotAudio(secondBotAudio, secondBotPosition, secondBotAnimation)));

        // Sequence 4
        yield return StartCoroutine(RunSequence(PlayBotAudio(thirdBotAudio, thirdBotPosition, thirdBotAnimation)));

        // Sequence 5: strand sequences
        foreach (StrandSequence strand in strandSequences)
        {
            yield return StartCoroutine(RunSequence(PlayStrandSequence(strand)));
        }

        // Sequence 6
        yield return StartCoroutine(RunSequence(PlayBotAudio(fourthBotAudio, fourthBotPosition, fourthBotAnimation)));

        // Sequence 7
        yield return StartCoroutine(RunSequence(PlayBotAudioWithVideo(fifthBotAudio, fifthBotVideo, fifthBotPosition, fifthBotAnimation)));

        // Sequence 8
        foreach (BotTalkWithVideo sequence in sixthSequenceArray)
        {
            yield return StartCoroutine(RunSequence(PlayBotTalkWithVideo(sequence)));
        }

        // Sequence 9
        yield return StartCoroutine(RunSequence(PlayBotAudio(finalBotAudio, finalBotPosition, finalBotAnimation)));

        yield return StartCoroutine(FadeToBlack());

        Debug.Log("Intro sequence complete!");
        SceneLoader.LoadSceneWithLoading("UserInfoScene");
    }

    // Play voice clip with optional segments (tracks currentVoiceCoroutine so we can cancel)
    IEnumerator PlayVoiceClip(AudioClip clip, SubtitleSegment[] segments = null)
    {
        if (clip == null) yield break;

        // make sure previous voice coroutine is stopped
        if (currentVoiceCoroutine != null)
        {
            StopCoroutine(currentVoiceCoroutine);
            currentVoiceCoroutine = null;
        }

        // stop previous audio & subtitle coroutine
        if (botAudioSource.isPlaying)
            botAudioSource.Stop();

        if (audioMonitorCoroutine != null)
        {
            StopCoroutine(audioMonitorCoroutine);
            audioMonitorCoroutine = null;
        }

        // set clip & play (so we can read botAudioSource.time)
        botAudioSource.clip = clip;
        botAudioSource.Play();

        // start monitoring (mouth animation)
        audioMonitorCoroutine = StartCoroutine(MonitorAudioAndSwitchAnimation());

        // start subtitle sequence if segments provided
        if (segments != null && segments.Length > 0)
        {
            // stop any existing subtitle coroutine first
            if (subtitleCoroutine != null) StopCoroutine(subtitleCoroutine);
            subtitleCoroutine = StartCoroutine(SubtitleSequenceCoroutine(clip, segments));
        }

        // wait until clip ends or skip requested
        while (botAudioSource != null && botAudioSource.isPlaying && !skipRequested)
        {
            yield return null;
        }

        // stop subtitle display
        StopSubtitleSequence();

        // ensure idle state
        if (idleAnimation != null && botAnimator != null)
            botAnimator.Play("Idle");

        isCurrentlyTalking = false;
    }

    // Start subtitle sequence coroutine for an audio clip has been moved: PlayVoiceClip is handed the subtitles directly.

    // Stop current subtitle coroutine & hide panel
    void StopSubtitleSequence()
    {
        if (subtitleCoroutine != null)
        {
            StopCoroutine(subtitleCoroutine);
            subtitleCoroutine = null;
        }
        HideSubtitle();
    }

    // Coroutine to display multiple subtitle segments synchronized to botAudioSource.time
    IEnumerator SubtitleSequenceCoroutine(AudioClip clip, SubtitleSegment[] segments)
    {
        if (clip == null || segments == null || segments.Length == 0)
            yield break;

        // sort segments by timestamp ascending just in case
        System.Array.Sort(segments, (a, b) => a.timestamp.CompareTo(b.timestamp));

        int idx = 0;
        float clipLength = clip.length;

        while (idx < segments.Length && botAudioSource != null && botAudioSource.isPlaying && !skipRequested)
        {
            float currentTime = botAudioSource.time;

            SubtitleSegment seg = segments[idx];

            if (currentTime + 0.0001f >= seg.timestamp) // timestamp reached
            {
                // Determine duration:
                float segDuration = seg.duration;
                if (segDuration <= 0f)
                {
                    // if there's a next segment, last until next segment; otherwise until clip end
                    if (idx + 1 < segments.Length) segDuration = Mathf.Max(0.02f, segments[idx + 1].timestamp - seg.timestamp);
                    else segDuration = Mathf.Max(0.02f, clipLength - seg.timestamp);
                }

                // show it
                ShowSubtitle(seg.text, segDuration);

                // wait for duration or until skip requested or audio stops
                float waited = 0f;
                while (waited < segDuration && botAudioSource != null && botAudioSource.isPlaying && !skipRequested)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                // hide subtitle before proceeding to next segment
                HideSubtitle();

                idx++;
            }
            else
            {
                // wait a frame until timestamp is reached
                yield return null;
            }
        }

        // ensure subtitle hidden at the end
        HideSubtitle();
        subtitleCoroutine = null;
    }

    // Show subtitle with shared background (no per-segment sprite)
    public void ShowSubtitle(string text, float duration)
    {
        if (subtitlePanel == null || subtitleText == null || subtitleBackground == null)
        {
            Debug.LogWarning("Subtitle UI not assigned.");
            return;
        }

        // stop single-shot subtitle coroutine if it's running
        if (subtitleCoroutine != null)
        {
            StopCoroutine(subtitleCoroutine);
            subtitleCoroutine = null;
        }

        subtitleCoroutine = StartCoroutine(SubtitleRoutine(text, duration));
    }

    IEnumerator SubtitleRoutine(string text, float duration)
    {
        subtitlePanel.SetActive(true);
        subtitleText.text = text ?? "";
        subtitleBackground.enabled = true; // shared background used for all subtitles

        if (duration > 0f)
        {
            float elapsed = 0f;
            while (elapsed < duration && !skipRequested)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            while (!skipRequested)
                yield return null;
        }

        subtitlePanel.SetActive(false);
        subtitleCoroutine = null;
    }

    public void HideSubtitle()
    {
        if (subtitleCoroutine != null)
        {
            StopCoroutine(subtitleCoroutine);
            subtitleCoroutine = null;
        }
        if (subtitlePanel != null)
            subtitlePanel.SetActive(false);
    }

    IEnumerator PlayBotAudio(AudioWithSubtitles audioWithSubs, Transform targetPosition, AnimationClip transitionAnimation)
    {
        if (audioWithSubs == null || audioWithSubs.clip == null) yield break;

        if (targetPosition != null && botModel != null)
        {
            yield return StartCoroutine(MoveAndPlayTransition(targetPosition, 1f, transitionAnimation));
            botBasePosition = targetPosition.position;
        }
        else if (transitionAnimation != null && botModel != null)
        {
            yield return StartCoroutine(MoveAndPlayTransition(botModel.transform, 0.01f, transitionAnimation));
        }

        // run voice clip (pass segments)
        currentVoiceCoroutine = StartCoroutine(PlayVoiceClip(audioWithSubs.clip, audioWithSubs.segments));
        yield return currentVoiceCoroutine;

        // small buffer
        yield return new WaitForSeconds(0.25f);
    }

    IEnumerator PlayBotAudioWithVideo(AudioWithSubtitles audioWithSubs, VideoClip video, Transform targetPosition, AnimationClip transitionAnimation)
    {
        if ((video == null) && (audioWithSubs == null || audioWithSubs.clip == null)) yield break;

        if (targetPosition != null && botModel != null)
        {
            yield return StartCoroutine(MoveAndPlayTransition(targetPosition, 1f, transitionAnimation));
            botBasePosition = targetPosition.position;
        }
        else if (transitionAnimation != null && botModel != null)
        {
            yield return StartCoroutine(MoveAndPlayTransition(botModel.transform, 0.01f, transitionAnimation));
        }

        if (video != null)
        {
            if (videoCanvas == null || videoPlayer == null)
            {
                Debug.LogError("Video canvas or VideoPlayer missing");
            }
            else
            {
                videoCanvas.SetActive(true);
                if (videoCanvasGroup != null) videoCanvasGroup.alpha = 0f;

                ClearVideoRenderTexture();

                videoPlayer.Stop();
                videoPlayer.clip = video;
                videoPlayer.playbackSpeed = 1f;
                videoPlayer.isLooping = false;
                videoPlayer.Prepare();

                float prepareTimeout = 10f;
                float prepareTimer = 0f;
                while (!videoPlayer.isPrepared && prepareTimer < prepareTimeout)
                {
                    prepareTimer += Time.deltaTime;
                    yield return null;
                }

                if (!videoPlayer.isPrepared)
                {
                    Debug.LogError("Video failed to prepare!");
                    videoCanvas.SetActive(false);
                    yield break;
                }

                if (videoLight != null) videoLight.enabled = true;
                yield return StartCoroutine(FadeInVideo());

                videoPlayer.Play();
                Debug.Log("Video playing (combined): " + video.name);
            }
        }

        if (audioWithSubs != null && audioWithSubs.clip != null)
        {
            currentVoiceCoroutine = StartCoroutine(PlayVoiceClip(audioWithSubs.clip, audioWithSubs.segments));
            yield return currentVoiceCoroutine;
        }

        bool isVideoPlaying() => video != null && videoPlayer != null && videoPlayer.isPlaying;
        bool isAudioPlaying() => botAudioSource != null && botAudioSource.isPlaying;

        while (isVideoPlaying() || isAudioPlaying())
        {
            yield return null;
        }

        if (video != null)
        {
            yield return StartCoroutine(FadeOutVideo());
            if (videoLight != null) videoLight.enabled = false;
        }

        yield return new WaitForSeconds(0.2f);
    }

    IEnumerator MoveAndPlayTransition(Transform target, float defaultDuration, AnimationClip transitionClip = null)
    {
        if (botModel == null || target == null)
            yield break;

        float moveDuration = defaultDuration;
        if (transitionClip != null && transitionClip.length > 0f)
        {
            moveDuration = transitionClip.length;
        }

        string transName = null;
        if (transitionClip != null && botAnimator != null)
        {
            transName = "__TRANS_" + transitionClip.name;
            if (botAnimator.GetClip(transName) == null)
            {
                botAnimator.AddClip(transitionClip, transName);
            }
        }

        if (transName != null && botAnimator != null)
        {
            botAnimator.Stop();
            botAnimator.Play(transName);
            SetMouthState(false);
        }

        Vector3 startPos = botModel.transform.position;
        Quaternion startRot = botModel.transform.rotation;
        Vector3 targetPos = target.position;
        Quaternion targetRot = target.rotation;

        float elapsed = 0f;
        while (elapsed < moveDuration && !skipRequested)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / moveDuration);

            botBasePosition = Vector3.Lerp(startPos, targetPos, t);

            if (botModel != null)
                botModel.transform.rotation = Quaternion.Slerp(startRot, targetRot, t);

            yield return null;
        }

        botBasePosition = targetPos;
        if (botModel != null)
            botModel.transform.rotation = targetRot;

        if (idleAnimation != null && botAnimator != null)
        {
            botAnimator.Play("Idle");
        }
    }

    IEnumerator PlayVideoWithTimedAudio(VideoClip video, TimedAudioWithPosition[] timedAudios)
    {
        if (video == null) yield break;

        if (videoCanvas == null || videoPlayer == null)
        {
            Debug.LogError("Video canvas or VideoPlayer missing");
            yield break;
        }

        videoCanvas.SetActive(true);
        if (videoCanvasGroup != null)
        {
            videoCanvasGroup.alpha = 0f;
        }

        ClearVideoRenderTexture();

        videoPlayer.Stop();
        videoPlayer.clip = video;
        videoPlayer.playbackSpeed = 1f;
        videoPlayer.isLooping = false;
        videoPlayer.Prepare();

        float prepareTimeout = 10f;
        float prepareTimer = 0f;
        while (!videoPlayer.isPrepared && prepareTimer < prepareTimeout)
        {
            prepareTimer += Time.deltaTime;
            yield return null;
        }

        if (!videoPlayer.isPrepared)
        {
            Debug.LogError("Video failed to prepare!");
            videoCanvas.SetActive(false);
            yield break;
        }

        if (videoLight != null)
        {
            videoLight.enabled = true;
        }

        if (directionalLight != null)
        {
            dirLightOriginalIntensity = directionalLight.intensity;
            StartCoroutine(FadeDirectionalLight(0f, dirLightTargetIntensity, dirLightFadeDuration));
        }

        yield return StartCoroutine(FadeInVideo());

        videoPlayer.Play();
        Debug.Log("Video playing: " + video.name);

        int currentAudioIndex = 0;

        while (videoPlayer.isPlaying && !skipRequested)
        {
            double currentVideoTime = videoPlayer.time;

            if (timedAudios != null && currentAudioIndex < timedAudios.Length)
            {
                TimedAudioWithPosition timedAudio = timedAudios[currentAudioIndex];
                if (currentVideoTime >= timedAudio.timestamp)
                {
                    // bot position/animation transitions if any
                    if (timedAudio.botPosition != null && botModel != null)
                    {
                        if (timedAudio.botAnimation != null)
                        {
                            yield return StartCoroutine(MoveAndPlayTransition(timedAudio.botPosition, 0.5f, timedAudio.botAnimation));
                        }
                        else
                        {
                            yield return StartCoroutine(MoveAndPlayTransition(timedAudio.botPosition, 0.5f, null));
                        }
                        botBasePosition = timedAudio.botPosition.position;
                    }
                    else if (timedAudio.botAnimation != null && botModel != null)
                    {
                        yield return StartCoroutine(MoveAndPlayTransition(botModel.transform, 0.01f, timedAudio.botAnimation));
                    }

                    // play timed audio (on botAudioSource so mouth + subtitles sync)
                    if (timedAudio.audioClip != null)
                    {
                        // timedAudio.subtitleSegments may exist
                        currentVoiceCoroutine = StartCoroutine(PlayVoiceClip(timedAudio.audioClip, timedAudio.subtitleSegments));
                        yield return currentVoiceCoroutine;
                    }

                    currentAudioIndex++;
                }
            }

            yield return null;
        }

        Debug.Log("Video finished");

        if (idleAnimation != null && botAnimator != null)
        {
            botAnimator.Play("Idle");
        }
        isCurrentlyTalking = false;

        yield return StartCoroutine(FadeOutVideo());

        if (videoLight != null)
        {
            videoLight.enabled = false;
        }

        if (directionalLight != null)
        {
            StartCoroutine(FadeDirectionalLight(directionalLight.intensity, dirLightOriginalIntensity, dirLightFadeDuration));
        }

        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator PlayStrandSequence(StrandSequence strand)
    {
        if (strand == null) yield break;
        Debug.Log("Playing strand: " + strand.strandName);

        // strand.botAudio is AudioWithSubtitles
        yield return StartCoroutine(RunSequence(PlayBotAudio(strand.botAudio, strand.botPosition, strand.botAnimation)));

        yield return StartCoroutine(RunSequence(PlayVideoWithTimedAudio(strand.video, strand.timedAudios)));
    }

    IEnumerator PlayBotTalkWithVideo(BotTalkWithVideo sequence)
    {
        Debug.Log("Playing bot talk with video sequence (concurrent audio+video)");

        if (sequence == null) yield break;

        if (sequence.botPosition != null && botModel != null)
        {
            yield return StartCoroutine(MoveAndPlayTransition(sequence.botPosition, 1f, sequence.botAnimation));
            botBasePosition = sequence.botPosition.position;
        }
        else if (sequence.botAnimation != null && botModel != null)
        {
            yield return StartCoroutine(MoveAndPlayTransition(botModel.transform, 0.01f, sequence.botAnimation));
        }

        if (sequence.video != null && videoPlayer != null && videoCanvas != null)
        {
            videoCanvas.SetActive(true);
            if (videoCanvasGroup != null) videoCanvasGroup.alpha = 0f;

            ClearVideoRenderTexture();

            videoPlayer.Stop();
            videoPlayer.clip = sequence.video;
            videoPlayer.playbackSpeed = 1f;
            videoPlayer.isLooping = false;
            videoPlayer.Prepare();

            float prepareTimeout = 10f;
            float prepareTimer = 0f;
            while (!videoPlayer.isPrepared && prepareTimer < prepareTimeout)
            {
                prepareTimer += Time.deltaTime;
                yield return null;
            }

            if (!videoPlayer.isPrepared)
            {
                Debug.LogError("Video failed to prepare!");
                videoCanvas.SetActive(false);
                yield break;
            }

            if (videoLight != null) videoLight.enabled = true;
            yield return StartCoroutine(FadeInVideo());

            videoPlayer.Play();
            Debug.Log("Video playing (sequence array): " + sequence.video.name);
        }

        // Play main bot audio (AudioWithSubtitles)
        if (sequence.botAudio != null && sequence.botAudio.clip != null)
        {
            currentVoiceCoroutine = StartCoroutine(PlayVoiceClip(sequence.botAudio.clip, sequence.botAudio.segments));
            yield return currentVoiceCoroutine;
        }

        // timed audios relative to the video (if any)
        if (sequence.timedAudios != null && sequence.timedAudios.Length > 0 && videoPlayer != null)
        {
            int currentAudioIndex = 0;
            while (videoPlayer.isPlaying && !skipRequested)
            {
                double currentVideoTime = videoPlayer.time;

                if (currentAudioIndex < sequence.timedAudios.Length)
                {
                    TimedAudioWithPosition timedAudio = sequence.timedAudios[currentAudioIndex];
                    if (currentVideoTime >= timedAudio.timestamp)
                    {
                        if (timedAudio.botPosition != null && botModel != null)
                        {
                            if (timedAudio.botAnimation != null)
                            {
                                yield return StartCoroutine(MoveAndPlayTransition(timedAudio.botPosition, 0.5f, timedAudio.botAnimation));
                            }
                            else
                            {
                                yield return StartCoroutine(MoveAndPlayTransition(timedAudio.botPosition, 0.5f, null));
                            }
                            botBasePosition = timedAudio.botPosition.position;
                        }
                        else if (timedAudio.botAnimation != null && botModel != null)
                        {
                            yield return StartCoroutine(MoveAndPlayTransition(botModel.transform, 0.01f, timedAudio.botAnimation));
                        }

                        if (timedAudio.audioClip != null)
                        {
                            currentVoiceCoroutine = StartCoroutine(PlayVoiceClip(timedAudio.audioClip, timedAudio.subtitleSegments));
                            yield return currentVoiceCoroutine;
                        }

                        currentAudioIndex++;
                    }
                }

                yield return null;
            }
        }
        else
        {
            // if no timed audios, just wait while video or audio plays
            bool isVideoPlaying() => sequence.video != null && videoPlayer != null && videoPlayer.isPlaying;
            bool isAudioPlaying() => botAudioSource != null && botAudioSource.isPlaying;

            while (isVideoPlaying() || isAudioPlaying())
            {
                yield return null;
            }
        }

        if (idleAnimation != null && botAnimator != null)
        {
            botAnimator.Play("Idle");
        }
        isCurrentlyTalking = false;

        if (sequence.video != null)
        {
            yield return StartCoroutine(FadeOutVideo());
            if (videoLight != null) videoLight.enabled = false;
        }

        yield return new WaitForSeconds(0.25f);
    }

    IEnumerator FadeInVideo()
    {
        if (videoCanvasGroup == null) yield break;

        float elapsed = 0f;
        videoCanvasGroup.alpha = 0f;

        while (elapsed < videoFadeDuration)
        {
            elapsed += Time.deltaTime;
            videoCanvasGroup.alpha = elapsed / videoFadeDuration;
            yield return null;
        }

        videoCanvasGroup.alpha = 1f;
    }

    IEnumerator FadeOutVideo()
    {
        if (videoCanvasGroup == null)
        {
            if (videoCanvas != null) videoCanvas.SetActive(false);
            yield break;
        }

        float elapsed = 0f;
        videoCanvasGroup.alpha = 1f;

        while (elapsed < videoFadeDuration)
        {
            elapsed += Time.deltaTime;
            videoCanvasGroup.alpha = 1f - (elapsed / videoFadeDuration);
            yield return null;
        }

        videoCanvasGroup.alpha = 0f;
        if (videoCanvas != null) videoCanvas.SetActive(false);
    }

    IEnumerator FadeToBlack()
    {
        float elapsed = 0f;

        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            fadeAlpha = elapsed / fadeOutDuration;
            yield return null;
        }

        fadeAlpha = 1f;
    }

    void OnGUI()
    {
        if (fadeAlpha > 0f && fadeTexture != null)
        {
            Color color = Color.black;
            color.a = fadeAlpha;
            GUI.color = color;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), fadeTexture);
        }
    }

    void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.loopPointReached -= OnVideoFinished;
        }

        if (audioMonitorCoroutine != null)
        {
            StopCoroutine(audioMonitorCoroutine);
            audioMonitorCoroutine = null;
        }

        if (subtitleCoroutine != null)
        {
            StopCoroutine(subtitleCoroutine);
            subtitleCoroutine = null;
        }

        if (currentVoiceCoroutine != null)
        {
            StopCoroutine(currentVoiceCoroutine);
            currentVoiceCoroutine = null;
        }
    }

    void ClearVideoRenderTexture()
    {
        if (videoRenderTexture == null) return;

        RenderTexture current = RenderTexture.active;
        RenderTexture.active = videoRenderTexture;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = current;
    }

    // Called to request skipping of current sequence
    public void RequestSkipCurrentSequence()
    {
        // hide skip panel immediately and schedule respawn after configured delay
        if (skipCanvasGroup != null)
        {
            // immediately fade out quickly for immediate feedback
            StartCoroutine(FadeCanvasGroup(skipCanvasGroup, skipCanvasGroup.alpha, 0f, 0.12f));
        }

        // mark skip requested
        skipRequested = true;

        // stop playing audio/video quickly
        if (botAudioSource != null) botAudioSource.Stop();
        if (videoPlayer != null && videoPlayer.isPlaying) videoPlayer.Stop();

        // hide video canvas
        if (videoCanvas != null) videoCanvas.SetActive(false);

        // cancel voice coroutine
        if (currentVoiceCoroutine != null)
        {
            StopCoroutine(currentVoiceCoroutine);
            currentVoiceCoroutine = null;
        }

        // stop subtitle sequence
        StopSubtitleSequence();

        // set animator back to idle
        if (botAnimator != null && idleAnimation != null)
        {
            botAnimator.Play("Idle");
        }

        // Cancel any pending respawn and start a new one
        if (skipRespawnCoroutine != null)
        {
            StopCoroutine(skipRespawnCoroutine);
            skipRespawnCoroutine = null;
        }
        skipRespawnCoroutine = StartCoroutine(RespawnSkipAfterDelay());
    }

    IEnumerator RespawnSkipAfterDelay()
    {
        yield return new WaitForSeconds(skipDelay);

        if (skipCanvasGroup != null)
        {
            // fade from 0 to visible alpha
            yield return StartCoroutine(FadeCanvasGroup(skipCanvasGroup, 0f, skipVisibleAlpha, 0.6f));
        }

        skipRespawnCoroutine = null;
    }

    // Fade coroutine (already present earlier)
    IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
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

    IEnumerator SequenceWrapper(IEnumerator sequence, System.Action onComplete)
    {
        yield return StartCoroutine(sequence);
        onComplete?.Invoke();
    }

    IEnumerator RunSequence(IEnumerator sequence)
    {
        bool done = false;
        // start wrapper so we can observe completion
        Coroutine wrapper = StartCoroutine(SequenceWrapper(sequence, () => done = true));
        currentSequenceCoroutine = wrapper;

        while (!done && !skipRequested)
            yield return null;

        if (!done && skipRequested)
        {
            // stop the sequence wrapper
            StopCoroutine(wrapper);
        }

        // reset state
        skipRequested = false;
        currentSequenceCoroutine = null;
        yield return null;
    }
}

// --- Serializable helper classes below (used by inspector) ---
[System.Serializable]
public class TimedAudioWithPosition
{
    [Tooltip("Time in seconds when this audio should start")]
    public float timestamp;

    [Tooltip("The audio clip to play")]
    public AudioClip audioClip;

    [Tooltip("Bot position during this audio")]
    public Transform botPosition;

    [Tooltip("Bot animation during this audio (Optional transition that will play BEFORE audio)")]
    public AnimationClip botAnimation;

    [Tooltip("Subtitle segments (multiple subtitles per this timed audio)")]
    public IntroSceneController.SubtitleSegment[] subtitleSegments;

    public float subtitleDuration = 0f; // legacy single duration (kept optional)
}

[System.Serializable]
public class StrandSequence
{
    [Tooltip("Name for organization")]
    public string strandName;

    [Tooltip("Bot audio before video (audio + subtitles pair)")]
    public IntroSceneController.AudioWithSubtitles botAudio;

    [Tooltip("Bot position")]
    public Transform botPosition;

    [Tooltip("Bot animation (Optional transition that will play BEFORE audio)")]
    public AnimationClip botAnimation;

    [Tooltip("Video to play")]
    public VideoClip video;

    [Tooltip("Timed audio clips during video")]
    public TimedAudioWithPosition[] timedAudios;
}

[System.Serializable]
public class BotTalkWithVideo
{
    [Tooltip("Bot audio")]
    public IntroSceneController.AudioWithSubtitles botAudio;

    [Tooltip("Bot position")]
    public Transform botPosition;

    [Tooltip("Bot animation (Optional transition that will play BEFORE audio)")]
    public AnimationClip botAnimation;

    [Tooltip("Video to play")]
    public VideoClip video;

    [Tooltip("Timed audio clips during video")]
    public TimedAudioWithPosition[] timedAudios;
}
