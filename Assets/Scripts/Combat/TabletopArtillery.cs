using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>集中配置桌面摆放位置；根据船上实际安装的火炮同步模型。</summary>
[DisallowMultipleComponent]
public class TabletopArtillery : MonoBehaviour
{
    [Serializable]
    public class DisplaySlot
    {
        public Cannon cannon;
        public int oceanCannonIndex = -1;
        public Transform anchor;
        [Tooltip("可放入场景中已有的外观；为空时按需生成。")]
        public TabletopCannon model;
    }

    [SerializeField] private ShipArtillery shipArtillery;
    [SerializeField] private TabletopCannon modelPrefab;
    [SerializeField] private List<DisplaySlot> displaySlots = new List<DisplaySlot>();
    private bool started;

    public IReadOnlyList<DisplaySlot> DisplaySlots => displaySlots;

    public void BindShip(ShipArtillery artillery)
    {
        if (shipArtillery != null) shipArtillery.CannonAdded -= OnCannonAdded;
        shipArtillery = artillery;
        if (shipArtillery != null && isActiveAndEnabled) shipArtillery.CannonAdded += OnCannonAdded;
        foreach (DisplaySlot slot in displaySlots)
            if (slot != null && slot.oceanCannonIndex >= 0)
                slot.cannon = artillery != null && slot.oceanCannonIndex < artillery.InstallationOrder.Count
                    ? artillery.InstallationOrder[slot.oceanCannonIndex] : null;
        if (started) Synchronize();
    }

    private void OnEnable()
    {
        if (shipArtillery != null) shipArtillery.CannonAdded += OnCannonAdded;
        if (started) Synchronize();
    }

    private void Start()
    {
        started = true;
        Synchronize();
    }

    private void OnDisable()
    {
        if (shipArtillery != null) shipArtillery.CannonAdded -= OnCannonAdded;
    }

    private void OnCannonAdded(Cannon cannon)
    {
        if (started) Synchronize();
    }

    public void Synchronize()
    {
        var assigned = new HashSet<Cannon>();
        foreach (DisplaySlot slot in displaySlots)
        {
            if (slot == null) continue;
            bool installed = shipArtillery != null && slot.cannon != null && assigned.Add(slot.cannon) && IsInstalled(slot.cannon);
            if (!installed || slot.anchor == null)
            {
                if (slot.model != null)
                {
                    slot.model.Bind(null);
                    slot.model.gameObject.SetActive(false);
                }
                continue;
            }

            if (slot.model == null)
            {
                if (modelPrefab == null) continue;
                slot.model = Instantiate(modelPrefab, slot.anchor, false);
                slot.model.name = "TabletopCannon_" + slot.cannon.name;
            }
            else if (slot.model.transform.parent != slot.anchor)
            {
                slot.model.transform.SetParent(slot.anchor, false);
            }

            slot.model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            slot.model.Bind(slot.cannon);
            slot.model.gameObject.SetActive(true);
        }
    }

    private bool IsInstalled(Cannon cannon)
    {
        foreach (Cannon installed in shipArtillery.InstalledCannons)
            if (installed == cannon) return true;
        return false;
    }
}
