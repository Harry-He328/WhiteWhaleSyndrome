using UnityEngine;

[CreateAssetMenu(menuName = "White Whale/Cannon Definition")]
public class CannonDefinition : ScriptableObject
{
    public CannonProjectile projectilePrefab;
    [Min(0.01f)] public float muzzleSpeed = 3.5f;
    [Range(0f, 15f)] public float elevation = 5f;
    [Min(0f)] public float cooldown = 1f;
}
