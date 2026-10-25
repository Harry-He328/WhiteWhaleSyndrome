using UnityEngine;

/// <summary>集中配置各操作装置的线性响应；与普通视角灵敏度独立。</summary>
[CreateAssetMenu(menuName = "White Whale/Drag Input Settings")]
public class DragInputSettings : ScriptableObject
{
    [Tooltip("船舵：每像素改变的轮盘角度。")]
    [Min(0f)] public float helmDegreesPerPixel = 0.4f;
    [Tooltip("帆绳：每像素改变的放帆比例，越低越便于微调。")]
    [Min(0f)] public float sailAmountPerPixel = 0.00175f;
    [Tooltip("击发绳：向后拖动多少像素尝试击发一次。")]
    [Min(1f)] public float cannonPullDistancePixels = 60f;
}
