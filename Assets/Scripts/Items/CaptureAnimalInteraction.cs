using UnityEngine;

/// <summary>
/// “捕捉动物” + “动物”：动物接到手里，消耗原来手持的捕捉工具。
/// </summary>
public static class CaptureAnimalInteraction
{
    public static bool CanInteract(PlayerPickup pickup, Item tool, Item target)
    {
        if (pickup == null || pickup.HeldItem != tool ||
            !LabelSystem.HasLabel(tool, ItemLabel.CaptureAnimal) ||
            !LabelSystem.HasLabel(target, ItemLabel.Animal))
        {
            return false;
        }

        return pickup.CanReplaceHeldItem(target.GetComponent<Rigidbody>());
    }

    public static bool TryInteract(PlayerPickup pickup, Item tool, Item target)
    {
        if (!CanInteract(pickup, tool, target))
        {
            return false;
        }

        Rigidbody targetBody = target.GetComponent<Rigidbody>();
        if (!pickup.TryReplaceHeldItem(targetBody))
        {
            return false;
        }

        // 先停用，避免延迟销毁前还参与这一帧的碰撞。
        tool.gameObject.SetActive(false);
        Object.Destroy(tool.gameObject);
        return true;
    }
}
