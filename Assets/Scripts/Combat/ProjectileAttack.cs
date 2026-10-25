using UnityEngine;

/// <summary>攻击效果的独立扩展入口；可在炮弹预制体上组合多个效果脚本。</summary>
public abstract class ProjectileAttack : MonoBehaviour
{
    public virtual void OnLaunched(CannonProjectile projectile) { }
    public abstract void OnImpact(ProjectileImpact impact);
}

public readonly struct ProjectileImpact
{
    public CannonProjectile Projectile { get; }
    public Rigidbody Owner { get; }
    public Collider Target { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public bool IsTabletop { get; }

    public ProjectileImpact(CannonProjectile projectile, Rigidbody owner, Collider target,
        Vector3 point, Vector3 normal, bool isTabletop)
    {
        Projectile = projectile;
        Owner = owner;
        Target = target;
        Point = point;
        Normal = normal;
        IsTabletop = isTabletop;
    }
}
