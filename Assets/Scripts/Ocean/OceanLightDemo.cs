using UnityEngine;

/// <summary>Local tabletop light, animated independently of the ocean simulation.</summary>
[RequireComponent(typeof(Light))]
public sealed class OceanLightDemo : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField, Min(0.1f)] private float orbitSeconds = 8f;
    [SerializeField, Min(0.1f)] private float colorSeconds = 6f;
    [SerializeField] private float radius = 0.65f;
    [SerializeField] private float height = 0.8f;
    private Light lamp;
    private Renderer marker;
    private MaterialPropertyBlock properties;

    public void Configure(Transform center) => target = center;

    private void Awake()
    {
        lamp = GetComponent<Light>();
        marker = GetComponentInChildren<Renderer>();
        properties = new MaterialPropertyBlock();
    }

    private void Update() => EvaluateAtTime(Time.time);

    public void EvaluateAtTime(float seconds)
    {
        if (lamp == null) Awake();
        if (target == null) return;
        float angle = seconds / Mathf.Max(0.1f, orbitSeconds) * Mathf.PI * 2f;
        transform.position = target.position + new Vector3(Mathf.Cos(angle) * radius,
            height + Mathf.Sin(angle * 2f) * 0.12f, Mathf.Sin(angle) * radius);
        transform.LookAt(target.position);
        lamp.color = Color.HSVToRGB(Mathf.Repeat(seconds / Mathf.Max(0.1f, colorSeconds), 1f), 0.85f, 1f);
        if (marker == null) return;
        properties.SetColor("_BaseColor", lamp.color);
        properties.SetColor("_EmissionColor", lamp.color * 2f);
        marker.SetPropertyBlock(properties);
    }
}
