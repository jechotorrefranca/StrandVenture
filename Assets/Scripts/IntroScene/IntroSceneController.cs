using UnityEngine;
using UnityEngine.Video;
using System.Collections;

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

    [Tooltip("AudioSource component on the bot")]
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
    public Light directionalLight;         // Your directional light
    public RenderTexture videoRenderTexture; // Assign the same as VideoPlayer.targetTexture
    [Header("Directional Light Fade Settings")]
    public float dirLightFadeDuration = 1.2f;
    public float dirLightTargetIntensity = 1.2f;

    private float dirLightOriginalIntensity = 0f;


    [Range(0f, 10f)] public float colorLerpSpeed = 5f; // Smooth transition
    private float colorSampleTimer = 0f;
    private float colorSampleInterval = 0.1f; // every 0.1 sec

    private Texture2D videoTexture2D;

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

    [Tooltip("How often to check audio levels (in seconds)")]
    public float audioCheckInterval = 0.05f;

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

    [Header("Sequence 1: Initial Bot Talk")]
    [Tooltip("Audio that plays after light opens")]
    public AudioClip initialBotAudio;
    public Transform initialBotPosition;
    public AnimationClip initialBotAnimation;

    [Header("Sequence 2: First Video with Timed Audio")]
    [Tooltip("First video to play")]
    public VideoClip firstVideo;
    [Tooltip("Timed audio clips during first video")]
    public TimedAudioWithPosition[] firstVideoAudios;

    [Header("Sequence 3: Second Bot Talk")]
    [Tooltip("Audio after first video")]
    public AudioClip secondBotAudio;
    public Transform secondBotPosition;
    public AnimationClip secondBotAnimation;

    [Header("Sequence 4: Third Bot Talk")]
    [Tooltip("Audio before strand sequences")]
    public AudioClip thirdBotAudio;
    public Transform thirdBotPosition;
    public AnimationClip thirdBotAnimation;

    [Header("Sequence 5: Strand Sequences (Array)")]
    [Tooltip("Multiple strand sequences with audio and video")]
    public StrandSequence[] strandSequences;

    [Header("Sequence 6: Fourth Bot Talk (No Video)")]
    [Tooltip("Audio after all strands")]
    public AudioClip fourthBotAudio;
    public Transform fourthBotPosition;
    public AnimationClip fourthBotAnimation;

    [Header("Sequence 7: Fifth Bot Talk (now with Video + Audio together)")]
    [Tooltip("Audio after fourth bot talk")]
    public AudioClip fifthBotAudio;
    public Transform fifthBotPosition;
    public AnimationClip fifthBotAnimation;
    [Tooltip("Video to play concurrently with the fifth bot audio")]
    public VideoClip fifthBotVideo; // NEW: video for 7th sequence

    [Header("Sequence 8: Sixth Bot Talk with Video Array")]
    [Tooltip("Multiple audio-video pairs")]
    public BotTalkWithVideo[] sixthSequenceArray;

    [Header("Sequence 9: Final Bot Talk")]
    [Tooltip("Final audio before fade")]
    public AudioClip finalBotAudio;
    public Transform finalBotPosition;
    public AnimationClip finalBotAnimation;

    [Header("Final Settings")]
    [Tooltip("Duration of fade to black")]
    public float fadeOutDuration = 2f;

    [Tooltip("Duration of video fade in/out")]
    public float videoFadeDuration = 0.5f;

    // For screen fading
    private Texture2D fadeTexture;
    private float fadeAlpha = 0f;

    // For bot floating animation
    private Vector3 botBasePosition;
    private float floatTimer = 0f;

    // For bot animation
    private Animation botAnimator;

    // For audio detection
    private float[] audioSamples = new float[128];
    private bool isCurrentlyTalking = false;
    private Coroutine audioMonitorCoroutine;

    void Start()
    {
        // Create black texture for fading
        fadeTexture = new Texture2D(1, 1);
        fadeTexture.SetPixel(0, 0, Color.black);
        fadeTexture.Apply();

        // Camera can look around, but player can't move
        if (cameraController != null)
        {
            cameraController.SetCanLookAround(true);
        }

        // Setup echoey reverb for bot audio
        if (botAudioSource != null)
        {
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

        // Setup reverb for SFX audio source
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

        // Get bot animator component and store initial position
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
                // Disable animation culling to ensure animations play correctly
                botAnimator.cullingType = AnimationCullingType.AlwaysAnimate;

                // Setup idle and talking animations
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

            // Set initial position and rotation based on first position
            if (initialBotPosition != null)
            {
                botModel.transform.position = initialBotPosition.position;
                botModel.transform.rotation = initialBotPosition.rotation;
            }

            botBasePosition = botModel.transform.position;

            // Start bot as inactive
            botModel.SetActive(false);
        }

        // Prepare video player
        if (videoPlayer != null)
        {
            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.skipOnDrop = true;
            videoPlayer.prepareCompleted += OnVideoPrepared;
            videoPlayer.loopPointReached += OnVideoFinished;
        }

        // Setup canvas group for video fading
        if (videoCanvasGroup == null && videoCanvas != null)
        {
            videoCanvasGroup = videoCanvas.GetComponent<CanvasGroup>();
            if (videoCanvasGroup == null)
            {
                videoCanvasGroup = videoCanvas.AddComponent<CanvasGroup>();
            }
        }

        // Start video canvas as invisible
        if (videoCanvasGroup != null)
        {
            videoCanvasGroup.alpha = 0f;
        }
        if (videoCanvas != null)
        {
            videoCanvas.SetActive(false);
        }

        // Setup video light
        if (videoLight != null)
        {
            videoLight.enabled = false;
            videoLight.intensity = videoLightIntensity;
        }

        // Start the sequence
        StartCoroutine(IntroSequence());
        StartCoroutine(FloatBot());
    }

    void Update()
    {
        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            colorSampleTimer += Time.deltaTime;
            if (colorSampleTimer >= colorSampleInterval)
            {
                colorSampleTimer = 0f;
                SampleVideoColor();
            }
        }
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
        Destroy(tex); // free memory
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

                // Only apply floating offset - don't touch rotation
                // The animation system will handle rotation
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
            // Read audio samples every frame
            botAudioSource.GetOutputData(audioSamples, 0);

            float sum = 0f;
            for (int i = 0; i < audioSamples.Length; i++)
            {
                sum += Mathf.Abs(audioSamples[i]);
            }

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
                        SetMouthState(true);    // show open mouth
                    }
                    else
                    {
                        botAnimator.Play("Idle");
                        SetMouthState(false);   // show closed mouth
                    }
                }
            }

            yield return null; // Check every frame
        }

        // Audio is finished. Reset to Idle immediately.
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

        // SEQUENCE 1: Light opens with SFX and bot appears INSTANTLY
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
            // Start with idle animation
            if (idleAnimation != null && botAnimator != null)
            {
                botAnimator.Play("Idle");
            }
        }

        yield return new WaitForSeconds(0.8f);

        // SEQUENCE 1: Initial bot talk
        yield return StartCoroutine(PlayBotAudio(initialBotAudio, initialBotPosition, initialBotAnimation));

        // SEQUENCE 2: First video with timed audio
        yield return StartCoroutine(PlayVideoWithTimedAudio(firstVideo, firstVideoAudios));

        // SEQUENCE 3: Second bot talk
        yield return StartCoroutine(PlayBotAudio(secondBotAudio, secondBotPosition, secondBotAnimation));

        // SEQUENCE 4: Third bot talk
        yield return StartCoroutine(PlayBotAudio(thirdBotAudio, thirdBotPosition, thirdBotAnimation));

        // SEQUENCE 5: Strand sequences (array)
        foreach (StrandSequence strand in strandSequences)
        {
            yield return StartCoroutine(PlayStrandSequence(strand));
        }

        // SEQUENCE 6: Fourth bot talk (no video)
        yield return StartCoroutine(PlayBotAudio(fourthBotAudio, fourthBotPosition, fourthBotAnimation));

        // SEQUENCE 7: Fifth bot talk (now plays video & audio together)
        yield return StartCoroutine(PlayBotAudioWithVideo(fifthBotAudio, fifthBotVideo, fifthBotPosition, fifthBotAnimation));

        // SEQUENCE 8: Sixth sequence array with video (each audio & video start together)
        foreach (BotTalkWithVideo sequence in sixthSequenceArray)
        {
            yield return StartCoroutine(PlayBotTalkWithVideo(sequence));
        }

        // SEQUENCE 9: Final bot talk
        yield return StartCoroutine(PlayBotAudio(finalBotAudio, finalBotPosition, finalBotAnimation));

        // Fade to black
        yield return StartCoroutine(FadeToBlack());

        Debug.Log("Intro sequence complete!");
        // UnityEngine.SceneManagement.SceneManager.LoadScene("NextScene");
    }

    IEnumerator PlayBotAudio(AudioClip audio, Transform targetPosition, AnimationClip transitionAnimation)
    {
        if (audio == null) yield break;

        // Move bot and (optionally) play transition animation concurrently.
        if (targetPosition != null && botModel != null)
        {
            // Use 1f as default move duration when no transitionAnimation provided.
            yield return StartCoroutine(MoveAndPlayTransition(targetPosition, 1f, transitionAnimation));
            botBasePosition = targetPosition.position;
        }
        else if (transitionAnimation != null && botModel != null)
        {
            // If there's no move but a transition animation was given, just play the transition.
            // pass current transform as target so coroutine has a valid non-null Transform
            yield return StartCoroutine(MoveAndPlayTransition(botModel.transform, 0.01f, transitionAnimation));
        }

        // Play audio and start monitoring
        if (botAudioSource != null)
        {
            botAudioSource.PlayOneShot(audio);
            Debug.Log("Playing bot audio: " + audio.name);

            // Start monitoring audio for idle/talking switch
            if (audioMonitorCoroutine != null)
            {
                StopCoroutine(audioMonitorCoroutine);
            }
            audioMonitorCoroutine = StartCoroutine(MonitorAudioAndSwitchAnimation());

            yield return new WaitForSeconds(audio.length);
        }

        // Ensure we're back to idle after audio finishes
        if (idleAnimation != null && botAnimator != null)
        {
            botAnimator.Play("Idle");
        }
        isCurrentlyTalking = false;

        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator PlayBotAudioWithVideo(AudioClip audio, VideoClip video, Transform targetPosition, AnimationClip transitionAnimation)
    {
        // This coroutine moves the bot (if needed), then starts the provided video and audio at the same time
        // and waits until *both* have finished before continuing. It also uses the same fade & RT-clearing logic.

        if (video == null && audio == null) yield break;

        // Move / transition first
        if (targetPosition != null && botModel != null)
        {
            yield return StartCoroutine(MoveAndPlayTransition(targetPosition, 1f, transitionAnimation));
            botBasePosition = targetPosition.position;
        }
        else if (transitionAnimation != null && botModel != null)
        {
            yield return StartCoroutine(MoveAndPlayTransition(botModel.transform, 0.01f, transitionAnimation));
        }

        // If there's a video, prepare & clear RT to avoid showing last frame
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

                // Clear the rendertexture so last frame doesn't persist
                ClearVideoRenderTexture();

                videoPlayer.Stop();
                videoPlayer.clip = video;
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

        // Play audio concurrently
        if (audio != null && botAudioSource != null)
        {
            botAudioSource.PlayOneShot(audio);
            if (audioMonitorCoroutine != null) StopCoroutine(audioMonitorCoroutine);
            audioMonitorCoroutine = StartCoroutine(MonitorAudioAndSwitchAnimation());
            Debug.Log("Playing bot audio (combined): " + audio.name);
        }

        // Wait until both are finished (or whichever exists)
        bool isVideoPlaying() => video != null && videoPlayer != null && videoPlayer.isPlaying;
        bool isAudioPlaying() => botAudioSource != null && botAudioSource.isPlaying;

        // Wait until neither is playing
        while (isVideoPlaying() || isAudioPlaying())
        {
            yield return null;
        }

        // Ensure bot returns to idle
        if (idleAnimation != null && botAnimator != null)
            botAnimator.Play("Idle");
        isCurrentlyTalking = false;

        // Fade out video and disable light
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

        // Determine movement duration: use transition clip length when provided (to sync).
        float moveDuration = defaultDuration;
        if (transitionClip != null && transitionClip.length > 0f)
        {
            moveDuration = transitionClip.length;
        }

        // Prepare/assign transition clip to animator under a unique name
        string transName = null;
        if (transitionClip != null && botAnimator != null)
        {
            transName = "__TRANS_" + transitionClip.name;
            // Animation.GetClip returns AnimationClip or null
            if (botAnimator.GetClip(transName) == null)
            {
                botAnimator.AddClip(transitionClip, transName);
            }
        }

        // Start the animation if available
        if (transName != null && botAnimator != null)
        {
            // Stop other animations and play transition
            botAnimator.Stop();
            botAnimator.Play(transName);
            SetMouthState(false); // ensure mouth default while transitioning
        }

        // Movement interpolation over moveDuration
        Vector3 startPos = botModel.transform.position;
        Quaternion startRot = botModel.transform.rotation;
        Vector3 targetPos = target.position;
        Quaternion targetRot = target.rotation;

        float elapsed = 0f;
        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / moveDuration);

            // Update base position for floating (so FloatBot offsets correctly)
            botBasePosition = Vector3.Lerp(startPos, targetPos, t);

            // Apply rotation to the bot model
            if (botModel != null)
                botModel.transform.rotation = Quaternion.Slerp(startRot, targetRot, t);

            yield return null;
        }

        // Ensure final values
        botBasePosition = targetPos;
        if (botModel != null)
            botModel.transform.rotation = targetRot;

        // Return to Idle after transition completes
        if (idleAnimation != null && botAnimator != null)
        {
            botAnimator.Play("Idle");
        }
    }

    IEnumerator PlayVideoWithTimedAudio(VideoClip video, TimedAudioWithPosition[] timedAudios)
    {
        // This method keeps the same behavior but clears the render texture BEFORE preparing
        if (video == null) yield break;

        if (videoCanvas == null || videoPlayer == null)
        {
            Debug.LogError("Video canvas or VideoPlayer missing");
            yield break;
        }

        // Enable canvas and set alpha to 0 first
        videoCanvas.SetActive(true);
        if (videoCanvasGroup != null)
        {
            videoCanvasGroup.alpha = 0f;
        }

        // Clear previous render texture to avoid showing previous frame
        ClearVideoRenderTexture();

        // Prepare video (must be active to prepare)
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

        // Enable light
        if (videoLight != null)
        {
            videoLight.enabled = true;
        }

        // Fade directional light IN
        if (directionalLight != null)
        {
            dirLightOriginalIntensity = directionalLight.intensity;
            StartCoroutine(FadeDirectionalLight(0f, dirLightTargetIntensity, dirLightFadeDuration));
        }


        // Fade in video
        yield return StartCoroutine(FadeInVideo());

        // Play video
        videoPlayer.Play();
        Debug.Log("Video playing: " + video.name);

        // Play timed audio during video using videoPlayer.time for timing
        int currentAudioIndex = 0;

        while (videoPlayer.isPlaying)
        {
            double currentVideoTime = videoPlayer.time;

            // Check if we need to play next audio
            if (timedAudios != null && currentAudioIndex < timedAudios.Length)
            {
                TimedAudioWithPosition timedAudio = timedAudios[currentAudioIndex];
                if (currentVideoTime >= timedAudio.timestamp)
                {
                    // Move + play transition concurrently so they finish together
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
                        // Play animation in-place
                        yield return StartCoroutine(MoveAndPlayTransition(botModel.transform, 0.01f, timedAudio.botAnimation));
                    }

                    // Play the timed audio AFTER move/animation
                    if (timedAudio.audioClip != null && botAudioSource != null)
                    {
                        botAudioSource.PlayOneShot(timedAudio.audioClip);
                        Debug.Log("Playing timed audio: " + timedAudio.audioClip.name);

                        if (audioMonitorCoroutine != null)
                        {
                            StopCoroutine(audioMonitorCoroutine);
                        }
                        audioMonitorCoroutine = StartCoroutine(MonitorAudioAndSwitchAnimation());
                    }

                    currentAudioIndex++;
                }
            }

            yield return null;
        }

        Debug.Log("Video finished");

        // Ensure bot returns to idle after video
        if (idleAnimation != null && botAnimator != null)
        {
            botAnimator.Play("Idle");
        }
        isCurrentlyTalking = false;

        // Fade out video and disable light
        yield return StartCoroutine(FadeOutVideo());

        if (videoLight != null)
        {
            videoLight.enabled = false;
        }

        // Fade directional light OUT back to original intensity
        if (directionalLight != null)
        {
            StartCoroutine(FadeDirectionalLight(directionalLight.intensity, dirLightOriginalIntensity, dirLightFadeDuration));
        }


        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator PlayStrandSequence(StrandSequence strand)
    {
        Debug.Log("Playing strand: " + strand.strandName);

        // Play bot audio (this will wait for move and optional transition animation)
        yield return StartCoroutine(PlayBotAudio(strand.botAudio, strand.botPosition, strand.botAnimation));

        // Play video with timed audio (timed audios will also wait for their transition animations)
        yield return StartCoroutine(PlayVideoWithTimedAudio(strand.video, strand.timedAudios));
    }

    IEnumerator PlayBotTalkWithVideo(BotTalkWithVideo sequence)
    {
        // Modified so the bot audio and the video start together.
        Debug.Log("Playing bot talk with video sequence (concurrent audio+video)");

        if (sequence == null) yield break;

        // Move + optional transition first (to position the bot)
        if (sequence.botPosition != null && botModel != null)
        {
            yield return StartCoroutine(MoveAndPlayTransition(sequence.botPosition, 1f, sequence.botAnimation));
            botBasePosition = sequence.botPosition.position;
        }
        else if (sequence.botAnimation != null && botModel != null)
        {
            yield return StartCoroutine(MoveAndPlayTransition(botModel.transform, 0.01f, sequence.botAnimation));
        }

        // Prepare and clear RT to avoid last-frame flash
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

            // Start video
            videoPlayer.Play();
            Debug.Log("Video playing (sequence array): " + sequence.video.name);
        }

        // Start main bot audio at the same time as the video
        if (sequence.botAudio != null && botAudioSource != null)
        {
            botAudioSource.PlayOneShot(sequence.botAudio);
            if (audioMonitorCoroutine != null) StopCoroutine(audioMonitorCoroutine);
            audioMonitorCoroutine = StartCoroutine(MonitorAudioAndSwitchAnimation());
            Debug.Log("Playing bot audio (sequence array): " + sequence.botAudio.name);
        }

        // While the video plays, handle timed audios relative to videoPlayer.time (same logic as PlayVideoWithTimedAudio)
        int currentAudioIndex = 0;
        if (sequence.timedAudios != null && sequence.timedAudios.Length > 0 && videoPlayer != null)
        {
            while (videoPlayer.isPlaying)
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

                        if (timedAudio.audioClip != null && botAudioSource != null)
                        {
                            botAudioSource.PlayOneShot(timedAudio.audioClip);
                            if (audioMonitorCoroutine != null)
                            {
                                StopCoroutine(audioMonitorCoroutine);
                            }
                            audioMonitorCoroutine = StartCoroutine(MonitorAudioAndSwitchAnimation());
                        }

                        currentAudioIndex++;
                    }
                }

                yield return null;
            }
        }
        else
        {
            // If no timed audios, just wait until both video and main audio finish
            bool isVideoPlaying() => sequence.video != null && videoPlayer != null && videoPlayer.isPlaying;
            bool isAudioPlaying() => botAudioSource != null && botAudioSource.isPlaying;

            while (isVideoPlaying() || isAudioPlaying())
            {
                yield return null;
            }
        }

        // End of this sequence: make sure idle state
        if (idleAnimation != null && botAnimator != null)
        {
            botAnimator.Play("Idle");
        }
        isCurrentlyTalking = false;

        // Fade out video and disable light
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
        }
    }

    // Helper: clear the assigned render texture so the last frame doesn't show.
    void ClearVideoRenderTexture()
    {
        if (videoRenderTexture == null) return;

        RenderTexture current = RenderTexture.active;
        RenderTexture.active = videoRenderTexture;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = current;
    }
}

// Timed audio with bot position and animation
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
}

// Strand sequence (bot talk + video with timed audio)
[System.Serializable]
public class StrandSequence
{
    [Tooltip("Name for organization")]
    public string strandName;

    [Tooltip("Bot audio before video")]
    public AudioClip botAudio;

    [Tooltip("Bot position")]
    public Transform botPosition;

    [Tooltip("Bot animation (Optional transition that will play BEFORE audio)")]
    public AnimationClip botAnimation;

    [Tooltip("Video to play")]
    public VideoClip video;

    [Tooltip("Timed audio clips during video")]
    public TimedAudioWithPosition[] timedAudios;
}

// Bot talk with video sequence
[System.Serializable]
public class BotTalkWithVideo
{
    [Tooltip("Bot audio")]
    public AudioClip botAudio;

    [Tooltip("Bot position")]
    public Transform botPosition;

    [Tooltip("Bot animation (Optional transition that will play BEFORE audio)")]
    public AnimationClip botAnimation;

    [Tooltip("Video to play")]
    public VideoClip video;

    [Tooltip("Timed audio clips during video")]
    public TimedAudioWithPosition[] timedAudios;
}
