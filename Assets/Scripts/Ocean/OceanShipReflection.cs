using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Draws selected ship meshes mirrored across the mean water level.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class OceanShipReflection : MonoBehaviour
{
    [SerializeField] private Camera previewCamera;
    [SerializeField] private Light previewSun;
    [SerializeField] private Transform reflectedShip;
    [SerializeField] private Shader captureShader;
    [SerializeField, Range(128, 1024)] private int width = 640;

    private Camera projectionCamera;
    private MeshRenderer waterRenderer;
    private MeshRenderer[] shipRenderers;
    private RenderTexture capture;
    private Material captureMaterial;
    private CommandBuffer commands;
    private MaterialPropertyBlock drawProperties;
    private readonly System.Collections.Generic.List<Material> sourceMaterials = new System.Collections.Generic.List<Material>();

    public void SetProjectionCamera(Camera camera) => projectionCamera = camera;

    private void OnEnable()
    {
        waterRenderer = GetComponent<MeshRenderer>();
        shipRenderers = reflectedShip != null ? reflectedShip.GetComponentsInChildren<MeshRenderer>(true) : System.Array.Empty<MeshRenderer>();
        RenderPipelineManager.beginCameraRendering += RenderReflection;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= RenderReflection;
        Shader.SetGlobalFloat("_OceanShipReflectionReady", 0f);
        commands?.Release();
        commands = null;
        ReleaseCapture();
        if (captureMaterial != null) CoreUtils.Destroy(captureMaterial);
        captureMaterial = null;
    }

    private void ReleaseCapture()
    {
        if (capture == null) return;
        capture.Release();
        CoreUtils.Destroy(capture);
        capture = null;
    }

    private void RenderReflection(ScriptableRenderContext context, Camera camera)
    {
        Shader.SetGlobalFloat("_OceanShipReflectionReady", 0f);
        if ((camera != previewCamera && camera != projectionCamera) ||
            captureShader == null || shipRenderers.Length == 0 || camera.transform.position.y <= transform.position.y)
            return;
        Material waterMaterial = waterRenderer != null ? waterRenderer.sharedMaterial : null;
        if (waterMaterial == null || !waterMaterial.HasProperty("_ShipReflectionStrength") ||
            waterMaterial.GetFloat("_ShipReflectionStrength") <= 0f) return;

        int height = Mathf.Max(1, Mathf.RoundToInt(width / Mathf.Max(0.1f, camera.aspect)));
        if (capture == null || capture.width != width || capture.height != height)
        {
            ReleaseCapture();
            capture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "Ocean Ship Reflection",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }
        if (captureMaterial == null) captureMaterial = CoreUtils.CreateEngineMaterial(captureShader);
        commands ??= new CommandBuffer { name = "Ocean Ship Reflection" };
        drawProperties ??= new MaterialPropertyBlock();

        Matrix4x4 viewProjection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;
        Light sun = camera == previewCamera && previewSun != null ? previewSun : RenderSettings.sun;
        Vector3 lightDirection = sun != null ? -sun.transform.forward : Vector3.up;
        Color lightColor = sun != null ? sun.color.linear * Mathf.Min(sun.intensity, 2f) : Color.white;
        commands.Clear();
        commands.SetRenderTarget(capture);
        commands.ClearRenderTarget(true, true, Color.clear);
        commands.SetGlobalMatrix("_OceanShipReflectionVP", viewProjection);
        commands.SetGlobalFloat("_OceanReflectionWaterY", transform.position.y);
        commands.SetGlobalVector("_OceanReflectionLightDirection", lightDirection);
        commands.SetGlobalColor("_OceanReflectionLightColor", lightColor);
        foreach (MeshRenderer renderer in shipRenderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                !renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
            renderer.GetSharedMaterials(sourceMaterials);
            for (int i = 0; i < Mathf.Min(sourceMaterials.Count, filter.sharedMesh.subMeshCount); i++)
            {
                Material source = sourceMaterials[i];
                if (source == null) continue;
                bool hasBaseMap = source.HasProperty("_BaseMap");
                Texture texture = hasBaseMap ? source.GetTexture("_BaseMap") : source.mainTexture;
                Vector2 scale = hasBaseMap ? source.GetTextureScale("_BaseMap") : Vector2.one;
                Vector2 offset = hasBaseMap ? source.GetTextureOffset("_BaseMap") : Vector2.zero;
                drawProperties.SetTexture("_ReflectionBaseMap", texture != null ? texture : Texture2D.whiteTexture);
                drawProperties.SetVector("_ReflectionBaseMap_ST", new Vector4(scale.x, scale.y, offset.x, offset.y));
                drawProperties.SetColor("_ReflectionBaseColor", source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white);
                commands.DrawMesh(filter.sharedMesh, renderer.localToWorldMatrix, captureMaterial, i, 0, drawProperties);
            }
        }
        commands.SetGlobalTexture("_OceanShipReflectionTexture", capture);
        commands.SetGlobalFloat("_OceanShipReflectionReady", 1f);
        context.ExecuteCommandBuffer(commands);
    }
}
