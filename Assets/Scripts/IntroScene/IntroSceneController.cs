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

    [Header("Animation Settings")]
    [Tooltip("How long the light takes to fade in")]
    public float lightFadeInDuration = 2f;

    [Tooltip("Target light intensity")]
    public float targetLightIntensity = 5f; // Increased for better visibility

    [Header("Light & Sound Effects")]
    [Tooltip("Sound effect when lights turn on (plays with spotlight)")]
    public AudioClip lightOpeningSFX;

    [Tooltip("AudioSource for sound effects (separate from bot voice)")]
    public AudioSource sfxAudioSource;

    [Header("Intro Audio")]
    [Tooltip("Audio that plays right after bot appears, before talk sequences")]
    public AudioClip introAudio;

    [Header("Audio/Video Sequences")]
    [Tooltip("Array of audio-video pairs for the talk sequences")]
    public TalkSequence[] talkSequences;

    [Header("Final Sequence")]
    [Tooltip("Final audio before fade out")]
    public AudioClip finalAudio;

    [Tooltip("Final video before fade out")]
    public VideoClip finalVideo;

    [Tooltip("Duration of fade to black")]
    public float fadeOutDuration = 2f;

    // For screen fading
    private Texture2D fadeTexture;
    private float fadeAlpha = 1f; // Start at black

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
            // Settings for large empty space echo
            reverb.reverbPreset = AudioReverbPreset.Auditorium;
            reverb.dryLevel = 0; // Full wet signal for maximum echo
            reverb.room = -1000;
            reverb.roomHF = -100;
            reverb.decayTime = 5.0f; // Long echo
        }

        // Setup reverb for SFX audio source too
        if (sfxAudioSource != null)
        {
            AudioReverbFilter reverb = sfxAudioSource.GetComponent<AudioReverbFilter>();
            if (reverb == null)
            {
                reverb = sfxAudioSource.gameObject.AddComponent<AudioReverbFilter>();
            }
            reverb.reverbPreset = AudioReverbPreset.Auditorium;
            reverb.dryLevel = -500; // Less echo for SFX
            reverb.room = -1000;
            reverb.decayTime = 3.0f;
        }

        // Prepare video player
        if (videoPlayer != null)
        {
            // Make sure video player starts in correct state
            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.skipOnDrop = true;

            videoPlayer.prepareCompleted += OnVideoPrepared;
            videoPlayer.loopPointReached += OnVideoFinished;

            Debug.Log("Video Player initialized - Render Mode: " + videoPlayer.renderMode);
            Debug.Log("Target Texture: " + (videoPlayer.targetTexture != null ? videoPlayer.targetTexture.name : "NULL"));
        }
        else
        {
            Debug.LogError("Video Player is null! Make sure it's assigned in the inspector.");
        }

        // Start the sequence
        StartCoroutine(IntroSequence());
    }

    void OnVideoPrepared(VideoPlayer source)
    {
        Debug.Log("Video prepared and ready to play");
    }

    void OnVideoFinished(VideoPlayer source)
    {
        Debug.Log("Video finished playing");
    }

    IEnumerator IntroSequence()
    {
        // Start with black screen
        yield return new WaitForSeconds(1f);

        // Fade from black
        yield return StartCoroutine(FadeFromBlack(1f));

        // SUDDEN spotlight turn on and bot appearance (instant, not gradual)
        if (ufoLight != null)
        {
            ufoLight.intensity = targetLightIntensity; // Instant light on
        }

        // Play light opening sound effect
        if (lightOpeningSFX != null && sfxAudioSource != null)
        {
            sfxAudioSource.PlayOneShot(lightOpeningSFX);
            Debug.Log("Playing light opening SFX");
        }

        if (botModel != null)
        {
            botModel.SetActive(true); // Bot appears instantly
        }

        // Wait a moment for dramatic effect
        yield return new WaitForSeconds(0.8f);

        // Play intro audio
        if (introAudio != null && botAudioSource != null)
        {
            Debug.Log("Playing intro audio: " + introAudio.name);
            botAudioSource.PlayOneShot(introAudio);
            yield return new WaitForSeconds(introAudio.length);
            yield return new WaitForSeconds(0.5f); // Small pause after intro
        }

        // Now play all talk sequences
        foreach (TalkSequence sequence in talkSequences)
        {
            yield return StartCoroutine(PlayTalkSequence(sequence));
        }

        // Play final sequence
        yield return StartCoroutine(PlayFinalSequence());

        // Fade to black
        yield return StartCoroutine(FadeToBlack());

        // Here you can load the next scene
        Debug.Log("Intro sequence complete!");
        // UnityEngine.SceneManagement.SceneManager.LoadScene("NextScene");
    }

    IEnumerator PlayTalkSequence(TalkSequence sequence)
    {
        Debug.Log("Playing sequence: " + sequence.sequenceName);

        // Play audio with timestamps
        float lastTimestamp = 0f;
        foreach (TimedAudio timedAudio in sequence.timedAudios)
        {
            // Wait until this timestamp (relative to last)
            float waitTime = timedAudio.timestamp - lastTimestamp;
            if (waitTime > 0)
            {
                yield return new WaitForSeconds(waitTime);
            }

            // Play the audio
            if (botAudioSource != null && timedAudio.audioClip != null)
            {
                botAudioSource.PlayOneShot(timedAudio.audioClip);
                Debug.Log("Playing audio: " + timedAudio.audioClip.name);
            }

            lastTimestamp = timedAudio.timestamp;
        }

        // Wait for last audio to finish
        if (sequence.timedAudios.Length > 0)
        {
            TimedAudio lastAudio = sequence.timedAudios[sequence.timedAudios.Length - 1];
            if (lastAudio.audioClip != null)
            {
                yield return new WaitForSeconds(lastAudio.audioClip.length);
            }
        }

        // Small pause before video
        yield return new WaitForSeconds(0.5f);

        // Show and play video
        if (sequence.videoClip != null && videoPlayer != null && videoCanvas != null)
        {
            Debug.Log("Preparing to play video: " + sequence.videoClip.name);

            // Make sure video player is stopped first
            videoPlayer.Stop();

            // Activate canvas
            videoCanvas.SetActive(true);
            Debug.Log("Canvas activated");

            // Set the clip
            videoPlayer.clip = sequence.videoClip;

            // Important: Set playback speed and other settings
            videoPlayer.playbackSpeed = 1f;
            videoPlayer.isLooping = false;

            // Prepare the video first
            videoPlayer.Prepare();
            Debug.Log("Video preparing...");

            // Wait for video to be prepared with timeout
            float prepareTimeout = 10f;
            float prepareTimer = 0f;
            while (!videoPlayer.isPrepared && prepareTimer < prepareTimeout)
            {
                prepareTimer += Time.deltaTime;
                yield return null;
            }

            if (!videoPlayer.isPrepared)
            {
                Debug.LogError("Video failed to prepare after " + prepareTimeout + " seconds!");
                videoCanvas.SetActive(false);
                yield break;
            }

            Debug.Log("Video prepared successfully, now playing...");
            videoPlayer.Play();

            // Double check it's playing
            yield return new WaitForSeconds(0.1f);
            if (!videoPlayer.isPlaying)
            {
                Debug.LogError("Video Play() was called but video is not playing!");
                Debug.LogError("Video Player enabled: " + videoPlayer.enabled);
                Debug.LogError("Video clip: " + (videoPlayer.clip != null ? videoPlayer.clip.name : "NULL"));
            }

            // Wait for video to finish
            while (videoPlayer.isPlaying)
            {
                yield return null;
            }

            Debug.Log("Video finished");

            // Hide video screen
            videoCanvas.SetActive(false);
        }
        else
        {
            if (sequence.videoClip == null)
            {
                Debug.LogWarning("Video clip is null for sequence: " + sequence.sequenceName);
            }
            if (videoPlayer == null)
            {
                Debug.LogWarning("Video player is null!");
            }
            if (videoCanvas == null)
            {
                Debug.LogWarning("Video canvas is null!");
            }
        }

        // Pause before next sequence
        yield return new WaitForSeconds(1f);
    }

    IEnumerator PlayFinalSequence()
    {
        Debug.Log("Playing final sequence");

        // Play final audio
        if (botAudioSource != null && finalAudio != null)
        {
            botAudioSource.PlayOneShot(finalAudio);
            yield return new WaitForSeconds(finalAudio.length);
        }

        yield return new WaitForSeconds(0.5f);

        // Play final video
        if (finalVideo != null && videoPlayer != null && videoCanvas != null)
        {
            Debug.Log("Playing final video");

            videoPlayer.Stop();
            videoCanvas.SetActive(true);
            videoPlayer.clip = finalVideo;
            videoPlayer.playbackSpeed = 1f;
            videoPlayer.isLooping = false;

            // Prepare the video
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
                Debug.LogError("Final video failed to prepare!");
                videoCanvas.SetActive(false);
                yield break;
            }

            videoPlayer.Play();

            // Wait for video to finish
            while (videoPlayer.isPlaying)
            {
                yield return null;
            }

            Debug.Log("Final video finished");
        }

        yield return new WaitForSeconds(1f);
    }

    IEnumerator FadeFromBlack(float duration = 1f)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            fadeAlpha = 1f - (elapsed / duration);
            yield return null;
        }

        fadeAlpha = 0f;
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
        // Unsubscribe from events
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.loopPointReached -= OnVideoFinished;
        }
    }
}

// Custom class for talk sequences
[System.Serializable]
public class TalkSequence
{
    [Tooltip("Name for organization (e.g., 'Strand 1', 'Introduction')")]
    public string sequenceName;

    [Tooltip("Array of audio clips with their timestamps")]
    public TimedAudio[] timedAudios;

    [Tooltip("Video to play after audio")]
    public VideoClip videoClip;
}

// Custom class for timed audio
[System.Serializable]
public class TimedAudio
{
    [Tooltip("Time in seconds when this audio should start (from beginning of sequence)")]
    public float timestamp;

    [Tooltip("The audio clip to play")]
    public AudioClip audioClip;
}