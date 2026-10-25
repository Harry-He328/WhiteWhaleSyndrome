using UnityEngine;

/// <summary>Seeded, slowly varying visual wind. No global Random state or abrupt target changes.</summary>
[DisallowMultipleComponent]
public sealed class OceanWind : MonoBehaviour
{
    [SerializeField] private int seed = 731;
    [SerializeField, Min(5f)] private float variationSeconds = 32f;
    [SerializeField, Range(0f, 75f)] private float directionVariation = 40f;
    [SerializeField] private Vector2 strengthRange = new Vector2(0.7f, 1.3f);

    public void Sample(float time, out float directionOffset, out float strength)
    {
        float t = time / Mathf.Max(5f, variationSeconds);
        float offset = Mathf.Abs(seed % 10000) * 0.137f;
        directionOffset = (Mathf.PerlinNoise(offset + 12.7f, t) * 2f - 1f) * directionVariation;
        float breeze = Mathf.PerlinNoise(offset + 57.3f, t * 1.37f + 17f);
        strength = Mathf.Lerp(Mathf.Max(0f, strengthRange.x), Mathf.Max(strengthRange.x, strengthRange.y), breeze);
    }
}
