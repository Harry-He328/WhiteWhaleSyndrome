using UnityEngine;

/// <summary>
/// 炮弹。直线飞，不受重力，飞满 lifetime 秒、或者飞出海洋显示范围就消失。
///
/// 它身上既没有碰撞体也没有刚体，是自己每帧挪 transform、再从上一步的位置往前打一条
/// 球形扫描来判命中。理由：炮弹一帧要走四厘米上下，而船体最窄的地方只有五厘米宽，
/// 靠物理的触发体很容易在两帧之间从船身上穿过去。自己扫就没这个空子，而且不用配刚体、
/// 不用配 Layer，少一堆能在 Inspector 里填错的东西。
///
/// 扫描只认「带血量的东西」。撞到岛、桌子、地板都不会有反应，炮弹从它们中间穿过去。
/// 以后要做「岛能挡炮弹」，就在挑命中的循环里加一条：撞到的不是船就继续往前找。
///
/// 飞出显示范围必须自己销毁。范围只有 3.32 × 2.12 米，炮弹 2 米/秒 飞 5 秒能跑 10 米，
/// 半个房间那么远。不拦的话玩家会看见炮弹飞出水面、横穿房间。
/// </summary>
public class CannonShot : MonoBehaviour
{
    [Tooltip("飞多久自动消失（秒）")]
    [SerializeField] private float lifetime = 5f;

    [Tooltip("判定半径（米）。太小会从船缝里漏过去，太大会隔空命中")]
    [SerializeField] private float hitRadius = 0.005f;

    // 扫描结果的复用缓冲。炮弹同时在飞的数量很少，八条够用，且不产生每帧的垃圾。
    private static readonly RaycastHit[] Hits = new RaycastHit[8];

    private Vector3 velocity;
    private float damage;
    private ShipHealth owner;
    private OceanRegion region;
    private float age;
    private bool spent;

    /// <summary>
    /// 出膛。position 一般给炮口，direction 给舷侧方向。
    /// ownerHealth 是开炮那条船的血量组件，用来避免炮弹打到自己身上。
    /// </summary>
    public void Launch(Vector3 position, Vector3 direction, float speed, float damageAmount,
                       ShipHealth ownerHealth, OceanRegion oceanRegion)
    {
        transform.position = position;

        // 炮弹只在水平面里飞。炮口如果在船上被摆高了一点，方向也仍然是水平的。
        direction.y = 0f;
        if (direction.sqrMagnitude < 1e-8f)
        {
            direction = Vector3.forward;
        }
        direction.Normalize();

        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        velocity = direction * Mathf.Max(0.01f, speed);
        damage = damageAmount;
        owner = ownerHealth;
        region = oceanRegion;
        age = 0f;
        spent = false;
    }

    private void FixedUpdate()
    {
        if (spent)
        {
            return;
        }

        float dt = Time.fixedDeltaTime;
        age += dt;

        if (age >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 from = transform.position;
        Vector3 to = from + velocity * dt;

        if (Sweep(from, to))
        {
            return;
        }

        transform.position = to;

        // 出界就收掉。边界外面是房间，炮弹不该飞出去。
        if (region != null && !region.Contains(to))
        {
            Destroy(gameObject);
        }
    }

    /// <summary>从上一步扫到这一步，命中带血量的船就结算。返回 true 表示这一发已经用掉了。</summary>
    private bool Sweep(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float distance = delta.magnitude;

        if (distance < 1e-6f)
        {
            return false;
        }

        Vector3 direction = delta / distance;

        int count = Physics.SphereCastNonAlloc(from, hitRadius, direction, Hits, distance,
                                               ~0, QueryTriggerInteraction.Ignore);

        // 扫描出来的顺序是不保证的，得自己挑最近的那一艘。挑错了会隔着一条船打到后面那条。
        int best = -1;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            ShipHealth health = Hits[i].collider.GetComponentInParent<ShipHealth>();

            // 没血量的（岛、桌子）跳过；自己的船跳过；已经沉了的跳过。
            if (health == null || health == owner || health.IsDead)
            {
                continue;
            }

            if (Hits[i].distance < bestDistance)
            {
                bestDistance = Hits[i].distance;
                best = i;
            }
        }

        if (best < 0)
        {
            return false;
        }

        ShipHealth target = Hits[best].collider.GetComponentInParent<ShipHealth>();

        transform.position = Hits[best].point;
        spent = true;
        target.TakeDamage(damage);

        Destroy(gameObject);
        return true;
    }
}
