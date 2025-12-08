using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(AudioSource))]
public class TimedBotSequence : MonoBehaviour
{
    [Header("Bot / Animation")]
    public GameObject botModel;
    public Animation botAnimation;

    [Header("Idle / Talking")]
    public AnimationClip idleAnimation;
    public AnimationClip talkingAnimation;

    [Header("Mouth meshes (optional)")]
    public GameObject mouthOpenMesh;
    public GameObject mouthClosedMesh;

    [Header("Audio (for talking detection)")]
    public AudioSource botAudioSource;
    [Range(0f, 0.5f)]
    public float audioThreshold = 0.01f;
    public float audioCheckInterval = 0.05f;

    [Header("Floating")]
    public float globalFloatAmplitude = 0.2f;
    public float globalFloatSpeed = 1f;

    [Header("Sequence / Fade")]
    public float overlayFadeDuration = 0.8f;
    public float startDelay = 0.1f;
    public string sceneToLoadAfter = "";
    public float endPadding = 0.35f;

    [Header("Movement defaults")]
    public float defaultMoveDuration = 0.8f;

    [Header("Follow Player (rotation)")]
    public Transform playerTransform;
    public bool followPlayerRotation = true;
    public float lookAtSpeed = 6f;
    public bool allowFollowDuringMove = true;
    [Tooltip("Offset applied to the follow rotation in degrees (X = pitch, Y = yaw, Z = roll).")]
    public Vector3 followRotationOffsetEuler = Vector3.zero;

    [Header("Actions (play in this order)")]
    public TimedBotAction[] actions;

    [Header("Integration")]
    [Tooltip("If assigned the TimedBotSequence will call ActivateInteractables() at the end of the sequence instead of loading a scene")]
    public InteractionManager interactionManager;

    // -------------------------
    // Subtitle UI
    // -------------------------
    [Header("Subtitle UI")]
    public GameObject subtitlePanel;    // panel containing subtitle UI
    public Image subtitleBackground;    // background image
    public TMP_Text subtitleText;       // subtitle text

    // -------------------------
    // Skip UI (per-action speech)
    // -------------------------
    [Header("Skip UI")]
    public GameObject skipUIPanel;      // small "Hold Space to Skip" panel
    public Image skipFillImage;         // radial fill image
    public float skipHoldDuration = 1.5f;
    [Tooltip("Delay before skip UI appears for a talking action")]
    public float skipVisibleDelay = 1.5f;

    // skip internals
    private CanvasGroup skipCanvasGroup;
    private float skipHoldTimer = 0f;
    private bool skipActive = false;        // true when skip UI visible & usable
    private Coroutine skipShowCoroutine;

    // internals
    private AudioSource internalAudio;
    private Coroutine audioMonitorCoroutine;
    private bool isCurrentlyTalking = false;
    private float[] audioSamples = new float[256];

    private float fadeAlpha = 1f;
    private bool useGuiFade = true;

    private Vector3 botBasePosition;
    private Quaternion botBaseRotation;
    private float floatTimer;

    private Coroutine currentMoveCoroutine;
    private Coroutine currentActionCoroutine;
    private Coroutine subtitleCoroutine;

    // skip only the CURRENT action, not the whole sequence
    private bool skipCurrentActionRequested = false;

    void Reset()
    {
        if (botModel != null)
            botAnimation = botModel.GetComponentInChildren<Animation>();
    }

    void Awake()
    {
        internalAudio = GetComponent<AudioSource>();
        internalAudio.playOnAwake = false;

        if (botAudioSource == null)
            botAudioSource = internalAudio;

        // Make sure dialogue audio does NOT loop
        botAudioSource.loop = false;

        if (botModel != null)
        {
            if (botAnimation == null)
            {
                botAnimation = botModel.GetComponentInChildren<Animation>();
                if (botAnimation == null)
                    botAnimation = botModel.AddComponent<Animation>();
            }
            if (botAnimation != null)
                botAnimation.cullingType = AnimationCullingType.AlwaysAnimate;

            // Initial base transform
            botBasePosition = botModel.transform.position;
            botBaseRotation = botModel.transform.rotation;

            // If first action has a position, optionally snap there
            if (actions != null && actions.Length > 0 && actions[0] != null && actions[0].botPosition != null)
            {
                botBasePosition = actions[0].botPosition.position;
                botBaseRotation = actions[0].botPosition.rotation;
                botModel.transform.position = botBasePosition;
                botModel.transform.rotation = botBaseRotation;
            }
        }

        PrepareBuiltinClips();
        PrepareAnimationClips();

        // Subtitle UI initial state
        if (subtitlePanel != null)
            subtitlePanel.SetActive(false);
        if (subtitleBackground != null)
        {
            subtitleBackground.enabled = false;
            subtitleBackground.gameObject.SetActive(false);
        }
        if (subtitleText != null)
            subtitleText.text = "";

        // Skip UI setup
        if (skipUIPanel != null)
        {
            skipCanvasGroup = skipUIPanel.GetComponent<CanvasGroup>();
            if (skipCanvasGroup == null)
                skipCanvasGroup = skipUIPanel.AddComponent<CanvasGroup>();

            skipCanvasGroup.alpha = 0f;
            skipUIPanel.SetActive(true);   // keep active; alpha controls visibility
        }
        skipActive = false;
        skipHoldTimer = 0f;
        if (skipFillImage != null) skipFillImage.fillAmount = 0f;
    }

    IEnumerator Start()
    {
        fadeAlpha = 1f;
        skipCurrentActionRequested = false;

        if (botModel != null)
        {
            botModel.SetActive(true);
            botModel.transform.position = botBasePosition;
            botModel.transform.rotation = botBaseRotation;

            if (botAnimation != null && idleAnimation != null)
            {
                if (botAnimation.GetClip("Idle") != null)
                    botAnimation.Play("Idle");
            }
        }

        yield return null;

        // fade from black to clear
        yield return StartCoroutine(FadeOverlay(false));

        yield return new WaitForSeconds(startDelay);

        // start floating idle motion
        StartCoroutine(FloatBot());

        // Play actions in inspector order
        if (actions != null && actions.Length > 0)
        {
            for (int idx = 0; idx < actions.Length; idx++)
            {
                TimedBotAction action = actions[idx];
                if (action == null) continue;

                // reset per-action skip flag
                skipCurrentActionRequested = false;

                currentActionCoroutine = StartCoroutine(PlayAction(action));
                yield return currentActionCoroutine;
                currentActionCoroutine = null;
                // Immediately continues to next action (or end) after PlayAction returns
            }
        }
        else
        {
            yield return new WaitForSeconds(0.5f);
        }

        // optional padding at the very end
        if (endPadding > 0f)
            yield return new WaitForSeconds(endPadding);

        // at end of sequence
        if (interactionManager != null)
        {
            interactionManager.ActivateInteractables();
            yield break;
        }

        if (!string.IsNullOrEmpty(sceneToLoadAfter))
        {
            yield return StartCoroutine(FadeOverlay(true));
            SceneManager.LoadScene(sceneToLoadAfter);
        }
    }

    void Update()
    {
        // Handle skip input only when skip is active
        if (!skipActive) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.spaceKey.isPressed)
        {
            skipHoldTimer += Time.deltaTime;
            if (skipFillImage != null)
                skipFillImage.fillAmount = Mathf.Clamp01(skipHoldTimer / skipHoldDuration);

            if (skipHoldTimer >= skipHoldDuration)
            {
                SkipNow();
                skipHoldTimer = 0f;
                if (skipFillImage != null) skipFillImage.fillAmount = 0f;
            }
        }
        else if (kb.spaceKey.wasReleasedThisFrame)
        {
            skipHoldTimer = 0f;
            if (skipFillImage != null) skipFillImage.fillAmount = 0f;
        }
    }

    public void TriggerImmediateAction(TimedBotAction action)
    {
        if (action == null) return;
        StartCoroutine(PlayAction(action));
    }

    public IEnumerator TriggerImmediateActionAndWait(TimedBotAction action)
    {
        if (action == null) yield break;

        // Stop any current action
        if (currentActionCoroutine != null)
        {
            StopCoroutine(currentActionCoroutine);
            currentActionCoroutine = null;
        }

        if (currentMoveCoroutine != null)
        {
            StopCoroutine(currentMoveCoroutine);
            currentMoveCoroutine = null;
        }

        if (audioMonitorCoroutine != null)
        {
            StopCoroutine(audioMonitorCoroutine);
            audioMonitorCoroutine = null;
        }

        // Stop audio & subtitles & skip
        if (botAudioSource != null && botAudioSource.isPlaying)
        {
            botAudioSource.Stop();
        }
        StopSubtitleSequence();
        HideSkipUIImmediate();

        // Reset to idle
        if (botAnimation != null && idleAnimation != null)
        {
            botAnimation.CrossFade("Idle", 0.1f);
        }
        SetMouthState(false);

        // reset per-action skip
        skipCurrentActionRequested = false;

        // Play new action
        currentActionCoroutine = StartCoroutine(PlayAction(action));
        yield return currentActionCoroutine;
        currentActionCoroutine = null;

        // Wait for audio to finish (MonitorAudio sets audioMonitorCoroutine to null when done)
        while (audioMonitorCoroutine != null)
            yield return null;
    }

    public IEnumerator FadeToBlackAndLoad(string nextScene = null)
    {
        yield return StartCoroutine(FadeOverlay(true));
        string sceneName = !string.IsNullOrEmpty(nextScene) ? nextScene : sceneToLoadAfter;
        if (!string.IsNullOrEmpty(sceneName))
        {
            SceneLoader.LoadSceneWithLoading(sceneName);
        }
    }

    IEnumerator PlayAction(TimedBotAction action)
    {
        if (action == null) yield break;

        float animLen = action.animationClip != null ? action.animationClip.length : 0f;
        float moveDur = action.moveDuration > 0f ? action.moveDuration : (animLen > 0f ? animLen : defaultMoveDuration);

        bool hasAudio = (action.audioClip != null && botAudioSource != null);
        float audioMaxDuration = hasAudio ? action.audioClip.length + 0.25f : 0f; // safety margin

        // AUDIO BEFORE ANIMATION (if flagged)
        if (hasAudio && !action.startAudioAfterAnimation)
        {
            StartAudioForAction(action);
        }

        // ANIMATION
        if (action.animationClip != null && botAnimation != null)
        {
            string key = action.GetClipKey();
            if (botAnimation.GetClip(key) == null)
                botAnimation.AddClip(action.animationClip, key);

            botAnimation.CrossFade(key, 0.05f);
        }

        // MOVEMENT
        currentMoveCoroutine = null;
        if (action.botPosition != null && botModel != null)
        {
            bool respectRotation = action.lockRotationDuringMove || !followPlayerRotation || !allowFollowDuringMove;
            if (respectRotation)
            {
                currentMoveCoroutine = StartCoroutine(MoveAndRotate(botModel.transform.position, action.botPosition.position,
                                                                   botModel.transform.rotation, action.botPosition.rotation,
                                                                   moveDur));
            }
            else
            {
                currentMoveCoroutine = StartCoroutine(MovePositionOnly(botModel.transform.position, action.botPosition.position, moveDur));
            }
        }

        // WAIT FOR ANIMATION OR MOVE (with skip support)
        if (action.animationClip != null && animLen > 0f)
        {
            float waited = 0f;
            while (waited < animLen && !skipCurrentActionRequested)
            {
                waited += Time.deltaTime;
                yield return null;
            }
        }
        else if (currentMoveCoroutine != null)
        {
            // wait until movement coroutine sets itself to null OR skip is requested
            while (currentMoveCoroutine != null && !skipCurrentActionRequested)
            {
                yield return null;
            }
        }
        else
        {
            yield return null;
        }

        // AUDIO AFTER ANIMATION (if flagged)
        if (hasAudio && action.startAudioAfterAnimation && !skipCurrentActionRequested)
        {
            StartAudioForAction(action);
        }

        // WAIT FOR AUDIO (either it started before or after animation)
        if (hasAudio && !skipCurrentActionRequested)
        {
            float audioWaited = 0f;
            while (botAudioSource != null &&
                   botAudioSource.isPlaying &&
                   !skipCurrentActionRequested &&
                   audioWaited < audioMaxDuration)
            {
                audioWaited += Time.deltaTime;
                yield return null;
            }
        }

        // when action completes (naturally or via skip), reset animation to idle (if no other audio)
        if ((botAudioSource == null || !botAudioSource.isPlaying) && botAnimation != null && idleAnimation != null)
        {
            botAnimation.CrossFade("Idle", 0.08f);
            SetMouthState(false);
        }

        // hide subtitles & skip UI at end of this action
        StopSubtitleSequence();
        HideSkipUIImmediate();

        // reset per-action skip flag for next action
        skipCurrentActionRequested = false;
    }

    // Centralized audio + subtitles + skip for a TimedBotAction
    private void StartAudioForAction(TimedBotAction action)
    {
        if (action == null || action.audioClip == null || botAudioSource == null)
            return;

        // stop previous audio / monitor / subtitles / skip
        if (audioMonitorCoroutine != null)
        {
            StopCoroutine(audioMonitorCoroutine);
            audioMonitorCoroutine = null;
        }

        if (botAudioSource.isPlaying)
        {
            botAudioSource.Stop();
        }

        StopSubtitleSequence();
        HideSkipUIImmediate();

        // reset per-action skip
        skipCurrentActionRequested = false;

        // play new audio
        botAudioSource.clip = action.audioClip;
        botAudioSource.loop = false;
        botAudioSource.Play();

        // talk detection & mouth
        audioMonitorCoroutine = StartCoroutine(MonitorAudioAndSwitchAnimation());

        // ---- SUBTITLES (AUTO ONLY) ----
        SubtitleSegment[] segments = null;

        // Use runtime cache if we already generated them once
        if (action.runtimeSubtitles != null && action.runtimeSubtitles.Length > 0)
        {
            segments = action.runtimeSubtitles;
        }
        else if (!string.IsNullOrWhiteSpace(action.autoSubtitleText))
        {
            segments = GenerateAutoSubtitles(action);
            action.runtimeSubtitles = segments;
        }

        if (segments != null && segments.Length > 0)
        {
            subtitleCoroutine = StartCoroutine(SubtitleSequenceCoroutine(action.audioClip, segments));
        }
        // --------------------------------



        // skip UI
        if (action.allowSkip && skipCanvasGroup != null)
        {
            skipCanvasGroup.alpha = 0f;
            skipActive = false;
            skipHoldTimer = 0f;
            if (skipFillImage != null) skipFillImage.fillAmount = 0f;

            if (skipShowCoroutine != null)
            {
                StopCoroutine(skipShowCoroutine);
                skipShowCoroutine = null;
            }

            skipShowCoroutine = StartCoroutine(ShowSkipAfterDelay(skipVisibleDelay));
        }
    }

    IEnumerator MovePositionOnly(Vector3 fromPos, Vector3 toPos, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            Vector3 current = Vector3.Lerp(fromPos, toPos, smoothT);
            botModel.transform.position = current;
            botBasePosition = current;
            yield return null;
        }

        botModel.transform.position = toPos;
        botBasePosition = toPos;

        // IMPORTANT: mark movement finished so PlayAction can continue
        currentMoveCoroutine = null;
    }

    IEnumerator MoveAndRotate(Vector3 fromPos, Vector3 toPos, Quaternion fromRot, Quaternion toRot, float duration)
    {
        if (botModel == null)
        {
            botBasePosition = toPos;
            botBaseRotation = toRot;
            currentMoveCoroutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            Vector3 current = Vector3.Lerp(fromPos, toPos, smoothT);
            Quaternion currentRot = Quaternion.Slerp(fromRot, toRot, smoothT);

            botModel.transform.position = current;
            botModel.transform.rotation = currentRot;
            botBasePosition = current;
            botBaseRotation = currentRot;

            yield return null;
        }

        botModel.transform.position = toPos;
        botModel.transform.rotation = toRot;
        botBasePosition = toPos;
        botBaseRotation = toRot;

        // IMPORTANT: mark movement finished
        currentMoveCoroutine = null;
    }

    IEnumerator FloatBot()
    {
        while (true)
        {
            if (botModel != null && botModel.activeSelf)
            {
                floatTimer += Time.deltaTime * globalFloatSpeed;
                float yOffset = Mathf.Sin(floatTimer) * globalFloatAmplitude;
                Vector3 floatTarget = botBasePosition + new Vector3(0f, yOffset, 0f);
                botModel.transform.position = floatTarget;

                if (followPlayerRotation && playerTransform != null)
                {
                    if (allowFollowDuringMove || currentMoveCoroutine == null)
                    {
                        Vector3 lookDir = playerTransform.position - botModel.transform.position;
                        if (lookDir.sqrMagnitude > 0.0001f)
                        {
                            Quaternion lookRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
                            Quaternion offset = Quaternion.Euler(followRotationOffsetEuler);
                            Quaternion target = lookRot * offset;

                            botModel.transform.rotation = Quaternion.Slerp(botModel.transform.rotation, target, Time.deltaTime * lookAtSpeed);
                            botBaseRotation = botModel.transform.rotation;
                        }
                    }
                }
            }
            yield return null;
        }
    }

    IEnumerator FadeOverlay(bool fadeToBlack)
    {
        float startAlpha = fadeAlpha;
        float endAlpha = fadeToBlack ? 1f : 0f;
        float elapsed = 0f;
        while (elapsed < overlayFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / overlayFadeDuration);
            fadeAlpha = Mathf.Lerp(startAlpha, endAlpha, t);
            yield return null;
        }
        fadeAlpha = endAlpha;
    }

    void OnGUI()
    {
        if (!useGuiFade) return;
        if (fadeAlpha <= 0f) return;

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, fadeAlpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = prev;
    }

    void PrepareBuiltinClips()
    {
        if (botAnimation == null) return;

        if (idleAnimation != null)
        {
            if (botAnimation.GetClip("Idle") == null)
                botAnimation.AddClip(idleAnimation, "Idle");
            botAnimation["Idle"].wrapMode = WrapMode.Loop;
        }

        if (talkingAnimation != null)
        {
            if (botAnimation.GetClip("Talking") == null)
                botAnimation.AddClip(talkingAnimation, "Talking");
            botAnimation["Talking"].wrapMode = WrapMode.Loop;
        }

        if (idleAnimation != null)
            botAnimation.Play("Idle");

        SetMouthState(false);
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
                botAnimation.AddClip(action.animationClip, key);
        }
    }

    IEnumerator MonitorAudioAndSwitchAnimation()
    {
        if (botAudioSource == null) yield break;

        isCurrentlyTalking = false;

        while (botAudioSource.isPlaying)
        {
            yield return new WaitForSeconds(audioCheckInterval);

            botAudioSource.GetOutputData(audioSamples, 0);

            float sum = 0f;
            for (int i = 0; i < audioSamples.Length; i++)
                sum += Mathf.Abs(audioSamples[i]);

            float avg = sum / audioSamples.Length;
            bool shouldTalk = avg > audioThreshold;

            if (shouldTalk != isCurrentlyTalking)
            {
                isCurrentlyTalking = shouldTalk;
                if (isCurrentlyTalking)
                {
                    if (botAnimation != null && talkingAnimation != null)
                        botAnimation.CrossFade("Talking", 0.06f);
                    SetMouthState(true);
                }
                else
                {
                    if (botAnimation != null && idleAnimation != null)
                        botAnimation.CrossFade("Idle", 0.06f);
                    SetMouthState(false);
                }
            }
        }

        isCurrentlyTalking = false;
        if (botAnimation != null && idleAnimation != null)
            botAnimation.CrossFade("Idle", 0.08f);
        SetMouthState(false);
        audioMonitorCoroutine = null;
    }

    void SetMouthState(bool talking)
    {
        if (mouthOpenMesh != null) mouthOpenMesh.SetActive(talking);
        if (mouthClosedMesh != null) mouthClosedMesh.SetActive(!talking);
    }

    // -------------------------
    // Subtitle helpers (AUTO GENERATION)
    // -------------------------
    private SubtitleSegment[] GenerateAutoSubtitles(TimedBotAction action)
    {
        if (action == null || action.audioClip == null)
            return null;

        string raw = action.autoSubtitleText;
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        List<string> chunks = (action.subtitleChunkMode == SubtitleChunkMode.ByWords)
            ? BuildChunksByWords(raw, action.wordsPerSubtitle)
            : BuildChunksByLines(raw);

        if (chunks.Count == 0)
            return null;

        float clipLength = action.audioClip.length;
        if (clipLength <= 0f)
        {
            // fallback: 1 second per chunk if length invalid
            clipLength = chunks.Count;
        }

        float slice = clipLength / chunks.Count;

        SubtitleSegment[] segments = new SubtitleSegment[chunks.Count];
        for (int i = 0; i < chunks.Count; i++)
        {
            segments[i] = new SubtitleSegment
            {
                timestamp = i * slice,
                duration = slice,
                text = chunks[i]
                // backgroundColor uses default value
            };
        }

        return segments;
    }

    private List<string> BuildChunksByLines(string rawText)
    {
        string[] rawLines = rawText.Split('\n');
        List<string> lines = new List<string>();

        foreach (var raw in rawLines)
        {
            string line = raw.Trim();
            if (!string.IsNullOrEmpty(line))
                lines.Add(line);
        }

        return lines;
    }

    private List<string> BuildChunksByWords(string rawText, int wordsPerChunk)
    {
        if (wordsPerChunk <= 0) wordsPerChunk = 6;

        char[] sep = { ' ', '\t', '\n', '\r' };
        string[] words = rawText.Split(sep, StringSplitOptions.RemoveEmptyEntries);

        List<string> chunks = new List<string>();
        List<string> current = new List<string>();

        for (int i = 0; i < words.Length; i++)
        {
            current.Add(words[i]);
            if (current.Count >= wordsPerChunk)
            {
                chunks.Add(string.Join(" ", current));
                current.Clear();
            }
        }

        if (current.Count > 0)
            chunks.Add(string.Join(" ", current));

        return chunks;
    }

    // -------------------------
    // Subtitles (display)
    // -------------------------
    private IEnumerator SubtitleSequenceCoroutine(AudioClip clip, SubtitleSegment[] segments)
    {
        if (clip == null || segments == null || segments.Length == 0) yield break;
        if (botAudioSource == null) yield break;

        SubtitleSegment[] sorted = (SubtitleSegment[])segments.Clone();
        System.Array.Sort(sorted, (a, b) => a.timestamp.CompareTo(b.timestamp));

        int idx = 0;
        float clipLength = clip.length;

        while (idx < sorted.Length && botAudioSource != null && botAudioSource.isPlaying)
        {
            float currentTime = botAudioSource.time;
            SubtitleSegment seg = sorted[idx];

            if (currentTime + 0.0001f >= seg.timestamp)
            {
                float segDuration = seg.duration;
                if (segDuration <= 0f)
                {
                    if (idx + 1 < sorted.Length)
                        segDuration = Mathf.Max(0.02f, sorted[idx + 1].timestamp - seg.timestamp);
                    else
                        segDuration = Mathf.Max(0.02f, clipLength - seg.timestamp);
                }

                // show subtitle + background
                if (subtitlePanel != null) subtitlePanel.SetActive(true);
                if (subtitleText != null) subtitleText.text = seg.text ?? "";

                if (subtitleBackground != null)
                {
                    subtitleBackground.enabled = true;
                    subtitleBackground.gameObject.SetActive(true);
                    Color c = seg.backgroundColor;
                    if (c.a <= 0.01f) c.a = 0.85f;
                    subtitleBackground.color = c;
                }

                float waited = 0f;
                while (waited < segDuration && botAudioSource != null && botAudioSource.isPlaying)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                // hide between segments
                if (subtitlePanel != null) subtitlePanel.SetActive(false);
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

        StopSubtitleSequence();
    }

    private void StopSubtitleSequence()
    {
        if (subtitleCoroutine != null)
        {
            StopCoroutine(subtitleCoroutine);
            subtitleCoroutine = null;
        }

        if (subtitlePanel != null) subtitlePanel.SetActive(false);
        if (subtitleBackground != null)
        {
            subtitleBackground.enabled = false;
            subtitleBackground.gameObject.SetActive(false);
        }
        if (subtitleText != null)
            subtitleText.text = "";
    }

    // -------------------------
    // Skip UI helpers
    // -------------------------
    private IEnumerator ShowSkipAfterDelay(float delay)
    {
        if (skipCanvasGroup == null) yield break;

        yield return new WaitForSeconds(delay);

        // fade in skip UI
        yield return StartCoroutine(FadeCanvasGroup(skipCanvasGroup, skipCanvasGroup.alpha, 1f, 0.4f));
        skipActive = true;
        skipHoldTimer = 0f;
        if (skipFillImage != null) skipFillImage.fillAmount = 0f;
        skipShowCoroutine = null;
    }

    private void HideSkipUIImmediate()
    {
        if (skipShowCoroutine != null)
        {
            StopCoroutine(skipShowCoroutine);
            skipShowCoroutine = null;
        }

        if (skipCanvasGroup != null)
        {
            skipCanvasGroup.alpha = 0f;
        }

        skipActive = false;
        skipHoldTimer = 0f;
        if (skipFillImage != null) skipFillImage.fillAmount = 0f;
    }

    private void SkipNow()
    {
        if (!skipActive) return;

        // mark skip requested for THIS action
        skipActive = false;
        skipCurrentActionRequested = true;

        // stop audio immediately
        if (botAudioSource != null && botAudioSource.isPlaying)
            botAudioSource.Stop();

        // stop talk monitor
        if (audioMonitorCoroutine != null)
        {
            StopCoroutine(audioMonitorCoroutine);
            audioMonitorCoroutine = null;
        }

        // stop movement if it's running
        if (currentMoveCoroutine != null)
        {
            StopCoroutine(currentMoveCoroutine);
            currentMoveCoroutine = null;
        }

        // reset animation to idle
        if (botAnimation != null && idleAnimation != null)
            botAnimation.CrossFade("Idle", 0.08f);
        SetMouthState(false);

        // stop subtitles
        StopSubtitleSequence();

        // hide skip UI
        if (skipCanvasGroup != null)
            StartCoroutine(FadeCanvasGroup(skipCanvasGroup, skipCanvasGroup.alpha, 0f, 0.2f));

        if (skipFillImage != null) skipFillImage.fillAmount = 0f;
        skipHoldTimer = 0f;
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
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

    void OnDestroy()
    {
        if (audioMonitorCoroutine != null) StopCoroutine(audioMonitorCoroutine);
        if (currentMoveCoroutine != null) StopCoroutine(currentMoveCoroutine);
        if (currentActionCoroutine != null) StopCoroutine(currentActionCoroutine);
        if (subtitleCoroutine != null) StopCoroutine(subtitleCoroutine);
        if (skipShowCoroutine != null) StopCoroutine(skipShowCoroutine);
    }
}

// -------------------------
// Shared subtitle data type
// -------------------------
[System.Serializable]
public class SubtitleSegment
{
    [Tooltip("Seconds from clip start when this subtitle appears")]
    public float timestamp;

    [TextArea(1, 4)]
    public string text;

    [Tooltip("Duration in seconds (0 = auto until next segment or clip end)")]
    public float duration;

    [Tooltip("Background color for this subtitle segment (RGBA)")]
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.85f);
}

public enum SubtitleChunkMode
{
    ByLines,   // each line = one subtitle
    ByWords    // chunk by wordsPerSubtitle
}

[System.Serializable]
public class TimedBotAction
{
    [Header("Optional animation for this step")]
    public AnimationClip animationClip;

    [Header("Audio for this step")]
    public AudioClip audioClip;

    [Header("Target position for this step (optional)")]
    public Transform botPosition;
    public float moveDuration = 0f;

    [Header("Flow")]
    [Tooltip("If true: play animation first, then start audio. If false: start audio first, then animation.")]
    public bool startAudioAfterAnimation = true;

    public bool lockRotationDuringMove = false;

    [Header("Dialogue / Subtitles (auto)")]
    [Tooltip("Text used to automatically generate subtitles for this action's audio clip.")]
    [TextArea(2, 4)]
    public string autoSubtitleText;

    [Tooltip("How to split autoSubtitleText into chunks.")]
    public SubtitleChunkMode subtitleChunkMode = SubtitleChunkMode.ByLines;

    [Tooltip("Words per subtitle when using 'ByWords' mode.")]
    public int wordsPerSubtitle = 6;

    // 🔹 Runtime-only cache. NOT serialized, so your old arrays are effectively gone.
    [System.NonSerialized]
    public SubtitleSegment[] runtimeSubtitles;

    [Tooltip("Allow skipping this audio by holding Space (radial pie)")]
    public bool allowSkip = true;

    public string GetClipKey()
    {
        if (animationClip != null)
            return "__ACTION_" + animationClip.name;
        return "__ACTION_noanim";
    }
}
