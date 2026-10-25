using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// WASD 移动，鼠标控制视角，空格低跳。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [SerializeField] private Camera viewCamera;
    [SerializeField, Min(0f)] private float moveSpeed = 3f;
    [SerializeField, Min(0f)] private float mouseSensitivity = 0.1f;
    [SerializeField, Min(0f)] private float jumpHeight = 0.35f;

    private CharacterController controller;
    private float pitch;
    private float verticalSpeed;
    private PlayerHelmInput helmInput;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        helmInput = GetComponent<PlayerHelmInput>();
        if (viewCamera == null)
        {
            viewCamera = GetComponentInChildren<Camera>();
        }

        if (viewCamera == null)
        {
            Debug.LogError("第一人称移动需要玩家下的相机。", this);
            enabled = false;
            return;
        }

        pitch = Mathf.DeltaAngle(0f, viewCamera.transform.localEulerAngles.x);
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            SetCursorLocked(true);
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        bool acceptInput = Application.isFocused;

        if (acceptInput && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(false);
            acceptInput = false;
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (acceptInput && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                SetCursorLocked(true);
            }

            acceptInput = false;
        }

        bool draggingControl = helmInput != null && helmInput.IsDragging;
        if (acceptInput && mouse != null && !draggingControl)
        {
            Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
            transform.Rotate(0f, look.x, 0f);
            pitch = Mathf.Clamp(pitch - look.y, -80f, 80f);
            viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        MovePlayer(acceptInput && !draggingControl ? keyboard : null);
    }

    private void MovePlayer(Keyboard keyboard)
    {
        if (!controller.enabled)
        {
            return;
        }

        Vector2 input = Vector2.zero;
        if (keyboard != null)
        {
            float sideways = (keyboard.dKey.isPressed ? 1f : 0f) -
                             (keyboard.aKey.isPressed ? 1f : 0f);
            float forward = (keyboard.wKey.isPressed ? 1f : 0f) -
                            (keyboard.sKey.isPressed ? 1f : 0f);
            input = Vector2.ClampMagnitude(new Vector2(sideways, forward), 1f);
        }

        if (controller.isGrounded && verticalSpeed < 0f)
        {
            verticalSpeed = -2f;
        }

        float gravity = Physics.gravity.y;
        if (controller.isGrounded && keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
        {
            // 使用当前碰撞体的实际高度限制跳高，之后调整身高也不会超过半身高。
            float playerHeight = controller.height * Mathf.Abs(transform.lossyScale.y);
            float height = Mathf.Clamp(jumpHeight, 0f, playerHeight * 0.45f);
            verticalSpeed = Mathf.Sqrt(-2f * gravity * height);
        }

        verticalSpeed += gravity * Time.deltaTime;
        Vector3 movement = (transform.right * input.x + transform.forward * input.y) * moveSpeed;
        movement.y = verticalSpeed;
        CollisionFlags collisions = controller.Move(movement * Time.deltaTime);

        if ((collisions & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
        {
            verticalSpeed = 0f;
        }
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void OnDisable()
    {
        verticalSpeed = 0f;
        SetCursorLocked(false);
    }
}
