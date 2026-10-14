using UnityEngine;

/// <summary>
/// 屏幕左上角显示「打死了几个 / 一共要打几个」。演示时得看得见进度，
/// 不然打死第六个的时候玩家不知道发生了什么。
///
/// 是占位的：用 OnGUI 画，不需要 Canvas、不需要字体资源，加个空物体挂上就出字。
/// 正式做 UI 的时候整个删掉换掉，不影响任何战斗逻辑——它只读登记处，不写。
/// </summary>
public class KillCounterDisplay : MonoBehaviour
{
    [SerializeField] private int fontSize = 26;
    [SerializeField] private Vector2 margin = new Vector2(24f, 20f);

    private GUIStyle style;

    private void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                normal = { textColor = Color.white },
            };
        }

        int target = EnemyRegistry.KillTarget;

        string text = target > 0
            ? $"击杀  {EnemyRegistry.TotalKills} / {target}"
            : $"击杀  {EnemyRegistry.TotalKills}";

        if (EnemyRegistry.Stopped)
        {
            text += "     清场完毕";
        }

        GUI.Label(new Rect(margin.x, margin.y, 700f, 60f), text, style);
    }
}
