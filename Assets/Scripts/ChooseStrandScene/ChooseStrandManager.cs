using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class ChooseStrandManager : MonoBehaviour
{
    [System.Serializable]
    public class StrandInfo
    {
        public string name;
        [TextArea]
        public string description;
        public GameObject cardObject;

        [Header("Highlight / Badge (optional)")]
        public Graphic outlineTarget;   // UI element to glow (Image/Text/etc); if null, will auto-find
        public GameObject bestBadge;    // e.g. crown/star icon shown only for best strand(s)
    }

    [System.Serializable]
    public class TimedSubtitle
    {
        public float startTime;   // seconds from start of audio
        public float duration;    // how long to show this line
        [TextArea] public string text;
    }

    [Header("Strand Configuration")]
    public List<StrandInfo> strands = new List<StrandInfo>();

    [Header("Animation Settings")]
    public float transitionDuration = 0.5f;
    public float sideScale = 0.7f;
    public float centerScale = 1.2f;
    public float backScale = 0.4f;
    public float sideAlpha = 1f;
    public float backAlpha = 0.3f;

    [Header("Positions")]
    public Vector3 leftPosition = new Vector3(-400, 0, 0);
    public Vector3 centerPosition = new Vector3(0, 0, 0);
    public Vector3 rightPosition = new Vector3(400, 0, 0);
    public Vector3 backPosition = new Vector3(0, -50, 0);

    [Header("UI References")]
    public Button leftButton;
    public Button rightButton;
    public Button selectButton;
    public Button finishButton; // finish button reference
    public TMP_Text descriptionText;
    public GameObject carouselContainer;

    [Header("Input Settings")]
    public bool useKeyboardInput = true;

    [Header("Intro Animation")]
    public CanvasGroup fadeOverlay;
    public Image botImage;
    public Sprite botIdleSprite;
    public Sprite botTalkingSprite;
    public AudioClip botIntroClip;
    public float fadeInDuration = 1f;
    public float soundThreshold = 0.01f;
    public float botEntranceDuration = 1f;
    public float botExitDuration = 0.8f;
    public float botFloatAmount = 10f;
    public float botFloatSpeed = 2f;
    public float carouselFadeInDuration = 0.8f;

    [Header("Subtitles")]
    public TMP_Text subtitleText;
    public GameObject subtitleBackground;
    public float subtitleClearExtraDelay = 0.5f;
    public TimedSubtitle[] introSubtitles;   // manual timeline for intro clip

    [Header("Best Strand Highlight")]
    public Color bestStrandGlowColor = Color.white;
    public float bestStrandGlowDistance = 3f;

    private int currentIndex = 0;
    private bool isAnimating = false;
    private bool introComplete = false;
    private AudioSource audioSource;
    private Vector3 botOriginalPosition;
    private Coroutine floatingCoroutine;

    // subtitle state
    private Coroutine subtitleCoroutine;
    private Coroutine subtitleTimelineCoroutine;

    private void Start()
    {
        Cursor.visible = true;
        if (!ValidateReferences())
        {
            Debug.LogError("ChooseStrandManager: Missing required references! Please assign all fields in the Inspector.");
            enabled = false;
            return;
        }

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        // Subtitles start hidden
        if (subtitleText != null)
        {
            subtitleText.text = "";
            subtitleText.gameObject.SetActive(false);
        }
        if (subtitleBackground != null)
        {
            subtitleBackground.SetActive(false);
        }

        // Sort strands and apply best glow/badges
        SortStrandsByBest();

        if (carouselContainer != null)
        {
            carouselContainer.SetActive(false);
        }
        else
        {
            foreach (var strand in strands)
            {
                if (strand.cardObject != null)
                {
                    strand.cardObject.SetActive(false);
                }
            }
        }

        if (leftButton != null) leftButton.interactable = false;
        if (rightButton != null) rightButton.interactable = false;
        if (selectButton != null) selectButton.interactable = false;
        if (finishButton != null) finishButton.interactable = false; // initially disabled

        StartCoroutine(IntroSequence());
    }

    private IEnumerator IntroSequence()
    {
        if (fadeOverlay != null)
        {
            fadeOverlay.alpha = 1f;
            fadeOverlay.gameObject.SetActive(true);
        }

        if (botImage != null && botIdleSprite != null)
        {
            botImage.sprite = botIdleSprite;
            botImage.gameObject.SetActive(true);
            botImage.transform.localScale = Vector3.zero;
            botImage.transform.localRotation = Quaternion.identity;
            botOriginalPosition = botImage.transform.localPosition;
        }

        yield return new WaitForSeconds(0.5f);

        if (fadeOverlay != null)
        {
            float elapsed = 0f;
            while (elapsed < fadeInDuration)
            {
                fadeOverlay.alpha = 1f - (elapsed / fadeInDuration);
                elapsed += Time.deltaTime;
                yield return null;
            }
            fadeOverlay.alpha = 0f;
            fadeOverlay.gameObject.SetActive(false);
        }

        if (botImage != null)
        {
            float elapsed = 0f;
            Vector3 originalScale = Vector3.one;

            while (elapsed < botEntranceDuration)
            {
                float t = elapsed / botEntranceDuration;
                float smoothT = Mathf.SmoothStep(0, 1, t);

                botImage.transform.localScale = Vector3.Lerp(Vector3.zero, originalScale, smoothT);

                float rotation = Mathf.Lerp(0f, 360f, smoothT);
                botImage.transform.localRotation = Quaternion.Euler(0, 0, rotation);

                elapsed += Time.deltaTime;
                yield return null;
            }

            botImage.transform.localScale = originalScale;
            botImage.transform.localRotation = Quaternion.identity;
        }

        if (botIntroClip != null && audioSource != null)
        {
            if (botImage != null)
            {
                floatingCoroutine = StartCoroutine(FloatBot());
            }

            audioSource.clip = botIntroClip;
            audioSource.Play();

            // Subtitles for intro (using array with timestamps)
            if (introSubtitles != null && introSubtitles.Length > 0)
            {
                StartSubtitleTimeline(introSubtitles, audioSource);
            }

            if (botImage != null && botTalkingSprite != null && botIdleSprite != null)
            {
                StartCoroutine(AnimateBotSpeaking());
            }

            yield return new WaitForSeconds(botIntroClip.length);

            if (floatingCoroutine != null)
            {
                StopCoroutine(floatingCoroutine);
                floatingCoroutine = null;
            }

            if (botImage != null)
            {
                botImage.transform.localPosition = botOriginalPosition;
            }

            // stop subtitles after intro
            StopSubtitleTimeline();
        }

        if (botImage != null && botIdleSprite != null)
        {
            botImage.sprite = botIdleSprite;
        }

        yield return new WaitForSeconds(0.3f);

        if (botImage != null)
        {
            float elapsed = 0f;
            Vector3 originalScale = botImage.transform.localScale;

            while (elapsed < botExitDuration)
            {
                float t = elapsed / botExitDuration;
                float smoothT = Mathf.SmoothStep(0, 1, t);

                botImage.transform.localScale = Vector3.Lerp(originalScale, Vector3.zero, smoothT);

                float rotation = Mathf.Lerp(0f, 360f, smoothT);
                botImage.transform.localRotation = Quaternion.Euler(0, 0, rotation);

                elapsed += Time.deltaTime;
                yield return null;
            }

            botImage.gameObject.SetActive(false);
        }

        yield return new WaitForSeconds(0.3f);

        UpdateCarouselInstant();

        if (carouselContainer != null)
        {
            CanvasGroup carouselCanvasGroup = carouselContainer.GetComponent<CanvasGroup>();
            if (carouselCanvasGroup == null)
            {
                carouselCanvasGroup = carouselContainer.AddComponent<CanvasGroup>();
            }

            carouselCanvasGroup.alpha = 0f;
            carouselContainer.SetActive(true);

            float elapsed = 0f;

            while (elapsed < carouselFadeInDuration)
            {
                float t = elapsed / carouselFadeInDuration;
                carouselCanvasGroup.alpha = Mathf.SmoothStep(0f, 1f, t);
                elapsed += Time.deltaTime;
                yield return null;
            }

            carouselCanvasGroup.alpha = 1f;
        }

        if (leftButton != null)
        {
            leftButton.interactable = true;
            leftButton.onClick.AddListener(ShowPrevious);
        }
        if (rightButton != null)
        {
            rightButton.interactable = true;
            rightButton.onClick.AddListener(ShowNext);
        }
        if (selectButton != null)
        {
            selectButton.interactable = true;
            selectButton.onClick.AddListener(OnSelectClicked);
        }
        if (finishButton != null)
        {
            finishButton.interactable = true;
            finishButton.onClick.AddListener(OnFinishClicked);
        }

        introComplete = true;
    }

    private IEnumerator FloatBot()
    {
        if (botImage == null) yield break;

        float time = 0f;

        while (true)
        {
            time += Time.deltaTime * botFloatSpeed;
            float yOffset = Mathf.Sin(time) * botFloatAmount;
            botImage.transform.localPosition = botOriginalPosition + new Vector3(0, yOffset, 0);
            yield return null;
        }
    }

    private IEnumerator AnimateBotSpeaking()
    {
        if (botImage == null || botTalkingSprite == null || botIdleSprite == null || audioSource == null)
            yield break;

        while (audioSource.isPlaying)
        {
            float[] samples = new float[256];
            audioSource.GetOutputData(samples, 0);

            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += Mathf.Abs(samples[i]);
            }
            float average = sum / samples.Length;

            if (average > soundThreshold)
            {
                botImage.sprite = botTalkingSprite;
            }
            else
            {
                botImage.sprite = botIdleSprite;
            }

            yield return new WaitForSeconds(0.1f);
        }

        botImage.sprite = botIdleSprite;
    }

    private void Update()
    {
        if (!useKeyboardInput || isAnimating || !introComplete) return;

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.LeftArrow)) ShowPrevious();
        if (Input.GetKeyDown(KeyCode.RightArrow)) ShowNext();
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) OnSelectClicked();
#endif
    }

    private bool ValidateReferences()
    {
        bool isValid = true;

        if (strands == null || strands.Count == 0)
        {
            Debug.LogError("No strands configured!");
            isValid = false;
        }
        else
        {
            for (int i = 0; i < strands.Count; i++)
            {
                if (strands[i].cardObject == null)
                {
                    Debug.LogError($"Strand at index {i} ({strands[i].name}) has no card object assigned!");
                    isValid = false;
                }
            }
        }

        if (leftButton == null) { Debug.LogError("Left Button is not assigned!"); isValid = false; }
        if (rightButton == null) { Debug.LogError("Right Button is not assigned!"); isValid = false; }
        if (selectButton == null) { Debug.LogError("Select Button is not assigned!"); isValid = false; }
        if (descriptionText == null) { Debug.LogError("Description Text is not assigned!"); isValid = false; }

        // finishButton is optional but warn if missing
        if (finishButton == null) { Debug.LogWarning("Finish Button is not assigned - 'Finish' option will be unavailable."); }

        if (carouselContainer == null) Debug.LogWarning("Carousel Container is not assigned - will hide individual cards instead");
        if (fadeOverlay == null) Debug.LogWarning("Fade Overlay is not assigned - intro animation will be skipped");
        if (botImage == null) Debug.LogWarning("Bot Image is not assigned - bot animation will be skipped");
        if (botIntroClip == null) Debug.LogWarning("Bot Intro Clip is not assigned - audio will be skipped");

        // subtitles are optional; just warn if text is missing but background exists, etc
        if (subtitleBackground != null && subtitleText == null)
        {
            Debug.LogWarning("Subtitle background assigned but Subtitle Text is missing.");
        }

        return isValid;
    }

    /// <summary>
    /// Sorts strands so the ones with the highest Strand_{name}_Percent come first,
    /// supports ties (multiple best), applies glow & best badge, and logs the result.
    /// </summary>
    private void SortStrandsByBest()
    {
        if (strands == null || strands.Count == 0)
            return;

        List<string> bestNames = new List<string>();
        float bestPercent = 0f;

        // Find best percent (ties allowed), using PlayerPrefs values from previous results scene
        foreach (var strand in strands)
        {
            float percent = Mathf.Max(
                0f,
                PlayerPrefs.GetFloat($"Strand_{strand.name}_Percent", 0f)
            );

            Debug.Log($"ChooseStrandManager: Strand {strand.name} has {percent:F1}%");

            if (percent > bestPercent + 0.01f)
            {
                bestPercent = percent;
                bestNames.Clear();
                if (percent > 0f)
                    bestNames.Add(strand.name);
            }
            else if (Mathf.Approximately(percent, bestPercent) && percent > 0f)
            {
                if (!bestNames.Contains(strand.name))
                    bestNames.Add(strand.name);
            }
        }

        // Debug log for best strands
        if (bestNames.Count == 0 || bestPercent <= 0f)
        {
            Debug.Log("ChooseStrandManager: No best strand found (all Strand_*_Percent are 0 or missing).");
        }
        else
        {
            string joined = string.Join(", ", bestNames);
            Debug.Log($"ChooseStrandManager: Best strand(s) = {joined} at {bestPercent:F1}%.");
        }

        // Reorder strands so best come first
        if (bestNames.Count > 0 && bestPercent > 0f)
        {
            List<StrandInfo> bestList = new List<StrandInfo>();
            List<StrandInfo> restList = new List<StrandInfo>();

            foreach (var strand in strands)
            {
                if (bestNames.Contains(strand.name))
                    bestList.Add(strand);
                else
                    restList.Add(strand);
            }

            strands.Clear();
            strands.AddRange(bestList);
            strands.AddRange(restList);
        }

        // Apply glow outline and badge to best strands (multiple or none)
        ApplyBestStrandGlow(bestNames);
    }

    private void UpdateCarouselInstant()
    {
        if (strands.Count == 0) return;

        currentIndex = Mathf.Clamp(currentIndex, 0, strands.Count - 1);

        for (int i = 0; i < strands.Count; i++)
        {
            GameObject card = strands[i].cardObject;
            if (card == null) continue;

            card.SetActive(true);

            if (i == currentIndex)
            {
                card.transform.localPosition = centerPosition;
                card.transform.localScale = Vector3.one * centerScale;
                SetCardAlpha(card, 1f);
                SetSortingOrder(card, 3);
            }
            else if (i == GetPreviousIndex())
            {
                card.transform.localPosition = leftPosition;
                card.transform.localScale = Vector3.one * sideScale;
                SetCardAlpha(card, sideAlpha);
                SetSortingOrder(card, 2);
            }
            else if (i == GetNextIndex())
            {
                card.transform.localPosition = rightPosition;
                card.transform.localScale = Vector3.one * sideScale;
                SetCardAlpha(card, sideAlpha);
                SetSortingOrder(card, 2);
            }
            else
            {
                card.SetActive(false);
            }
        }

        if (descriptionText != null)
        {
            descriptionText.text = strands[currentIndex].description;
        }
    }

    private void SetCardAlpha(GameObject card, float alpha)
    {
        CanvasGroup canvasGroup = card.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = card.AddComponent<CanvasGroup>();
        }
        canvasGroup.alpha = alpha;
    }

    private void SetSortingOrder(GameObject card, int order)
    {
        Canvas canvas = card.GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.sortingOrder = order;
        }
    }

    private int GetNextIndex()
    {
        return (currentIndex + 1) % strands.Count;
    }

    private int GetPreviousIndex()
    {
        return (currentIndex - 1 + strands.Count) % strands.Count;
    }

    public void ShowNext()
    {
        if (isAnimating || strands.Count <= 1) return;
        StartCoroutine(AnimateCarousel(1));
    }

    public void ShowPrevious()
    {
        if (isAnimating || strands.Count <= 1) return;
        StartCoroutine(AnimateCarousel(-1));
    }

    private IEnumerator AnimateCarousel(int direction)
    {
        isAnimating = true;

        int nextIndex = (currentIndex + direction + strands.Count) % strands.Count;
        int prevIndex = GetPreviousIndex();
        int nextNextIndex = GetNextIndex();

        int incomingIndex;
        if (direction > 0)
        {
            incomingIndex = (nextNextIndex + 1 + strands.Count) % strands.Count;
        }
        else
        {
            incomingIndex = (prevIndex - 1 + strands.Count) % strands.Count;
        }

        GameObject leftCard = strands[prevIndex].cardObject;
        GameObject centerCard = strands[currentIndex].cardObject;
        GameObject rightCard = strands[nextNextIndex].cardObject;
        GameObject incomingCard = strands[incomingIndex].cardObject;

        if (centerCard == null || rightCard == null || leftCard == null || incomingCard == null)
        {
            isAnimating = false;
            yield break;
        }

        leftCard.SetActive(true);
        centerCard.SetActive(true);
        rightCard.SetActive(true);
        incomingCard.SetActive(true);

        Vector3 leftStartPos = leftCard.transform.localPosition;
        Vector3 centerStartPos = centerCard.transform.localPosition;
        Vector3 rightStartPos = rightCard.transform.localPosition;

        float leftStartScale = leftCard.transform.localScale.x;
        float centerStartScale = centerCard.transform.localScale.x;
        float rightStartScale = rightCard.transform.localScale.x;

        float leftStartAlpha = GetCardAlpha(leftCard);
        float centerStartAlpha = GetCardAlpha(centerCard);
        float rightStartAlpha = GetCardAlpha(rightCard);

        Vector3 leftEndPos, centerEndPos, rightEndPos, incomingStartPos, incomingEndPos;
        float leftEndScale, centerEndScale, rightEndScale, incomingStartScale, incomingEndScale;
        float leftEndAlpha, centerEndAlpha, rightEndAlpha, incomingStartAlpha, incomingEndAlpha;

        if (direction > 0)
        {
            leftEndPos = backPosition;
            leftEndScale = backScale;
            leftEndAlpha = backAlpha;
            SetSortingOrder(leftCard, 0);

            centerEndPos = leftPosition;
            centerEndScale = sideScale;
            centerEndAlpha = sideAlpha;
            SetSortingOrder(centerCard, 2);

            rightEndPos = centerPosition;
            rightEndScale = centerScale;
            rightEndAlpha = 1f;
            SetSortingOrder(rightCard, 3);

            incomingStartPos = backPosition;
            incomingStartScale = backScale;
            incomingStartAlpha = backAlpha;
            incomingEndPos = rightPosition;
            incomingEndScale = sideScale;
            incomingEndAlpha = sideAlpha;
            SetSortingOrder(incomingCard, 1);
        }
        else
        {
            rightEndPos = backPosition;
            rightEndScale = backScale;
            rightEndAlpha = backAlpha;
            SetSortingOrder(rightCard, 0);

            centerEndPos = rightPosition;
            centerEndScale = sideScale;
            centerEndAlpha = sideAlpha;
            SetSortingOrder(centerCard, 2);

            leftEndPos = centerPosition;
            leftEndScale = centerScale;
            leftEndAlpha = 1f;
            SetSortingOrder(leftCard, 3);

            incomingStartPos = backPosition;
            incomingStartScale = backScale;
            incomingStartAlpha = backAlpha;
            incomingEndPos = leftPosition;
            incomingEndScale = sideScale;
            incomingEndAlpha = sideAlpha;
            SetSortingOrder(incomingCard, 1);
        }

        incomingCard.transform.localPosition = incomingStartPos;
        incomingCard.transform.localScale = Vector3.one * incomingStartScale;
        SetCardAlpha(incomingCard, incomingStartAlpha);

        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            float t = elapsed / transitionDuration;
            float smoothT = Mathf.SmoothStep(0, 1, t);

            leftCard.transform.localPosition = Vector3.Lerp(leftStartPos, leftEndPos, smoothT);
            leftCard.transform.localScale = Vector3.one * Mathf.Lerp(leftStartScale, leftEndScale, smoothT);
            SetCardAlpha(leftCard, Mathf.Lerp(leftStartAlpha, leftEndAlpha, smoothT));

            centerCard.transform.localPosition = Vector3.Lerp(centerStartPos, centerEndPos, smoothT);
            centerCard.transform.localScale = Vector3.one * Mathf.Lerp(centerStartScale, centerEndScale, smoothT);
            SetCardAlpha(centerCard, Mathf.Lerp(centerStartAlpha, centerEndAlpha, smoothT));

            rightCard.transform.localPosition = Vector3.Lerp(rightStartPos, rightEndPos, smoothT);
            rightCard.transform.localScale = Vector3.one * Mathf.Lerp(rightStartScale, rightEndScale, smoothT);
            SetCardAlpha(rightCard, Mathf.Lerp(rightStartAlpha, rightEndAlpha, smoothT));

            incomingCard.transform.localPosition = Vector3.Lerp(incomingStartPos, incomingEndPos, smoothT);
            incomingCard.transform.localScale = Vector3.one * Mathf.Lerp(incomingStartScale, incomingEndScale, smoothT);
            SetCardAlpha(incomingCard, Mathf.Lerp(incomingStartAlpha, incomingEndAlpha, smoothT));

            elapsed += Time.deltaTime;
            yield return null;
        }

        currentIndex = nextIndex;
        UpdateCarouselInstant();

        isAnimating = false;
    }

    private float GetCardAlpha(GameObject card)
    {
        CanvasGroup canvasGroup = card.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = card.AddComponent<CanvasGroup>();
            return 1f;
        }
        return canvasGroup.alpha;
    }

    public void OnSelectClicked()
    {
        if (strands.Count == 0 || isAnimating) return;

        StartCoroutine(SelectAndTransition());
    }

    private IEnumerator SelectAndTransition()
    {
        isAnimating = true;

        if (leftButton != null) leftButton.interactable = false;
        if (rightButton != null) rightButton.interactable = false;
        if (selectButton != null) selectButton.interactable = false;
        if (finishButton != null) finishButton.interactable = false;

        string selectedStrand = strands[currentIndex].name;
        string sceneName = selectedStrand + "Scene";

        Debug.Log($"Selected Strand: {selectedStrand}, Loading Scene: {sceneName}");

        PlayerPrefs.SetString("SelectedStrand", selectedStrand);
        PlayerPrefs.Save();

        if (fadeOverlay != null)
        {
            fadeOverlay.gameObject.SetActive(true);
            float elapsed = 0f;
            float fadeDuration = 0.5f;

            while (elapsed < fadeDuration)
            {
                fadeOverlay.alpha = elapsed / fadeDuration;
                elapsed += Time.deltaTime;
                yield return null;
            }

            fadeOverlay.alpha = 1f;
        }

        yield return new WaitForSeconds(0.2f);

        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneLoader.LoadSceneWithLoading(sceneName);
        }
        else
        {
            Debug.LogError($"Scene '{sceneName}' not found in build settings! Please add it to File > Build Settings > Scenes in Build");

            if (fadeOverlay != null)
            {
                fadeOverlay.alpha = 0f;
                fadeOverlay.gameObject.SetActive(false);
            }

            if (leftButton != null) leftButton.interactable = true;
            if (rightButton != null) rightButton.interactable = true;
            if (selectButton != null) selectButton.interactable = true;
            if (finishButton != null) finishButton.interactable = true;

            isAnimating = false;
        }
    }

    public void OnFinishClicked()
    {
        if (isAnimating) return;
        StartCoroutine(FinishAndTransition());
    }

    private IEnumerator FinishAndTransition()
    {
        isAnimating = true;

        if (leftButton != null) leftButton.interactable = false;
        if (rightButton != null) rightButton.interactable = false;
        if (selectButton != null) selectButton.interactable = false;
        if (finishButton != null) finishButton.interactable = false;

        string sceneName = "JobExpoScene";
        Debug.Log($"Finish clicked. Loading Scene: {sceneName}");

        if (fadeOverlay != null)
        {
            fadeOverlay.gameObject.SetActive(true);
            float elapsed = 0f;
            float fadeDuration = 0.5f;

            while (elapsed < fadeDuration)
            {
                fadeOverlay.alpha = elapsed / fadeDuration;
                elapsed += Time.deltaTime;
                yield return null;
            }

            fadeOverlay.alpha = 1f;
        }

        yield return new WaitForSeconds(0.2f);

        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneLoader.LoadSceneWithLoading(sceneName);
        }
        else
        {
            Debug.LogError($"Scene '{sceneName}' not found in build settings! Please add it to File > Build Settings > Scenes in Build");

            if (fadeOverlay != null)
            {
                fadeOverlay.alpha = 0f;
                fadeOverlay.gameObject.SetActive(false);
            }

            if (leftButton != null) leftButton.interactable = true;
            if (rightButton != null) rightButton.interactable = true;
            if (selectButton != null) selectButton.interactable = true;
            if (finishButton != null) finishButton.interactable = true;

            isAnimating = false;
        }
    }

    private void OnDestroy()
    {
        if (leftButton != null) leftButton.onClick.RemoveListener(ShowPrevious);
        if (rightButton != null) rightButton.onClick.RemoveListener(ShowNext);
        if (selectButton != null) selectButton.onClick.RemoveListener(OnSelectClicked);
        if (finishButton != null) finishButton.onClick.RemoveListener(OnFinishClicked);
    }

    // ---------- SUBTITLE HELPERS (for this scene) ----------

    private void SetSubtitle(string text, float autoClearAfterSeconds = -1f)
    {
        if (subtitleText == null) return;

        StopSubtitleTimeline();

        if (subtitleCoroutine != null)
        {
            StopCoroutine(subtitleCoroutine);
            subtitleCoroutine = null;
        }

        if (string.IsNullOrEmpty(text))
        {
            HideSubtitleUI();
            return;
        }

        ShowSubtitleUI(text);

        if (autoClearAfterSeconds > 0f)
        {
            subtitleCoroutine = StartCoroutine(ClearSubtitleAfterDelay(autoClearAfterSeconds));
        }
    }

    private IEnumerator ClearSubtitleAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HideSubtitleUI();
    }

    private void ShowSubtitleUI(string text)
    {
        if (subtitleText == null) return;

        subtitleText.text = text;
        subtitleText.gameObject.SetActive(true);
        if (subtitleBackground != null)
            subtitleBackground.SetActive(true);
    }

    private void HideSubtitleUI()
    {
        if (subtitleText != null)
        {
            subtitleText.text = "";
            subtitleText.gameObject.SetActive(false);
        }
        if (subtitleBackground != null)
            subtitleBackground.SetActive(false);
    }

    private void StartSubtitleTimeline(TimedSubtitle[] timeline, AudioSource source)
    {
        if (timeline == null || timeline.Length == 0 || source == null) return;

        if (subtitleTimelineCoroutine != null)
        {
            StopCoroutine(subtitleTimelineCoroutine);
        }
        subtitleTimelineCoroutine = StartCoroutine(SubtitleTimelineRoutine(timeline, source));
    }

    private void StopSubtitleTimeline()
    {
        if (subtitleTimelineCoroutine != null)
        {
            StopCoroutine(subtitleTimelineCoroutine);
            subtitleTimelineCoroutine = null;
        }
        HideSubtitleUI();
    }

    private IEnumerator SubtitleTimelineRoutine(TimedSubtitle[] timeline, AudioSource source)
    {
        if (timeline == null || timeline.Length == 0 || source == null || source.clip == null)
            yield break;

        // wait until audio starts
        while (source.clip != null && !source.isPlaying)
            yield return null;

        while (source.isPlaying)
        {
            float t = source.time;
            bool hasSubtitle = false;

            for (int i = 0; i < timeline.Length; i++)
            {
                float start = Mathf.Max(0f, timeline[i].startTime);
                float duration = Mathf.Max(0f, timeline[i].duration);
                float end = start + duration;

                if (t >= start && t < end)
                {
                    ShowSubtitleUI(timeline[i].text);
                    hasSubtitle = true;
                    break;
                }
            }

            if (!hasSubtitle)
            {
                HideSubtitleUI();
            }

            yield return null;
        }

        HideSubtitleUI();
        subtitleTimelineCoroutine = null;
    }

    // ---------- BEST STRAND GLOW & BADGE ----------

    private void ApplyBestStrandGlow(List<string> bestNames)
    {
        foreach (var strand in strands)
        {
            if (strand.cardObject == null) continue;

            bool isBest = bestNames != null && bestNames.Contains(strand.name);

            // Handle badge visibility
            if (strand.bestBadge != null)
            {
                strand.bestBadge.SetActive(isBest);
                if (isBest)
                {
                    Debug.Log($"ChooseStrandManager: Showing bestBadge for '{strand.name}'.");
                }
            }
            else if (isBest)
            {
                Debug.LogWarning($"ChooseStrandManager: '{strand.name}' is best but has no bestBadge assigned.");
            }

            // Determine which Graphic to outline
            Graphic targetGraphic = strand.outlineTarget;
            GameObject targetGO = strand.cardObject;

            if (targetGraphic == null)
            {
                // Try to auto-find a Graphic on the card or its children
                targetGraphic = strand.cardObject.GetComponent<Graphic>();
                if (targetGraphic == null)
                {
                    targetGraphic = strand.cardObject.GetComponentInChildren<Graphic>();
                }
                if (targetGraphic != null)
                {
                    targetGO = targetGraphic.gameObject;
                    Debug.Log($"ChooseStrandManager: Auto-found outline Graphic on '{strand.name}' at '{targetGO.name}'.");
                }
            }
            else
            {
                targetGO = targetGraphic.gameObject;
            }

            if (targetGraphic == null)
            {
                if (isBest)
                {
                    Debug.LogWarning($"ChooseStrandManager: No Graphic found on '{strand.name}' card for outline. " +
                                     $"Assign 'outlineTarget' in the inspector for this strand.");
                }
                continue;
            }

            Outline outline = targetGO.GetComponent<Outline>();

            if (isBest)
            {
                if (outline == null)
                {
                    outline = targetGO.AddComponent<Outline>();
                    Debug.Log($"ChooseStrandManager: Added Outline component to '{targetGO.name}' for strand '{strand.name}'.");
                }
                outline.effectColor = bestStrandGlowColor;
                outline.effectDistance = new Vector2(bestStrandGlowDistance, bestStrandGlowDistance);
                outline.useGraphicAlpha = false; // make the glow use pure effectColor, not multiply by graphic alpha
                outline.enabled = true;
            }
            else
            {
                if (outline != null)
                {
                    outline.enabled = false;
                }
            }
        }
    }
}
