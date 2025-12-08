using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

public class InteractableItem : MonoBehaviour
{
    [Header("Interaction data (bot)")]
    public Transform botTargetPosition;
    public AnimationClip interactionAnimation;
    public AudioClip interactionAudio;

    [Header("Subtitles (bot audio)")]
    [Tooltip("If true, subtitles will be generated automatically from Auto Subtitle Text using the audio clip length.")]
    public bool autoGenerateSubtitles = false;

    public enum AutoSubtitleMode
    {
        ByLines,   // each line in autoSubtitleText = one subtitle
        ByWords    // chunk by wordsPerSubtitle
    }

    [Tooltip("How to split the Auto Subtitle Text into subtitle chunks.")]
    public AutoSubtitleMode autoSubtitleMode = AutoSubtitleMode.ByLines;

    [Tooltip("Multiline text used to auto-generate subtitles.")]
    [TextArea(3, 10)]
    public string autoSubtitleText;

    [Tooltip("Words per subtitle when using 'ByWords' mode.")]
    public int wordsPerSubtitle = 6;

    [Tooltip("If autoGenerateSubtitles is false, these subtitles will be used directly (optional).")]
    public SubtitleSegment[] manualSubtitles;

    // Cached/computed at runtime (either from auto or manual)
    [HideInInspector] public SubtitleSegment[] computedSubtitles;

    [Header("Player view (for E interaction)")]
    public Transform playerViewTransform;
    [TextArea] public string infoText = "Info about this item...";

    [Header("Panel (per-item)")]
    [Tooltip("Prefab for the per-item interaction panel. Can be any UI prefab with a close button + IInteractableUI script.")]
    public GameObject interactionPanelPrefab;

    [Header("Simple text panel data")]
    [TextArea] public string itemInfo = "Info about this item...";

    [Header("Slideshow data")]
    [Tooltip("Sprites used by SlideshowPanel / SlideshowPanelFit prefabs.")]
    public Sprite[] slideshowImages;
    [Tooltip("Captions for each slide (same length as slideshowImages).")]
    public string[] slideshowCaptions;

    [Header("Video data")]
    [Tooltip("Video clip used by VideoPanel prefab.")]
    public VideoClip videoClip;

    [Header("3D Preview")]
    [Tooltip("Prefab of the 3D model to preview inside Item3DPanel.")]
    public GameObject previewModelPrefab;

    [Header("Glow Settings")]
    [Tooltip("Renderers to apply glow effect to. Leave empty to auto-find all renderers in children.")]
    public Renderer[] glowRenderers;

    [Tooltip("Color to pulse to when highlighted")]
    public Color glowColor = Color.yellow;

    [Tooltip("Original color (will be auto-detected if not set)")]
    public Color originalColor = Color.white;

    [Tooltip("Pulse speed (breathing)")]
    public float pulseSpeed = 2f;

    [Tooltip("Pulse intensity (0..1)")]
    [Range(0f, 1f)]
    public float pulseIntensity = 0.7f;

    [Header("Performance")]
    [Tooltip("Update interval in seconds. Higher = less lag, lower = smoother animation")]
    public float updateInterval = 0.05f; // 20Hz default

    [HideInInspector] public bool inspected = false; // marks if E was pressed
    [HideInInspector] public bool spaceDone = false; // marks if Q/bot interaction finished

    // Store all materials from all renderers
    private readonly List<Material> allMaterials = new List<Material>();
    private readonly List<Color> allOriginalColors = new List<Color>();
    private Coroutine pulseCoroutine;
    private bool originalColorCaptured = false;

    [Header("Debug")]
    public bool debugLogs = false;

    // Optional helpers if you ever want to branch on type from code
    public bool HasSlideshow => slideshowImages != null && slideshowImages.Length > 0;
    public bool HasVideo => videoClip != null;
    public bool HasSimpleText => !string.IsNullOrEmpty(itemInfo);

    void Awake()
    {
        CacheMaterials();
    }

    // 🔹 PUBLIC: used by InteractionManager / TimedBotSequence to get subtitles for this item's audio
    public SubtitleSegment[] GetSubtitlesForAudio()
    {
        // Reuse cached if already built
        if (computedSubtitles != null && computedSubtitles.Length > 0)
            return computedSubtitles;

        // 1) If auto is OFF, but manual subtitles exist → use them
        if (!autoGenerateSubtitles)
        {
            if (manualSubtitles != null && manualSubtitles.Length > 0)
            {
                computedSubtitles = manualSubtitles;
                return computedSubtitles;
            }
            return null;
        }

        // 2) Auto-generate from text + audio length
        if (interactionAudio == null)
            return null;

        if (string.IsNullOrWhiteSpace(autoSubtitleText))
            return null;

        List<string> chunks = autoSubtitleMode == AutoSubtitleMode.ByWords
            ? BuildChunksByWords(autoSubtitleText, wordsPerSubtitle)
            : BuildChunksByLines(autoSubtitleText);

        if (chunks.Count == 0)
            return null;

        float clipLength = interactionAudio.length;
        if (clipLength <= 0f)
        {
            // Fallback: 1 second per chunk if audio length isn't valid
            clipLength = chunks.Count;
        }

        float slice = clipLength / chunks.Count;
        computedSubtitles = new SubtitleSegment[chunks.Count];

        for (int i = 0; i < chunks.Count; i++)
        {
            computedSubtitles[i] = new SubtitleSegment
            {
                timestamp = i * slice,
                duration = slice,
                text = chunks[i],
                // backgroundColor uses default value from SubtitleSegment
            };
        }

        return computedSubtitles;
    }

    // Split by lines (each non-empty line = one subtitle)
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

    // Split by words, group into chunks of wordsPerSubtitle
    private List<string> BuildChunksByWords(string rawText, int wordsPerChunk)
    {
        if (wordsPerChunk <= 0) wordsPerChunk = 6;

        // Split on whitespace
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

        // Any leftover words
        if (current.Count > 0)
            chunks.Add(string.Join(" ", current));

        return chunks;
    }

    void CacheMaterials()
    {
        // If no renderers assigned, find all in children (including self)
        if (glowRenderers == null || glowRenderers.Length == 0)
        {
            glowRenderers = GetComponentsInChildren<Renderer>(true);
            if (debugLogs)
                Debug.Log($"[{name}] Auto-found {glowRenderers.Length} renderer(s) in children");
        }

        if (glowRenderers == null || glowRenderers.Length == 0)
        {
            Debug.LogWarning($"[{name}] No Renderers found!");
            return;
        }

        // Collect ALL materials from ALL renderers
        foreach (Renderer renderer in glowRenderers)
        {
            if (renderer == null) continue;

            Material[] materials = renderer.materials;

            foreach (Material mat in materials)
            {
                if (mat == null) continue;

                allMaterials.Add(mat);

                if (mat.HasProperty("_Color"))
                {
                    Color originalCol = mat.color;
                    allOriginalColors.Add(originalCol);

                    if (!originalColorCaptured)
                    {
                        originalColor = originalCol;
                        originalColorCaptured = true;
                    }
                }
                else
                {
                    allOriginalColors.Add(Color.white);
                }
            }

            // reassign so Unity instantiates materials (instance per object)
            renderer.materials = materials;
        }

        if (debugLogs)
            Debug.Log($"[{name}] Total materials cached: {allMaterials.Count} from {glowRenderers.Length} renderer(s)");
    }

    /// <summary>
    /// Turn highlight on/off. Inspected items won't pulse (will restore original color).
    /// </summary>
    public void SetHighlight(bool on)
    {
        // Don't pulse if inspected: ensure color restored and return
        if (inspected)
        {
            StopHighlight();
            return;
        }

        if (on)
        {
            if (pulseCoroutine == null && allMaterials.Count > 0)
                pulseCoroutine = StartCoroutine(PulseRoutine());
        }
        else
        {
            StopHighlight();
        }
    }

    public void StopHighlight()
    {
        if (pulseCoroutine != null)
        {
            StopCoroutine(pulseCoroutine);
            pulseCoroutine = null;
        }

        // Restore all materials to original colors
        for (int i = 0; i < allMaterials.Count; i++)
        {
            if (allMaterials[i] != null && allMaterials[i].HasProperty("_Color"))
            {
                allMaterials[i].color = allOriginalColors[i];
            }
        }
    }

    IEnumerator PulseRoutine()
    {
        if (allMaterials.Count == 0) yield break;

        float t = 0f;

        while (true)
        {
            // Stop glow once inspected
            if (inspected)
            {
                StopHighlight();
                yield break;
            }

            t += updateInterval * pulseSpeed;
            float s = (Mathf.Sin(t) * 0.5f + 0.5f); // 0..1

            Color currentColor = Color.Lerp(originalColor, glowColor, s * pulseIntensity);

            // Apply to all materials
            for (int i = 0; i < allMaterials.Count; i++)
            {
                if (allMaterials[i] != null && allMaterials[i].HasProperty("_Color"))
                {
                    allMaterials[i].color = currentColor;
                }
            }

            yield return new WaitForSeconds(updateInterval);
        }
    }

    void OnDestroy()
    {
        // Clean up instance materials
        foreach (Material mat in allMaterials)
        {
            if (mat != null)
            {
                Destroy(mat);
            }
        }
        allMaterials.Clear();
        allOriginalColors.Clear();
    }
}
