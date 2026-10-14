using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 船的前进与转向。只做两件事：让船沿着船头方向往前走，以及让 Z / X 把船头扳左扳右。
///
/// 三条必须守住的规矩，都是踩过坑换来的：
///
/// 1. 转向只做「加法」。这里每帧只是在船**现有的**朝向上，绕世界竖轴再转一点点，
///    绝不写「船的朝向是某某」。海浪每帧也会按浪面法线掰一下船的俯仰/横滚，
///    那个是叠加式的；只要这边也不做替换，两者就能共存。一旦有一边改成直接设定朝向，
///    另一边掰出来的倾斜就会被整份清零 —— 表现就是「一转起来船就变平了，转完又恢复」。
///
/// 2. 高度不归它管。船浮多高、怎么随浪起伏，是 Floater 的事，这里只算水平位移。
///
/// 3. 位置只能写一次。这里算出水平位移后放在 LastStep 里，由 Floater 取走，
///    和它算的高度合成同一次 MovePosition。原因见 Floater.FixedUpdate 里的注释。
///
/// 输入直接读设备，和 FirstPersonController 一样，不走 Input Action 资源。
/// Z / X 要不要生效由 ControlEnabled 说了算：单独挂一个船时默认就是开着的，
/// 场景里放了舵位之后交给 HelmStation 管 —— 玩家站到舵跟前才接管，走开就撒手，
/// 但已经打下的舵角保持不动，船照旧转。
/// </summary>
[DefaultExecutionOrder(-50)] // 保证在 Floater 之前跑，步进和转向都是当帧最新的
[RequireComponent(typeof(Rigidbody))]
public class ShipMovement : MonoBehaviour, IHorizontalDrive
{
    [Header("航速")]
    [Tooltip("满帆时的巡航速度（米/秒）")]
    [SerializeField] private float cruiseSpeed = 0.3f;

    [Tooltip("帆的开度 0~1。现在固定 1；以后做控帆时改这个值就行")]
    [SerializeField, Range(0f, 1f)] private float throttle = 1f;

    [Tooltip("加速度（米/秒²），当前速度低于目标时用")]
    [SerializeField] private float accel = 0.6f;

    [Tooltip("减速度（米/秒²），当前速度高于目标时用")]
    [SerializeField] private float decel = 0.6f;

    [Header("转向")]
    [Tooltip("船体特征长度（米）。留 0 会自动从 BoxCollider 取。回转直径和转向快慢都按它算")]
    [SerializeField] private float hullLength = 0f;

    [Tooltip("舵角上限（度）")]
    [SerializeField] private float maxRudder = 35f;

    // 「舵从正中间打满到一边要花几秒」不存在这里。它是**舵位**的属性 ——
    // 舵角扫得多快，取决于舵手摇轮子的速度，那是舵位上的人和机械的事，
    // 不是船本身固有的东西（船的固有量是下面那个舵角上限）。
    // 舵位开局时会把它的值推过来（见下面的 RudderFullTravelTime）；没舵位时
    // 用这个默认值，好让一艘光秃秃的船也能自己动起来。
    private const float DefaultRudderFullTravelTime = 4f;

    private float rudderFullTravelTime = DefaultRudderFullTravelTime;

    [Tooltip("回转系数。真实帆船的回转直径是船长的 3~5 倍，这个值决定船转得灵不灵")]
    [SerializeField] private float turnCoefficient = 0.55f;

    [Tooltip("转向建立起来的快慢（秒）。越大越笨重，「压舵」的感觉越明显")]
    [SerializeField] private float turnResponse = 1.5f;

    private Rigidbody rb;
    private float speed;
    private float rudder;
    private float yawRate;

    /// <summary>这一物理帧的水平位移（世界空间）。Floater 取走它，和高度合成一次写入。</summary>
    public Vector3 LastStep { get; private set; }

    /// <summary>当前舵角（度）。正 = 右，负 = 左。以后舵轮的视觉照这个转。</summary>
    public float RudderAngle => rudder;

    /// <summary>当前航速（米/秒）。</summary>
    public float CurrentSpeed => speed;

    /// <summary>帆的开度 0~1。以后控帆的逻辑调这个。</summary>
    public float Throttle
    {
        get => throttle;
        set => throttle = Mathf.Clamp01(value);
    }

    /// <summary>
    /// 是否接受 Z / X 的输入。默认开着，单独挂一个船就能用。
    /// 场景里放了舵位之后，由舵位在玩家走进走出时开关它 —— 见 HelmStation。
    /// 关掉只是「不再接收新输入」，已经打下的舵角会原地保持，船照着继续转。
    /// </summary>
    public bool ControlEnabled { get; set; } = true;

    /// <summary>
    /// 舵从正中间打满到一边要花几秒。**这个值住在舵位上**，由舵位推过来（见 HelmStation）——
    /// 因为舵角扫得多快，是舵手摇轮子的速度和机械的事，不是船本身固有的。
    /// 舵轮转多快完全由它决定（轮子和舵齿轮咬死），所以这里不再暴露第二个转速旋钮，
    /// 两个旋钮各调各的只会互相打架。
    /// </summary>
    public float RudderFullTravelTime
    {
        get => rudderFullTravelTime;
        set => rudderFullTravelTime = Mathf.Max(0.05f, value);
    }

    /// <summary>舵角占满舵的比例，-1 ~ 1。正 = 右。舵轮的视觉照这个转。</summary>
    public float RudderRatio => maxRudder > 1e-4f ? rudder / maxRudder : 0f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (hullLength <= 0f)
        {
            // 船长取碰撞盒在船头方向上的长度。用 localSize * lossyScale，不用 bounds：
            // bounds 是世界空间的轴对齐盒子，船一转向它就跟着变，量出来的不是船长。
            BoxCollider box = GetComponent<BoxCollider>();
            hullLength = box != null ? box.size.z * Mathf.Abs(transform.lossyScale.z) : 1f;
        }

        speed = 0f;
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        StepSpeed(dt);
        StepRudder(dt);
        StepYaw(dt);

        // 沿船头方向前进。用的是**水平**的船头，不能用 transform.forward：
        // 船被浪推得低头/侧倾时 transform.forward 是斜的，照它走船会往水里钻。
        Vector3 heading = transform.forward;
        heading.y = 0f;
        if (heading.sqrMagnitude < 1e-8f)
        {
            heading = Vector3.forward;
        }
        heading.Normalize();

        LastStep = heading * (speed * dt);
    }

    private void StepSpeed(float dt)
    {
        float target = cruiseSpeed * throttle;
        float rate = target > speed ? accel : decel;
        speed = Mathf.MoveTowards(speed, target, rate * dt);
    }

    private void StepRudder(float dt)
    {
        float want = 0f;
        Keyboard keyboard = Keyboard.current;
        if (ControlEnabled && keyboard != null)
        {
            if (keyboard.zKey.isPressed) want -= 1f;
            if (keyboard.xKey.isPressed) want += 1f;
        }

        // 松手就停在原地，不回中 —— 真实的舵就是这样：你会一直转下去，
        // 想停得反着打舵，把转动刹住。这是「重」的手感来源，别顺手改成自动回中。
        if (want != 0f)
        {
            // 舵角扫过去的速度 = 满舵角度 ÷ 打满要用的时间。
            // 别再单独开一个「每秒多少度」的旋钮：舵轮视觉上转多快，完全由这个时间决定
            // （轮子和舵是齿轮咬死的，见 HelmStation）。两个旋钮各调各的只会互相打架。
            // 这个值由舵位推过来（见 RudderFullTravelTime），下限在它的 setter 里保证。
            float rate = maxRudder / rudderFullTravelTime;
            rudder = Mathf.MoveTowards(rudder, want * maxRudder, rate * dt);
        }
    }

    private void StepYaw(float dt)
    {
        // 目标转向率正比于航速。船不走，舵就是一块挂在船尾的木头，扳不动。
        // 这不是手感调节，是帆船的定义性行为 —— 也是为什么必须先把「往前走」做出来。
        float targetYawRate = speed / Mathf.Max(1e-4f, hullLength)
                            * turnCoefficient
                            * Mathf.Sin(rudder * Mathf.Deg2Rad)
                            * Mathf.Rad2Deg;

        // 慢慢逼近目标值，而不是直接取等 —— 这就是转动惯量。
        // 舵角停住之后转向率会保持；把舵打回中间，它慢慢衰减，船还在继续转，
        // 得反打舵才能刹住。这就是「压舵」。
        yawRate = Mathf.Lerp(yawRate, targetYawRate, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, turnResponse)));

        // 只绕世界竖轴加一点点，绝不重设姿态。见文件头第 1 条。
        rb.rotation = Quaternion.AngleAxis(yawRate * dt, Vector3.up) * rb.rotation;
    }
}
