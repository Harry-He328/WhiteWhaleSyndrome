using UnityEngine;

/// <summary>
/// 敌人的船。只干三件事：朝玩家开过去、进范围之后绕着转、舷侧对上玩家就开炮。
///
/// 转向**不跟航速绑**。玩家的船是绑的（船不走，舵就扳不动），那是为了让玩家有开船的重量感。
/// 但敌人绕的圈半径只有一米上下，用那套「速度越快转得越快」根本绕不过来——会冲出去、
/// 掉头、再冲出去。敌人单独给一个每秒多少度的转向速度。
///
/// 环绕参数（半径、往哪边绕）只在**进入环绕的那一刻摇一次**，之后一直用。每帧重摇的话
/// 船会原地抖、左右横跳，看着像坏了。
///
/// 位置和姿态的写法跟玩家的船一样：这一帧要挪多少记在 LastStep 里，交给浮力脚本合成
/// 一次写入；转向是往当前姿态上叠加，绝不直接设定姿态（浪每帧也在掰船的俯仰和横滚，
/// 直接设定会把浪掰出来的倾斜清掉）。
/// </summary>
[DefaultExecutionOrder(-50)]   // 和 ShipMovement 同序，都要在浮力脚本之前算好这一帧的位移
[RequireComponent(typeof(Rigidbody))]
public class EnemyShip : MonoBehaviour, IHorizontalDrive
{
    [Header("目标")]
    [Tooltip("要追的船。留空则开局自动找玩家的船")]
    [SerializeField] private Transform target;

    [Header("航行")]
    [Tooltip("航速（米/秒）")]
    [SerializeField] private float speed = 0.25f;

    [Tooltip("转向速度（度/秒）。跟航速无关——见文件头")]
    [SerializeField] private float turnRate = 60f;

    [Header("接敌 / 环绕")]
    [Tooltip("进到这个距离就不再直冲，改成绕圈（米）")]
    [SerializeField] private float engageRange = 1.3f;

    [Tooltip("环绕半径占 engageRange 的比例，在这个区间里随机取一个")]
    [SerializeField] private Vector2 orbitFactor = new Vector2(0.55f, 0.8f);

    [Header("开火")]
    [SerializeField] private Broadside port;
    [SerializeField] private Broadside starboard;

    [Tooltip("目标落在舷侧正负这个角度内才开火（度）")]
    [SerializeField] private float fireSector = 45f;

    private Rigidbody body;
    private ShipHealth health;
    private ShipSink sink;

    private float orbitRadius;
    private float orbitSign = 1f;   // +1 / −1：往哪边绕。进环绕时摇一次
    private bool orbiting;

    public Vector3 LastStep { get; private set; }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        health = GetComponent<ShipHealth>();
        sink = GetComponent<ShipSink>();

        if (health != null)
        {
            health.Died += OnDied;
        }

        // 登记放在 Awake，不放 Start。Awake 在 Instantiate 的当帧就跑完，
        // 刷新点同一帧读存活数就能看到新刷出来的这一条，不会因为差一帧而多刷几个。
        EnemyRegistry.Register(this);
    }

    private void Start()
    {
        if (target == null)
        {
            PlayerCannons player = FindFirstObjectByType<PlayerCannons>();
            if (player != null)
            {
                target = player.transform;
            }
        }

        // 开局先把船头对着目标。不然会从侧面横着冲过去，看着很蠢。
        FaceTarget();
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.Died -= OnDied;
        }

        // 没被打死就被删掉的情况（换场景、手动删）也要退登记，不然存活数会一直虚高。
        EnemyRegistry.Unregister(this);
    }

    private void FixedUpdate()
    {
        LastStep = Vector3.zero;

        if (target == null || health == null || health.IsDead)
        {
            return;
        }

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        if (!orbiting && distance <= engageRange)
        {
            EnterOrbit();
        }

        Vector3 desired = orbiting ? OrbitDirection(toTarget, distance) : toTarget;

        if (desired.sqrMagnitude > 1e-8f)
        {
            Steer(desired.normalized, Time.fixedDeltaTime);
        }

        LastStep = HorizontalForward() * (speed * Time.fixedDeltaTime);

        TryFire(toTarget);
    }

    private void EnterOrbit()
    {
        orbiting = true;

        // 只摇这一次。之后每帧重摇会让船原地抖。
        float low = Mathf.Min(orbitFactor.x, orbitFactor.y);
        float high = Mathf.Max(orbitFactor.x, orbitFactor.y);
        orbitRadius = Mathf.Max(0.05f, engageRange * Random.Range(low, high));
        orbitSign = Random.value < 0.5f ? -1f : 1f;
    }

    /// <summary>
    /// 环绕时想走的方向：沿切线绕圈，加一点把半径拉回理想值的修正。
    /// 修正按「离理想半径差多远」给，不是按速度给——半径对了修正就是零，
    /// 进出半径的时候不会顿一下。
    /// </summary>
    private Vector3 OrbitDirection(Vector3 toTarget, float distance)
    {
        Vector3 radialOut = -toTarget.normalized;                        // 从目标指向自己
        Vector3 tangent = Vector3.Cross(Vector3.up, radialOut) * orbitSign;

        float error = distance - orbitRadius;
        return tangent + radialOut * Mathf.Clamp(error * 1.5f, -1f, 1f);
    }

    /// <summary>把船头朝 desired 转，这一帧最多转 turnRate * dt 度。</summary>
    private void Steer(Vector3 desired, float dt)
    {
        Vector3 forward = HorizontalForward();
        Vector3 next = Vector3.RotateTowards(forward, desired, turnRate * dt * Mathf.Deg2Rad, 0f);
        next.y = 0f;

        if (next.sqrMagnitude < 1e-8f)
        {
            return;
        }

        // 叠加，不是设定。见文件头。
        Quaternion delta = Quaternion.FromToRotation(forward, next.normalized);
        body.rotation = delta * body.rotation;
    }

    private void TryFire(Vector3 toTarget)
    {
        if (toTarget.sqrMagnitude < 1e-8f)
        {
            return;
        }

        Vector3 direction = toTarget.normalized;

        // 一次只开一侧。两边同时响没有意义，而且一次齐射的伤害翻倍，
        // 数值就不是设计里的那个了。
        if (port != null && port.IsReady && Vector3.Angle(port.BeamDirection, direction) <= fireSector)
        {
            port.TryFire();
            return;
        }

        if (starboard != null && starboard.IsReady
            && Vector3.Angle(starboard.BeamDirection, direction) <= fireSector)
        {
            starboard.TryFire();
        }
    }

    /// <summary>被清场要求直接沉掉。血量扣到超过上限即可，伤害数值走的是同一条路。</summary>
    public void Kill()
    {
        if (health != null)
        {
            health.TakeDamage(health.MaxHealth + 1f);
        }
    }

    private void OnDied(ShipHealth _)
    {
        // 先报击杀再沉。报的时候登记处会把它从存活列表里摘掉，
        // 刷新点立刻就能补新的，不用等沉船动画放完。
        EnemyRegistry.ReportKill(this);

        // 沉船由 ShipSink 自己听 Died 去做。这里只在没挂那个组件的时候兜个底，
        // 免得血扣光了船还杵在原地不动 —— 刷新点会以为它还活着。
        if (sink == null)
        {
            Destroy(gameObject);
        }
    }

    private void FaceTarget()
    {
        if (target == null)
        {
            return;
        }

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude < 1e-8f)
        {
            return;
        }

        body.rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
    }

    private Vector3 HorizontalForward()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 1e-8f)
        {
            forward = Vector3.forward;
        }

        return forward.normalized;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 1f);
        Gizmos.DrawWireSphere(transform.position, engageRange);
    }
}
