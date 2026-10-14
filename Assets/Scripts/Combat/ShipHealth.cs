using System;
using UnityEngine;

/// <summary>
/// 船的血量。玩家的船和敌人的船各挂一个，数值在 Inspector 里分别填。
///
/// 它只管三件事：记血、扣血、血扣光时通知一声。谁打的、打完该沉还是该计数，
/// 它都不知道——那是听 Died 的人的事。
///
/// 演示要用的两个旋钮都故意留在这儿：
///   · 想让敌人一炮就沉 —— 敌人的上限填得跟玩家炮弹伤害一样大（或者更小）。
///   · 想让玩家不容易死 —— 敌人的炮弹伤害调低。
/// 两个值都不写死在代码里，改数值不用重新编译。
/// </summary>
public class ShipHealth : MonoBehaviour
{
    [Tooltip("血量上限。敌人的这个值 ≤ 玩家炮弹的伤害时，就是一炮一沉")]
    [SerializeField] private float maxHealth = 100f;

    private float current;

    /// <summary>血扣光时广播一次。参数是自己。沉船、计数都挂在这里。</summary>
    public event Action<ShipHealth> Died;

    /// <summary>每挨一下广播一次。参数是这一下的伤害量。做受击闪光、音效用。</summary>
    public event Action<float> Damaged;

    public float MaxHealth => maxHealth;
    public float Current => current;
    public float Normalized => maxHealth > 1e-4f ? Mathf.Clamp01(current / maxHealth) : 0f;
    public bool IsDead => current <= 0f;

    private void Awake()
    {
        current = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        // 已经沉了的船不再吃伤害。不然同一帧好几发炮弹一起命中的时候，Died 会被广播好几次，
        // 订阅它的地方（沉船、计数）就会重复执行——击杀数会一次涨好几个。
        if (amount <= 0f || IsDead)
        {
            return;
        }

        current = Mathf.Max(0f, current - amount);
        Damaged?.Invoke(amount);

        if (current <= 0f)
        {
            Died?.Invoke(this);
        }
    }

    /// <summary>回满。暂时没人调，留给以后做重开。</summary>
    public void ResetHealth()
    {
        current = maxHealth;
    }
}
