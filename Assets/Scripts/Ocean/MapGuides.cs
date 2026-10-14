using UnityEngine;

/// <summary>
/// 摆地图时的参照。只在 Scene 视图里画线，不生成任何几何体或材质，所以运行时删不删都无所谓
/// （它挂的 _EditorRefs 会被 MapRoot 一起删掉）。
///
/// 画两样东西：
///   · 里面那个绿框 = 桌上真正会显示的那块海面。摆东西照着它摆：框里的会被看见，
///     框外的会被从边界竖直切掉。
///   · 外面那个蓝框 = 海平面，给你一个"水在多高"的参照。地图的 y=0 就是海面，
///     所以东西沉到 y 以下就是沉到水里。
///
/// 注意：地图场景不认识 OceanRegion，框的尺寸只能手抄。改了 OceanRegion 的
/// width / length，记得把这里也改一致。
/// </summary>
[ExecuteAlways]
public class MapGuides : MonoBehaviour
{
    [Header("显示范围（要和 OceanRegion 的 width / length 一致）")]
    [SerializeField] private float windowWidth = 3.32f;
    [SerializeField] private float windowLength = 2.12f;

    [Header("海平面参照")]
    [Tooltip("海平面参考方框的边长。只是个视觉参照，不影响任何东西")]
    [SerializeField] private float seaLevelSize = 6f;

    private void OnDrawGizmos()
    {
        Vector3 c = transform.position;

        Gizmos.color = new Color(0.35f, 0.6f, 0.95f, 0.5f);
        DrawRect(c, seaLevelSize * 0.5f, seaLevelSize * 0.5f);

        float hx = Mathf.Max(0f, windowWidth) * 0.5f;
        float hz = Mathf.Max(0f, windowLength) * 0.5f;

        Gizmos.color = new Color(0.2f, 1f, 0.4f, 1f);
        DrawRect(c, hx, hz);

        // 四角立柱：让平放的框从侧面看也有个形，斜着看的时候不会和地面糊在一起
        float postHeight = Mathf.Max(0.25f, Mathf.Max(hx, hz) * 0.25f);
        Vector3 up = Vector3.up * postHeight;
        DrawPost(c, -hx, -hz, up);
        DrawPost(c, hx, -hz, up);
        DrawPost(c, hx, hz, up);
        DrawPost(c, -hx, hz, up);
    }

    private static void DrawRect(Vector3 center, float hx, float hz)
    {
        Vector3 a = center + new Vector3(-hx, 0f, -hz);
        Vector3 b = center + new Vector3(hx, 0f, -hz);
        Vector3 d = center + new Vector3(hx, 0f, hz);
        Vector3 e = center + new Vector3(-hx, 0f, hz);

        Gizmos.DrawLine(a, b);
        Gizmos.DrawLine(b, d);
        Gizmos.DrawLine(d, e);
        Gizmos.DrawLine(e, a);
    }

    private static void DrawPost(Vector3 center, float x, float z, Vector3 up)
    {
        Vector3 p = center + new Vector3(x, 0f, z);
        Gizmos.DrawLine(p, p + up);
    }
}
