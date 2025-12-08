using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;   // NEW input system

/// <summary>
/// Panel that shows item details + a 3D preview rendered into a RawImage.
/// Uses a separate preview camera + a RenderTexture that is created at runtime.
/// </summary>
public class Item3DPanel : MonoBehaviour, IInteractableUI
{
    [Header("UI References")]
    public TMP_Text titleText;
    public TMP_Text descriptionText;
    public RawImage previewImage;          // RawImage that shows the RenderTexture

    [Header("Preview Camera Setup")]
    [Tooltip("Camera that renders the preview model into a RenderTexture.")]
    public Camera previewCamera;
    [Tooltip("Root transform where the preview model will be instantiated (WORLD SPACE).")]
    public Transform modelRoot;
    [Tooltip("Layer used by the preview camera (must match its Culling Mask).")]
    public string previewLayerName = "ItemPreview";

    [Header("Model Settings")]
    [Tooltip("Optional override scale for the preview model.")]
    public Vector3 modelPreviewScale = Vector3.one;
    [Tooltip("Initial rotation of the preview model (local euler angles).")]
    public Vector3 initialModelRotation = new Vector3(0f, 180f, 0f);

    [Header("Rotation Controls")]
    public float rotationSpeed = 0.4f;
    public bool useUnscaledTime = true;
    [Tooltip("If true, only rotate while pointer is over the Rotation Area.")]
    public bool requirePointerOverPanel = true;
    public RectTransform rotationArea;     // usually the same rect as the RawImage

    [Header("RenderTexture")]
    [Tooltip("Desired resolution for the preview texture (leave as default if unsure).")]
    public int renderTextureWidth = 512;
    public int renderTextureHeight = 512;

    private GameObject previewInstance;
    private int previewLayer = -1;
    private RenderTexture runtimeRT;       // created at runtime

    // --------------------------
    // IInteractableUI.Init
    // --------------------------
    public void Init(InteractableItem item, InteractionManager manager)
    {
        if (item == null)
        {
            Debug.LogWarning("[Item3DPanel] Init called with null item.");
            return;
        }

        // 1) Fill basic text
        if (titleText != null)
            titleText.text = item.name;

        if (descriptionText != null)
        {
            // Prefer item.itemInfo if set, else fall back to infoText
            string textToUse = string.IsNullOrEmpty(item.itemInfo)
                ? item.infoText
                : item.itemInfo;
            descriptionText.text = textToUse;
        }

        // 2) Setup preview camera & model root
        if (previewCamera == null)
        {
            Debug.LogWarning("[Item3DPanel] PreviewCamera is not assigned.");
            return;
        }

        if (modelRoot == null)
        {
            Debug.LogWarning("[Item3DPanel] ModelRoot is not assigned.");
            return;
        }

        // Make sure model root has neutral scale (to avoid spaghetti)
        modelRoot.localScale = Vector3.one;

        // Determine preview layer index
        if (!string.IsNullOrEmpty(previewLayerName))
        {
            previewLayer = LayerMask.NameToLayer(previewLayerName);
            if (previewLayer == -1)
            {
                Debug.LogWarning($"[Item3DPanel] Layer '{previewLayerName}' does not exist. " +
                                 "Create it and assign it to the preview camera's Culling Mask.");
            }
        }

        // 3) Ensure we have a RenderTexture & hook it up
        EnsureRenderTexture();

        // 4) Instantiate the item’s 3D model for preview
        if (item.previewModelPrefab == null)
        {
            Debug.LogWarning($"[Item3DPanel] Item '{item.name}' has no previewModelPrefab assigned.");
            return;
        }

        if (previewInstance != null)
        {
            Destroy(previewInstance);
            previewInstance = null;
        }

        previewInstance = Instantiate(item.previewModelPrefab, modelRoot);
        previewInstance.transform.localPosition = Vector3.zero;
        previewInstance.transform.localRotation = Quaternion.Euler(initialModelRotation);
        previewInstance.transform.localScale = modelPreviewScale;

        // Apply preview layer recursively so only previewCamera sees it
        if (previewLayer != -1)
        {
            SetLayerRecursively(previewInstance, previewLayer);
        }
    }

    private void EnsureRenderTexture()
    {
        if (previewImage == null)
        {
            Debug.LogWarning("[Item3DPanel] previewImage is not assigned.");
            return;
        }

        if (runtimeRT == null)
        {
            int w = renderTextureWidth <= 0 ? 512 : renderTextureWidth;
            int h = renderTextureHeight <= 0 ? 512 : renderTextureHeight;

            runtimeRT = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32);
            runtimeRT.name = "ItemPreviewRT";
            runtimeRT.Create();
        }

        // Feed it to both camera and RawImage
        previewCamera.targetTexture = runtimeRT;
        previewImage.texture = runtimeRT;

        // Nice to have: solid color with 0 alpha (so panel background shows)
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        Color bg = previewCamera.backgroundColor;
        bg.a = 0f;
        previewCamera.backgroundColor = bg;
    }

    private void OnDisable()
    {
        if (previewInstance != null)
        {
            Destroy(previewInstance);
            previewInstance = null;
        }
    }

    private void OnDestroy()
    {
        if (runtimeRT != null)
        {
            runtimeRT.Release();
            Destroy(runtimeRT);
            runtimeRT = null;
        }
    }

    private void Update()
    {
        if (previewInstance == null) return;

        // NEW input system: Mouse.current
        if (Mouse.current == null) return;

        if (requirePointerOverPanel && !IsPointerOverRotationArea())
            return;

        if (Mouse.current.leftButton.isPressed)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            Vector2 delta = Mouse.current.delta.ReadValue();
            float deltaX = delta.x;
            float deltaY = delta.y;

            Vector3 euler = previewInstance.transform.localEulerAngles;
            euler.y -= deltaX * rotationSpeed * 500f * dt;
            euler.x += deltaY * rotationSpeed * 500f * dt;
            previewInstance.transform.localEulerAngles = euler;
        }
    }

    private bool IsPointerOverRotationArea()
    {
        if (rotationArea == null) return true; // no area assigned → always rotate
        if (Mouse.current == null) return false;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        return RectTransformUtility.RectangleContainsScreenPoint(rotationArea, mousePos);
    }

    private void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null) return;
        go.layer = layer;
        foreach (Transform child in go.transform)
        {
            if (child == null) continue;
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}
