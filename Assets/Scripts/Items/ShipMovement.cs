using UnityEngine;

/// <summary>水平面航行；放帆比例线性决定目标速度，舵量和航速共同决定转向。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class ShipMovement : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float maxSpeed = 0.35f;
    [SerializeField, Min(0.01f)] private float acceleration = 0.3f;
    [SerializeField, Min(0.01f)] private float deceleration = 0.3f;
    [SerializeField, Min(0f)] private float maxTurnSpeed = 45f;
    [SerializeField, Range(0f, 1f)] private float sailAmount = 1f;
    [SerializeField, Range(-1f, 1f)] private float steering;
    [Header("海洋浮力（留空时使用原平面航行）")]
    [SerializeField] private OceanSurface ocean;
    [SerializeField] private float waterlineOffset = -0.15f;
    [SerializeField, Min(0.01f)] private float sampleLength = 2f;
    [SerializeField, Min(0.01f)] private float sampleWidth = 0.7f;
    [SerializeField, Min(0f)] private float heightFollowRate = 5f;
    [SerializeField, Min(0f)] private float tiltFollowRate = 4f;

    private Rigidbody body;
    public float CurrentSpeed { get; private set; }
    public float SailAmount => sailAmount;
    public float Steering => steering;

    public void SetSailAmount(float amount) => sailAmount = Mathf.Clamp01(amount);
    public void SetSteering(float amount)
    {
        steering = Mathf.Clamp(amount, -1f, 1f);
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        if (ocean == null)
            body.constraints |= RigidbodyConstraints.FreezePositionY |
                RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        else body.constraints &= ~(RigidbodyConstraints.FreezePositionY |
                RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ);
    }

    private void FixedUpdate()
    {
        if (body.isKinematic)
        {
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(body.rotation * Vector3.forward, Vector3.up).normalized;
        // 从实际速度继续加减速，碰撞阻挡后不会积累虚假的航速。
        float speed = Mathf.Max(0f, Vector3.Dot(body.linearVelocity, forward));
        float targetSpeed = Mathf.Clamp01(sailAmount) * maxSpeed;
        CurrentSpeed = Mathf.MoveTowards(speed, targetSpeed,
            (targetSpeed > speed ? acceleration : deceleration) * Time.fixedDeltaTime);
        float verticalSpeed = 0f;
        Vector3 tiltVelocity = Vector3.zero;
        if (ocean != null)
        {
            Vector3 position = body.position;
            verticalSpeed = (ocean.GetHeight(position) + waterlineOffset - position.y) * heightFollowRate;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float front = ocean.GetHeight(position + forward * sampleLength);
            float back = ocean.GetHeight(position - forward * sampleLength);
            float starboard = ocean.GetHeight(position + right * sampleWidth);
            float port = ocean.GetHeight(position - right * sampleWidth);
            Vector3 along = forward * (2f * sampleLength) + Vector3.up * (front - back);
            Vector3 across = right * (2f * sampleWidth) + Vector3.up * (starboard - port);
            Vector3 normal = Vector3.Cross(along, across).normalized;
            tiltVelocity = Vector3.Cross(body.rotation * Vector3.up, normal) * tiltFollowRate;
        }
        body.linearVelocity = forward * CurrentSpeed + Vector3.up * verticalSpeed;
        float speedRatio = maxSpeed > 0f ? Mathf.Clamp01(CurrentSpeed / maxSpeed) : 0f;
        body.angularVelocity = tiltVelocity + Vector3.up * (Mathf.Clamp(steering, -1f, 1f) * maxTurnSpeed * speedRatio * Mathf.Deg2Rad);
    }

    private void OnDisable()
    {
        CurrentSpeed = 0f;
        if (body == null || body.isKinematic) return;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }
}
