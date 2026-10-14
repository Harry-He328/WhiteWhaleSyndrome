using UnityEngine;

/// <summary>
/// 一个舷侧的炮组。左边一个、右边一个，各记各的冷却——按住一边不会把另一边的冷却也吃掉。
///
/// 它不知道是谁在开炮。玩家的船由 PlayerCannons 按键调 TryFire()，敌人的船由 EnemyShip
/// 在自己的射界内调。两边共用这一个组件。
///
/// 炮弹是沿**船的正横方向**打出去的，不是朝船头。所以想打到人得先把船横过来，让目标落在
/// 舷侧——这是舷炮的定义，也是这套玩法的全部意思。
///
/// 炮口只提供**位置**，不提供方向。方向永远从船体朝向推出来，这样在 Inspector 里把炮口
/// 转歪了也不会把炮弹打飞。
/// </summary>
public class Broadside : MonoBehaviour
{
    public enum Side { Left, Right }

    [Tooltip("这是哪一边的炮。左 = 朝船的左边打，右 = 朝右边打")]
    [SerializeField] private Side side = Side.Left;

    [Tooltip("炮口位置。留空就从组件所在的这个点打出去。只取位置，不看它的朝向")]
    [SerializeField] private Transform muzzle;

    [Tooltip("炮弹预制体。出膛时在炮口生成")]
    [SerializeField] private GameObject shotPrefab;

    [Tooltip("两炮之间的间隔（秒）。左右两边各算各的")]
    [SerializeField] private float reloadTime = 1.5f;

    [Tooltip("一发的伤害。玩家的炮调大、敌人的炮调小")]
    [SerializeField] private float damage = 5f;

    [Tooltip("炮弹速度（米/秒）")]
    [SerializeField] private float muzzleSpeed = 2f;

    private float readyAt;
    private ShipHealth owner;

    // 场景里只有一个海洋范围，所有炮共用一份查找结果，不必每门炮各找一次。
    private static OceanRegion cachedRegion;

    /// <summary>这一侧现在能不能开火。</summary>
    public bool IsReady => Time.time >= readyAt;

    /// <summary>还要等几秒。给 UI、音效用。</summary>
    public float ReloadRemaining => Mathf.Max(0f, readyAt - Time.time);

    /// <summary>这一侧朝外的水平方向。敌人 AI 用它判断目标在不在射界里。</summary>
    public Vector3 BeamDirection
    {
        get
        {
            // 船被浪推得左右倾斜的时候 transform.right 会带上一点竖直分量，
            // 抹掉它，保证炮弹是平着打出去的。
            Vector3 right = transform.right;
            right.y = 0f;
            if (right.sqrMagnitude < 1e-8f)
            {
                right = Vector3.right;
            }
            right.Normalize();

            return side == Side.Right ? right : -right;
        }
    }

    public Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position;

    private void Awake()
    {
        // 炮挂在船上（自己身上或者船的子物体上都行），开炮的那条船就是它的船。
        owner = GetComponentInParent<ShipHealth>();
    }

    /// <summary>开一炮。冷却没好、或者没配炮弹预制体，就什么都不做并返回 false。</summary>
    public bool TryFire()
    {
        // isActiveAndEnabled 这一条不能少：沉船的时候是把炮组 enabled = false，
        // 但玩家开炮的脚本每帧还在直接调这个方法 —— enabled 只管住 Unity 自己的回调，
        // 管不住外面直接调进来。少了它就等于沉到一半还在放炮。
        if (!isActiveAndEnabled || !IsReady || shotPrefab == null)
        {
            return false;
        }

        readyAt = Time.time + Mathf.Max(0.01f, reloadTime);

        Vector3 direction = BeamDirection;

        // 往舷侧挪一点点再生成。不然炮弹正好生成在船体的碰撞盒里，
        // 扫描的第一步就可能从船身内部开始。
        Vector3 position = MuzzlePosition + direction * 0.01f;

        GameObject shot = Instantiate(shotPrefab, position, Quaternion.LookRotation(direction, Vector3.up));

        CannonShot shell = shot.GetComponent<CannonShot>();
        if (shell != null)
        {
            shell.Launch(position, direction, muzzleSpeed, damage, owner, ResolveRegion());
        }

        return true;
    }

    private static OceanRegion ResolveRegion()
    {
        if (cachedRegion == null)
        {
            cachedRegion = FindFirstObjectByType<OceanRegion>();
        }

        return cachedRegion;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 1f);
        Vector3 from = MuzzlePosition;
        Gizmos.DrawWireSphere(from, 0.012f);
        Gizmos.DrawLine(from, from + BeamDirection * 0.35f);
    }
}
