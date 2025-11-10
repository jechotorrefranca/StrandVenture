using UnityEngine;
using UnityEngine.Video;
using System.Collections;

// Place this script on an empty GameObject called "SceneController"
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

    [Tooltip("AudioSource component on the bot")]
    public AudioSource botAudioSource;

    [Tooltip("The Canvas GameObject with Video Player component")]
    public GameObject videoCanvas;

    [Tooltip("Reference to the Video Player component")]
    public VideoPlayer videoPlayer;

    [Tooltip("CanvasGroup for fading video in/out")]
    public CanvasGroup videoCanvasGroup;

    [Header("Animation Settings")]
    [Tooltip("Target light intensity")]
    public float targetLightIntensity = 5f;

    [Tooltip("Bot floating animation settings")]
    public float floatAmplitude = 0.3f;
    public float floatSpeed = 1f;

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

    [Header("Sequence 7: Fifth Bot Talk")]
    [Tooltip("Audio with bot movement")]
    public AudioClip fifthBotAudio;
    public Transform fifthBotPosition;
    public AnimationClip fifthBotAnimation;

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
    private float fadeAlpha = 0f; // Start transparent (no black screen)

    // For bot floating animation
    private Vector3 botOriginalPosition;
    private float floatTimer = 0f;

    // For bot animation
    private Animation botAnimator;

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

        // Get bot animator component
        if (botModel != null)
        {
            botAnimator = botModel.GetComponent<Animation>();
            if (botAnimator == null)
            {
                botAnimator = botModel.AddComponent<Animation>();
            }
            botOriginalPosition = botModel.transform.position;
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

        // Setup canvas group for video fading if not assigned
        if (videoCanvasGroup == null && videoCanvas != null)
        {
            videoCanvasGroup = videoCanvas.GetComponent<CanvasGroup>();
            if (videoCanvasGroup == null)
            {
                videoCanvasGroup = videoCanvas.AddComponent<CanvasGroup>();
            }
        }

        // Start the sequence
        StartCoroutine(IntroSequence());
        StartCoroutine(FloatBot());
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
                botModel.transform.position = botOriginalPosition + new Vector3(0, yOffset, 0);
            }
            yield return null;
        }
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

        // SEQUENCE 7: Fifth bot talk
        yield return StartCoroutine(PlayBotAudio(fifthBotAudio, fifthBotPosition, fifthBotAnimation));

        // SEQUENCE 8: Sixth sequence array with video
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

    IEnumerator PlayBotAudio(AudioClip audio, Transform targetPosition, AnimationClip animation)
    {
        if (audio == null) yield break;

        // Move bot to position
        if (targetPosition != null && botModel != null)
        {
            yield return StartCoroutine(MoveBotToPosition(targetPosition.position, 1f));
            botOriginalPosition = targetPosition.position;
        }

        // Play animation
        if (animation != null && botAnimator != null)
        {
            Debug.Log("Playing bot animation: " + animation.name);
            botAnimator.AddClip(animation, animation.name);
            botAnimator.Play(animation.name);
        }

        // Play audio
        if (botAudioSource != null)
        {
            botAudioSource.PlayOneShot(audio);
            Debug.Log("Playing bot audio: " + audio.name);
            yield return new WaitForSeconds(audio.length);
        }

        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator MoveBotToPosition(Vector3 targetPos, float duration)
    {
        Vector3 startPos = botModel.transform.position;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            botModel.transform.position = Vector3.Lerp(startPos, targetPos, t);
            yield return null;
        }

        botModel.transform.position = targetPos;
    }

    IEnumerator PlayVideoWithTimedAudio(VideoClip video, TimedAudioWithPosition[] timedAudios)
    {
        if (video == null) yield break;

        // Fade in video
        yield return StartCoroutine(FadeInVideo());

        // Prepare and play video
        videoPlayer.Stop();
        videoCanvas.SetActive(true);
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
            yield return StartCoroutine(FadeOutVideo());
            yield break;
        }

        videoPlayer.Play();
        Debug.Log("Video playing: " + video.name);

        // Play timed audio during video
        float videoStartTime = Time.time;
        int currentAudioIndex = 0;

        while (videoPlayer.isPlaying)
        {
            float currentVideoTime = Time.time - videoStartTime;

            // Check if we need to play next audio
            if (currentAudioIndex < timedAudios.Length)
            {
                TimedAudioWithPosition timedAudio = timedAudios[currentAudioIndex];
                if (currentVideoTime >= timedAudio.timestamp)
                {
                    // Move bot if position specified
                    if (timedAudio.botPosition != null && botModel != null)
                    {
                        StartCoroutine(MoveBotToPosition(timedAudio.botPosition.position, 0.5f));
                        botOriginalPosition = timedAudio.botPosition.position;
                    }

                    // Play animation
                    if (timedAudio.botAnimation != null && botAnimator != null)
                    {
                        botAnimator.AddClip(timedAudio.botAnimation, timedAudio.botAnimation.name);
                        botAnimator.Play(timedAudio.botAnimation.name);
                    }

                    // Play audio
                    if (timedAudio.audioClip != null && botAudioSource != null)
                    {
                        botAudioSource.PlayOneShot(timedAudio.audioClip);
                        Debug.Log("Playing timed audio: " + timedAudio.audioClip.name);
                    }

                    currentAudioIndex++;
                }
            }

            yield return null;
        }

        Debug.Log("Video finished");

        // Fade out video
        yield return StartCoroutine(FadeOutVideo());

        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator PlayStrandSequence(StrandSequence strand)
    {
        Debug.Log("Playing strand: " + strand.strandName);

        // Play bot audio
        yield return StartCoroutine(PlayBotAudio(strand.botAudio, strand.botPosition, strand.botAnimation));

        // Play video with timed audio
        yield return StartCoroutine(PlayVideoWithTimedAudio(strand.video, strand.timedAudios));
    }

    IEnumerator PlayBotTalkWithVideo(BotTalkWithVideo sequence)
    {
        Debug.Log("Playing bot talk with video sequence");

        // Play bot audio
        yield return StartCoroutine(PlayBotAudio(sequence.botAudio, sequence.botPosition, sequence.botAnimation));

        // Play video with timed audio
        yield return StartCoroutine(PlayVideoWithTimedAudio(sequence.video, sequence.timedAudios));
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
            videoCanvas.SetActive(false);
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
        videoCanvas.SetActive(false);
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
        if (fadeAlpha > 0f)
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

    [Tooltip("Bot animation during this audio")]
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

    [Tooltip("Bot animation")]
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

    [Tooltip("Bot animation")]
    public AnimationClip botAnimation;

    [Tooltip("Video to play")]
    public VideoClip video;

    [Tooltip("Timed audio clips during video")]
    public TimedAudioWithPosition[] timedAudios;
}