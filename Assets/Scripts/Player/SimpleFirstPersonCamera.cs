using UnityEngine;
using UnityEngine.InputSystem;

public class SimpleFirstPersonCamera : MonoBehaviour
{
    [Header("Camera Settings")]
    public float mouseSensitivity = 2f;
    public float verticalLookLimit = 15f;
    public float horizontalLookLimit = 20f;

    [Header("Smoothing")]
    public float followDelay = 10f;
    public float returnSpeed = 2f;

    [Header("Control Settings")]
    public bool canLookAround = true;

    private Vector2 targetRotation;
    private Vector2 smoothedRotation;

    private Vector2 lookInput;

    void Start()
    {
        if (canLookAround)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
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
        {
            targetRotation = Vector2.Lerp(targetRotation, Vector2.zero, returnSpeed * Time.deltaTime);
        }

        smoothedRotation = Vector2.Lerp(smoothedRotation, targetRotation, followDelay * Time.deltaTime);

        transform.localRotation = Quaternion.Euler(smoothedRotation.y, smoothedRotation.x, 0f);

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
        canLookAround = canLook;

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
}
