using UnityEngine;

/// <summary>
/// 带“灵活”label 时随机移动和跳跃，碰撞完全交给刚体物理。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Item), typeof(Rigidbody))]
public class AgileMovement : MonoBehaviour
{
    [SerializeField, Min(0f)] private float moveSpeed = 2f;
    [SerializeField, Min(0f)] private float jumpHeight = 0.4f;
    [SerializeField, Min(0.1f)] private float directionInterval = 2f;
    [SerializeField, Min(0.1f)] private float jumpInterval = 1.2f;

    private Item item;
    private Rigidbody body;
    private Collider bodyCollider;
    private Vector3 direction;
    private float nextDirectionTime;
    private float nextJumpTime;

    private void Awake()
    {
        item = GetComponent<Item>();
        body = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();

        if (bodyCollider == null)
        {
            Debug.LogError("灵活物件需要一个碰撞体。", this);
            enabled = false;
            return;
        }

        nextJumpTime = Time.time + Random.Range(0.5f, 1.5f);
    }

    private void FixedUpdate()
    {
        if (body.isKinematic || !LabelSystem.HasLabel(item, ItemLabel.Agile))
        {
            return;
        }

        if (Time.time >= nextDirectionTime)
        {
            Vector2 randomDirection = Random.insideUnitCircle.normalized;
            direction = new Vector3(randomDirection.x, 0f, randomDirection.y);
            nextDirectionTime = Time.time + directionInterval * Random.Range(0.5f, 1.5f);
        }

        Bounds bounds = bodyCollider.bounds;
        bool grounded = Physics.Raycast(bounds.center, Vector3.down, out RaycastHit hit,
            bounds.extents.y + 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && hit.normal.y > 0.5f;

        if (!grounded)
        {
            return;
        }

        // 只推动自己，撞到的物件由碰撞自然受到影响。
        Vector3 velocity = body.linearVelocity;
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
        body.AddForce((direction * moveSpeed - horizontalVelocity) * 8f, ForceMode.Acceleration);

        if (Time.time >= nextJumpTime)
        {
            float jumpSpeed = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);
            body.AddForce(Vector3.up * jumpSpeed, ForceMode.VelocityChange);
            nextJumpTime = Time.time + jumpInterval * Random.Range(0.7f, 1.3f);
        }
    }
}
