using System.Collections;
using System.Linq;
using UnityEngine;
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
    [Tooltip("Duration for fade in/out (seconds)")]
    public float overlayFadeDuration = 0.8f;
    public float startDelay = 0.1f;
    public string sceneToLoadAfter = "";
    public float endPadding = 0.35f;

    [Header("Movement defaults")]
    [Tooltip("Default duration used when moving between positions if no animation length is available")]
    public float defaultMoveDuration = 0.8f;

    [Tooltip("Assign the player's transform (camera or player root) for the bot to face")]
    [Header("Follow Player (rotation)")]
    public Transform playerTransform;
    public bool followPlayerRotation = true;
    public float lookAtSpeed = 6f;
    public bool allowFollowDuringMove = true;

    [Tooltip("Offset applied to the follow rotation in degrees (X = pitch, Y = yaw, Z = roll). " +
             "Use this if your model's rest pose looks tilted when aiming at the player.")]
    public Vector3 followRotationOffsetEuler = Vector3.zero;


    [Header("Timed Actions")]
    public TimedBotAction[] actions;

    private AudioSource internalAudio;
    private Coroutine audioMonitorCoroutine;
    private bool isCurrentlyTalking = false;
    private float[] audioSamples = new float[256];

    private float fadeAlpha = 1f;
    private bool useGuiFade = true;

    private Vector3 botBasePosition;
    private float floatTimer;

    private Coroutine currentMoveCoroutine;

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

            if (actions != null && actions.Length > 0 && actions[0] != null && actions[0].botPosition != null)
            {
                botBasePosition = actions[0].botPosition.position;
                botModel.transform.position = botBasePosition;
                botModel.transform.rotation = actions[0].botPosition.rotation;
            }
            else
            {
                botBasePosition = botModel.transform.position;
            }
        }

        if (actions != null && actions.Length > 1)
            actions = actions.OrderBy(a => a.timestamp).ToArray();

        PrepareBuiltinClips();
        PrepareAnimationClips();
    }

    IEnumerator Start()
    {
        fadeAlpha = 1f;

        if (botModel != null)
        {
            botModel.SetActive(true);
            botModel.transform.position = botBasePosition;

            if (botAnimation != null && idleAnimation != null)
            {
                if (botAnimation.GetClip("Idle") != null)
                    botAnimation.Play("Idle");
            }
        }

        yield return null;

        yield return StartCoroutine(FadeOverlay(false));

        yield return new WaitForSeconds(startDelay);

        StartCoroutine(FloatBot());

        float sequenceStart = Time.time;

        if (actions == null || actions.Length == 0)
        {
            yield return new WaitForSeconds(0.5f);
        }
        else
        {
            int idx = 0;
            float sequenceExpectedEnd = 0f;

            while (idx < actions.Length)
            {
                TimedBotAction action = actions[idx];
                float waitFor = sequenceStart + action.timestamp - Time.time;
                if (waitFor > 0f) yield return new WaitForSeconds(waitFor);

                StartCoroutine(PlayAction(action));

                float clipLen = Mathf.Max(
                    action.audioClip != null ? action.audioClip.length : 0f,
                    action.animationClip != null ? action.animationClip.length : 0f
                );
                sequenceExpectedEnd = Mathf.Max(sequenceExpectedEnd, action.timestamp + clipLen);

                idx++;
            }

            float remaining = (sequenceStart + sequenceExpectedEnd + endPadding) - Time.time;
            if (remaining > 0f) yield return new WaitForSeconds(remaining + 3f);
        }

        yield return StartCoroutine(FadeOverlay(true));

        if (!string.IsNullOrEmpty(sceneToLoadAfter))
            SceneManager.LoadScene(sceneToLoadAfter);
    }

    IEnumerator PlayAction(TimedBotAction action)
    {
        if (action == null) yield break;

        float animLen = action.animationClip != null ? action.animationClip.length : 0f;
        float moveDur = action.moveDuration > 0f ? action.moveDuration : (animLen > 0f ? animLen : defaultMoveDuration);

        if (action.animationClip != null && botAnimation != null)
        {
            string key = action.GetClipKey();
            if (botAnimation.GetClip(key) == null)
                botAnimation.AddClip(action.animationClip, key);

            botAnimation.CrossFade(key, 0.05f);
        }

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

        if (action.animationClip != null)
        {
            yield return new WaitForSeconds(animLen);
        }
        else if (currentMoveCoroutine != null)
        {
            yield return currentMoveCoroutine;
        }
        else
        {
            yield return null;
        }

        if (action.audioClip != null && action.startAudioAfterAnimation && botAudioSource != null)
        {
            botAudioSource.PlayOneShot(action.audioClip);
            if (audioMonitorCoroutine != null) StopCoroutine(audioMonitorCoroutine);
            audioMonitorCoroutine = StartCoroutine(MonitorAudioAndSwitchAnimation());
        }
        else if (action.audioClip != null && !action.startAudioAfterAnimation && botAudioSource != null)
        {
            botAudioSource.PlayOneShot(action.audioClip);
            if (audioMonitorCoroutine != null) StopCoroutine(audioMonitorCoroutine);
            audioMonitorCoroutine = StartCoroutine(MonitorAudioAndSwitchAnimation());
        }

        yield break;
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
    }

    IEnumerator MoveAndRotate(Vector3 fromPos, Vector3 toPos, Quaternion fromRot, Quaternion toRot, float duration)
    {
        if (botModel == null)
        {
            botBasePosition = toPos;
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

            yield return null;
        }

        botModel.transform.position = toPos;
        botModel.transform.rotation = toRot;
        botBasePosition = toPos;
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
                        lookDir.y = 0f;

                        if (lookDir.sqrMagnitude > 0.0001f)
                        {
                            Quaternion yawRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
                            float targetYaw = yawRot.eulerAngles.y;

                            Vector3 currentEuler = botModel.transform.eulerAngles;
                            float targetPitch = currentEuler.x + followRotationOffsetEuler.x;
                            float targetRoll = currentEuler.z + followRotationOffsetEuler.z;
                            float targetYawWithOffset = targetYaw + followRotationOffsetEuler.y;

                            Quaternion target = Quaternion.Euler(targetPitch, targetYawWithOffset, targetRoll);

                            botModel.transform.rotation = Quaternion.Slerp(botModel.transform.rotation, target, Time.deltaTime * lookAtSpeed);
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
        yield break;
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
}

[System.Serializable]
public class TimedBotAction
{
    [Tooltip("Seconds from sequence start when this action begins")]
    public float timestamp;

    [Tooltip("Animation clip to play (legacy AnimationClip). If present, movement defaults to animation length")]
    public AnimationClip animationClip;

    [Tooltip("Audio to play AFTER the animation/move completes (set startAudioAfterAnimation = false to play immediately)")]
    public AudioClip audioClip;

    [Tooltip("Optional Transform to move the bot to before/during the animation")]
    public Transform botPosition;

    [Tooltip("If > 0, forces the movement duration (in seconds). If 0, movement defaults to animation length or defaultMoveDuration")]
    public float moveDuration = 0f;

    [Tooltip("If true (default) audio will start after animation/move completes")]
    public bool startAudioAfterAnimation = true;

    [Tooltip("If true, the action will rotate the bot to the action's rotation while moving (ignores followPlayerRotation for that move)")]
    public bool lockRotationDuringMove = false;

    public string GetClipKey()
    {
        string namePart = animationClip != null ? animationClip.name : "noanim";
        return $"__TIMED_{timestamp:F2}_{namePart}";
    }
}
