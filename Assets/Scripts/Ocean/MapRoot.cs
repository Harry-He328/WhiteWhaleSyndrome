using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地图场景的根。地图上所有手摆的内容都挂在它下面，每个岛是它的一个**直接子物体**。
///
/// 它只干三件事：把每个岛登记成一条「块」，量出这块在自身局部空间里的包围盒，然后
/// 把渲染器和碰撞体先全部关掉——等显示层（OceanMapLayer）把它挂到海面底下、
/// 摆好这一帧的位置之后再统一打开。
///
/// 为什么要先关掉：这个物体在被挂到海面底下之前，位置是世界原点，也就是桌子底下。
/// 加载进来的那一帧如果不管，就会在桌子底下闪一下。
///
/// 这块只认「我有哪些东西、各占多大地方」，不认识桌子、船、相机、显示范围，
/// 也不认识滚动。怎么摆、哪些该显示，是显示层的事。
///
/// 前提（改这个场景时要注意）：
///   1. 本物体必须是**单位旋转、单位缩放**（位置随便，挂上去的时候会被覆盖）。
///   2. 每个岛是直接子物体，在加载时必须处于**激活状态**——
///      没激活的物体量出来的包围盒是零，位置也是错的。
///   3. 加载之后这些块不能再动（旋转、缩放、换网格），包围盒只在这时候量一次。
/// </summary>
public class MapRoot : MonoBehaviour
{
    /// <summary>编辑参照物的名字。填了引用就按引用跳，没填就按这个名字跳。</summary>
    private const string EditorRefsName = "_EditorRefs";

    [Tooltip("编辑用的参照物（海平面标记、方向光）。运行时会被关掉并销毁，" +
             "绝不能当成地图的一块内容登记进去。")]
    [SerializeField] private GameObject editorRefs;

    /// <summary>当前实例。显示层优先按场景查找，这个只做兜底。</summary>
    public static MapRoot Current { get; private set; }

    /// <summary>
    /// 地图上的一块内容（一个岛）。包围盒是**本物体局部空间**里的 XZ 矩形，
    /// 加载时量一次就定死。
    /// </summary>
    public sealed class Entry
    {
        public Transform transform;
        public Renderer[] renderers;
        public Collider[] colliders;

        /// <summary>渲染器的局部 XZ 包围盒。</summary>
        public Vector2 renderMin;
        public Vector2 renderMax;

        /// <summary>碰撞体的局部 XZ 包围盒。和上面那份分开存，见 OceanMapLayer 里的说明。</summary>
        public Vector2 colliderMin;
        public Vector2 colliderMax;
    }

    private readonly List<Entry> entries = new List<Entry>();

    public IReadOnlyList<Entry> Entries => entries;

    private void Awake()
    {
        if (Current != null && Current != this)
        {
            // 地图同时被加载了两份。留第一份。
            Destroy(gameObject);
            return;
        }
        Current = this;

        // 先禁用、再销毁。Destroy 要到这一帧结束才生效，只调 Destroy 的话，
        // 下面登记时照样能枚举到它里面的渲染器，包围盒会被撑成整个显示范围。
        if (editorRefs != null)
        {
            editorRefs.SetActive(false);
            Destroy(editorRefs);
        }

        BuildEntries();

        // 挂到海面底下之前，一块都不许露。
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    private void BuildEntries()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);

            if (IsEditorRefs(child))
            {
                continue;
            }

            Renderer[] renderers = child.GetComponentsInChildren<Renderer>(false);
            Collider[] colliders = child.GetComponentsInChildren<Collider>(false);

            if (renderers.Length == 0 && colliders.Length == 0)
            {
                Debug.LogWarning($"[MapRoot] 「{child.name}」下面没有渲染器也没有碰撞体，" +
                                 "没有被当成地图内容。纯分组用的空物体请挪到别的岛下面；" +
                                 "如果这个岛是被禁用状态，请把它在场景里激活。", child);
                continue;
            }

            Entry entry = new Entry
            {
                transform = child,
                renderers = renderers,
                colliders = colliders,
            };

            BakeBounds(transform, BoundsOf(renderers), out entry.renderMin, out entry.renderMax);
            BakeBounds(transform, BoundsOf(colliders), out entry.colliderMin, out entry.colliderMax);

            // 这块碰撞体是**每帧跟着地图平移**的。静态碰撞体逐帧瞬移是物理上的反模式，
            // 物理引擎的宽阶段不为此设计，船那种每帧被推着走的刚体会出现穿插和抖动。
            // 挂一个运动学刚体，让引擎知道"这是个会被搬着走的实体"。
            if (colliders.Length > 0)
            {
                Rigidbody body = child.GetComponent<Rigidbody>();
                if (body == null)
                {
                    body = child.gameObject.AddComponent<Rigidbody>();
                }
                body.isKinematic = true;
                body.useGravity = false;
            }

            entries.Add(entry);
        }
    }

    private bool IsEditorRefs(Transform child)
    {
        if (editorRefs != null && child.gameObject == editorRefs)
        {
            return true;
        }
        // 引用漏填时的兜底。参照物被登记进去会把包围盒撑成整个显示范围，范围判定就废了。
        return child.name == EditorRefsName;
    }

    private static Bounds[] BoundsOf(Renderer[] items)
    {
        Bounds[] result = new Bounds[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            result[i] = items[i].bounds;
        }
        return result;
    }

    private static Bounds[] BoundsOf(Collider[] items)
    {
        Bounds[] result = new Bounds[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            result[i] = items[i].bounds;
        }
        return result;
    }

    /// <summary>
    /// 把一组世界包围盒并起来，换算到 <paramref name="space"/> 的局部空间，取 XZ 上的矩形。
    /// 走 8 个角逐点换算，**不是**拿世界尺寸去除以 lossyScale——那个只在旋转为零时才对。
    /// </summary>
    private static void BakeBounds(Transform space, Bounds[] worldBounds, out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        bool any = false;

        for (int i = 0; i < worldBounds.Length; i++)
        {
            Bounds b = worldBounds[i];
            Vector3 c = b.center;
            Vector3 e = b.extents;

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = new Vector3(
                    c.x + ((corner & 1) == 0 ? -e.x : e.x),
                    c.y + ((corner & 2) == 0 ? -e.y : e.y),
                    c.z + ((corner & 4) == 0 ? -e.z : e.z));

                Vector3 local = space.InverseTransformPoint(p);
                min = Vector2.Min(min, new Vector2(local.x, local.z));
                max = Vector2.Max(max, new Vector2(local.x, local.z));
                any = true;
            }
        }

        if (!any)
        {
            // 没有可用包围盒。退化成一个点，交给显示层按范围判定处理。
            min = Vector2.zero;
            max = Vector2.zero;
        }
    }

    /// <summary>整张地图显示 / 隐藏。显示层就位之前必须是关的。</summary>
    public void SetVisible(bool visible)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            SetRenderers(entries[i], visible);
            SetColliders(entries[i], visible);
        }
    }

    public static void SetRenderers(Entry entry, bool visible)
    {
        for (int i = 0; i < entry.renderers.Length; i++)
        {
            entry.renderers[i].enabled = visible;
        }
    }

    public static void SetColliders(Entry entry, bool visible)
    {
        for (int i = 0; i < entry.colliders.Length; i++)
        {
            entry.colliders[i].enabled = visible;
        }
    }
}
