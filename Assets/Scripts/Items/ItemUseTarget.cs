using UnityEngine;

/// <summary>Interaction position is in the player's world, even when Item lives in another space.</summary>
public readonly struct ItemUseTarget
{
    public readonly Item Item;
    public readonly Vector3 Point;
    public readonly Vector3 Normal;
    public readonly IItemTargetSource Source;
    public readonly Component Surface;
    public ItemUseTarget(Item item, Vector3 point, Vector3 normal, IItemTargetSource source = null, Component surface = null)
    { Item = item; Point = point; Normal = normal; Source = source; Surface = surface; }
}

public interface IItemTargetSource
{
    bool TryGetTarget(Ray ray, float distance, out ItemUseTarget target);
    void SetHighlighted(Item item);
}

/// <summary>Optional rules on the held item; the item system has no knowledge of their game domain.</summary>
public interface IItemUseRule
{
    bool CanUse(PlayerPickup pickup, ItemUseTarget target);
    bool TryUse(PlayerPickup pickup, ItemUseTarget target);
}
