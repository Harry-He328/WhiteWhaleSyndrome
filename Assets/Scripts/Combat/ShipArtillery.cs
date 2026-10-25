using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>炮位列表是唯一的安装顺序；桌面模型随后按独立 Cannon 实例绑定。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class ShipArtillery : MonoBehaviour
{
    [Tooltip("依次为左中、右中、左前、右前、左后、右后；可拖动重排。")]
    [SerializeField] private List<Cannon> installationOrder = new List<Cannon>();
    [SerializeField, Min(0)] private int initialCannonCount = 2;
    [SerializeField] private bool fireOnStart = true;
    private readonly List<Cannon> installedCannons = new List<Cannon>();
    private Rigidbody body;

    public IReadOnlyList<Cannon> InstalledCannons => installedCannons;
    public int Capacity => installationOrder.Count;
    public IReadOnlyList<Cannon> InstallationOrder => installationOrder;
    public event Action<Cannon> CannonAdded;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        for (int i = 0; i < Mathf.Min(initialCannonCount, Capacity); i++)
            if (!TryAddCannon(out _)) break;
    }

    private void Start()
    {
        if (fireOnStart) FireAll();
    }

    public bool TryAddCannon(out Cannon cannon)
    {
        foreach (Cannon candidate in installationOrder)
        {
            if (candidate == null || installedCannons.Contains(candidate)) continue;
            candidate.Install(body);
            installedCannons.Add(candidate);
            cannon = candidate;
            CannonAdded?.Invoke(candidate);
            return true;
        }
        cannon = null;
        return false;
    }

    public int FireAll()
    {
        int fired = 0;
        foreach (Cannon cannon in installedCannons)
            if (cannon != null && cannon.TryFire()) fired++;
        return fired;
    }
}
