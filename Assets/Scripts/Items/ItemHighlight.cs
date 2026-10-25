using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 只负责轮廓显示，不决定物件能否交互。支持一个 SpriteRenderer 或 MeshRenderer 外观。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Item))]
public class ItemHighlight : MonoBehaviour
{
    [SerializeField] private Material spriteOutlineMaterial;
    [SerializeField] private Material meshOutlineMaterial;

    private SpriteRenderer spriteRenderer;
    private Material originalSpriteMaterial;
    private Material spriteMaterialInstance;
    private GameObject meshOutline;

    public void SetHighlighted(bool highlighted)
    {
        if (highlighted && !isActiveAndEnabled)
        {
            return;
        }

        if (highlighted && spriteRenderer == null && meshOutline == null)
        {
            CreateOutline();
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.sharedMaterial = highlighted ? spriteMaterialInstance : originalSpriteMaterial;
        }

        if (meshOutline != null)
        {
            meshOutline.SetActive(highlighted);
        }
    }

    private void CreateOutline()
    {
        Item item = GetComponent<Item>();
        Renderer appearance = item.appearance != null ? item.appearance : GetComponentInChildren<Renderer>();
        if (appearance == null || !appearance.enabled)
        {
            return;
        }

        if (appearance is SpriteRenderer sprite && spriteOutlineMaterial != null)
        {
            if (sprite.sprite == null)
            {
                return;
            }

            spriteRenderer = sprite;
            originalSpriteMaterial = sprite.sharedMaterial;
            spriteMaterialInstance = new Material(spriteOutlineMaterial);

            // 限制采样范围，避免图集里相邻图片影响轮廓。
            Vector2 min = new Vector2(1f, 1f);
            Vector2 max = Vector2.zero;
            foreach (Vector2 uv in sprite.sprite.uv)
            {
                min = Vector2.Min(min, uv);
                max = Vector2.Max(max, uv);
            }

            spriteMaterialInstance.SetVector("_SpriteUVRect", new Vector4(min.x, min.y, max.x, max.y));
            return;
        }

        MeshFilter source = appearance.GetComponent<MeshFilter>();
        if (!(appearance is MeshRenderer) || source == null || source.sharedMesh == null || meshOutlineMaterial == null)
        {
            return;
        }

        // 显示用子物体没有碰撞体，不参与射线或物件交互。
        meshOutline = new GameObject("Interaction Outline");
        meshOutline.layer = appearance.gameObject.layer;
        meshOutline.transform.SetParent(appearance.transform, false);
        meshOutline.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
        MeshRenderer outlineRenderer = meshOutline.AddComponent<MeshRenderer>();
        Material[] materials = new Material[source.sharedMesh.subMeshCount];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = meshOutlineMaterial;
        }

        outlineRenderer.sharedMaterials = materials;
        outlineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        outlineRenderer.receiveShadows = false;
        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        properties.SetVector("_MeshCenter", source.sharedMesh.bounds.center);
        outlineRenderer.SetPropertyBlock(properties);
        meshOutline.SetActive(false);
    }

    private void OnDisable()
    {
        SetHighlighted(false);
    }

    private void OnDestroy()
    {
        if (spriteMaterialInstance != null)
        {
            Destroy(spriteMaterialInstance);
        }

        if (meshOutline != null)
        {
            Destroy(meshOutline);
        }
    }
}
