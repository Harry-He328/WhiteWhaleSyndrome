using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>船舵、帆绳和火炮拉环共用鼠标拖动输入。先于视角和拿取更新。</summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerPickup))]
public class PlayerHelmInput : MonoBehaviour
{
    [SerializeField] private Camera viewCamera;
    [SerializeField, Min(0.1f)] private float reach = 3f;
    [SerializeField] private DragInputSettings dragSettings;
    [SerializeField] private LayerMask interactionMask = ~0;

    private PlayerPickup pickup;
    private ShipHelm activeHelm;
    private ShipSail activeSail;
    private CannonPullHandle activeCannonHandle;
    private Collider activeCollider;
    public bool IsDragging => activeHelm != null || activeSail != null || activeCannonHandle != null;

    private void Awake()
    {
        pickup = GetComponent<PlayerPickup>();
        if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
        bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        if (!Application.isFocused || Cursor.lockState != CursorLockMode.Locked ||
            mouse == null || escape ||
            pickup.HeldBody != null)
        {
            EndDrag();
            return;
        }

        if (IsDragging)
        {
            if (!mouse.leftButton.isPressed ||
                (activeHelm != null && !activeHelm.isActiveAndEnabled) ||
                (activeSail != null && !activeSail.isActiveAndEnabled) ||
                (activeCannonHandle != null && (!activeCannonHandle.isActiveAndEnabled || !activeCannonHandle.CanOperate)) ||
                activeCollider == null || !activeCollider.enabled || !activeCollider.gameObject.activeInHierarchy ||
                viewCamera == null || Vector3.Distance(viewCamera.transform.position,
                    activeCollider.ClosestPoint(viewCamera.transform.position)) > reach)
            {
                EndDrag();
                return;
            }

            // 鼠标位移已经是本帧的累计量，不再乘 deltaTime。
            Vector2 delta = mouse.delta.ReadValue();
            if (activeHelm != null) activeHelm.Turn(delta.x * (dragSettings != null ? dragSettings.helmDegreesPerPixel : 0.4f));
            else if (activeSail != null) activeSail.Drag(delta.y);
            else activeCannonHandle.Drag(delta);
            return;
        }

        if (!clicked) return;
        if (viewCamera == null)
        {
            return;
        }
        Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        // 操作装置共用检测与输入占用，普通碰撞体仍会遮挡。
        float nearestDistance = float.PositiveInfinity;
        ShipHelm nearestHelm = null;
        ShipSail nearestSail = null;
        CannonPullHandle nearestHandle = null;
        Collider nearestCollider = null;
        foreach (RaycastHit hit in Physics.RaycastAll(ray, reach, interactionMask, QueryTriggerInteraction.Collide))
        {
            ShipHelm helm = hit.collider.GetComponentInParent<ShipHelm>();
            ShipSail sail = hit.collider.GetComponentInParent<ShipSail>();
            CannonPullHandle handle = hit.collider.GetComponentInParent<CannonPullHandle>();
            if (hit.collider.isTrigger && helm == null && sail == null && handle == null) continue;
            if (hit.distance >= nearestDistance) continue;
            nearestDistance = hit.distance;
            nearestHelm = helm;
            nearestSail = sail;
            nearestHandle = handle;
            nearestCollider = hit.collider;
        }

        if (nearestHelm != null && nearestHelm.isActiveAndEnabled)
        {
            activeHelm = nearestHelm;
            activeCollider = nearestCollider;
        }
        else if (nearestSail != null && nearestSail.isActiveAndEnabled)
        {
            activeSail = nearestSail;
            activeCollider = nearestCollider;
        }
        else if (nearestHandle != null && nearestHandle.isActiveAndEnabled && nearestHandle.BeginPull(viewCamera))
        {
            activeCannonHandle = nearestHandle;
            activeCollider = nearestCollider;
        }
    }

    private void EndDrag()
    {
        if (activeCannonHandle != null) activeCannonHandle.EndPull();
        activeHelm = null;
        activeSail = null;
        activeCannonHandle = null;
        activeCollider = null;
    }

    private void OnDisable()
    {
        EndDrag();
    }
    private void OnApplicationFocus(bool focused)
    {
        if (!focused)
        {
            EndDrag();
        }
    }
}
