using UnityEngine;

/// <summary>船舵绕本地 Z 轴转动，保留松手时的舵角。</summary>
[DisallowMultipleComponent]
public class ShipHelm : MonoBehaviour
{
    [SerializeField] private ShipMovement ship;
    [SerializeField] private OceanRoomBridge oceanBridge;
    [SerializeField] private Transform wheelVisual;
    [SerializeField, Min(1f)] private float maxWheelAngle = 135f;
    [SerializeField] private float wheelAngle;
    private Quaternion neutralRotation;

    public float WheelAngle => wheelAngle;

    private void Awake()
    {
        if (wheelVisual == null) wheelVisual = transform;
        neutralRotation = wheelVisual.localRotation;
        ApplySteering();
    }

    public void Turn(float degrees)
    {
        wheelAngle = Mathf.Clamp(wheelAngle + degrees, -maxWheelAngle, maxWheelAngle);
        ApplySteering();

    }

    private void ApplySteering()
    {
        wheelAngle = Mathf.Clamp(wheelAngle, -maxWheelAngle, maxWheelAngle);
        // 从玩家所在的负 Z 一侧看，正 Z 旋转表现为顺时针右打舵。
        wheelVisual.localRotation = neutralRotation * Quaternion.AngleAxis(wheelAngle, Vector3.forward);
        if (oceanBridge != null) oceanBridge.SetSteering(wheelAngle / Mathf.Max(1f, maxWheelAngle));
        else if (ship != null) ship.SetSteering(wheelAngle / Mathf.Max(1f, maxWheelAngle));
    }
}
