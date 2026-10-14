using System.Collections;
using UnityEngine;

/// <summary>
/// 船沉下去。血扣光之后由外面调一次 Begin()，它负责：关掉碰撞体、关掉所有炮
/// （免得沉到一半还在撞人、还在开火），把船歪过去，慢慢往下沉，沉够了删掉物体。
///
/// 这里直接写 transform 的位置和姿态，故意跟「位置只写一次」那条规矩错开。
/// 那条规矩管的是**浮着的时候**：推进和拉回中心两边都要写位置，所以必须合成一次。
/// 沉的时候浮力脚本已经被关掉了，写位置的人只剩这一个，不会打架。
/// </summary>
public class ShipSink : MonoBehaviour
{
    [Tooltip("往哪边歪（度）。正数往右舷倒")]
    [SerializeField] private float tiltDegrees = 30f;

    [Tooltip("沉多久（秒）")]
    [SerializeField] private float sinkSeconds = 3f;

    [Tooltip("一共往下沉多少米")]
    [SerializeField] private float sinkDepth = 0.3f;

    [Tooltip("沉完之后隔多久把物体删掉。0 = 不删")]
    [SerializeField] private float destroyAfter = 8f;

    private ShipHealth health;
    private bool sinking;
    private Quaternion startRotation;
    private Vector3 startPosition;

    private void Awake()
    {
        // 自己听自己的血量。玩家那条船上没有别的脚本会去调沉船，所以这一步不能省 ——
        // 少了它，玩家血量归零之后什么都不会发生，船还照常浮着、照常开炮。
        health = GetComponent<ShipHealth>();
        if (health != null)
        {
            health.Died += OnDied;
        }
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.Died -= OnDied;
        }
    }

    private void OnDied(ShipHealth _)
    {
        Begin();
    }

    /// <summary>开始沉。重复调只会生效一次。</summary>
    public void Begin()
    {
        if (sinking)
        {
            return;
        }

        sinking = true;

        // 先关掉会继续跟世界发生关系的东西：碰撞体（沉下去还挡路、还撞船）、
        // 所有炮（沉到一半还在开火）、浮力脚本（不然它每帧还在把船往水面上顶）。
        // 关炮靠的是 Broadside 自己检查 enabled —— 光把组件 enabled 设成 false 不会拦住
        // 外面直接调过来的 TryFire()，得它自己配合。
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }

        Broadside[] cannons = GetComponentsInChildren<Broadside>(true);
        for (int i = 0; i < cannons.Length; i++)
        {
            cannons[i].enabled = false;
        }

        Floater floater = GetComponent<Floater>();
        if (floater != null)
        {
            floater.enabled = false;
        }

        startRotation = transform.rotation;
        startPosition = transform.position;

        StartCoroutine(SinkRoutine());
    }

    private IEnumerator SinkRoutine()
    {
        float duration = Mathf.Max(0.01f, sinkSeconds);
        Quaternion targetRotation = startRotation * Quaternion.Euler(0f, 0f, tiltDegrees);

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);

            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, k);
            transform.position = startPosition + Vector3.down * (sinkDepth * k);

            // 沉的过程里还要在水面上下晃，不然看着像电梯。
            yield return null;
        }

        transform.rotation = targetRotation;
        transform.position = startPosition + Vector3.down * sinkDepth;

        if (destroyAfter > 0f)
        {
            Destroy(gameObject, destroyAfter);
        }
    }
}
