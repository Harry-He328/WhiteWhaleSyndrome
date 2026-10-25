/// <summary>
/// 物件交互的统一入口。各条规则的具体效果写在各自的脚本里。
/// </summary>
public static class ItemInteraction
{
    public static bool CanInteract(PlayerPickup pickup, Item heldItem, Item target)
    {
        if (heldItem == null || target == null || heldItem == target)
        {
            return false;
        }

        return CaptureAnimalInteraction.CanInteract(pickup, heldItem, target);
    }

    public static bool TryInteract(PlayerPickup pickup, Item heldItem, Item target)
    {
        return CanInteract(pickup, heldItem, target) &&
            CaptureAnimalInteraction.TryInteract(pickup, heldItem, target);
    }
}
