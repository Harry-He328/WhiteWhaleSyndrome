using UnityEngine;

/// <summary>一门独立火炮；空物体的位置是出生点，本地 Z 正向是基础射向。</summary>
[DisallowMultipleComponent]
public class Cannon : MonoBehaviour
{
    [SerializeField] private CannonDefinition definition;
    private Rigidbody owner;
    private bool installed;
    private float nextFireTime;

    public CannonDefinition Definition => definition;
    public bool IsInstalled => installed;
    public bool IsReady => installed && isActiveAndEnabled && Time.time >= nextFireTime &&
        definition != null && definition.projectilePrefab != null;

    public void Install(Rigidbody ship)
    {
        owner = ship;
        installed = true;
    }

    public bool TryFire()
    {
        if (!IsReady) return false;
        Vector3 direction = Quaternion.AngleAxis(-definition.elevation, transform.right) * transform.forward;
        CannonProjectile shot = Instantiate(definition.projectilePrefab, transform.position,
            Quaternion.LookRotation(direction, transform.up));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(shot.gameObject, gameObject.scene);
        if (owner != null) shot.transform.SetParent(owner.transform.parent, true);
        Vector3 inheritedVelocity = owner != null ? owner.GetPointVelocity(transform.position) : Vector3.zero;
        shot.Launch(owner, direction * definition.muzzleSpeed + inheritedVelocity);
        OceanVisualEvents.Notify(gameObject.scene);
        nextFireTime = Time.time + definition.cooldown;
        return true;
    }
}
