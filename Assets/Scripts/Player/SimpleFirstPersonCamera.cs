using UnityEngine;
using UnityEngine.InputSystem;

public class SimpleFirstPersonCamera : MonoBehaviour
{
    [Header("Camera Settings")]
    public float mouseSensitivity = 2f;
    public float verticalLookLimit = 80f;
    public float horizontalLookLimit = 180f;

    [Header("Smoothing")]
    public float followDelay = 10f;
    public float returnSpeed = 2f;

    [Header("Control Settings")]
    public bool canLookAround = true;

    private Vector2 targetRotation;
    private Vector2 smoothedRotation;
    private Vector2 lookInput;

    private Vector2 startRotation;

    void Start()
    {
        if (canLookAround)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        Vector3 euler = transform.localRotation.eulerAngles;
        float pitch = NormalizeAngle(euler.x);
        float yaw = NormalizeAngle(euler.y);

        startRotation = new Vector2(yaw, pitch);
        targetRotation = startRotation;
        smoothedRotation = startRotation;
    }

    void Update()
    {
        if (!canLookAround) return;

        lookInput = Mouse.current.delta.ReadValue();

        float mouseX = lookInput.x * mouseSensitivity * 0.02f;
        float mouseY = lookInput.y * mouseSensitivity * 0.02f;

        targetRotation.x += mouseX;
        targetRotation.y -= mouseY;

        targetRotation.x = Mathf.Clamp(targetRotation.x, -horizontalLookLimit, horizontalLookLimit);
        targetRotation.y = Mathf.Clamp(targetRotation.y, -verticalLookLimit, verticalLookLimit);

        if (lookInput == Vector2.zero)
            targetRotation = Vector2.Lerp(targetRotation, startRotation, returnSpeed * Time.deltaTime);

        smoothedRotation = Vector2.Lerp(smoothedRotation, targetRotation, followDelay * Time.deltaTime);

        transform.localRotation = Quaternion.Euler(smoothedRotation.y, smoothedRotation.x, 0f);

        HandleCursor();
    }

    private void HandleCursor()
    {
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public void SetCanLookAround(bool canLook)
    {
        this.canLookAround = canLook;

        if (!canLook)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private float NormalizeAngle(float angle)
    {
        if (angle > 180f) return angle - 360f;
        if (angle < -180f) return angle + 360f;
        return angle;
    }
}
