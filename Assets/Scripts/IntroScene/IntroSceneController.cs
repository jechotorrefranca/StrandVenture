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
    public Light directionalLight;
    public RenderTexture videoRenderTexture;
    [Header("Directional Light Fade Settings")]
    public float dirLightFadeDuration = 1.2f;
    public float dirLightTargetIntensity = 1.2f;

    private float dirLightOriginalIntensity = 0f;


    [Range(0f, 10f)] public float colorLerpSpeed = 5f;

    private float colorSampleTimer = 0f;
    private float colorSampleInterval = 0.1f;

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
    public VideoClip fifthBotVideo;

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

    private Texture2D fadeTexture;
    private float fadeAlpha = 0f;

    private Vector3 botBasePosition;
    private float floatTimer = 0f;

    private Animation botAnimator;

    private float[] audioSamples = new float[128];
    private bool isCurrentlyTalking = false;
    private Coroutine audioMonitorCoroutine;

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

        yield return StartCoroutine(PlayBotAudio(initialBotAudio, initialBotPosition, initialBotAnimation));

        yield return StartCoroutine(PlayVideoWithTimedAudio(firstVideo, firstVideoAudios));

        yield return StartCoroutine(PlayBotAudio(secondBotAudio, secondBotPosition, secondBotAnimation));

        yield return StartCoroutine(PlayBotAudio(thirdBotAudio, thirdBotPosition, thirdBotAnimation));

        foreach (StrandSequence strand in strandSequences)
        {
            yield return StartCoroutine(PlayStrandSequence(strand));
        }

        yield return StartCoroutine(PlayBotAudio(fourthBotAudio, fourthBotPosition, fourthBotAnimation));

        yield return StartCoroutine(PlayBotAudioWithVideo(fifthBotAudio, fifthBotVideo, fifthBotPosition, fifthBotAnimation));

        foreach (BotTalkWithVideo sequence in sixthSequenceArray)
        {
            yield return StartCoroutine(PlayBotTalkWithVideo(sequence));
        }

        yield return StartCoroutine(PlayBotAudio(finalBotAudio, finalBotPosition, finalBotAnimation));

        yield return StartCoroutine(FadeToBlack());

        Debug.Log("Intro sequence complete!");
        SceneLoader.LoadSceneWithLoading("UserInfoScene");

    }

    IEnumerator PlayBotAudio(AudioClip audio, Transform targetPosition, AnimationClip transitionAnimation)
    {
        if (audio == null) yield break;

        if (targetPosition != null && botModel != null)
        {
            yield return StartCoroutine(MoveAndPlayTransition(targetPosition, 1f, transitionAnimation));
            botBasePosition = targetPosition.position;
        }
        else if (transitionAnimation != null && botModel != null)
        {
            yield return StartCoroutine(MoveAndPlayTransition(botModel.transform, 0.01f, transitionAnimation));
        }

        if (botAudioSource != null)
        {
            botAudioSource.PlayOneShot(audio);
            Debug.Log("Playing bot audio: " + audio.name);

            if (audioMonitorCoroutine != null)
            {
                StopCoroutine(audioMonitorCoroutine);
            }
            audioMonitorCoroutine = StartCoroutine(MonitorAudioAndSwitchAnimation());

            yield return new WaitForSeconds(audio.length);
        }

        if (idleAnimation != null && botAnimator != null)
        {
            botAnimator.Play("Idle");
        }
        isCurrentlyTalking = false;

        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator PlayBotAudioWithVideo(AudioClip audio, VideoClip video, Transform targetPosition, AnimationClip transitionAnimation)
    {

        if (video == null && audio == null) yield break;

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

        if (audio != null && botAudioSource != null)
        {
            botAudioSource.PlayOneShot(audio);
            if (audioMonitorCoroutine != null) StopCoroutine(audioMonitorCoroutine);
            audioMonitorCoroutine = StartCoroutine(MonitorAudioAndSwitchAnimation());
            Debug.Log("Playing bot audio (combined): " + audio.name);
        }

        bool isVideoPlaying() => video != null && videoPlayer != null && videoPlayer.isPlaying;
        bool isAudioPlaying() => botAudioSource != null && botAudioSource.isPlaying;

        while (isVideoPlaying() || isAudioPlaying())
        {
            yield return null;
        }

        if (idleAnimation != null && botAnimator != null)
            botAnimator.Play("Idle");
        isCurrentlyTalking = false;

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
        while (elapsed < moveDuration)
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

        while (videoPlayer.isPlaying)
        {
            double currentVideoTime = videoPlayer.time;

            if (timedAudios != null && currentAudioIndex < timedAudios.Length)
            {
                TimedAudioWithPosition timedAudio = timedAudios[currentAudioIndex];
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
        Debug.Log("Playing strand: " + strand.strandName);

        yield return StartCoroutine(PlayBotAudio(strand.botAudio, strand.botPosition, strand.botAnimation));

        yield return StartCoroutine(PlayVideoWithTimedAudio(strand.video, strand.timedAudios));
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

        if (sequence.botAudio != null && botAudioSource != null)
        {
            botAudioSource.PlayOneShot(sequence.botAudio);
            if (audioMonitorCoroutine != null) StopCoroutine(audioMonitorCoroutine);
            audioMonitorCoroutine = StartCoroutine(MonitorAudioAndSwitchAnimation());
            Debug.Log("Playing bot audio (sequence array): " + sequence.botAudio.name);
        }

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
}

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
