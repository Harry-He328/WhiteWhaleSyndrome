using UnityEngine;

/// <summary>重力弹道和生命周期。具体攻击效果交给独立 ProjectileAttack。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public class CannonProjectile : MonoBehaviour
{
    [SerializeField] private string tabletopTag;
    [SerializeField, Min(0.1f)] private float maxLifetime = 8f;
    [SerializeField, Min(0f)] private float gravityScale = 1f;
    private Rigidbody body;
    private Collider projectileCollider;
    private ProjectileAttack[] attacks;
    private Rigidbody owner;
    private bool finished;
    private bool launched;
    private float expiresAt;

    public Rigidbody Owner => owner;
    public string TabletopTag => tabletopTag;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        projectileCollider = GetComponent<Collider>();
        attacks = GetComponents<ProjectileAttack>();
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    public void Launch(Rigidbody source, Vector3 velocity)
    {
        owner = source;
        if (source != null)
        {
            foreach (Collider shipCollider in source.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(projectileCollider, shipCollider);
        }
        body.linearVelocity = velocity;
        expiresAt = Time.time + Mathf.Max(0.1f, maxLifetime);
        launched = true;
        foreach (ProjectileAttack attack in attacks)
            if (attack.isActiveAndEnabled) attack.OnLaunched(this);
    }

    private void Update()
    {
        if (launched && !finished && Time.time >= expiresAt) Finish();
    }

    private void FixedUpdate()
    {
        if (launched && !finished && gravityScale != 1f)
            body.AddForce(Physics.gravity * (gravityScale - 1f), ForceMode.Acceleration);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!launched || finished) return;
        Collider target = collision.collider;
        if (owner != null && (target.attachedRigidbody == owner || target.transform.IsChildOf(owner.transform))) return;

        // Tag 读取命中的碰撞体本身，不依赖父物体自动继承。
        bool isTabletop = !string.IsNullOrEmpty(tabletopTag) && target.CompareTag(tabletopTag);
        ContactPoint contact = collision.GetContact(0);
        var impact = new ProjectileImpact(this, owner, target, contact.point, contact.normal, isTabletop);
        // 延迟销毁前先封住入口，多个碰撞体也只结算一次。
        finished = true;
        projectileCollider.enabled = false;
        try
        {
            foreach (ProjectileAttack attack in attacks)
                if (attack.isActiveAndEnabled) attack.OnImpact(impact);
        }
        finally { Destroy(gameObject); }
    }

    private void Finish()
    {
        finished = true;
        projectileCollider.enabled = false;
        Destroy(gameObject);
    }

    private void OnDestroy() => OceanVisualEvents.Notify(gameObject.scene);
}
