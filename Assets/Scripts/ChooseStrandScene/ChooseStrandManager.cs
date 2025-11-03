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
    public TMP_Text descriptionText;
    public GameObject carouselContainer; // NEW: Container for all carousel elements

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
    public float botFloatAmount = 10f; // How much the bot moves up/down while floating
    public float botFloatSpeed = 2f; // Speed of the floating animation
    public float carouselFadeInDuration = 0.8f; // Duration for carousel fade in

    private int currentIndex = 0;
    private bool isAnimating = false;
    private bool introComplete = false;
    private AudioSource audioSource;
    private Vector3 botOriginalPosition; // Store bot's original position for floating
    private Coroutine floatingCoroutine; // Reference to floating coroutine

    private void Start()
    {
        if (!ValidateReferences())
        {
            Debug.LogError("ChooseStrandManager: Missing required references! Please assign all fields in the Inspector.");
            enabled = false;
            return;
        }

        // Setup audio source
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        SortStrandsByBest();

        // Hide carousel container initially
        if (carouselContainer != null)
        {
            carouselContainer.SetActive(false);
        }
        else
        {
            // Fallback: Hide individual cards if container not assigned
            foreach (var strand in strands)
            {
                if (strand.cardObject != null)
                {
                    strand.cardObject.SetActive(false);
                }
            }
        }

        // Disable buttons during intro
        if (leftButton != null) leftButton.interactable = false;
        if (rightButton != null) rightButton.interactable = false;
        if (selectButton != null) selectButton.interactable = false;

        // Start intro sequence
        StartCoroutine(IntroSequence());
    }

    private IEnumerator IntroSequence()
    {
        // Start with black screen
        if (fadeOverlay != null)
        {
            fadeOverlay.alpha = 1f;
            fadeOverlay.gameObject.SetActive(true);
        }

        // Set bot to idle initially and start small
        if (botImage != null && botIdleSprite != null)
        {
            botImage.sprite = botIdleSprite;
            botImage.gameObject.SetActive(true);
            botImage.transform.localScale = Vector3.zero; // Start at zero scale
            botImage.transform.localRotation = Quaternion.identity;
            botOriginalPosition = botImage.transform.localPosition; // Store original position
        }

        // Wait a moment before starting
        yield return new WaitForSeconds(0.5f);

        // Fade in from black
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
            fadeOverlay.gameObject.SetActive(false); // Disable after fade
        }

        // Animate bot entrance: scale up and spin 360 degrees
        if (botImage != null)
        {
            float elapsed = 0f;
            Vector3 originalScale = Vector3.one;

            while (elapsed < botEntranceDuration)
            {
                float t = elapsed / botEntranceDuration;
                float smoothT = Mathf.SmoothStep(0, 1, t);

                // Scale from 0 to 1
                botImage.transform.localScale = Vector3.Lerp(Vector3.zero, originalScale, smoothT);

                // Rotate 360 degrees
                float rotation = Mathf.Lerp(0f, 360f, smoothT);
                botImage.transform.localRotation = Quaternion.Euler(0, 0, rotation);

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Ensure final values
            botImage.transform.localScale = originalScale;
            botImage.transform.localRotation = Quaternion.identity;
        }

        // Play bot intro audio if available
        if (botIntroClip != null && audioSource != null)
        {
            // Start floating animation
            if (botImage != null)
            {
                floatingCoroutine = StartCoroutine(FloatBot());
            }

            audioSource.clip = botIntroClip;
            audioSource.Play();

            // Animate bot while speaking
            if (botImage != null && botTalkingSprite != null && botIdleSprite != null)
            {
                StartCoroutine(AnimateBotSpeaking());
            }

            // Wait for audio to finish
            yield return new WaitForSeconds(botIntroClip.length);

            // Stop floating animation
            if (floatingCoroutine != null)
            {
                StopCoroutine(floatingCoroutine);
                floatingCoroutine = null;
            }

            // Reset bot position to original
            if (botImage != null)
            {
                botImage.transform.localPosition = botOriginalPosition;
            }
        }

        // Set bot back to idle
        if (botImage != null && botIdleSprite != null)
        {
            botImage.sprite = botIdleSprite;
        }

        // Wait a moment before exit animation
        yield return new WaitForSeconds(0.3f);

        // Animate bot exit: spin and scale down
        if (botImage != null)
        {
            float elapsed = 0f;
            Vector3 originalScale = botImage.transform.localScale;

            while (elapsed < botExitDuration)
            {
                float t = elapsed / botExitDuration;
                float smoothT = Mathf.SmoothStep(0, 1, t);

                // Scale from 1 to 0
                botImage.transform.localScale = Vector3.Lerp(originalScale, Vector3.zero, smoothT);

                // Rotate 360 degrees
                float rotation = Mathf.Lerp(0f, 360f, smoothT);
                botImage.transform.localRotation = Quaternion.Euler(0, 0, rotation);

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Hide bot
            botImage.gameObject.SetActive(false);
        }

        // Wait a moment before showing carousel
        yield return new WaitForSeconds(0.3f);

        // Initialize carousel positions BEFORE showing it
        UpdateCarouselInstant();

        // Show and fade in carousel container
        if (carouselContainer != null)
        {
            // Add CanvasGroup if not present
            CanvasGroup carouselCanvasGroup = carouselContainer.GetComponent<CanvasGroup>();
            if (carouselCanvasGroup == null)
            {
                carouselCanvasGroup = carouselContainer.AddComponent<CanvasGroup>();
            }

            // Set alpha to 0 before activating
            carouselCanvasGroup.alpha = 0f;
            carouselContainer.SetActive(true);

            // Fade in carousel
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

        // Enable buttons
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
            // Sample audio to detect if sound is playing
            float[] samples = new float[256];
            audioSource.GetOutputData(samples, 0);

            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += Mathf.Abs(samples[i]);
            }
            float average = sum / samples.Length;

            // Switch sprite based on audio volume
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

        // Ensure idle sprite at the end
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

        // Warn about optional references
        if (carouselContainer == null) Debug.LogWarning("Carousel Container is not assigned - will hide individual cards instead");
        if (fadeOverlay == null) Debug.LogWarning("Fade Overlay is not assigned - intro animation will be skipped");
        if (botImage == null) Debug.LogWarning("Bot Image is not assigned - bot animation will be skipped");
        if (botIntroClip == null) Debug.LogWarning("Bot Intro Clip is not assigned - audio will be skipped");

        return isValid;
    }

    private void SortStrandsByBest()
    {
        string bestStrands = PlayerPrefs.GetString("BestStrand", "");
        if (string.IsNullOrEmpty(bestStrands)) return;

        string[] bestArray = bestStrands.Split(',');
        List<StrandInfo> bestList = new List<StrandInfo>();
        List<StrandInfo> restList = new List<StrandInfo>();

        foreach (var strand in strands)
        {
            bool isBest = false;
            foreach (var best in bestArray)
            {
                if (strand.name.Trim().Equals(best.Trim(), System.StringComparison.OrdinalIgnoreCase))
                {
                    bestList.Add(strand);
                    isBest = true;
                    break;
                }
            }
            if (!isBest) restList.Add(strand);
        }

        strands.Clear();
        strands.AddRange(bestList);
        strands.AddRange(restList);
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

        // Calculate the card that will come from behind
        int incomingIndex;
        if (direction > 0) // Moving right
        {
            incomingIndex = (nextNextIndex + 1 + strands.Count) % strands.Count;
        }
        else // Moving left
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

        // Activate all cards
        leftCard.SetActive(true);
        centerCard.SetActive(true);
        rightCard.SetActive(true);
        incomingCard.SetActive(true);

        // Store starting values
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

        if (direction > 0) // Moving RIGHT (Next)
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
        else // Moving LEFT (Previous)
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

        // Set incoming card initial state
        incomingCard.transform.localPosition = incomingStartPos;
        incomingCard.transform.localScale = Vector3.one * incomingStartScale;
        SetCardAlpha(incomingCard, incomingStartAlpha);

        float elapsed = 0f;

        // Animate all cards
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

        // Disable buttons to prevent multiple clicks
        if (leftButton != null) leftButton.interactable = false;
        if (rightButton != null) rightButton.interactable = false;
        if (selectButton != null) selectButton.interactable = false;

        string selectedStrand = strands[currentIndex].name;
        string sceneName = selectedStrand + "Scene";

        Debug.Log($"Selected Strand: {selectedStrand}, Loading Scene: {sceneName}");

        PlayerPrefs.SetString("SelectedStrand", selectedStrand);
        PlayerPrefs.Save();

        // Fade to black
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

        // Small delay before scene transition
        yield return new WaitForSeconds(0.2f);

        // Load scene
        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError($"Scene '{sceneName}' not found in build settings! Please add it to File > Build Settings > Scenes in Build");

            // Fade back if scene not found
            if (fadeOverlay != null)
            {
                fadeOverlay.alpha = 0f;
                fadeOverlay.gameObject.SetActive(false);
            }

            // Re-enable buttons
            if (leftButton != null) leftButton.interactable = true;
            if (rightButton != null) rightButton.interactable = true;
            if (selectButton != null) selectButton.interactable = true;

            isAnimating = false;
        }
    }

    private void OnDestroy()
    {
        if (leftButton != null) leftButton.onClick.RemoveListener(ShowPrevious);
        if (rightButton != null) rightButton.onClick.RemoveListener(ShowNext);
        if (selectButton != null) selectButton.onClick.RemoveListener(OnSelectClicked);
    }
}