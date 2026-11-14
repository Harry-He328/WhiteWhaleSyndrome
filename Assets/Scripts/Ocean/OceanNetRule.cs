using UnityEngine;
using WhiteWhale.Combat;

namespace WhiteWhale.OceanItems
{
    /// <summary>Ocean-specific rule attached to an ordinary physical item.</summary>
    public sealed class OceanNetRule : MonoBehaviour, IItemUseRule
    {
        [Range(0f, 1f)] public float speedMultiplier = 0.5f;
        [Min(0.1f)] public float duration = 10f;
        [Min(0.1f)] public float flightDuration = 0.55f;
        private Rigidbody body;
        private OceanItemTargetSource source;
        private MovementSpeedModifier receiver;
        private Health health;
        private Vector3 start;
        private float started;
        private Vector3 initialScale;
        public bool IsFlying { get; private set; }

        public bool CanUse(PlayerPickup pickup, ItemUseTarget target)
        {
            if (IsFlying || pickup == null || target.Item == null || pickup.HeldItem != GetComponent<Item>() ||
                !(target.Source is OceanItemTargetSource)) return false;
            var hp = target.Item.GetComponent<Health>();
            return hp != null && !hp.IsDead && hp.AcceptsDamage && target.Item.GetComponent<MovementSpeedModifier>() != null;
        }

        public bool TryUse(PlayerPickup pickup, ItemUseTarget target)
        {
            if (!CanUse(pickup, target)) return false;
            body = GetComponent<Rigidbody>();
            source = (OceanItemTargetSource)target.Source;
            receiver = target.Item.GetComponent<MovementSpeedModifier>();
            health = target.Item.GetComponent<Health>();
            pickup.Drop();
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            body.isKinematic = true; body.useGravity = false;
            start = transform.position; started = Time.time; IsFlying = true;
            initialScale = transform.localScale;
            return true;
        }

        private void Update()
        {
            if (!IsFlying) return;
            if (source == null || !source.isActiveAndEnabled || source.display == null || source.display.SourceRoot == null ||
                receiver == null || !receiver.gameObject.activeInHierarchy ||
                health == null || health.IsDead || !health.AcceptsDamage)
            { gameObject.SetActive(false); Destroy(gameObject); return; }
            float t = Mathf.Clamp01((Time.time - started) / flightDuration);
            Vector3 destination = source.Project(receiver.transform.position + Vector3.up * 2f);
            body.position = Vector3.Lerp(start, destination, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.25f);
            transform.localScale = initialScale * Mathf.Lerp(1f, 0.18f, t);
            if (t < 1f) return;
            receiver.Apply(speedMultiplier, duration);
            gameObject.SetActive(false); Destroy(gameObject);
        }
    }
}
