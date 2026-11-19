using UnityEngine;

/// <summary>Describes the waterline footprint of an object that creates contact foam.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class OceanContactFoamSource : MonoBehaviour
{
    private enum Footprint { Ellipse, RoundedRectangle }

    [SerializeField, Tooltip("Optional if the object is under an ocean root.")]
    private OceanSurface ocean;
    [SerializeField] private Footprint footprint;
    [SerializeField, Tooltip("Waterline half width and half length in ocean units.")]
    private Vector2 halfSize = new Vector2(0.5f, 1f);
    [SerializeField, Tooltip("Waterline center offset in ocean units: sideways, up, forward.")]
    private Vector3 centerOffset;
    [SerializeField, Range(0f, 1f)] private float strength = 1f;

    private OceanSurface registeredOcean;
    private bool needsRebind;

    private void OnEnable() => Rebind();
    private void OnDisable() => Unregister();
    private void OnValidate() => needsRebind = true;

    private void Update()
    {
        if (needsRebind) Rebind();
    }

    private void OnTransformParentChanged()
    {
        if (isActiveAndEnabled && ocean == null) Rebind();
    }

    /// <summary>Assigns the surface when spawning an object outside the ocean hierarchy.</summary>
    public void Bind(OceanSurface surface)
    {
        ocean = surface;
        if (isActiveAndEnabled) Rebind();
    }

    private void Rebind()
    {
        needsRebind = false;
        OceanSurface target = ocean;
        for (Transform ancestor = transform.parent; target == null && ancestor != null; ancestor = ancestor.parent)
            target = ancestor.GetComponentInChildren<OceanSurface>(true);
        if (target == registeredOcean) return;
        Unregister();
        registeredOcean = target;
        if (registeredOcean != null) registeredOcean.RegisterContactFoam(this);
    }

    private void Unregister()
    {
        if (registeredOcean != null) registeredOcean.UnregisterContactFoam(this);
        registeredOcean = null;
    }

    internal bool TryGetShaderData(Transform oceanTransform, out Vector4 hull, out Vector4 axes)
    {
        hull = axes = Vector4.zero;
        if (strength <= 0f || halfSize.x <= 0f || halfSize.y <= 0f) return false;

        Vector3 center = oceanTransform.InverseTransformPoint(transform.position);
        Vector3 heading = oceanTransform.InverseTransformDirection(transform.forward);
        Vector2 forward = new Vector2(heading.x, heading.z).normalized;
        if (forward.sqrMagnitude < 0.5f) return false;
        Vector2 right = new Vector2(forward.y, -forward.x);
        Vector2 hullCenter = new Vector2(center.x, center.z) + right * centerOffset.x + forward * centerOffset.z;
        hull = new Vector4(hullCenter.x, hullCenter.y, halfSize.x, halfSize.y);
        // The sign distinguishes rounded rectangles while the magnitude stores opacity.
        float shapeStrength = footprint == Footprint.RoundedRectangle ? -strength : strength;
        axes = new Vector4(forward.x, forward.y, center.y + centerOffset.y, shapeStrength);
        return true;
    }
}
