using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 第一人称行走：走路 + 转头 + 重力，就这三样。不含跳跃、蹲下、交互。
/// 角色本体是个胶囊（脚底在物体的 y=0 处），相机挂在眼睛高度。
///
/// 输入直接读 Input System 包的设备（Keyboard / Mouse），不依赖 Input Action 资源——
/// 本项目的 Active Input Handler 设的是 New，老的 Input.GetKey 跑起来会直接抛异常。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Tooltip("眼睛所在的 Transform，一般就是相机。留空会自动去找 MainCamera")]
    [SerializeField] private Transform view;

    [Tooltip("行走速度（米/秒）。真人散步约 1.4，这里默认略快一点")]
    [SerializeField] private float moveSpeed = 2.4f;

    [Tooltip("鼠标灵敏度：每移动一像素转多少度")]
    [SerializeField] private float lookSensitivity = 0.12f;

    [Tooltip("抬头 / 低头的角度上限")]
    [SerializeField] private float pitchLimit = 85f;

    [SerializeField] private float gravity = -9.81f;

    private CharacterController controller;
    private float pitch;
    private float verticalSpeed;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (view == null && Camera.main != null)
        {
            view = Camera.main.transform;
        }
    }

    private void OnEnable()
    {
        SetCursorLocked(true);
    }

    private void OnDisable()
    {
        SetCursorLocked(false);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        // 切出去的时候把鼠标还给系统，否则在编辑器里点不到任何东西
        SetCursorLocked(hasFocus);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void Update()
    {
        Look();
        Move();
    }

    private void Look()
    {
        if (Mouse.current == null || view == null)
        {
            return;
        }

        // 在编辑器里按 Esc 会解锁鼠标（Unity 自己的行为），点一下画面重新锁上
        if (Cursor.lockState != CursorLockMode.Locked && Mouse.current.leftButton.wasPressedThisFrame)
        {
            SetCursorLocked(true);
        }

        Vector2 delta = Mouse.current.delta.ReadValue();

        // 转身加在身体上，俯仰只加在眼睛上——这样走路的朝向不会跟着抬头低头变
        transform.Rotate(0f, delta.x * lookSensitivity, 0f, Space.Self);

        pitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -pitchLimit, pitchLimit);
        view.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void Move()
    {
        float right = 0f;
        float forward = 0f;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.dKey.isPressed) right += 1f;
            if (keyboard.aKey.isPressed) right -= 1f;
            if (keyboard.wKey.isPressed) forward += 1f;
            if (keyboard.sKey.isPressed) forward -= 1f;
        }

        // 身体只有 yaw 没有俯仰，所以 right / forward 本来就是水平的；再压一次 y 只是保险
        Vector3 direction = transform.right * right + transform.forward * forward;
        direction.y = 0f;
        if (direction.sqrMagnitude > 1f)
        {
            direction.Normalize();
        }

        // 贴地时给一个小的向下速度，否则 CharacterController 在斜坡上会一跳一跳
        if (controller.isGrounded && verticalSpeed < 0f)
        {
            verticalSpeed = -2f;
        }
        verticalSpeed += gravity * Time.deltaTime;

        controller.Move((direction * moveSpeed + Vector3.up * verticalSpeed) * Time.deltaTime);
    }
}
