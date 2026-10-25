/// <summary>
/// 统一查询、添加和移除物件的 label。
/// </summary>
public static class LabelSystem
{
    public static bool HasLabel(Item item, ItemLabel label)
    {
        return item != null && item.labels.Contains(label);
    }

    public static void AddLabel(Item item, ItemLabel label)
    {
        if (item != null && !HasLabel(item, label))
        {
            item.labels.Add(label);
        }
    }

    public static void RemoveLabel(Item item, ItemLabel label)
    {
        if (item != null)
        {
            item.labels.Remove(label);
        }
    }
}
