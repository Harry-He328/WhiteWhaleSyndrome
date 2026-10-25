using UnityEngine;

/// <summary>宽松地向炮尾后方拖动，达到距离后尝试击发一次；松手才能再次击发。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class CannonPullHandle : MonoBehaviour
{
    [SerializeField] private DragInputSettings dragSettings;
    private TabletopCannon model;
    private Cannon cannon;
    private Vector2 screenPullDirection;
    private float pullDistance;
    private bool pulling;
    private bool attemptedShot;

    public bool CanOperate => model != null && model.BoundCannon != null &&
        model.BoundCannon.IsInstalled && model.BoundCannon.isActiveAndEnabled;

    private void Awake()
    {
        model = GetComponentInParent<TabletopCannon>();
    }

    public bool BeginPull(Camera viewCamera)
    {
        EndPull();
        if (viewCamera == null || !CanOperate) return false;

        cannon = model.BoundCannon;
        Vector3 backward = -model.transform.forward;
        // 将炮尾后方投影到当前视角，允许从不同站位操作，也允许斜着拉。
        screenPullDirection = new Vector2(Vector3.Dot(backward, viewCamera.transform.right),
            Vector3.Dot(backward, viewCamera.transform.up));
        screenPullDirection = screenPullDirection.sqrMagnitude < 0.01f
            ? Vector2.down : screenPullDirection.normalized;
        pulling = true;
        return true;
    }

    public bool Drag(Vector2 mouseDelta)
    {
        if (!pulling || attemptedShot || cannon == null || !CanOperate) return false;

        // 只累计向后方向的净位移；斜拉有效，反向拖动会退回进度。
        pullDistance = Mathf.Max(0f, pullDistance + Vector2.Dot(mouseDelta, screenPullDirection));
        float threshold = dragSettings != null ? dragSettings.cannonPullDistancePixels : 60f;
        if (pullDistance < Mathf.Max(1f, threshold)) return false;

        attemptedShot = true;
        return cannon.TryFire();
    }

    public void EndPull()
    {
        pulling = false;
        attemptedShot = false;
        pullDistance = 0f;
        cannon = null;
    }

    private void OnDisable() => EndPull();
}
