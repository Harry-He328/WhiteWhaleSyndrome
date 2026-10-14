using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class Waves : MonoBehaviour
{
    [Header("网格范围（本物体的局部单位，会被 transform 的缩放等比放大）")]
    [Tooltip("网格尺寸跟着这个区域走。留空则用下面手填的宽/长")]
    [SerializeField] private OceanRegion region;
    [Tooltip("区域为空时的兜底宽度，局部单位")]
    [SerializeField] private float worldWidth = 20f;
    [Tooltip("区域为空时的兜底长度，局部单位")]
    [SerializeField] private float worldLength = 20f;

    [Tooltip("一个格子的目标边长（局部单位）。实际格子只会比它小（为了正好铺满），顶点数按这个值的平方增长。" +
             "因为它是局部量，缩放 transform 时顶点数不变，只有世界尺寸变")]
    [SerializeField] private float cellSize = 0.25f;

    [Tooltip("浪高整体缩放。1 = 波形公式算出来的原始高度")]
    [SerializeField] private float heightScale = 1f;

    [Tooltip("一张贴图铺多大（局部单位）。只影响水的细纹，和网格密度无关")]
    [SerializeField] private float uvTileWorld = 2f;

    // 旧 Perlin Octave 方案暂时停用
    // public Octave[] octaves;

    protected MeshFilter meshFilter;
    protected Mesh mesh;

    [Header("Directional Waves (Gerstner-style height, no horizontal displacement)")]
    [SerializeField] private int directionalWaveCount = 2;
    [SerializeField] private float gravity = 9.8f;
    [SerializeField] private float baseWavelength = 3f;
    [SerializeField, Range(0f, 1f)] private float baseSteepness = 0.35f;
    [SerializeField, Range(0.1f, 2f)] private float waveTimeScale = 0.25f;
    [Tooltip("采样空间的缩放：把局部坐标除以它再喂给波形公式。1 = 波长按局部单位直接读")]
    [SerializeField] private float samplingScale = 1f;
    [SerializeField, Range(0f, 1f)] private float directionSpread = 0.15f;
    [SerializeField] private float wavelengthMultiplier = 2f;
    [SerializeField, Range(0f, 1f)] private float steepnessFalloff = 0.6f;

    [Header("Performance")]
    [Tooltip("每 N 帧刷新一次网格顶点，1 = 每帧")]
    [SerializeField] private int meshUpdateInterval = 1;
    [Tooltip("是否生成 MeshCollider。Floater 走的是解析式采样，不依赖它；只有需要刚体/炮弹与水面真正发生碰撞时才需要。开启时是非凸网格，代价不小")]
    [SerializeField] private bool generateMeshCollider = false;
    [Tooltip("是否每次刷新顶点后重新烘焙 MeshCollider。开销很大，非必要不要开")]
    [SerializeField] private bool refreshMeshColliderEachFrame = false;

    private MeshCollider meshCollider;

    // ---- 网格规格：由「区域尺寸 ÷ 格子大小」推出来，不再是手填的一个 dimension ----
    private Vector2 meshSize;          // 网格覆盖的局部尺寸（x = 沿本地 X，y = 沿本地 Z）；世界尺寸 = 它 × lossyScale
    private int cellsX, cellsZ;        // 每边的格子数
    private int vertsX, vertsZ;        // 每边的顶点数 = 格子数 + 1
    private float stepX, stepZ;        // 实际格距；用 meshSize / cells 算，保证正好铺满

    // 复用的顶点列表：Mesh.vertices 的 getter 每次都会新分配数组，是这里最大的 GC 来源
    private readonly List<Vector3> vertices = new List<Vector3>();

    private int frameCounter;

    // 运行时生效的波形参数（序列化基准值 × 风力系数）
    private float effectiveWavelength;
    private float effectiveSteepness;

    // 波长的下限。相对 baseWavelength 定，不是绝对米数——见 RefreshWindScaling
    private float minWavelength = 1e-3f;

    /// <summary>网格实际覆盖的局部尺寸。给外部（比如区域可视化）核对用。</summary>
    public Vector2 MeshSize => meshSize;

    /// <summary>
    /// 网格覆盖范围的局部半尺寸（x 沿本地 X，y 沿本地 Z）。
    /// 「海面在局部空间里铺到哪儿」就是这个数。别人要按显示范围定尺寸时直接用，
    /// 不用自己再乘一遍 lossyScale —— 缩放 Diorama 时它自动跟着变。
    /// </summary>
    public Vector2 HalfExtentLocal
    {
        get
        {
            // meshSize 只在 Rebuild 之后才有值（运行时的 Start）。编辑模式下取不到，
            // 所以退回去现算一次，Gizmo 之类的地方才能画出正确的范围。
            Vector2 s = meshSize.x > 0f && meshSize.y > 0f ? meshSize : ResolveSize();
            return s * 0.5f;
        }
    }

    /// <summary>
    /// 波形在局部 XZ 上的采样平移。网格本身不动，靠平移采样点把整片浪推着走，
    /// 于是看起来是「船在原地、海从底下流过去」。见 OceanScroll。
    ///
    /// 会一直累加（船一直在走）。float 在几千米之后，格距的精度会掉到百分之一量级，
    /// 波峰会出现轻微抖动。一场游戏跑不到那个距离，先不管；
    /// 真要长时间连续航行，再按波长给它取模。
    /// </summary>
    private Vector2 scrollOffset;

    public Vector2 ScrollOffset
    {
        get => scrollOffset;
        set => scrollOffset = value;
    }

    /// <summary>把采样平移往前推一段。单位是局部单位，和网格顶点同一套坐标。</summary>
    public void AddScroll(Vector2 delta)
    {
        scrollOffset += delta;
    }

    void Start()
    {
        Rebuild();
        RefreshWindScaling();
    }

    private void Awake()
    {
        // 保证任何在 Start 之前发生的 GetHeight 调用也能拿到有效的波形参数
        RefreshWindScaling();
    }

    /// <summary>
    /// 按当前区域尺寸重建整张网格的拓扑（顶点、索引、UV）。
    /// 顶点位置每帧会重算，但网格的「规格」只在尺寸变了的时候重建一次。
    /// </summary>
    private void Rebuild()
    {
        meshSize = ResolveSize();

        float cell = Mathf.Max(1e-3f, cellSize);
        cellsX = Mathf.Max(1, Mathf.CeilToInt(meshSize.x / cell));
        cellsZ = Mathf.Max(1, Mathf.CeilToInt(meshSize.y / cell));
        vertsX = cellsX + 1;
        vertsZ = cellsZ + 1;

        // 用「尺寸 ÷ 格子数」而不是 cellSize 本身：格子数是从 cellSize 向上取整来的，
        // 直接用 cellSize 会差出一条边，水就铺不满区域。这样算出来正好铺满。
        stepX = meshSize.x / cellsX;
        stepZ = meshSize.y / cellsZ;

        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = gameObject.name;

            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
            }
            meshFilter.mesh = mesh;
        }

        int vertexCount = vertsX * vertsZ;

        mesh.Clear();
        // 顶点数超过 65535 时 UInt16 索引会溢出，格子调细之后必须切 UInt32
        mesh.indexFormat = vertexCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;

        GenerateVerts(vertices);
        mesh.SetVertices(vertices);
        mesh.triangles = GenerateTris();
        mesh.uv = generateUVs();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        SetupMeshCollider();
    }

    /// <summary>网格该铺多大：优先跟区域，区域没给就用序列化里手填的宽/长。</summary>
    private Vector2 ResolveSize()
    {
        if (region != null)
        {
            // 用 WorldBounds 而不是 width/length：区域被旋转时它给出的仍然是水平包围盒。
            // 但它给的是「世界」尺寸，而 meshSize 在这里是拿去做「局部」顶点范围的，
            // 所以要按本物体的缩放除回去。否则 transform 一缩放，缩放会被乘两次，
            // 网格按缩放系数的平方增长，和区域对不上。
            // （前提：Wave 与 OceanRegion 朝向一致。WorldBounds 是旋转矩形的 AABB，
            //   两边朝向不同的话，除缩放补不回旋转带来的误差。）
            Vector3 size = region.WorldBounds.size;
            Vector3 s = transform.lossyScale;
            if (size.x > 0f && size.z > 0f)
            {
                return new Vector2(size.x / Mathf.Max(1e-6f, Mathf.Abs(s.x)),
                                  size.z / Mathf.Max(1e-6f, Mathf.Abs(s.z)));
            }
        }

        return new Vector2(Mathf.Max(1e-6f, worldWidth), Mathf.Max(1e-6f, worldLength));
    }

    /// <summary>
    /// 根据当前风力刷新运行时波形缩放（基准为中等风力 + 北风的两层 Octave）。
    /// 原实现直接把风力系数乘进 baseSteepness / baseWavelength 这两个序列化字段，
    /// 在 Editor 下会把改动永久写回资源；这里改为写入运行时字段，基准值保持不变。
    /// 同时在风力等级变化时自动跟随，改风后浪会立刻变化。
    /// </summary>
    private void RefreshWindScaling()
    {
        float windScale = 1f;

        if (Wind.instance != null)
        {
            WindLevel level = Wind.instance.CurrentWindLevel;

            // 风力系数：无风/1级=小浪，2级=中浪（基准），3级=大浪
            windScale = level == WindLevel.Level3 ? 1.5f
                : level == WindLevel.Level2 ? 1f
                : 0.5f; // None / Level1
        }

        // 解析波参数随风力缩放（方向由 Wind 决定；视觉/漂浮都会用同一套公式）
        effectiveSteepness = Mathf.Clamp01(baseSteepness * windScale);

        // 下限必须相对 baseWavelength，不能是绝对米数。
        // 这里原来写的是 Mathf.Max(0.5f, baseWavelength * windScale)——那是 20 米海面时代的遗留。
        // 海洋缩小之后 0.5 米的下限永远生效，波长被钉死在半米，波幅 a = steepness/k 跟着涨，
        // 于是波谷扎进桌子的托盘底、波峰冒出桌沿顶。现在改成基准波长的十分之一。
        minWavelength = Mathf.Max(1e-3f, Mathf.Abs(baseWavelength) * 0.1f);
        effectiveWavelength = Mathf.Max(minWavelength, baseWavelength * windScale);
    }

    private void SetupMeshCollider()
    {
        meshCollider = GetComponent<MeshCollider>();

        if (!generateMeshCollider)
        {
            if (meshCollider != null)
            {
                meshCollider.enabled = false;
            }
            return;
        }

        if (meshCollider == null)
        {
            meshCollider = gameObject.AddComponent<MeshCollider>();
        }

        // 必须是非凸：顶点数远超凸包上限，凸的烘焙会直接失败
        meshCollider.convex = false;
        meshCollider.sharedMesh = mesh;
    }

    private void Update()
    {
        if (mesh == null)
        {
            return;
        }

        // 区域被改大改小、或者格子大小被改了，网格规格就得重来。
        // 带容差比较：WorldBounds 每帧从 lossyScale 重算，父级缩放只要有一点浮点抖动，
        // 精确比较就会一直判定"变了"，每帧白重建整张网格。
        Vector2 wanted = ResolveSize();
        if (Mathf.Abs(wanted.x - meshSize.x) > 1e-4f || Mathf.Abs(wanted.y - meshSize.y) > 1e-4f)
        {
            Rebuild();
        }

        // 按配置降频刷新，1 = 每帧
        int interval = Mathf.Max(1, meshUpdateInterval);
        frameCounter++;
        if (frameCounter < interval)
        {
            return;
        }
        frameCounter = 0;

        // 风力等级可能在运行时变化（WeatherUI），这里跟随刷新
        RefreshWindScaling();

        float time = Time.time;
        float halfX = meshSize.x * 0.5f;
        float halfZ = meshSize.y * 0.5f;

        for (int x = 0; x < vertsX; x++)
        {
            float localX = x * stepX - halfX;
            for (int z = 0; z < vertsZ; z++)
            {
                float localZ = z * stepZ - halfZ;

                // 波形在本物体的局部 XZ 上采样，顶点存的也是局部坐标。
                // 于是统一缩放 k 时，世界波形正好是局部波形的相似变换：
                // 世界波长 k·λ、波幅 k·a、波峰速度 k·c，而时间频率不变。
                // 所以缩放 transform 就能等比缩放整个海浪，不需要动任何波形参数，
                // 也不需要 sqrt(k) 的时间修正（加了反而会让世界速度变成 sqrt(k)·c，破坏自相似）。
                //
                // 采样点再整体平移 scrollOffset：顶点位置不动，整片浪从网格上流过去。
                // 这是「船在托盘里原地航行、海往后走」的全部机关，见 OceanScroll。
                float y = EvaluateDirectionalWaveHeight(
                    new Vector2(localX + scrollOffset.x, localZ + scrollOffset.y), time) * heightScale;

                vertices[index(x, z)] = new Vector3(localX, y, localZ);
            }
        }

        mesh.SetVertices(vertices);
        mesh.RecalculateNormals();
        // 顶点动了但 bounds 不更新会让水面在部分机位被视锥剔除掉（原始版本只在 Start 算过一次，
        // 且当时所有 y=0，bounds 的 Y 尺寸为 0）
        mesh.RecalculateBounds();

        if (refreshMeshColliderEachFrame && meshCollider != null && meshCollider.enabled)
        {
            // 非凸 MeshCollider 不会自动跟随顶点变化，必须重新赋一次才能触发重烘焙
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }
    }

    /// <summary>顶点铺成一张以本地原点为中心、y=0 的平面。高度每帧在 Update 里另算。</summary>
    private void GenerateVerts(List<Vector3> buffer)
    {
        buffer.Clear();

        float halfX = meshSize.x * 0.5f;
        float halfZ = meshSize.y * 0.5f;

        for (int x = 0; x < vertsX; x++)
        {
            float localX = x * stepX - halfX;
            for (int z = 0; z < vertsZ; z++)
            {
                buffer.Add(new Vector3(localX, 0f, z * stepZ - halfZ));
            }
        }
    }

    private int index(int x, int z)
    {
        return x * vertsZ + z;
    }

    private int[] GenerateTris()
    {
        // 索引是紧凑填的（步长 vertsZ），所以数组正好是「格子数 × 6」，不用再留余量
        var tris = new int[cellsX * cellsZ * 6];
        int i = 0;

        for (int x = 0; x < cellsX; x++)
        {
            for (int z = 0; z < cellsZ; z++)
            {
                int v00 = index(x, z);
                int v10 = index(x + 1, z);
                int v01 = index(x, z + 1);
                int v11 = index(x + 1, z + 1);

                tris[i++] = v00;
                tris[i++] = v11;
                tris[i++] = v10;
                tris[i++] = v00;
                tris[i++] = v01;
                tris[i++] = v11;
            }
        }
        return tris;
    }

    private Vector2[] generateUVs()
    {
        var uvs = new Vector2[vertsX * vertsZ];

        float tile = Mathf.Max(1e-3f, uvTileWorld);
        float halfX = meshSize.x * 0.5f;
        float halfZ = meshSize.y * 0.5f;

        for (int x = 0; x < vertsX; x++)
        {
            float u = (x * stepX - halfX) / tile;
            for (int z = 0; z < vertsZ; z++)
            {
                float v = (z * stepZ - halfZ) / tile;
                uvs[index(x, z)] = new Vector2(FoldUV(u), FoldUV(v));
            }
        }
        return uvs;
    }

    /// <summary>
    /// 把 UV 折返到 [0,1]：每 2 个单位来回一次。
    /// 原来写的是 (i / uvScale) % 2，只对非负下标成立；网格改成以原点为中心之后坐标有负数，
    /// C# 的 % 对负数返回负值，折返会错位，所以这里改用 floor 取模。
    /// </summary>
    private static float FoldUV(float t)
    {
        float f = t - Mathf.Floor(t * 0.5f) * 2f;   // → [0, 2)
        return f <= 1f ? f : 2f - f;
    }

    /// <summary>
    /// 采样某个「世界坐标」正下方的水面世界高度。
    /// 顶点和物理共用同一个高度函数，所以「看起来的浪」和「算出来的浪」永远一致。
    /// </summary>
    public float GetHeight(Vector3 position)
    {
        // 波形在局部空间里算，所以先把世界点转进局部。
        // 然后必须把完整的那个局部顶点 (local.x, hLocal, local.z) 映射回世界再取 y——
        // 只映射 (0, hLocal, 0) 会漏掉俯仰/横滚时 m10·lx + m12·lz 的贡献，
        // 船在斜面上的采样点就会对不上网格。（海洋搬上桌面、可能会被倾斜之后这一步才暴露。）
        Vector3 local = transform.InverseTransformPoint(position);

        // 采样平移必须和网格那边用同一份。漏掉的话，屏幕上波峰明明从船底下过去了，
        // 船却还按原地不动的那套浪在起伏 —— 眼睛看到的水和托着船的水对不上。
        float hLocal = EvaluateDirectionalWaveHeight(
            new Vector2(local.x + scrollOffset.x, local.z + scrollOffset.y), Time.time) * heightScale;

        return transform.TransformPoint(new Vector3(local.x, hLocal, local.z)).y;
    }

    private float EvaluateDirectionalWaveHeight(Vector2 xzWorld, float time)
    {
        float scale = samplingScale <= 0f ? 1f : samplingScale;

        // 归一化到采样空间，控制“单位距离内经历多少个波峰”
        Vector2 xz = xzWorld / scale;

        // 方向：由 Wind 提供；在其基础上叠加少量 spread 产生多波方向差异
        Vector2 baseDir;
        if (Wind.instance != null)
        {
            Vector3 w = Wind.instance.GetWindDirectionVector();
            baseDir = new Vector2(w.x, w.z);
        }
        else
        {
            baseDir = Vector2.up;
        }

        if (baseDir.sqrMagnitude < 0.0001f)
            baseDir = Vector2.up;
        baseDir.Normalize();

        float height = 0f;
        int n = Mathf.Clamp(directionalWaveCount, 1, 8);

        // 下限跟 RefreshWindScaling 里那个同源。这里再兜一道，防止有人绕过它直接改 effectiveWavelength。
        // 注意是「相对 baseWavelength」的下限，不是绝对米数——原来写死的 0.5 米把波长钉死，
        // 波幅 a = steepness/k 只好跟着涨，浪就顶穿了桌面。
        float wavelength = Mathf.Max(minWavelength, effectiveWavelength);
        float steepness = Mathf.Clamp01(effectiveSteepness);

        for (int i = 0; i < n; i++)
        {
            float t = n == 1 ? 0f : (float)i / (n - 1);
            float spreadAngle = Mathf.Lerp(-directionSpread, directionSpread, t) * 90f;
            Vector2 dir = Rotate2D(baseDir, spreadAngle);

            float k = 2f * Mathf.PI / wavelength;
            float c = Mathf.Sqrt(Mathf.Max(0.0001f, gravity) / k);
            float f = k * (Vector2.Dot(dir, xz) - c * (time * waveTimeScale));

            // 参考网页：a = steepness / k（steepness 控制“陡峭度”，同时隐含幅度与波长关系）
            float a = steepness / k;
            height += a * Mathf.Sin(f);

            wavelength *= Mathf.Max(1.01f, wavelengthMultiplier);
            steepness *= Mathf.Clamp01(steepnessFalloff);
        }

        return height;
    }

    private static Vector2 Rotate2D(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float s = Mathf.Sin(rad);
        float c = Mathf.Cos(rad);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c).normalized;
    }

    [Serializable]
    public struct Octave
    {
        public Vector2 speed;
        public Vector2 scale;
        public float height;
        public bool alternate;
    }
}
