using UnityEngine;

/// <summary>
/// 让 2D 外观朝向玩家，并保持竖直。只挂在显示用的子物体上。
/// </summary>
[DisallowMultipleComponent]
public class FaceCamera : MonoBehaviour
{
    [SerializeField] private Camera viewCamera;

    private void Awake()
    {
        if (viewCamera == null)
        {
            viewCamera = Camera.main;
        }
    }

    private void LateUpdate()
    {
        if (viewCamera == null)
        {
            return;
        }

        Vector3 forward = transform.position - viewCamera.transform.position;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(forward);
        }
    }
}
