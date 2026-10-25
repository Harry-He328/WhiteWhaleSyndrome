using UnityEngine;

/// <summary>桌面火炮外观与船上独立火炮的对应关系，供击发绳输入使用。</summary>
[DisallowMultipleComponent]
public class TabletopCannon : MonoBehaviour
{
    public Cannon BoundCannon { get; private set; }

    public void Bind(Cannon cannon)
    {
        BoundCannon = cannon;
    }
}
