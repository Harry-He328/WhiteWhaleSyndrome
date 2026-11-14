using UnityEngine;

namespace WhiteWhale.OceanItems
{
    /// <summary>Optional adapter on PlayerPickup. Converts the same ray used for room items to ocean space.</summary>
    public sealed class OceanItemTargetSource : MonoBehaviour, IItemTargetSource
    {
        public OceanTableDisplay display;
        public bool TryGetTarget(Ray ray, float distance, out ItemUseTarget target)
        {
            target = default;
            if (display == null || display.SourceRoot == null) return false;
            Matrix4x4 mapping = display.DisplayMatrix;
            Matrix4x4 inverse = mapping.inverse;
            Vector3 direction = inverse.MultiplyVector(ray.direction);
            Ray sourceRay = new Ray(inverse.MultiplyPoint3x4(ray.origin), direction.normalized);
            float nearest = distance;
            bool found = false;
            foreach (RaycastHit hit in Physics.RaycastAll(sourceRay, distance * direction.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(display.SourceRoot)) continue;
                Vector3 point = mapping.MultiplyPoint3x4(hit.point);
                float d = Vector3.Distance(ray.origin, point);
                if (d > nearest || !display.ContainsDisplayPoint(point)) continue;
                // An ocean obstacle must still block selection of an entity behind it.
                nearest = d;
                target = new ItemUseTarget(hit.collider.GetComponentInParent<Item>(), point,
                    mapping.inverse.transpose.MultiplyVector(hit.normal).normalized, this);
                found = true;
            }
            var water = display.SourceRoot.GetComponentInChildren<OceanSurface>();
            if (water != null && Mathf.Abs(sourceRay.direction.y) > 0.0001f)
            {
                float t = (water.transform.position.y - sourceRay.origin.y) / sourceRay.direction.y;
                for (int i = 0; i < 3; i++)
                    t = (water.GetHeight(sourceRay.GetPoint(t)) - sourceRay.origin.y) / sourceRay.direction.y;
                Vector3 point = Project(sourceRay.GetPoint(t));
                if (t >= 0 && Vector3.Distance(ray.origin, point) < nearest && display.ContainsDisplayPoint(point))
                {
                    target = new ItemUseTarget(null, point, Vector3.up, this, water);
                    return true;
                }
            }
            return found;
        }
        public Vector3 Project(Vector3 sourcePoint) => display.DisplayMatrix.MultiplyPoint3x4(sourcePoint);
        public void SetHighlighted(Item item) { if (display != null) display.HighlightItem(item); }
        private void OnDisable() => SetHighlighted(null);
    }
}
