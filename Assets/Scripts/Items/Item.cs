using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 物件的基础数据。是否能拿取，由 label 决定。
/// </summary>
[DisallowMultipleComponent]
public class Item : MonoBehaviour
{
    public string itemName;
    public List<ItemLabel> labels = new List<ItemLabel>();
    public Renderer appearance;

    private void Awake()
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            itemName = gameObject.name;
        }

        if (appearance == null)
        {
            appearance = GetComponentInChildren<Renderer>();
        }
    }
}
