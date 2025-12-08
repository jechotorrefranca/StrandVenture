using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Slideshow panel that auto-fits images of different sizes
/// into a container while preserving aspect,
/// WITHOUT moving the image from its designed RectTransform position.
/// </summary>
public class SlideshowPanelFit : MonoBehaviour, IInteractableUI
{
    [Header("UI References")]
    public Image image;
    public TMP_Text captionText;
    public Button nextButton;
    public Button prevButton;

    [Header("Transition")]
    [Tooltip("Seconds for full fade-out + fade-in transition (image only).")]
    public float transitionDuration = 0.25f;

    [Tooltip("CanvasGroup used for fading the IMAGE only. If null, one will be added on the Image object.")]
    public CanvasGroup imageCanvasGroup;

    [Header("Auto-Fit")]
    [Tooltip("Optional: container rect the image should fit inside. If null, will use image.rectTransform.parent.")]
    public RectTransform imageContainer;

    [Tooltip("If true, the image will be scaled to fit entirely inside the container (letterbox).")]
    public bool fitInside = true;

    private Sprite[] images;
    private string[] captions;
    private int index = 0;

    private Coroutine transitionCoroutine;

    // Called by InteractionManager after the panel is created
    public void Init(InteractableItem item, InteractionManager manager)
    {
        // Grab data from the item
        images = item.slideshowImages;
        captions = item.slideshowCaptions;

        if (images == null || images.Length == 0)
        {
            Debug.LogWarning($"[SlideshowPanelFit] Item '{item.name}' has no slideshowImages assigned.");
            return;
        }

        // Ensure captions array matches images length
        if (captions == null || captions.Length != images.Length)
        {
            string[] newCaps = new string[images.Length];
            if (captions != null)
            {
                for (int i = 0; i < Mathf.Min(captions.Length, newCaps.Length); i++)
                    newCaps[i] = captions[i];
            }
            captions = newCaps;
        }

        // Ensure we have a CanvasGroup on the IMAGE, not the whole panel
        if (imageCanvasGroup == null && image != null)
        {
            imageCanvasGroup = image.GetComponent<CanvasGroup>();
            if (imageCanvasGroup == null)
                imageCanvasGroup = image.gameObject.AddComponent<CanvasGroup>();
        }

        if (imageCanvasGroup != null)
            imageCanvasGroup.alpha = 1f;

        // Default container: parent of the image
        if (imageContainer == null && image != null)
            imageContainer = image.transform.parent as RectTransform;

        // Reset index and show first slide
        index = Mathf.Clamp(index, 0, images.Length - 1);
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }
        SetContentImmediate(index);

        // Wire buttons safely (remove old listeners so they don't pile up)
        if (nextButton != null)
        {
            nextButton.onClick.RemoveListener(OnNextClicked);
            nextButton.onClick.AddListener(OnNextClicked);
        }

        if (prevButton != null)
        {
            prevButton.onClick.RemoveListener(OnPrevClicked);
            prevButton.onClick.AddListener(OnPrevClicked);
        }
    }

    private void OnDisable()
    {
        // Clean listeners when panel is hidden/destroyed
        if (nextButton != null) nextButton.onClick.RemoveListener(OnNextClicked);
        if (prevButton != null) prevButton.onClick.RemoveListener(OnPrevClicked);
    }

    // -------------------
    // Button callbacks
    // -------------------
    private void OnNextClicked()
    {
        if (images == null || images.Length == 0) return;
        int newIndex = (index + 1) % images.Length;
        StartTransition(newIndex);
    }

    private void OnPrevClicked()
    {
        if (images == null || images.Length == 0) return;
        int newIndex = (index - 1 + images.Length) % images.Length;
        StartTransition(newIndex);
    }

    // -------------------
    // Transition logic
    // -------------------
    private void StartTransition(int newIndex)
    {
        if (transitionCoroutine != null)
            StopCoroutine(transitionCoroutine);

        transitionCoroutine = StartCoroutine(TransitionToIndex(newIndex));
    }

    private IEnumerator TransitionToIndex(int newIndex)
    {
        // If we don’t have an imageCanvasGroup, just swap instantly
        if (imageCanvasGroup == null)
        {
            index = newIndex;
            SetContentImmediate(index);
            yield break;
        }

        float half = transitionDuration * 0.5f;
        float t = 0f;

        // Fade OUT image
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / half);
            imageCanvasGroup.alpha = 1f - k;
            yield return null;
        }

        // Switch content at the middle of the fade
        index = newIndex;
        SetContentImmediate(index);

        // Fade IN image
        t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / half);
            imageCanvasGroup.alpha = k;
            yield return null;
        }

        imageCanvasGroup.alpha = 1f;
        transitionCoroutine = null;
    }

    // -------------------
    // Content + auto-fit
    // -------------------
    private void SetContentImmediate(int idx)
    {
        if (images != null && images.Length > 0 && idx >= 0 && idx < images.Length)
        {
            if (image != null)
            {
                image.sprite = images[idx];
                UpdateImageFit(images[idx]);
            }
        }

        if (captionText != null)
        {
            string cap = (captions != null && idx >= 0 && idx < captions.Length)
                ? captions[idx]
                : "";
            captionText.text = cap;
        }
    }

    private void UpdateImageFit(Sprite sprite)
    {
        if (image == null || sprite == null) return;

        RectTransform imgRT = image.rectTransform;
        RectTransform container = imageContainer != null ? imageContainer : imgRT.parent as RectTransform;
        if (container == null) return;

        // Sprite pixel size / aspect
        float spriteWidth = sprite.rect.width;
        float spriteHeight = sprite.rect.height;
        if (spriteHeight <= 0f || spriteWidth <= 0f) return;

        float spriteAspect = spriteWidth / spriteHeight;

        // Container size (in local space)
        Vector2 containerSize = container.rect.size;
        if (containerSize.y <= 0f || containerSize.x <= 0f) return;

        float containerAspect = containerSize.x / containerSize.y;

        // Fit inside: letterbox behavior
        float targetWidth, targetHeight;

        if (fitInside)
        {
            if (spriteAspect > containerAspect)
            {
                // Wider than container → match width, shrink height
                targetWidth = containerSize.x;
                targetHeight = targetWidth / spriteAspect;
            }
            else
            {
                // Taller than container → match height, shrink width
                targetHeight = containerSize.y;
                targetWidth = targetHeight * spriteAspect;
            }
        }
        else
        {
            // Fill behavior (crop)
            if (spriteAspect < containerAspect)
            {
                // Narrower than container → match width, extend height
                targetWidth = containerSize.x;
                targetHeight = targetWidth / spriteAspect;
            }
            else
            {
                // Shorter than container → match height, extend width
                targetHeight = containerSize.y;
                targetWidth = targetHeight * spriteAspect;
            }
        }

        imgRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);
        imgRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetHeight);

        // 🔴 IMPORTANT: DO NOT TOUCH POSITION
        // We *do not* change anchoredPosition here, so it stays where you placed it in the prefab.
        // imgRT.anchoredPosition = Vector2.zero;  // <-- removed

        image.preserveAspect = true;
    }
}
