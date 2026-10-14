using Ditzelgames;
using UnityEngine;

public class Floater : MonoBehaviour
{
    [Header("阻力设置")]
    [SerializeField] private float airDrag = 1f;
    [SerializeField] private float waterDrag = 10f;

    [Header("浮力行为")]
    [SerializeField] private bool affectDirection = true;
    [SerializeField] private bool attachToSurface = false;
    [SerializeField, Range(0.5f, 3f)]
    [Tooltip("等效重力系数：>1 下落更快、更跟浪，船更有重量感")]
    private float gravityScale = 1f;
    [SerializeField, Range(0f, 2f)]
    [Tooltip("船体跟随水面高度变化的强度：1=完全跟随，<1 更钝重，>1 会略微放大起伏")]
    private float followWaveStrength = 0.9f;

    [Header("浮点采样点")]
    public Transform[] floatPoints;

    [Header("旋转平滑参数（运行时状态）")]
    [SerializeField] private Vector3 smoothVectorRotation;

    protected Rigidbody rb;
    protected Waves waves;

    // 水平位移的来源。玩家的船是 ShipMovement，敌人的船是 EnemyShip，两种都实现 IHorizontalDrive。
    // 原来这里写死的是 ShipMovement，敌人挂上浮力就只能干看着不动。
    protected IHorizontalDrive drive;

    protected OceanScroll oceanScroll;

    protected float waterLines;
    protected Vector3[] waterLinePoints;

    // 浮点构成的船体中心，存的是「本地」偏移量，不是世界向量。
    // 存世界向量有两个毛病：船一旦被缩放，偏移量就不对了；而且它不跟着旋转，
    // 船一倾斜 Center 就已经偏了（这是原来就有的 bug）。存本地的就两个都解决。
    protected Vector3 centerOffsetLocal;

    // 船体特征长度（本地）。用来给浮力的钳位当饱和点，见 FixedUpdate。
    protected float hullLengthLocal = 1f;

    protected Vector3 targetUp;

    /// <summary>船体中心的世界坐标：本地偏移沿当前 transform 实时映射。</summary>
    public Vector3 Center
    {
        get { return transform.TransformPoint(centerOffsetLocal); }
    }

    /// <summary>船体特征长度的世界尺寸。</summary>
    protected float HullLengthWorld
    {
        get
        {
            Vector3 s = transform.lossyScale;
            // 均匀缩放下三个分量相等，取最大只是为了非等比时也有个确定的答案
            return hullLengthLocal * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        }
    }

    // Start is called before the first frame update
    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        waves = FindFirstObjectByType<Waves>();
        drive = GetComponent<IHorizontalDrive>();
        oceanScroll = FindFirstObjectByType<OceanScroll>();
        if (rb != null)
        {
            rb.useGravity = false;
        }

        if (floatPoints == null || floatPoints.Length == 0)
        {
            waterLinePoints = new Vector3[0];
            return;
        }

        waterLinePoints = new Vector3[floatPoints.Length];
        for (int i = 0; i < floatPoints.Length; i++)
        {
            if (floatPoints[i] != null)
            {
                waterLinePoints[i] = floatPoints[i].position;
            }
        }

        // 换算到本物体自己的空间再记下来，之后无论船怎么缩放、怎么倾斜都还成立
        Vector3[] localPoints = new Vector3[floatPoints.Length];
        for (int i = 0; i < floatPoints.Length; i++)
        {
            localPoints[i] = floatPoints[i] != null
                ? transform.InverseTransformPoint(floatPoints[i].position)
                : Vector3.zero;
        }

        centerOffsetLocal = transform.InverseTransformPoint(PhysicsHelper.GetCenter(waterLinePoints));

        // 特征长度 = 浮点两两之间的最大距离。退化成单点或零尺寸时兜一个 1，
        // 免得下面的钳位饱和点变成 0、浮力直接被削平。
        hullLengthLocal = 0f;
        for (int i = 0; i < localPoints.Length; i++)
        {
            for (int j = i + 1; j < localPoints.Length; j++)
            {
                hullLengthLocal = Mathf.Max(hullLengthLocal, Vector3.Distance(localPoints[i], localPoints[j]));
            }
        }
        if (hullLengthLocal <= 1e-4f)
        {
            hullLengthLocal = 1f;
        }
    }

    void FixedUpdate()
    {
        if (rb == null || waves == null || floatPoints == null || floatPoints.Length == 0)
        {
            return;
        }

        // 空引用要计入分母的话水线会被拉向 0，所以先数一遍有效浮点
        // 注意：ShipBase 沉船时只是 SetActive(false)，Transform 引用依然有效，仍按原点位参与计算
        int validPointCount = 0;
        for (int i = 0; i < floatPoints.Length; i++)
        {
            if (floatPoints[i] != null)
            {
                validPointCount++;
            }
        }

        if (validPointCount == 0)
        {
            return;
        }

        //default water surface
        var newWaterLine = 0f;
        var pointUnderWater = false;

        //set WaterLinePoints and WaterLine
        for (int i = 0; i < floatPoints.Length; i++)
        {
            //height
            if (floatPoints[i] == null)
            {
                continue;
            }

            waterLinePoints[i] = floatPoints[i].position;
            waterLinePoints[i].y = waves.GetHeight(floatPoints[i].position);
            newWaterLine += waterLinePoints[i].y / validPointCount;
            if (waterLinePoints[i].y > floatPoints[i].position.y)
                pointUnderWater = true;
        }

        var waterLineDelta = newWaterLine - waterLines;
        waterLines = newWaterLine;

        //compute up vector
        targetUp = PhysicsHelper.GetNormal(waterLinePoints);

        //gravity
        var gravity = Physics.gravity;
        rb.linearDamping = airDrag;

        // 水平推进由 ShipMovement / EnemyShip 算好放在 LastStep 里，这里只把它和高度合成同一次写入。
        Vector3 step = drive != null ? drive.LastStep : Vector3.zero;

        // 玩家那条船跑出托盘中心的自由区之后，超出的那部分位移不让它继续往外走，
        // 而是由 OceanScroll 转给海面（海往后退、船留在原地一带）。
        // 它和推进位移必须是同一次写入 —— 见下面 MovePosition 的注释。
        //
        // 这份位移是世界空间里「水在往后走」的那一段，所以水里所有东西都要跟着走，
        // 不分是不是玩家的船 —— 每条挂浮力的船都加上它。
        //
        // 加上的结果：船相对水面的速度 = 它自己填的航速，和玩家那条船同一把尺子。
        // 不加的结果：船相对水面的速度 = 它自己的航速 + 玩家巡航速度，两个数字没法比。
        // 代价：船的航速必须大于水流速度（玩家的巡航速度），否则会被水流带着倒退。
        if (oceanScroll != null)
        {
            step += oceanScroll.LastRecentre;
        }

        Vector3 currentPos = rb.position;

        // 出水时高度仍旧交给浮力那套力学去推。但 MovePosition 会覆盖刚体的速度，
        // 所以把刚体自己的竖直速度按 dt 带过去 —— 等效于「这一帧不调 MovePosition」，
        // 浮力推出来的运动不会被吃掉。
        float targetY = currentPos.y + rb.linearVelocity.y * Time.fixedDeltaTime;

        if (waterLines > Center.y)
        {
            rb.linearDamping = waterDrag;
            //under water
            if (attachToSurface)
            {
                //attach to water surface：把船体中心摆到水线上
                // （原来写的是 waterLines - centerOffset.y，那是「在 y=0 平面上算偏移量」的等价写法；
                //   现在偏移量是本地量，直接按「中心离现在的位置还有多高」来挪，两种缩放都成立）
                targetY = currentPos.y + (waterLines - Center.y);
            }
            else
            {
                //go up
                gravity = affectDirection ? targetUp * -Physics.gravity.y : -Physics.gravity;
                targetY = currentPos.y + waterLineDelta * followWaveStrength;
            }
        }

        // 位置在整个工程里只在这一个地方写。原来 MovePosition 只在「在水里」那一支里调用，
        // 出水的那几帧就没人管水平方向了；而且如果再加一个脚本自己调 MovePosition，
        // 两次调用是互相**覆盖**的（MovePosition 设的是整份目标位置，不是增量），
        // 谁赢取决于未定义的脚本执行顺序 —— 所以水平推进不能另起一次，只能并进这一句。
        rb.MovePosition(new Vector3(currentPos.x + step.x, targetY, currentPos.z + step.z));
        // 浮力：吃水越深推力越大，到一个船长的深度就饱和。
        // 原来上限写的是 1（1 世界米），那是 20 米海面的尺度；船缩到厘米级之后这个 1 永远够不着，
        // 饱和段形同虚设，船就沉不下去也顶不起来。改成「一个船长」之后，
        // 未饱和段 a ∝ 吃水（本来就随尺度走），饱和点也 ∝ k，整条响应曲线自相似。
        // 注意是「保留长度量纲、只挪饱和点」，不是除以船长——除以它会让力被钉在 0~1 的无量纲区间，
        // 那是把尺度不变性改坏，不是修好。
        float submersion = Mathf.Abs(waterLines - Center.y);
        rb.AddForce(gravity * gravityScale * Mathf.Clamp(submersion, 0f, Mathf.Max(1e-4f, HullLengthWorld)));

        // 倾斜：船中心在水面附近或任意浮点在水下时，用四个浮点处的水面高度拟合平面法线并让船跟随
        bool shouldTilt = pointUnderWater || (waterLines > Center.y);
        if (shouldTilt)
        {
            targetUp = Vector3.SmoothDamp(transform.up, targetUp, ref smoothVectorRotation, 0.2f);
            rb.rotation = Quaternion.FromToRotation(transform.up, targetUp) * rb.rotation;
        }
    }

    private void OnDrawGizmos()
    {
        if (floatPoints == null)
            return;

        // 装饰尺寸跟着船走。原来写死的 0.1 / 1 是 20 米海面的尺度，
        // 在厘米级的船上会画成一片糊住船体的绿云。
        // 这里现算一遍浮点的世界跨度，而不是用 hullLengthLocal——那个只在 Awake 里填，
        // 编辑模式下还没跑过，会退化成默认值 1。
        float spread = 0f;
        for (int i = 0; i < floatPoints.Length; i++)
        {
            if (floatPoints[i] == null) continue;
            for (int j = i + 1; j < floatPoints.Length; j++)
            {
                if (floatPoints[j] == null) continue;
                spread = Mathf.Max(spread, Vector3.Distance(floatPoints[i].position, floatPoints[j].position));
            }
        }
        float scale = spread > 1e-4f ? spread : 1f;
        float radius = Mathf.Max(1e-4f, scale * 0.05f);

        //draw sphere
        for (int i = 0; i < floatPoints.Length; i++)
        {
            if (floatPoints[i] == null)
                continue;

            Gizmos.color = Color.green;
            Gizmos.DrawSphere(floatPoints[i].position, radius);
        }

        //draw center
        if (Application.isPlaying)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(new Vector3(Center.x, waterLines, Center.z), targetUp * scale * 0.5f);
        }
    }
}
