using UnityEngine;

/// <summary>
/// 舵位。管两件事：
///
/// 1. 判断玩家是不是站在舵跟前。站在范围内，Z / X 才接管船头方向；走开了就撒手。
///    撒手不等于回中 —— 已经打下的舵角保持不动，船照旧按那个舵角继续转。
///    这是真船的行为：舵手走开，舵不会自己弹回去。
///
/// 2. 把舵轮的视觉转起来。轮子转的圈数是照真船定的：舵从最左打到最右，
///    轮子要转好几圈。所以舵角只动一点点，轮子得转很多 —— 这就是为什么
///    「舵角」和「轮子转了多少度」要分开算，不能直接拿舵角去转轮子。
///
/// 判断「在不在范围内」用的是每帧直接量一次玩家和这个框有没有重叠，
/// 而不是靠「走进来 / 走出去」那种事件。原因：角色控制器只有在自己**移动**的时候
/// 才会产生那种事件 —— 玩家站着不动、或者一开始就站在框里，事件可能压根不发，
/// 于是「明明站在舵前面却转不动」。每帧量一次就没这个空子。
///
/// 这个框本身还是个触发器（不挡路、不挤人），只是判断不靠它的事件而已。
/// </summary>
public class HelmStation : MonoBehaviour
{
    [Header("接管")]
    [Tooltip("玩家站进这个范围后，Z / X 才控制这艘船")]
    [SerializeField] private ShipMovement ship;

    [Header("舵轮")]
    [Tooltip("舵轮的旋转节点。绕它自己的 Z 轴转")]
    [SerializeField] private Transform wheel;

    [Tooltip("舵从正中间打满到一边要花几秒。舵轮转多快完全由它决定 —— 想调快慢就调这里")]
    [SerializeField] private float rudderFullTravelTime = 4f;

    /// <summary>
    /// 舵轮从**最左打到最右**要转几圈。真帆船是 3~5 圈，这里按 3 算。
    ///
    /// 注意是「最左到最右」的整个行程，不是「中间到一边」——
    /// 舵角比例是 -1 ~ 1（整个行程宽 2），换算轮子角度时要先折半，
    /// 别把这个数直接乘上去，那样会转出两倍的圈数。
    ///
    /// 这是齿轮比，是机械上定死的东西，**不是拿来做手感调节的旋钮**。
    /// 而且它也不是「轮子转多快」的原因 —— 轮子转多快 = 圈数 ÷ 打满舵用的时间，
    /// 想调快慢去调上面那个「打满舵要几秒」，别动这里。
    /// </summary>
    private const float WheelTurnsLockToLock = 3f;

    private BoxCollider zone;
    private readonly Collider[] overlap = new Collider[8];
    private bool playerPresent;

    private void Awake()
    {
        zone = GetComponent<BoxCollider>();

        // 先默认「没人站在这儿」。玩家一走进来就会在第一帧被量到，立刻打开。
        // 不管这个，玩家出生就在舵位里的话，得等他走一步才会生效。
        if (ship != null)
        {
            ship.ControlEnabled = false;

            // 「打满舵要几秒」住在舵位上，开局推给船。船自己那边没有这个旋钮 ——
            // 见 ShipMovement.RudderFullTravelTime 的注释。
            ship.RudderFullTravelTime = rudderFullTravelTime;
        }
    }

    private void Update()
    {
        UpdatePresence();
        UpdateWheel();
    }

    private void UpdatePresence()
    {
        if (ship == null || zone == null)
        {
            return;
        }

        // 用碰撞盒自己的尺寸和朝向去量，不用 bounds —— bounds 是世界空间的
        // 轴对齐盒子，这个框一旦不是正着的，量出来的范围就比实际的大。
        Vector3 center = zone.transform.TransformPoint(zone.center);
        Vector3 half = Vector3.Scale(zone.size, zone.transform.lossyScale) * 0.5f;
        half = new Vector3(Mathf.Abs(half.x), Mathf.Abs(half.y), Mathf.Abs(half.z));

        bool inside = false;
        int count = Physics.OverlapBoxNonAlloc(center, half, overlap, zone.transform.rotation, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            if (overlap[i] != null && overlap[i].GetComponentInParent<FirstPersonController>() != null)
            {
                inside = true;
                break;
            }
        }

        if (inside != playerPresent)
        {
            playerPresent = inside;
            ship.ControlEnabled = inside;
        }
    }

    private void UpdateWheel()
    {
        if (wheel == null || ship == null)
        {
            return;
        }

        // 舵角比例是 -1 ~ 1，整个行程宽 2，所以要先折半才是「最左到最右」的圈数。
        // 符号：按下 X 往右转，舵角是正的，轮子从玩家这个方向看是顺时针 —— 和方向盘一致。
        float angle = ship.RudderRatio * 0.5f * WheelTurnsLockToLock * 360f;
        wheel.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    /// <summary>玩家现在是不是站在舵位里。以后要做提示 UI 的话用它。</summary>
    public bool PlayerPresent => playerPresent;

    private void OnDrawGizmosSelected()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null)
        {
            return;
        }

        Gizmos.color = playerPresent ? Color.green : new Color(1f, 0.6f, 0f, 0.6f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(box.center, box.size);
    }
}
