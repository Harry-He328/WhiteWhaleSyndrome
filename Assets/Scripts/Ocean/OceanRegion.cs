using UnityEngine;

/// <summary>
/// 海洋在桌子上的显示范围。平面上一块水平矩形，矩形之外的内容被竖直"切"掉
/// （见 Assets/Shaders/Ocean/RegionClip.hlsl）。
///
/// 这里是这个区域的唯一真值来源：中心 / 朝向 / 尺寸全部从本物体的 Transform 取，
/// 每帧推给着色器全局变量，所以任何用 OceanLit 的材质都会自动跟随，
/// 不需要逐个材质去同步数值。
///
/// [ExecuteAlways]：编辑状态下也要生效，否则在 Scene 视图里摆地图时看不到真实的裁剪结果。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class OceanRegion : MonoBehaviour
{
    [Tooltip("区域宽度（本地单位，沿本地 X 轴）。会乘上 lossyScale，换算方式和海洋网格一致")]
    [SerializeField] private float width = 10f;

    [Tooltip("区域长度（本地单位，沿本地 Z 轴）。会乘上 lossyScale")]
    [SerializeField] private float length = 10f;

    [Tooltip("关掉就不裁剪，可以在 Scene 视图里看整张地图。正式运行时保持开启")]
    [SerializeField] private bool clipEnabled = true;

    private static readonly int CenterId = Shader.PropertyToID("_OceanRegionCenter");
    private static readonly int AxisXId = Shader.PropertyToID("_OceanRegionAxisX");
    private static readonly int AxisZId = Shader.PropertyToID("_OceanRegionAxisZ");
    private static readonly int ClipId = Shader.PropertyToID("_OceanRegionClip");

    public float Width => width;
    public float Length => length;
    public bool ClipEnabled => clipEnabled;

    public Vector3 Center => transform.position;

    /// <summary>从中心指向区域右边缘的向量：方向是右方，长度是半宽。</summary>
    public Vector3 HalfAxisX => transform.right * (width * 0.5f * transform.lossyScale.x);

    /// <summary>从中心指向区域前边缘的向量：方向是前方，长度是半长。</summary>
    public Vector3 HalfAxisZ => transform.forward * (length * 0.5f * transform.lossyScale.z);

    /// <summary>
    /// 区域在世界空间里的水平包围盒。高度是 0——它描述的是"海面的范围"，
    /// 而刀是无限高的，竖直方向没有边。想知道某个东西在不在显示范围内，
    /// 用 Contains 或者只看 xz。
    /// </summary>
    public Bounds WorldBounds
    {
        get
        {
            Vector3 ax = HalfAxisX;
            Vector3 az = HalfAxisZ;
            Vector3 extent = new Vector3(Mathf.Abs(ax.x) + Mathf.Abs(az.x),
                                         Mathf.Abs(ax.y) + Mathf.Abs(az.y),
                                         Mathf.Abs(ax.z) + Mathf.Abs(az.z));
            return new Bounds(Center, extent * 2f);
        }
    }

    /// <summary>水平方向上的内外判定，和着色器里用的是同一套算法。</summary>
    public bool Contains(Vector3 worldPos)
    {
        Vector3 ax = HalfAxisX;
        Vector3 az = HalfAxisZ;
        float axisSqX = ax.sqrMagnitude;
        float axisSqZ = az.sqrMagnitude;

        if (axisSqX < 1e-8f || axisSqZ < 1e-8f)
            return true;

        Vector3 d = worldPos - Center;
        float tX = Vector3.Dot(d, ax) / axisSqX;
        float tZ = Vector3.Dot(d, az) / axisSqZ;

        return Mathf.Abs(tX) <= 1f && Mathf.Abs(tZ) <= 1f;
    }

    private void PushToShaders()
    {
        Shader.SetGlobalVector(CenterId, Center);
        Shader.SetGlobalVector(AxisXId, HalfAxisX);
        Shader.SetGlobalVector(AxisZId, HalfAxisZ);
        Shader.SetGlobalFloat(ClipId, clipEnabled ? 1f : 0f);
    }

    private void OnEnable()
    {
        PushToShaders();
    }

    private void OnValidate()
    {
        PushToShaders();
    }

    private void Update()
    {
        // 区域本身不动，但它挂在场景里，可能在运行时被挪走或改了尺寸；
        // 每帧推一次比到处埋"改了记得通知"要省心，开销可以忽略。
        PushToShaders();
    }

    private void OnDisable()
    {
        // 区域被关掉时宁可"不裁"，也不要留下一个让整张地图都消失的状态
        Shader.SetGlobalFloat(ClipId, 0f);
    }

    private void OnDrawGizmos()
    {
        Vector3 c = Center;
        Vector3 ax = HalfAxisX;
        Vector3 az = HalfAxisZ;

        Gizmos.color = clipEnabled
            ? new Color(0.25f, 0.75f, 1f, 0.9f)
            : new Color(0.55f, 0.55f, 0.55f, 0.9f);

        Gizmos.DrawLine(c - ax - az, c + ax - az);
        Gizmos.DrawLine(c + ax - az, c + ax + az);
        Gizmos.DrawLine(c + ax + az, c - ax + az);
        Gizmos.DrawLine(c - ax + az, c - ax - az);

        // 四角的竖线，示意"刀是无限高的"（这里只画一小段当提示）。
        // 长度要从 ax/az 里取——它们已经是世界向量（含 lossyScale），
        // 而 width/length 是局部量，直接拿来定世界长度会在缩放后画错。
        float pole = Mathf.Max(1e-3f, Mathf.Min(ax.magnitude, az.magnitude) * 0.5f);
        Vector3 up = Vector3.up * pole;
        Gizmos.DrawLine(c - ax - az, c - ax - az + up);
        Gizmos.DrawLine(c + ax - az, c + ax - az + up);
        Gizmos.DrawLine(c + ax + az, c + ax + az + up);
        Gizmos.DrawLine(c - ax + az, c - ax + az + up);
    }
}
