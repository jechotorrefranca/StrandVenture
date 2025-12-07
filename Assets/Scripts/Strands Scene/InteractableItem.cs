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

    [Header("Player view (for E interaction)")]
    public Transform playerViewTransform;
    [TextArea] public string infoText = "Info about this item...";

    [Header("Panel (per-item)")]
    [Tooltip("Prefab for the per-item interaction panel. Can be any UI prefab with a close button.")]
    public GameObject interactionPanelPrefab;

    [Header("Simple text panel")]
    [TextArea] public string itemInfo = "Info about this item...";

    [Header("Slideshow data")]
    public Sprite[] slideshowImages;
    public string[] slideshowCaptions;

    [Header("Video data")]
    public VideoClip videoClip;

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
    [HideInInspector] public bool spaceDone = false; // Marks if Q/bot interaction finished

    // Store all materials from all renderers
    private List<Material> allMaterials = new List<Material>();
    private List<Color> allOriginalColors = new List<Color>();
    private Coroutine pulseCoroutine;
    private bool originalColorCaptured = false;

    [Header("Debug")]
    public bool debugLogs = false;

    void Awake()
    {
        CacheMaterials();
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
            // Check inspected at start of loop
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

            // Wait for interval instead of every frame (reduces lag)
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
