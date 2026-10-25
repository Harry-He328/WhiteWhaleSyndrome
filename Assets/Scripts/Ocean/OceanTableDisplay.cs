using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>One display window: draws shared ocean meshes at tabletop scale, without cloning simulation objects.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class OceanTableDisplay : MonoBehaviour
{
    [SerializeField] private string oceanScene = "Ocean";
    [SerializeField] private Transform sourceRoot;
    [SerializeField] private Vector2 windowSize = new Vector2(2f, 2f);
    [SerializeField, Min(0.0001f)] private float displayScale = 0.05f;
    [SerializeField] private Vector2 oceanCenter;
    [SerializeField] private bool followPlayerShip;
    [SerializeField] private bool showValidationFixtures;
    private Transform trackedShip;
    private readonly List<MeshFilter> filters = new List<MeshFilter>();
    private readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
    private readonly List<Material> sourceMaterials = new List<Material>();
    private float nextRefresh;
    public int SubmittedMeshes { get; private set; }
    public int CulledMeshes { get; private set; }
    public Transform SourceRoot => sourceRoot;

    private void OnEnable() => OceanVisualEvents.Changed += OnVisualsChanged;

    private void OnVisualsChanged(Scene scene)
    {
        if (sourceRoot != null && sourceRoot.gameObject.scene == scene) RefreshSources();
    }

    public void Bind(Transform root)
    {
        sourceRoot = root;
        var ship = root != null ? root.GetComponentInChildren<ShipMovement>() : null;
        trackedShip = ship != null ? ship.transform : null;
        RefreshSources();
    }

    private IEnumerator Start()
    {
        if (!Application.isPlaying) yield break;
        Scene scene = SceneManager.GetSceneByName(oceanScene);
        if (!scene.isLoaded)
        {
            if (!Application.CanStreamedLevelBeLoaded(oceanScene))
            {
                Debug.LogError("Ocean scene must be enabled in Build Settings.", this);
                yield break;
            }
            yield return SceneManager.LoadSceneAsync(oceanScene, LoadSceneMode.Additive);
            scene = SceneManager.GetSceneByName(oceanScene);
        }
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "OceanWorld")
            {
                Transform fixtures = root.transform.Find("DisplayValidation");
                if (fixtures != null) fixtures.gameObject.SetActive(showValidationFixtures);
                Bind(root.transform);
            }
            if (root.name == "Preview") root.SetActive(false);
        }
    }

    private void RefreshSources()
    {
        filters.Clear();
        if (sourceRoot != null) sourceRoot.GetComponentsInChildren(false, filters);
        nextRefresh = Time.realtimeSinceStartup + 1f;
    }

    private Material DisplayMaterial(Material original)
    {
        if (original == null) return null;
        if (materials.TryGetValue(original, out Material result)) return result;
        if (original.shader.name == "WhiteWhale/Ocean Water")
        {
            result = new Material(original) { name = original.name + " (Table Display)", hideFlags = HideFlags.HideAndDontSave };
            result.SetFloat("_TableClip", 1f);
            materials.Add(original, result);
            return result;
        }
        Shader shader = Shader.Find("WhiteWhale/Clipped Lit");
        if (shader == null) return null;
        result = new Material(shader) { name = original.name + " (Table Display)", hideFlags = HideFlags.HideAndDontSave };
        // Prototype supports opaque static mesh materials. Special shaders require explicit adapters.
        string[] colors = { "_BaseColor", "_SpecColor", "_EmissionColor" };
        foreach (string key in colors) if (original.HasProperty(key)) result.SetColor(key, original.GetColor(key));
        if (original.HasProperty("_Smoothness")) result.SetFloat("_Smoothness", original.GetFloat("_Smoothness"));
        if (original.HasProperty("_BaseMap"))
        {
            result.SetTexture("_BaseMap", original.GetTexture("_BaseMap"));
            result.SetTextureScale("_BaseMap", original.GetTextureScale("_BaseMap"));
            result.SetTextureOffset("_BaseMap", original.GetTextureOffset("_BaseMap"));
        }
        materials.Add(original, result);
        return result;
    }

    private void LateUpdate()
    {
        SubmittedMeshes = CulledMeshes = 0;
        if (sourceRoot == null) return;
        if (followPlayerShip && trackedShip != null)
        {
            Vector3 center = sourceRoot.InverseTransformPoint(trackedShip.position);
            oceanCenter = new Vector2(center.x, center.z);
        }
        if (Time.realtimeSinceStartup >= nextRefresh) RefreshSources();
        float scale = Mathf.Max(0.0001f, displayScale);
        Vector2 half = new Vector2(Mathf.Max(0.001f, windowSize.x), Mathf.Max(0.001f, windowSize.y)) * 0.5f;
        // Window stays upright and unscaled; its Transform supplies only center and yaw.
        Quaternion yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        Matrix4x4 table = Matrix4x4.TRS(transform.position, yaw, Vector3.one);
        Matrix4x4 mapping = table * Matrix4x4.Scale(Vector3.one * scale)
            * Matrix4x4.Translate(new Vector3(-oceanCenter.x, 0f, -oceanCenter.y)) * sourceRoot.worldToLocalMatrix;
        Shader.SetGlobalVector("_OceanRegionCenter", transform.position);
        Shader.SetGlobalVector("_OceanRegionAxisX", yaw * Vector3.right * half.x);
        Shader.SetGlobalVector("_OceanRegionAxisZ", yaw * Vector3.forward * half.y);
        Shader.SetGlobalFloat("_OceanRegionClip", 1f);
        foreach (MeshFilter filter in filters)
        {
            if (filter == null || !filter.gameObject.activeInHierarchy || filter.sharedMesh == null) continue;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled) continue;
            Matrix4x4 matrix = mapping * filter.transform.localToWorldMatrix;
            Matrix4x4 local = table.inverse * matrix;
            Bounds b = filter.sharedMesh.bounds;
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = local.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                min = Vector2.Min(min, new Vector2(p.x, p.z));
                max = Vector2.Max(max, new Vector2(p.x, p.z));
            }
            if (min.x > half.x || max.x < -half.x || min.y > half.y || max.y < -half.y)
            { CulledMeshes++; continue; }
            renderer.GetSharedMaterials(sourceMaterials);
            for (int sub = 0; sub < Mathf.Min(filter.sharedMesh.subMeshCount, sourceMaterials.Count); sub++)
            {
                Material material = DisplayMaterial(sourceMaterials[sub]);
                if (material == null) continue;
                var parameters = new RenderParams(material) { layer = gameObject.layer,
                    shadowCastingMode = ShadowCastingMode.On, receiveShadows = true };
                Graphics.RenderMesh(parameters, filter.sharedMesh, sub, matrix);
                SubmittedMeshes++;
            }
        }
    }

    private void OnDisable()
    {
        OceanVisualEvents.Changed -= OnVisualsChanged;
        Shader.SetGlobalFloat("_OceanRegionClip", 0f);
        foreach (Material material in materials.Values)
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        materials.Clear();
        filters.Clear();
    }
}
