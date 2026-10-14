using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家开炮。N 打左舷，M 打右舷，两边的冷却各算各的（各 1.5 秒，在 Broadside 上填）。
///
/// 用的是「按住就一直打」，不是「按一下打一发」。原因很实际：1.5 秒的间隔里让玩家反复点
/// 同一个键很累，按住能自动按冷却节奏打，手感也更像真船上的炮组持续射击。
/// 想改成点一下打一发，把 isPressed 换成 wasPressedThisFrame 就行。
///
/// 冷却没好时按键不会有任何事发生。以后要加「哑火」的声音和烟，挂在这里。
/// </summary>
public class PlayerCannons : MonoBehaviour
{
    [Tooltip("左舷的炮组，按 N")]
    [SerializeField] private Broadside left;

    [Tooltip("右舷的炮组，按 M")]
    [SerializeField] private Broadside right;

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (left != null && keyboard.nKey.isPressed)
        {
            left.TryFire();
        }

        if (right != null && keyboard.mKey.isPressed)
        {
            right.TryFire();
        }
    }
}
