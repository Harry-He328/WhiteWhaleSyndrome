using UnityEngine;

/// <summary>
/// 船尾的泡沫尾迹。
///
/// 这个效果和普通尾迹最大的不同：**船在世界里是不动的，动的是水**。
/// 船往前跑的位移被 OceanScroll 扣掉、原样挪给了浪（见 OceanScroll 文件头），
/// 所以船的世界位置基本钉在原地，海面从它底下往后流。照搬「在船尾发射粒子、
/// 粒子留在原地」的做法，结果是泡沫堆在船尾不动，看不出船在走。
///
/// 所以这里的粒子被当成**浮在水上的泡沫**：每帧把水的世界速度直接写进粒子的速度，
/// 泡沫就会随着水往后漂，在船尾拖出一条尾迹。
///
/// 水的速度不用另外算 —— OceanScroll.LastRecentre 就是这一物理帧水在世界里挪动的
/// 位移，除以帧长就是速度。船在自由区里的时候它是 0（那时船是真的在世界里移动、
/// 水不动），这种情况泡沫也会跟着停住，不用分情况处理。
///
/// 注意泡沫跟的是**水团的平移**，不是波峰。真实的泡沫也是这样：波峰是从泡沫上面
/// 碾过去的，水质点自己在原地打转、平均不前进。所以这里不碰浪的相位速度。
///
/// 粒子系统本身放在场景根目录，不挂在船底下 —— 船那个 0.0232 的缩放会被粒子
/// 继承，尺寸和速度跟着缩掉四十多倍。这个脚本每帧把发射器摆到船尾的水线上。
/// </summary>
public class ShipWake : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("场景里的泡沫粒子系统。放在场景根目录，不要挂在船下面")]
    [SerializeField] private ParticleSystem foam;

    [Tooltip("海面。用来取波面高度")]
    [SerializeField] private Waves waves;

    [Tooltip("海面滚动。水的世界速度从这里取")]
    [SerializeField] private OceanScroll oceanScroll;

    [Header("发射点")]
    [Tooltip("船尾在船体局部坐标里的 Z。船头是 +Z，模型尾端大约在 -5.96")]
    [SerializeField] private float sternLocalZ = -5.6f;

    [Tooltip("泡沫浮在水面之上多少米。太小会和海面打架闪烁")]
    [SerializeField] private float surfaceOffset = 0.004f;

    [Header("发射率")]
    [Tooltip("到这个航速就按满发射率冒泡")]
    [SerializeField] private float fullSpeed = 0.2f;

    [Tooltip("低于这个航速不冒泡")]
    [SerializeField] private float minSpeed = 0.02f;

    [Tooltip("满发射率（个/秒）。寿命 5 秒，所以同时存在的大约是它的 5 倍")]
    [SerializeField] private float maxRate = 22f;

    [Header("漂移")]
    [Tooltip("粒子速度收敛到水速的快慢（每秒）。太小刚冒出来时的扩散会拖很久")]
    [SerializeField] private float settleRate = 6f;

    [Tooltip("泡沫漂到离水面边缘这么近就消失（米）。" +
             "水面的网格比托盘内沿略大一圈，留点余量，免得泡沫钻到边框底下甚至飘到桌面上")]
    [SerializeField] private float edgeInset = 0.03f;

    /// <summary>
    /// 航速从哪来。玩家的船是 ShipMovement，敌人的船是 EnemyShip，两种都实现 IHorizontalDrive，
    /// 所以这个脚本挂到敌船上一样能用。
    /// </summary>
    private IHorizontalDrive drive;

    /// <summary>
    /// GetParticles 要用的缓冲，缓存下来避免每帧分配。
    /// </summary>
    private ParticleSystem.Particle[] buffer;

    private void Awake()
    {
        drive = GetComponent<IHorizontalDrive>();

        if (foam == null || waves == null || oceanScroll == null)
        {
            Debug.LogWarning($"[ShipWake] {name} 的引用没配齐（foam / waves / oceanScroll），尾迹不会出现", this);
            enabled = false;
            return;
        }

        buffer = new ParticleSystem.Particle[Mathf.Max(64, foam.main.maxParticles)];
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
        {
            return;
        }

        // 1. 这一帧水在世界里往哪走、走多快。
        //    LastRecentre 是「拉回船的位移」，水拿到的正好是同一份（只差一个符号，
        //    见 OceanScroll 文件头第 2 条），所以水的速度就是它除以帧长。
        Vector3 waterVelocity = Vector3.zero;
        if (Time.fixedDeltaTime > 0f)
        {
            waterVelocity = oceanScroll.LastRecentre / Time.fixedDeltaTime;
        }

        // 2. 发射器摆到船尾。水平位置跟船走，高度取波面 ——
        //    船自己在浪里上下起伏、还会倾斜，用它的 y 会让发射点跟着飘出水面。
        Vector3 stern = transform.TransformPoint(new Vector3(0f, 0f, sternLocalZ));
        Vector3 emitterPos = new Vector3(stern.x, waves.GetHeight(stern), stern.z);

        // 让发射器自己的 +Z 指向船尾方向（形状模块是沿本地 +Z 抛粒子的），
        // 这样刚冒出来的泡沫带一点向后、向外散开的初速。
        // 只取水平朝向：船的俯仰横摇不该把泡沫往天上或者水底下抛。
        Vector3 back = Vector3.ProjectOnPlane(-transform.forward, Vector3.up);
        if (back.sqrMagnitude < 1e-8f)
        {
            back = Vector3.back;
        }
        foam.transform.SetPositionAndRotation(emitterPos, Quaternion.LookRotation(back.normalized, Vector3.up));

        // 3. 按航速调发射率。船停下来就不冒泡。
        var emission = foam.emission;
        emission.rateOverTime = Mathf.InverseLerp(minSpeed, fullSpeed, SpeedThroughWater()) * maxRate;

        // 4. 每个活着的粒子：速度收进水速，高度贴回波面。
        //    速度用「往水速插值」而不是直接赋值，是为了让刚冒出来那一瞬间的散开保留零点几秒，
        //    不然尾迹会是一条没有宽度的线。
        int count = foam.particleCount;
        if (count <= 0)
        {
            return;
        }

        if (buffer.Length < count)
        {
            buffer = new ParticleSystem.Particle[count];
        }

        count = foam.GetParticles(buffer);
        float settle = Mathf.Clamp01(dt * settleRate);

        Vector2 half = waves.HalfExtentLocal;
        float limitX = Mathf.Max(0f, half.x - edgeInset);
        float limitZ = Mathf.Max(0f, half.y - edgeInset);

        for (int i = 0; i < count; i++)
        {
            Vector3 position = buffer[i].position;

            // 漂出水面就让它消失。尾迹有一米长，船又会在托盘里晃，
            // 不拦的话泡沫会从水边一直飘到桌面上。
            Vector3 local = waves.transform.InverseTransformPoint(position);
            if (Mathf.Abs(local.x) > limitX || Mathf.Abs(local.z) > limitZ)
            {
                buffer[i].remainingLifetime = 0f;
                continue;
            }

            buffer[i].velocity = Vector3.Lerp(buffer[i].velocity, waterVelocity, settle);

            // 水面自己也在起伏（两个波分量加起来振幅大约 3.7 厘米），不跟着修高度的话
            // 泡沫会浮在水面之上或者沉到下面，最明显能差到船长的一成多。
            position.y = waves.GetHeight(position) + surfaceOffset;
            buffer[i].position = position;
        }

        foam.SetParticles(buffer, count);
    }

    /// <summary>
    /// 船相对水的航速。用推进脚本交出来的那一份位移反推，
    /// 不去问具体是哪个脚本，玩家的船和敌人的船都能用。
    /// </summary>
    private float SpeedThroughWater()
    {
        if (drive == null || Time.fixedDeltaTime <= 0f)
        {
            return 0f;
        }

        return drive.LastStep.magnitude / Time.fixedDeltaTime;
    }
}
