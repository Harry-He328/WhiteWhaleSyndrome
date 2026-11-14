using System;
using UnityEngine;

namespace WhiteWhale.Combat
{
    [DisallowMultipleComponent]
    public sealed class Health : MonoBehaviour, IDamageable
    {
        [SerializeField, Min(1f)] private float maximum = 100f;
        public float Maximum => maximum;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;
        public bool AcceptsDamage { get; set; }
        public event Action Changed;
        public event Action Died;

        private void Awake() => ResetHealth(maximum);

        public void ResetHealth(float value)
        {
            maximum = Mathf.Max(1f, value);
            Current = maximum;
            AcceptsDamage = false;
            Changed?.Invoke();
        }

        public void TakeDamage(float amount)
        {
            if (!AcceptsDamage || IsDead || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            Current = Mathf.Max(0f, Current - amount);
            Changed?.Invoke();
            if (IsDead) { AcceptsDamage = false; Died?.Invoke(); }
        }
    }
}
