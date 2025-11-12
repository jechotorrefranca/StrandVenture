using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems; // << add this

public class FirstPersonCameraMovement : MonoBehaviour
{
    [Header("Camera Settings")]
    public float mouseSensitivity = 2f;
    public float verticalLookLimit = 80f;
    public float horizontalLookLimit = 180f;

    [Header("Smoothing")]
    public float followDelay = 10f;
    public float returnSpeed = 2f;

    [Header("Movement")]
    public bool canMove = true;
    public float moveSpeed = 5f;
    public float gravity = -9.81f;

    [Header("Control Settings")]
    public bool canLookAround = true;
    public bool uiIsOpen = false;


    private Vector2 targetRotation;
    private Vector2 smoothedRotation;
    private Vector2 lookInput;

    private Vector2 startRotation;

    private CharacterController controller;
    private float verticalVelocity = 0f;

    void Start()
    {
        if (canLookAround)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        controller = GetComponent<CharacterController>();
        if (controller == null)
        {
            controller = gameObject.AddComponent<CharacterController>();
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
        HandleCursor();

        if (canLookAround)
        {
            HandleLook();
        }

        if (canMove)
        {
            HandleMove();
        }
        else
        {
            if (controller != null && controller.isGrounded)
                verticalVelocity = -1f;
        }
    }

    private void HandleLook()
    {
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
    }

    private void HandleMove()
    {
        if (controller == null) return;

        float forward = 0f;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) forward += 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) forward -= 1f;

        float right = 0f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) right += 1f;
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) right -= 1f;

        Vector3 desired = Vector3.zero;

        Quaternion yawRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        Vector3 forwardDir = yawRotation * Vector3.forward;
        Vector3 rightDir = yawRotation * Vector3.right;

        desired = forwardDir * forward + rightDir * right;
        if (desired.magnitude > 1f) desired = desired.normalized;

        Vector3 horizontalVelocity = desired * moveSpeed;

        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f) verticalVelocity = -1f;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        Vector3 finalVelocity = horizontalVelocity + Vector3.up * verticalVelocity;

        controller.Move(finalVelocity * Time.deltaTime);
    }

    private void HandleCursor()
    {
        // If player can't look around (UI open / inspecting), don't auto-relock
        if (!canLookAround)
            return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        // If pointer is over UI don't relock (prevents clicks on UI from hiding cursor)
        bool pointerOverUI = false;
        var es = EventSystem.current;
        if (es != null)
        {
            // In editor and builds this will detect if pointer is over UI
            pointerOverUI = es.IsPointerOverGameObject();
        }

        // Only relock when clicking and not clicking the UI
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
            && Cursor.lockState == CursorLockMode.None && !pointerOverUI)
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

    public void SetCanMove(bool canMove)
    {
        this.canMove = canMove;

        if (!canMove && !canLookAround)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private float NormalizeAngle(float angle)
    {
        if (angle > 180f) return angle - 360f;
        if (angle < -180f) return angle + 360f;
        return angle;
    }
}
