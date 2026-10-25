using UnityEngine;

/// <summary>Room controls retain their state while the ocean loads; all simulation stays on the ocean ship.</summary>
[DisallowMultipleComponent]
public sealed class OceanRoomBridge : MonoBehaviour
{
    [SerializeField] private OceanTableDisplay display;
    [SerializeField] private TabletopArtillery tabletopArtillery;
    private Transform boundRoot;
    private ShipMovement ship;
    private float sailAmount = 1f;
    private float steering;
    public ShipMovement Ship => ship;
    public bool IsReady => ship != null;

    public void SetSailAmount(float amount)
    {
        sailAmount = Mathf.Clamp01(amount);
        if (ship != null) ship.SetSailAmount(sailAmount);
    }

    public void SetSteering(float amount)
    {
        steering = Mathf.Clamp(amount, -1f, 1f);
        if (ship != null) ship.SetSteering(steering);
    }

    private void Update()
    {
        Transform root = display != null ? display.SourceRoot : null;
        if (root == null) root = null; // Normalize Unity's destroyed-object references before comparing bindings.
        if (ReferenceEquals(root, boundRoot) && (root == null || ship != null)) return;
        boundRoot = root;
        ship = root != null ? root.GetComponentInChildren<ShipMovement>() : null;
        if (tabletopArtillery != null) tabletopArtillery.BindShip(ship != null ? ship.GetComponent<ShipArtillery>() : null);
        if (ship == null) return;
        ship.SetSailAmount(sailAmount);
        ship.SetSteering(steering);
    }

    private void OnDisable()
    {
        if (tabletopArtillery != null) tabletopArtillery.BindShip(null);
        boundRoot = null;
        ship = null;
    }
}
