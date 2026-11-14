using UnityEngine;

namespace WhiteWhale.Flow
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Flow/Region")]
    public sealed class FlowRegion : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float radius = 1f;
        [SerializeField] private bool planar;

        public bool Contains(Vector3 position)
        {
            Vector3 offset = position - transform.position;
            if (planar) offset.y = 0;
            return offset.sqrMagnitude <= radius * radius;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            if (!planar) { Gizmos.DrawWireSphere(transform.position, radius); return; }
            Vector3 previous = transform.position + Vector3.right * radius;
            for (int i = 1; i <= 48; i++)
            {
                float angle = i * (2f * Mathf.PI / 48);
                var next = transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
