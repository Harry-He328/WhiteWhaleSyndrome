/// <summary>
/// 物件交互的统一入口。各条规则的具体效果写在各自的脚本里。
/// </summary>
public static class ItemInteraction
{
    public static bool CanInteract(PlayerPickup pickup, Item heldItem, ItemUseTarget target)
    {
        if (heldItem == null || heldItem == target.Item) return false;
        foreach (var component in heldItem.GetComponents<UnityEngine.MonoBehaviour>())
            if (component.isActiveAndEnabled && component is IItemUseRule rule && rule.CanUse(pickup, target)) return true;
        return target.Item != null && CaptureAnimalInteraction.CanInteract(pickup, heldItem, target.Item);
    }

    public static bool TryInteract(PlayerPickup pickup, Item heldItem, ItemUseTarget target)
    {
        if (heldItem == null || heldItem == target.Item) return false;
        foreach (var component in heldItem.GetComponents<UnityEngine.MonoBehaviour>())
            if (component.isActiveAndEnabled && component is IItemUseRule rule && rule.CanUse(pickup, target))
                return rule.TryUse(pickup, target);
        return target.Item != null && CaptureAnimalInteraction.TryInteract(pickup, heldItem, target.Item);
    }

    public static bool TryInteract(PlayerPickup pickup, Item heldItem, Item target)
    {
        return target != null && TryInteract(pickup, heldItem,
            new ItemUseTarget(target, target.transform.position, UnityEngine.Vector3.up));
    }
}
