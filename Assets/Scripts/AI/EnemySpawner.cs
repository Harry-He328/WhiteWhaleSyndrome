using UnityEngine;

/// <summary>
/// 敌人刷新点。只有逻辑，没有外观——一个空物体挂上这个脚本就行。
///
/// 节奏是三条规矩：
///   · 每 spawnInterval 秒刷一个。
///   · 场上活着的到了 maxAlive 就**不再刷**。这是防堆积的闸门，没有它一场演示跑一分钟
///     场上就会堆满船，帧率跟着塌。
///   · 少于 minAlive 就**立刻补**，不等计时器。
///
/// 刷在海洋显示范围的边上、靠里一点的地方。为什么不刷在范围外面让敌人开进来：敌人的船
/// 用的是普通材质，不带海洋那套裁剪，刷在范围外面会被玩家看见它悬在桌子上。所以只能刷在
/// 范围内侧，代价是「凭空出现」——但边缘就是水面边缘，看着像是从远处驶来的。
///
/// 方向偏向玩家的船头。海面是往后滚的，代表船在往前走，所以从船头方向冒出来的敌船读起来
/// 是「前方发现敌船」；从屁股后面冒出来就很怪。
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("刷什么")]
    [SerializeField] private GameObject enemyPrefab;

    [Header("节奏")]
    [Tooltip("每几秒刷一个")]
    [SerializeField] private float spawnInterval = 4f;

    [Tooltip("场上少于这个数就立刻补一个，不等计时器")]
    [SerializeField] private int minAlive = 1;

    [Tooltip("场上到了这个数就不再刷。防止无限堆积")]
    [SerializeField] private int maxAlive = 4;

    [Tooltip("累计打死这么多就关闭所有刷新点，并清掉场上剩下的敌人")]
    [SerializeField] private int killTarget = 6;

    [Header("刷在哪")]
    [Tooltip("海洋显示范围。刷新点落在它里面靠边的位置")]
    [SerializeField] private OceanRegion region;

    [Tooltip("朝谁的方向偏。一般填玩家的船。留空则纯随机方向")]
    [SerializeField] private Transform headingSource;

    [Tooltip("刷新点离显示范围边界往里缩的比例。0.12 = 缩一成二")]
    [SerializeField, Range(0f, 0.45f)] private float edgeInset = 0.12f;

    [Tooltip("方向偏向船头的程度。0 = 全随机，1 = 全刷在正前方")]
    [SerializeField, Range(0f, 1f)] private float aheadBias = 0.6f;

    private float timer;

    private void Start()
    {
        // 「打死多少算完」是全局的一件事，写进登记处，让所有刷新点看到同一个数。
        // 场景里如果有多个刷新点，这个值要填成一样的。
        EnemyRegistry.SetKillTarget(killTarget);
    }

    private void Update()
    {
        if (enemyPrefab == null || EnemyRegistry.Stopped)
        {
            return;
        }

        int alive = EnemyRegistry.AliveCount;

        if (alive >= maxAlive)
        {
            // 到顶了。计时器清零，等有空位时从头计时，不然一有空位会立刻补一串出来。
            timer = 0f;
            return;
        }

        timer -= Time.deltaTime;

        if (alive < minAlive || timer <= 0f)
        {
            Spawn();
            timer = spawnInterval;
        }
    }

    private void Spawn()
    {
        Vector3 point = PickSpawnPoint();
        Vector3 facing = PickFacing(point);

        Instantiate(enemyPrefab, point, Quaternion.LookRotation(facing, Vector3.up));
    }

    private Vector3 PickSpawnPoint()
    {
        Vector3 center = region != null ? region.Center : transform.position;
        Vector2 half = HalfExtent();
        Vector2 dir = RandomDirection();

        // 沿着这个方向一直走到显示范围的边界，再往里缩一点。
        // 不能拿一个固定半径去算：显示范围是长方形，固定半径在窄的那一边会跑到外面去。
        float toX = Mathf.Abs(dir.x) > 1e-5f ? half.x / Mathf.Abs(dir.x) : float.PositiveInfinity;
        float toZ = Mathf.Abs(dir.y) > 1e-5f ? half.y / Mathf.Abs(dir.y) : float.PositiveInfinity;

        float distance = Mathf.Min(toX, toZ);
        if (float.IsInfinity(distance))
        {
            distance = Mathf.Min(half.x, half.y);
        }

        distance *= 1f - edgeInset;

        return new Vector3(center.x + dir.x * distance,
                           center.y,
                           center.z + dir.y * distance);
    }

    /// <summary>随机方向。aheadBias 大于零时，按这个比例往玩家的船头方向偏。</summary>
    private Vector2 RandomDirection()
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);

        if (aheadBias > 0f && headingSource != null)
        {
            Vector3 forward = headingSource.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude > 1e-8f)
            {
                // 这套角度约定：角度 a 对应方向 (cos a, sin a) 映射到世界 (x, z)。
                // 船头方向的 x、z 换成同一个约定就是 atan2(x, z)。
                float ahead = Mathf.Atan2(forward.x, forward.z);
                angle = Mathf.LerpAngle(angle * Mathf.Rad2Deg, ahead * Mathf.Rad2Deg, aheadBias)
                        * Mathf.Deg2Rad;
            }
        }

        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    private Vector3 PickFacing(Vector3 point)
    {
        Transform look = headingSource != null ? headingSource : (region != null ? region.transform : null);

        if (look == null)
        {
            return Vector3.forward;
        }

        Vector3 direction = look.position - point;
        direction.y = 0f;

        return direction.sqrMagnitude < 1e-8f ? Vector3.forward : direction.normalized;
    }

    /// <summary>显示范围的半宽、半长（米）。用本地的轴长，所以假设范围没有绕竖轴转过。</summary>
    private Vector2 HalfExtent()
    {
        if (region == null)
        {
            return new Vector2(1.5f, 1f);
        }

        return new Vector2(Mathf.Max(0.05f, region.HalfAxisX.magnitude),
                           Mathf.Max(0.05f, region.HalfAxisZ.magnitude));
    }

    private void OnDrawGizmosSelected()
    {
        if (region == null)
        {
            return;
        }

        Gizmos.color = new Color(1f, 0.4f, 0.2f, 1f);
        Vector3 center = region.Center;
        Vector2 half = HalfExtent() * (1f - edgeInset);

        Vector3 a = center + new Vector3(-half.x, 0f, -half.y);
        Vector3 b = center + new Vector3(half.x, 0f, -half.y);
        Vector3 c = center + new Vector3(half.x, 0f, half.y);
        Vector3 d = center + new Vector3(-half.x, 0f, half.y);

        Gizmos.DrawLine(a, b);
        Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, d);
        Gizmos.DrawLine(d, a);
    }
}
