using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class OceanWallProjection : MonoBehaviour
{
    private const int TextureWidth = 1280;
    private const int TextureHeight = 720;

    [SerializeField] private OceanRoomBridge ocean;
    [SerializeField] private Shader projectionShader;
    [SerializeField, Range(20f, 70f)] private float oceanFieldOfView = 40f;
    [SerializeField, Min(1f)] private float oceanCameraDistance = 29f;
    [SerializeField, Range(25f, 80f)] private float oceanLookDownAngle = 48f;
    [SerializeField, Min(0f)] private float oceanLookAhead = 3f;

    private Camera oceanCamera;
    private MeshRenderer wallImage;
    private RenderTexture image;
    private Material material;
    private Mesh quad;
    private OceanShipReflection shipReflection;

    private void OnEnable()
    {
        if (ocean == null || projectionShader == null) return;

        image = new RenderTexture(TextureWidth, TextureHeight, 24)
        {
            name = "Ocean Wall Feed",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        material = new Material(projectionShader) { mainTexture = image };

        // The room and ocean are spatially separated. A tilted perspective
        // camera frames the real ocean directly without changing gameplay layers.
        var cameraObject = new GameObject("Ocean Projection Camera");
        cameraObject.transform.SetParent(transform, false);
        oceanCamera = cameraObject.AddComponent<Camera>();
        oceanCamera.enabled = false;
        oceanCamera.orthographic = false;
        oceanCamera.aspect = (float)TextureWidth / TextureHeight;
        oceanCamera.nearClipPlane = 0.3f;
        oceanCamera.farClipPlane = 130f;
        oceanCamera.clearFlags = CameraClearFlags.SolidColor;
        oceanCamera.backgroundColor = new Color(0.035f, 0.12f, 0.17f);
        oceanCamera.cullingMask = ~LayerMask.GetMask("UI");
        oceanCamera.depth = -10;
        oceanCamera.targetTexture = image;

        var surface = new GameObject("Projected Ocean Image");
        surface.transform.SetParent(transform, false);
        quad = new Mesh { name = "Ocean Projection Surface" };
        quad.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f)
        };
        quad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        quad.RecalculateBounds();
        surface.AddComponent<MeshFilter>().sharedMesh = quad;
        wallImage = surface.AddComponent<MeshRenderer>();
        wallImage.sharedMaterial = material;
        wallImage.shadowCastingMode = ShadowCastingMode.Off;
        wallImage.receiveShadows = false;
        wallImage.enabled = false;
    }

    private void LateUpdate()
    {
        if (oceanCamera == null) return;

        Transform ship = ocean != null && ocean.Ship != null ? ocean.Ship.transform : null;
        if (ship != null)
        {
            if (shipReflection == null && ocean.Display != null && ocean.Display.SourceRoot != null)
            {
                shipReflection = ocean.Display.SourceRoot.GetComponentInChildren<OceanShipReflection>();
                if (shipReflection != null) shipReflection.SetProjectionCamera(oceanCamera);
            }
            oceanCamera.fieldOfView = oceanFieldOfView;
            float heading = ship.eulerAngles.y;
            Vector3 course = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            Vector3 focus = ship.position + course * oceanLookAhead;
            Quaternion angle = Quaternion.Euler(oceanLookDownAngle, heading, 0f);
            oceanCamera.transform.SetPositionAndRotation(
                focus - (angle * Vector3.forward) * oceanCameraDistance,
                angle);
        }
        oceanCamera.enabled = ship != null;
        wallImage.enabled = ship != null;
    }

    private void OnDisable()
    {
        if (oceanCamera == null) return;
        if (shipReflection != null) shipReflection.SetProjectionCamera(null);
        shipReflection = null;

        oceanCamera.enabled = false;
        oceanCamera.targetTexture = null;
        wallImage.enabled = false;
        Destroy(oceanCamera.gameObject);
        Destroy(wallImage.gameObject);
        Destroy(quad);
        Destroy(material);
        image.Release();
        Destroy(image);
    }
}
