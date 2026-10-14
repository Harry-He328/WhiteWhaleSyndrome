using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 海洋地图的显示层。地图内容在**另一个场景**里手工摆好，这个脚本负责把它摆到这一帧该在的位置，
/// 并且决定哪些块要开渲染、哪些块要关碰撞体。地图场景自己不知道桌子、船、相机、滚动，也不知道
/// 自己会被摆在哪——那些全在这里决定。
///
/// 位置只有一句话：把地图往海面的滚动量取反的方向挪。海面的波形是靠把采样坐标整体平移
/// _ScrollOffset 来"流动"的（见 Waves），地图用同一个数、同一个局部坐标系挪，两边的滚动就是
/// 恒等的，不会各算一遍然后慢慢错开。海平面也天然对齐：地图的 y=0 就是海面本体的 y=0。
///
/// 范围判定全部在海洋的局部空间里做，纯矩形比较：
///   · 渲染器——和显示范围矩形重叠就开。多画的那些像素反正会被边界裁剪扔掉，所以余量只影响
///     开关时机，不影响画面。
///   · 碰撞体——用**碰撞体自己的**包围盒，**余量取零**。用渲染器的包围盒会让"几何已经被切光、
///     但盒子擦到范围"的岛留着碰撞体；加余量同理。那就成了玩家在房间里能撞上的隐形障碍。
///
/// 两个前提和 OceanScroll 用的是同一套（不新增耦合）：显示范围与海面同朝向；
/// Waves.HalfExtentLocal 就是显示范围在局部单位的半宽半长。
/// </summary>
public class OceanMapLayer : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("海面。地图要挂在它下面，滚动量也从它读")]
    [SerializeField] private Waves waves;

    [Header("地图场景")]
    [Tooltip("地图内容所在的场景名。这个场景必须在 Build Settings 里，否则运行时加载不到")]
    [SerializeField] private string mapSceneName = "OceanMap";

    [Header("范围判定")]
    [Tooltip("渲染器的开关余量（局部单位）。只影响什么时候开画，不影响画面——" +
             "超出显示范围的部分会被边界裁剪扔掉")]
    [SerializeField] private float renderMargin = 0.05f;

    [Tooltip("关掉之后地图完全没有碰撞体。显示范围外无碰撞体这条不受影响，" +
             "这条只决定范围**内**的岛要不要挡住船")]
    [SerializeField] private bool enableColliders = true;

    private MapRoot mapRoot;
    private bool mapSceneReady;

    private void Awake()
    {
        Scene scene = SceneManager.GetSceneByName(mapSceneName);
        if (scene.IsValid() && scene.isLoaded)
        {
            // 地图场景已经在场景列表里了（比如两个场景一起打开做多场景编辑）。
            mapSceneReady = true;
            return;
        }

        SceneManager.LoadSceneAsync(mapSceneName, LoadSceneMode.Additive).completed += OnMapSceneLoaded;
    }

    private void OnMapSceneLoaded(AsyncOperation operation)
    {
        mapSceneReady = true;
        TryAttach();
    }

    // 所有场景的 Awake（包括异步加载进来的那个）都已经跑完，是挂载最稳的时机。
    private void Start()
    {
        TryAttach();
    }

    private void TryAttach()
    {
        if (mapRoot != null || !mapSceneReady || waves == null)
        {
            return;
        }

        MapRoot root = FindMapRoot();
        if (root == null)
        {
            return;
        }

        Attach(root);
    }

    private MapRoot FindMapRoot()
    {
        Scene scene = SceneManager.GetSceneByName(mapSceneName);
        if (scene.IsValid() && scene.isLoaded)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                MapRoot found = roots[i].GetComponentInChildren<MapRoot>(true);
                if (found != null)
                {
                    return found;
                }
            }
        }

        // 兜底。单例可能是别的实例，也可能这一帧还没建好，所以不做主路径。
        return MapRoot.Current;
    }

    private void Attach(MapRoot root)
    {
        mapRoot = root;

        // 必须在挂上去之前拿引用：挂进海面之后它就不再属于地图场景，
        // 从地图场景的根物体里就找不到它了。
        Transform t = root.transform;
        t.SetParent(waves.transform, false);
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        t.localPosition = Offset();

        // 位置摆好之后才打开。中间不露。
        Refresh();
    }

    private Vector3 Offset()
    {
        Vector2 scroll = waves.ScrollOffset;
        return new Vector3(-scroll.x, 0f, -scroll.y);
    }

    // 必须在所有 Update 之后：海面的网格是在 Waves.Update 里读 scrollOffset 重建的，
    // 这里再用同一个数摆地图，两边才是同一帧的同一个值。
    private void LateUpdate()
    {
        if (mapRoot == null)
        {
            TryAttach();
            return;
        }

        mapRoot.transform.localPosition = Offset();
        Refresh();
    }

    private void Refresh()
    {
        Vector2 half = waves.HalfExtentLocal;
        Vector2 offset = new Vector2(Offset().x, Offset().z);
        float margin = Mathf.Max(0f, renderMargin);

        var entries = mapRoot.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            MapRoot.Entry entry = entries[i];

            bool showRenderers = Overlaps(entry.renderMin + offset, entry.renderMax + offset, half, margin);
            MapRoot.SetRenderers(entry, showRenderers);

            if (entry.colliders.Length == 0)
            {
                continue;
            }

            bool showColliders = enableColliders
                && Overlaps(entry.colliderMin + offset, entry.colliderMax + offset, half, 0f);
            MapRoot.SetColliders(entry, showColliders);
        }
    }

    /// <summary>局部空间里，两块矩形有没有重叠。范围矩形是以原点为中心、半尺寸为 half 的那个。</summary>
    private static bool Overlaps(Vector2 min, Vector2 max, Vector2 half, float margin)
    {
        if (max.x < -half.x - margin || min.x > half.x + margin)
        {
            return false;
        }
        if (max.y < -half.y - margin || min.y > half.y + margin)
        {
            return false;
        }
        return true;
    }

    private void OnDisable()
    {
        // 组件关掉之后没人再摆位置、也没人再收范围。留着上一帧的可见状态，
        // 地图会僵在某个位置一直显示着。
        if (mapRoot != null)
        {
            mapRoot.SetVisible(false);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (waves == null)
        {
            return;
        }

        Vector2 half = waves.HalfExtentLocal;
        Vector3 c = waves.transform.position;
        Vector3 ax = waves.transform.rotation * Vector3.right * (half.x * waves.transform.lossyScale.x);
        Vector3 az = waves.transform.rotation * Vector3.forward * (half.y * waves.transform.lossyScale.z);

        Gizmos.color = new Color(1f, 0.6f, 0.1f, 1f);
        Gizmos.DrawLine(c - ax - az, c + ax - az);
        Gizmos.DrawLine(c + ax - az, c + ax + az);
        Gizmos.DrawLine(c + ax + az, c - ax + az);
        Gizmos.DrawLine(c - ax + az, c - ax - az);
    }
}
