using UnityEngine;

/// <summary>下拉绳子使帆上移收起；反向拖动放帆。帆和船速均按放帆比例线性变化。</summary>
[DisallowMultipleComponent]
public class ShipSail : MonoBehaviour
{
    [SerializeField] private ShipMovement ship;
    [SerializeField] private OceanRoomBridge oceanBridge;
    [SerializeField] private Transform sailVisual;
    [SerializeField] private Transform lowerSpar;
    [SerializeField] private Transform haulingRope;
    [SerializeField] private Transform sailLiftRope;
    [SerializeField] private Transform pulley;
    [SerializeField, Range(0f, 1f)] private float sailAmount = 1f;
    [SerializeField] private DragInputSettings dragSettings;
    [SerializeField, Min(0f)] private float sailTravel = 2.62f;
    [SerializeField, Min(0f)] private float handleTravel = 0.45f;

    private Vector3 loweredSailPosition;
    private Vector3 loweredSparPosition;
    private Vector3 releasedHandlePosition;
    private Vector3 liftAttachment;
    private Vector3 haulingAttachment;
    private Vector3 liftPulleyPoint;
    private Vector3 haulingPulleyPoint;

    public float SailAmount => sailAmount;

    private void Awake()
    {
        if ((ship == null && oceanBridge == null) || sailVisual == null || lowerSpar == null ||
            haulingRope == null || sailLiftRope == null || pulley == null)
        {
            Debug.LogError("帆绳需要绑定船、帆面、底杆、两段绳子及滑轮。", this);
            enabled = false;
            return;
        }

        loweredSailPosition = sailVisual.localPosition;
        loweredSparPosition = lowerSpar.localPosition;
        releasedHandlePosition = transform.localPosition;
        // 原型绳段为本地 Y 轴长度为 2 的圆柱，允许两段绳子的模型朝向相反。
        GetRopeEndpoints(sailLiftRope, out Vector3 liftBottom, out Vector3 liftTop);
        GetRopeEndpoints(haulingRope, out Vector3 haulingBottom, out Vector3 haulingTop);
        liftAttachment = lowerSpar.InverseTransformPoint(liftBottom);
        haulingAttachment = transform.InverseTransformPoint(haulingBottom);
        liftPulleyPoint = pulley.InverseTransformPoint(liftTop);
        haulingPulleyPoint = pulley.InverseTransformPoint(haulingTop);
        SetSailAmount(sailAmount);
    }

    // 鼠标 Y 位移向上为正：向上放绳，帆下降；向下拉绳，帆上升。
    public void Drag(float verticalPixels)
    {
        float sensitivity = dragSettings != null ? dragSettings.sailAmountPerPixel : 0.00175f;
        SetSailAmount(sailAmount + verticalPixels * sensitivity);
    }

    public void SetSailAmount(float amount)
    {
        sailAmount = Mathf.Clamp01(amount);
        if (!isActiveAndEnabled || sailVisual == null || lowerSpar == null ||
            haulingRope == null || sailLiftRope == null || pulley == null) return;

        float raised = 1f - sailAmount;
        sailVisual.localPosition = loweredSailPosition + Vector3.up * (raised * sailTravel);
        lowerSpar.localPosition = loweredSparPosition + Vector3.up * (raised * sailTravel);
        transform.localPosition = releasedHandlePosition - Vector3.up * (raised * handleTravel);
        UpdateRope(haulingRope, transform.TransformPoint(haulingAttachment), pulley.TransformPoint(haulingPulleyPoint));
        UpdateRope(sailLiftRope, lowerSpar.TransformPoint(liftAttachment), pulley.TransformPoint(liftPulleyPoint));
        if (oceanBridge != null) oceanBridge.SetSailAmount(sailAmount);
        else if (ship != null) ship.SetSailAmount(sailAmount);
    }

    private void GetRopeEndpoints(Transform rope, out Vector3 bottom, out Vector3 top)
    {
        Vector3 a = rope.TransformPoint(Vector3.down);
        Vector3 b = rope.TransformPoint(Vector3.up);
        bool aIsTop = (a - pulley.position).sqrMagnitude < (b - pulley.position).sqrMagnitude;
        bottom = aIsTop ? b : a;
        top = aIsTop ? a : b;
    }

    private static void UpdateRope(Transform rope, Vector3 bottom, Vector3 top)
    {
        Vector3 localBottom = rope.parent.InverseTransformPoint(bottom);
        Vector3 localTop = rope.parent.InverseTransformPoint(top);
        Vector3 direction = localTop - localBottom;
        rope.localPosition = (localBottom + localTop) * 0.5f;
        if (direction.sqrMagnitude > 0.000001f)
            rope.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
        Vector3 scale = rope.localScale;
        scale.y = direction.magnitude * 0.5f;
        rope.localScale = scale;
    }
}
