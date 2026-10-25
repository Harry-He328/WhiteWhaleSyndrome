using UnityEngine;

/// <summary>
/// 物件进入球形 Trigger 时，沿远离自身的方向施加一次推力。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public class RadialPush : MonoBehaviour
{
    [SerializeField, Min(0f)] private float pushImpulse = 6f;

    private Rigidbody body;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    private void OnTriggerEnter(Collider other)
    {
        // 手持时不生效，也不对其他检测用 Trigger 施力。
        if (!isActiveAndEnabled || body.isKinematic || other.isTrigger)
        {
            return;
        }

        Rigidbody target = other.attachedRigidbody;
        if (target == null || target == body || target.isKinematic ||
            target.GetComponent<Item>() == null)
        {
            return;
        }

        Vector3 direction = target.worldCenterOfMass - body.worldCenterOfMass;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        target.AddForce(direction.normalized * pushImpulse, ForceMode.Impulse);
    }
}
