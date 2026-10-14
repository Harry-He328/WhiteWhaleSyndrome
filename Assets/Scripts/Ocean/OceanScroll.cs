using UnityEngine;

/// <summary>
/// 让船在托盘里「原地航行」：船不往托盘外面跑，海面从船底下流过去。
///
/// 托盘只有三米多长，船按巡航速度一直开，几秒钟就能顶到边。所以船往外跑出一段距离之后，
/// 超出的那部分位移不再让船继续往外走，而是原封不动挪到浪的采样坐标上 ——
/// 浪往后推多少，船就少走多少。看起来就是船在走、海在退。
///
/// 三条必须守住的规矩：
///
/// 1. 船的位置只能由 Floater 一处写入。这里算出来的「拉回」位移放在 LastRecentre 里，
///    由 Floater 并进它那唯一一次位置写入。这里**不能**自己去动刚体 ——
///    两次位置写入是互相覆盖的（设的是整份目标位置，不是增量），谁赢取决于脚本执行顺序。
///
/// 2. 挪给浪的那一份，必须和从船上扣掉的那一份**严格相等**（只差一个符号）。
///    两边各算各的、或者少扣一点，都会让「船走了多远」和「海退了多远」对不上，
///    船和它脚下的浪会慢慢错开。所以这一份是同一个数，不是两次独立计算。
///
/// 3. 船的朝向由 ShipMovement 管，这里一个字节都不碰。只挪水平位置。
///
/// 自由区（死区）做成矩形，按海洋自己的长宽各取一个比例。托盘是长的，
/// 用圆形死区会让船在窄的那一边很快被拉住、在宽的那一边却很自由。
///
/// 拉回的力度是按**超出量**给的，不是按速度给的：超得越多拉得越狠。
/// 于是船会停在「往外的速度 = 拉回的速度」那个位置，不会一路贴到边上，
/// 看着始终在托盘中间一带晃。而且正好在自由区边界上时超出量为 0、拉力也为 0，
/// 船跨过边界的时候速度是连续的，不会突然顿一下。
/// </summary>
[DefaultExecutionOrder(-40)] // 要在 ShipMovement(-50) 之后、Floater(默认 0) 之前
public class OceanScroll : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("海面。采样平移往它身上推")]
    [SerializeField] private Waves waves;

    [Tooltip("被盯着的那条船")]
    [SerializeField] private Transform ship;

    [Header("自由区")]
    [Tooltip("船在这个范围里自由跑，不做任何修正。数是占海面半宽 / 半长的比例。" +
             "0.3 = 船能在托盘中心大约三成的范围里随便跑，超出才开始拉")]
    [SerializeField, Range(0f, 0.95f)] private float freeZone = 0.3f;

    [Header("阻尼")]
    [Tooltip("拉回中心的力度（每秒）。越大船越贴着中心、海退得越干脆；" +
             "越小船能跑得越靠外、手感越松。" +
             "船最后停稳的位置 ≈ 自由区边缘 + 巡航速度 ÷ 这个值（0.3 ÷ 2.5 ≈ 0.12 米）")]
    [SerializeField] private float recentreRate = 2.5f;

    /// <summary>
    /// 这一物理帧要加在船身上的「拉回中心」位移。世界空间，只有水平分量。
    /// Floater 把它和推进位移并成同一次位置写入。见文件头第 1 条。
    /// </summary>
    public Vector3 LastRecentre { get; private set; }

    /// <summary>
    /// 带出这条滚动的船 —— 玩家那条。海面每帧后退多少，是照它跑出去多少算的。
    ///
    /// Floater 那边已经不再用它做过滤了：LastRecentre 是世界空间里「水在动」的位移，
    /// 水里所有东西都要跟着动，所以敌船也吃同一份。留着这个属性只为说明滚动是谁带出来的。
    /// </summary>
    public Transform Ship => ship;

    private void FixedUpdate()
    {
        // 每帧先清零。下面任何一条提前返回，船这一帧就不该被额外挪动 ——
        // 留着上一帧的值会让船被重复拉，越拉越猛。
        LastRecentre = Vector3.zero;

        if (waves == null || ship == null)
        {
            return;
        }

        // 全部在海洋自己的局部空间里量：浪的采样平移本来就是局部量，
        // 在这里读船的位置就不用来回换算了，也不会被 Diorama 的旋转缩放绕进去。
        Vector3 local = waves.transform.InverseTransformPoint(ship.position);
        Vector2 half = waves.HalfExtentLocal;

        float freeX = half.x * freeZone;
        float freeZ = half.y * freeZone;

        float excessX = Mathf.Max(0f, Mathf.Abs(local.x) - freeX);
        float excessZ = Mathf.Max(0f, Mathf.Abs(local.z) - freeZ);

        if (excessX <= 0f && excessZ <= 0f)
        {
            return;
        }

        float rate = recentreRate * Time.fixedDeltaTime;
        Vector2 pull = new Vector2(
            -Mathf.Sign(local.x) * excessX * rate,
            -Mathf.Sign(local.z) * excessZ * rate);

        // 局部 → 世界：先按本物体的缩放拉长，再按它的朝向转。
        // 直接写出来，是为了不去赌 TransformDirection 在带缩放时到底算不算缩放。
        Vector3 scale = waves.transform.lossyScale;
        LastRecentre = waves.transform.rotation * new Vector3(pull.x * scale.x, 0f, pull.y * scale.z);

        // 从船上扣掉的那一份，原样交给浪，只差一个符号：船往回收，等于水往前走。
        // 见文件头第 2 条 —— 这两处必须是同一个数。
        waves.AddScroll(-pull);
    }

    private void OnDisable()
    {
        // 组件被关掉之后 FixedUpdate 就不再跑了，LastRecentre 会一直留着最后一帧的值，
        // 而 Floater 每帧照旧来取 —— 船就会被一份过期的位移一直往里拖。
        // 所以关掉的时候必须清干净。
        LastRecentre = Vector3.zero;
    }

    private void OnDrawGizmosSelected()
    {
        if (waves == null)
        {
            return;
        }

        // 自由区的四个角。位置和朝向都跟着海洋走，缩放 Diorama 时它自动变大变小。
        Transform t = waves.transform;
        Vector2 half = waves.HalfExtentLocal;
        Vector3 c = t.position;
        Vector3 ax = t.rotation * Vector3.right * (half.x * freeZone * t.lossyScale.x);
        Vector3 az = t.rotation * Vector3.forward * (half.y * freeZone * t.lossyScale.z);

        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
        Gizmos.DrawLine(c - ax - az, c + ax - az);
        Gizmos.DrawLine(c + ax - az, c + ax + az);
        Gizmos.DrawLine(c + ax + az, c - ax + az);
        Gizmos.DrawLine(c - ax + az, c - ax - az);
    }
}
