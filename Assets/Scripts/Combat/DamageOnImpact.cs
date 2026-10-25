using UnityEngine;

/// <summary>普通伤害示例。敌船或其他可受伤目标实现 IDamageable 即可接入。</summary>
public class DamageOnImpact : ProjectileAttack
{
    [SerializeField, Min(0f)] private float damage = 10f;

    public override void OnImpact(ProjectileImpact impact)
    {
        if (impact.IsTabletop) return;
        IDamageable target = impact.Target.GetComponentInParent<IDamageable>();
        target?.TakeDamage(damage);
    }
}
