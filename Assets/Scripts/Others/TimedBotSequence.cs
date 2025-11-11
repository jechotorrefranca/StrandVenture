using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(AudioSource))]
public class TimedBotSequence : MonoBehaviour
{
    [Header("Bot / Animation")]
    [Tooltip("Root GameObject of the bot model")]
    public GameObject botModel;

    [Tooltip("Legacy Animation component on the bot (recommended for direct AnimationClip playback). If missing, the script will add one.")]
    public Animation botAnimation;

    [Header("Floating")]
    public float globalFloatAmplitude = 0.2f;
    public float globalFloatSpeed = 1f;
    private Vector3 botBasePosition;
    private float floatTimer;

    [Header("Overlay Fade")]
    [Tooltip("Full-screen UI Image or CanvasGroup used for fading. If using Image, assign it here.")]
    public Image overlayImage;
    [Tooltip("Duration for fade in/out")]
    public float overlayFadeDuration = 0.8f;

    [Header("Sequence")]
    [Tooltip("Time in seconds to wait before starting the sequence (after fade in)")]
    public float startDelay = 0.1f;

    [Tooltip("Name of scene to load after sequence ends. Leave empty to not load a scene (or call your own loader).")]
    public string sceneToLoadAfter = "";

    [Tooltip("Small extra delay after the last audio/animation finishes before fading out")]
    public float endPadding = 0.35f;

    [Header("Timed Actions (order does not matter - they'll be sorted by timestamp)")]
    public TimedBotAction[] actions;

    // internal audio
    private AudioSource internalAudio;

    void Reset()
    {
        // try to guess the bot's Animation component
        if (botModel != null)
            botAnimation = botModel.GetComponentInChildren<Animation>();
    }

    void Awake()
    {
        internalAudio = GetComponent<AudioSource>();
        internalAudio.playOnAwake = false;

        if (botModel != null)
        {
            if (botAnimation == null)
            {
                botAnimation = botModel.GetComponentInChildren<Animation>();
                if (botAnimation == null)
                    botAnimation = botModel.AddComponent<Animation>(); // legacy Animation
            }

            // ensure culling doesn't stop the animations
            if (botAnimation != null)
                botAnimation.cullingType = AnimationCullingType.AlwaysAnimate;

            botBasePosition = botModel.transform.position;
        }

        // Sort actions by timestamp to simplify scheduling
        if (actions != null && actions.Length > 1)
        {
            actions = actions.OrderBy(a => a.timestamp).ToArray();
        }
    }

    IEnumerator Start()
    {
        // Start with overlay fully black if Image assigned
        if (overlayImage != null)
        {
            overlayImage.gameObject.SetActive(true);
            overlayImage.color = Color.black;
        }

        // Short wait to let scene initialize
        yield return null;

        // Fade overlay out (black -> clear)
        yield return StartCoroutine(FadeOverlay(false));

        // optional delay
        yield return new WaitForSeconds(startDelay);

        if (botModel != null)
            botModel.SetActive(true);

        // Start floating coroutine
        StartCoroutine(FloatBot());

        float sequenceStart = Time.time;

        // Pre-add clips to Animation component with unique names (so Play works)
        PrepareAnimationClips();

        // If no actions, just wait small moment then end
        if (actions == null || actions.Length == 0)
        {
            yield return new WaitForSeconds(0.5f);
        }
        else
        {
            // We'll start actions as their timestamps arrive. Each action triggers independently.
            int idx = 0;
            // Keep track of expected end time (timestamp + max(audioLength, animationLength))
            float sequenceExpectedEnd = 0f;

            // Launch a scheduler coroutine that triggers actions at timestamp relative to sequenceStart
            while (idx < actions.Length)
            {
                TimedBotAction action = actions[idx];
                float waitFor = sequenceStart + action.timestamp - Time.time;
                if (waitFor > 0f) yield return new WaitForSeconds(waitFor);

                // Trigger the action
                StartCoroutine(PlayAction(action));

                // update expected end
                float clipLen = Mathf.Max(
                    action.audioClip != null ? action.audioClip.length : 0f,
                    action.animationClip != null ? action.animationClip.length : 0f
                );
                sequenceExpectedEnd = Mathf.Max(sequenceExpectedEnd, action.timestamp + clipLen);

                idx++;
            }

            // Wait until the expected end time plus padding
            float remaining = (sequenceStart + sequenceExpectedEnd + endPadding) - Time.time;
            if (remaining > 0f) yield return new WaitForSeconds(remaining);
        }

        // All actions finished (approx). Fade out overlay to black
        yield return StartCoroutine(FadeOverlay(true));

        // optional: stop floating
        StopCoroutine(FloatBot());

        // Load next scene if provided (use SceneManager by default)
        if (!string.IsNullOrEmpty(sceneToLoadAfter))
        {
            // If you have a custom loader like SceneLoader.LoadSceneWithLoading, replace the next line
            SceneManager.LoadScene(sceneToLoadAfter);
        }
    }

    IEnumerator PlayAction(TimedBotAction action)
    {
        if (action == null) yield break;

        // Move bot to position if provided
        if (action.botPosition != null && botModel != null)
        {
            botBasePosition = action.botPosition.position;
            // also set immediate position (smooth move could be added)
            botModel.transform.position = botBasePosition;
            // rotation
            botModel.transform.rotation = action.botPosition.rotation;
        }

        // Play animation if exists
        if (action.animationClip != null && botAnimation != null)
        {
            string key = action.GetClipKey();
            if (botAnimation.GetClip(key) == null)
            {
                botAnimation.AddClip(action.animationClip, key);
            }

            // Play the clip (once)
            botAnimation.Play(key);
        }

        // Play audio (if assigned)
        if (action.audioClip != null && internalAudio != null)
        {
            internalAudio.PlayOneShot(action.audioClip);
        }

        yield break; // action runs independently; lengths are accounted for by scheduler
    }

    IEnumerator FloatBot()
    {
        while (true)
        {
            if (botModel != null && botModel.activeSelf)
            {
                floatTimer += Time.deltaTime * globalFloatSpeed;
                float yOffset = Mathf.Sin(floatTimer) * globalFloatAmplitude;
                Vector3 targetPos = botBasePosition + new Vector3(0f, yOffset, 0f);
                botModel.transform.position = targetPos;
            }
            yield return null;
        }
    }

    IEnumerator FadeOverlay(bool fadeToBlack)
    {
        if (overlayImage == null)
        {
            // nothing to fade
            yield break;
        }

        float elapsed = 0f;
        Color start = overlayImage.color;
        Color end = fadeToBlack ? Color.black : new Color(0, 0, 0, 0);

        // If starting from null alpha ensure color has same rgb as black
        if (start == default(Color))
            start = Color.black;

        while (elapsed < overlayFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / overlayFadeDuration);
            overlayImage.color = Color.Lerp(start, end, t);
            yield return null;
        }

        overlayImage.color = end;

        // disable image when fully clear
        if (!fadeToBlack && overlayImage.color.a <= 0.001f)
        {
            overlayImage.gameObject.SetActive(false);
        }
        else if (fadeToBlack)
        {
            overlayImage.gameObject.SetActive(true);
        }
    }

    void PrepareAnimationClips()
    {
        if (botAnimation == null || actions == null) return;

        foreach (var action in actions)
        {
            if (action == null) continue;
            if (action.animationClip == null) continue;

            string key = action.GetClipKey();
            if (botAnimation.GetClip(key) == null)
            {
                botAnimation.AddClip(action.animationClip, key);
            }
        }
    }

    [ContextMenu("Debug: Print Actions")]
    void DebugPrintActions()
    {
        if (actions == null) { Debug.Log("No actions"); return; }
        for (int i = 0; i < actions.Length; i++)
        {
            Debug.Log($"{i}: {actions[i].timestamp} s -> {actions[i].animationClip?.name} + {actions[i].audioClip?.name}");
        }
    }
}

[System.Serializable]
public class TimedBotAction
{
    [Tooltip("When (in seconds, relative to sequence start) this action should start")]
    public float timestamp;

    [Tooltip("Animation clip to play (legacy AnimationClip)")]
    public AnimationClip animationClip;

    [Tooltip("Audio to play at this timestamp")]
    public AudioClip audioClip;

    [Tooltip("Optional Transform to move the bot to before playing (instant)")]
    public Transform botPosition;

    // internal unique key generator for adding the clip to the Animation component
    public string GetClipKey()
    {
        string namePart = animationClip != null ? animationClip.name : "noanim";
        return $"__TIMED_{timestamp:F2}_{namePart}";
    }
}
