using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// E 拿起带“可拿取”label 的物件，再按 E 放到准星指向的表面。同一时间只拿一件。
/// </summary>
[DisallowMultipleComponent]
public class PlayerPickup : MonoBehaviour
{
    [SerializeField] private Camera viewCamera;
    [SerializeField, Min(0.1f)] private float reach = 3f;
    [SerializeField, Min(0.1f)] private float placementReach = 4f;
    [SerializeField] private Vector3 holdOffset = new Vector3(0.3f, -0.25f, 1.2f);
    [SerializeField] private LayerMask interactionMask = ~0;
    [SerializeField] private Transform placementPreview;

    private Rigidbody heldBody;
    private readonly List<Collider> heldColliders = new List<Collider>();
    private readonly List<bool> colliderWasEnabled = new List<bool>();
    private bool previousUseGravity;
    private Quaternion relativeRotation;
    private float heldRadius;
    private ItemHighlight highlightedItem;
    private PlayerHelmInput helmInput;

    public Rigidbody HeldBody => heldBody;
    public Item HeldItem => heldBody != null ? heldBody.GetComponent<Item>() : null;

    private void Awake()
    {
        helmInput = GetComponent<PlayerHelmInput>();
        if (viewCamera == null)
        {
            viewCamera = GetComponentInChildren<Camera>();
        }

        if (viewCamera == null)
        {
            viewCamera = Camera.main;
        }

        if (placementPreview != null)
        {
            placementPreview.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (!Application.isFocused || Cursor.lockState != CursorLockMode.Locked ||
            (helmInput != null && helmInput.IsDragging)) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.eKey.wasPressedThisFrame || viewCamera == null)
        {
            return;
        }

        if (heldBody != null)
        {
            PlaceHeldItem();
            return;
        }

        if (TryGetTarget(reach, out RaycastHit hit))
        {
            TryPickUp(hit.rigidbody);
        }
    }

    public bool TryPickUp(Rigidbody body)
    {
        if (!CanPickUp(body))
        {
            return false;
        }

        Hold(body);
        return true;
    }

    public bool CanPickUp(Rigidbody body)
    {
        return heldBody == null && CanHold(body) &&
            LabelSystem.HasLabel(body.GetComponent<Item>(), ItemLabel.Pickable);
    }

    public bool CanReplaceHeldItem(Rigidbody body)
    {
        return heldBody != null && body != heldBody && CanHold(body);
    }

    // 交互成功时换成目标物件，不要求目标本身带“可拿取”label。
    public bool TryReplaceHeldItem(Rigidbody body)
    {
        if (!CanReplaceHeldItem(body))
        {
            return false;
        }

        Drop();
        Hold(body);
        return true;
    }

    private bool CanHold(Rigidbody body)
    {
        return viewCamera != null && body != null && !body.isKinematic &&
            body.gameObject.activeInHierarchy &&
            !viewCamera.transform.IsChildOf(body.transform);
    }

    private void Hold(Rigidbody body)
    {
        SetHighlightedItem(null);
        heldBody = body;
        previousUseGravity = body.useGravity;
        relativeRotation = Quaternion.Inverse(viewCamera.transform.rotation) * body.rotation;
        heldColliders.Clear();
        colliderWasEnabled.Clear();
        Bounds bounds = new Bounds(body.position, Vector3.zero);

        foreach (Collider itemCollider in body.GetComponentsInChildren<Collider>(true))
        {
            if (itemCollider.attachedRigidbody != body)
            {
                continue;
            }

            // 关闭前记录归属和状态；放下时不再查询已关闭碰撞体的刚体。
            heldColliders.Add(itemCollider);
            colliderWasEnabled.Add(itemCollider.enabled);
            if (itemCollider.enabled && !itemCollider.isTrigger)
            {
                bounds.Encapsulate(itemCollider.bounds);
            }

            itemCollider.enabled = false;
        }

        heldRadius = Mathf.Max(0.05f, bounds.extents.magnitude);
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.isKinematic = true;
        MoveHeldItem();
    }

    private void LateUpdate()
    {
        if (heldBody != null && viewCamera != null)
        {
            MoveHeldItem();
        }

        UpdatePlacementPreview();
        UpdateTargetHighlight();
    }

    private bool TryGetPlacementTarget(out RaycastHit hit)
    {
        return TryGetTarget(placementReach, out hit);
    }

    private bool TryGetTarget(float distance, out RaycastHit hit)
    {
        if (viewCamera == null)
        {
            hit = default;
            return false;
        }

        Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        return Physics.Raycast(ray, out hit, distance,
            interactionMask, QueryTriggerInteraction.Ignore);
    }

    private void UpdateTargetHighlight()
    {
        Item target = null;
        float distance = heldBody == null ? reach : placementReach;
        if (TryGetTarget(distance, out RaycastHit hit))
        {
            if (heldBody == null)
            {
                if (CanPickUp(hit.rigidbody))
                {
                    target = hit.rigidbody.GetComponent<Item>();
                }
            }
            else
            {
                Item candidate = hit.collider.GetComponentInParent<Item>();
                if (ItemInteraction.CanInteract(this, HeldItem, candidate))
                {
                    target = candidate;
                }
            }
        }

        SetHighlightedItem(target);
    }

    private void SetHighlightedItem(Item target)
    {
        ItemHighlight next = target != null ? target.GetComponent<ItemHighlight>() : null;
        if (next == highlightedItem)
        {
            return;
        }

        if (highlightedItem != null)
        {
            highlightedItem.SetHighlighted(false);
        }

        highlightedItem = next;
        if (highlightedItem != null)
        {
            highlightedItem.SetHighlighted(true);
        }
    }

    private void UpdatePlacementPreview()
    {
        if (placementPreview == null)
        {
            return;
        }

        if (heldBody == null || !TryGetPlacementTarget(out RaycastHit hit))
        {
            placementPreview.gameObject.SetActive(false);
            return;
        }

        // 小标记只显示表面落点，不参与物理或物件交互。
        placementPreview.position = hit.point + hit.normal * 0.02f;
        placementPreview.gameObject.SetActive(true);
    }

    private void PlaceHeldItem()
    {
        // 按 E 时重新检测；没有目标就沿用原来的就地松手。
        if (TryGetPlacementTarget(out RaycastHit hit))
        {
            Item target = hit.collider.GetComponentInParent<Item>();
            if (ItemInteraction.TryInteract(this, HeldItem, target))
            {
                return;
            }

            heldBody.position = hit.point + hit.normal * heldRadius;
        }

        Drop();
    }

    private void MoveHeldItem()
    {
        Transform view = viewCamera.transform;
        Vector3 offset = view.TransformVector(holdOffset);
        float distance = offset.magnitude;
        Vector3 position = view.position + offset;

        // 拿着时避开前方障碍，避免放下时物件已经穿进墙里。
        if (distance > 0f && Physics.SphereCast(view.position, heldRadius,
                offset / distance, out RaycastHit hit, distance,
                interactionMask, QueryTriggerInteraction.Ignore))
        {
            position = view.position + offset.normalized * Mathf.Max(0f, hit.distance - 0.02f);
        }

        heldBody.transform.SetPositionAndRotation(position, view.rotation * relativeRotation);
    }

    public void Drop()
    {
        SetHighlightedItem(null);
        for (int i = 0; i < heldColliders.Count; i++)
        {
            Collider itemCollider = heldColliders[i];
            if (itemCollider != null)
            {
                itemCollider.enabled = colliderWasEnabled[i];
            }
        }

        if (heldBody != null)
        {
            heldBody.isKinematic = false;
            heldBody.useGravity = previousUseGravity;
        }

        heldBody = null;
        heldColliders.Clear();
        colliderWasEnabled.Clear();

        if (placementPreview != null)
        {
            placementPreview.gameObject.SetActive(false);
        }
    }

    private void OnDisable()
    {
        Drop();
    }
}
