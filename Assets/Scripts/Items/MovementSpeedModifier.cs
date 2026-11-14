using UnityEngine;

/// <summary>Locomotion state; knows nothing about nets, item input or ocean targeting.</summary>
[DisallowMultipleComponent]
public sealed class MovementSpeedModifier : MonoBehaviour
{
    private float multiplier = 1f;
    private float until;
    public float Multiplier => Time.time < until ? multiplier : 1f;
    public void Apply(float value, float seconds)
    { multiplier = Mathf.Clamp01(value); until = Time.time + Mathf.Max(0f, seconds); }
    public void Clear() { multiplier = 1f; until = 0f; }
    private void OnDisable() => Clear();
}
