using UnityEngine;
using UnityEngine.Rendering;

/// <summary>独立海洋的水面。网格与浮力采样共用波形；保持水面水平，仅绕 Y 轴旋转。</summary>
[ExecuteAlways, DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class OceanSurface : MonoBehaviour
{
    [SerializeField] private Vector2 size = new Vector2(40f, 40f);
    [SerializeField, Min(0.05f)] private float cellSize = 0.25f;
    [SerializeField, Min(0f)] private float amplitude = 0.25f;
    [SerializeField, Min(0.1f)] private float wavelength = 6f;
    [SerializeField, Min(0f)] private float speed = 1.2f;
    [SerializeField] private float directionDegrees = 25f;
    [SerializeField] private Transform followTarget;
    [SerializeField] private OceanWind wind;
    private readonly Vector2[] waveDirections = new Vector2[5];
    private readonly float[] waveNumbers = new float[5];
    private readonly float[] waveAmplitudes = new float[5];
    private readonly float[] wavePhases = new float[5];
    private static readonly float[] DirectionOffsets = { -48f, -13f, 23f, 61f, 104f };
    private static readonly float[] LengthRatios = { 1f, 0.73f, 0.49f, 0.34f, 0.23f };
    private static readonly float[] AmplitudeRatios = { 1f, 0.52f, 0.27f, 0.12f, 0.055f };
    private float preparedTime = float.NaN;

    private Mesh mesh;
    private Vector3[] vertices;
    private Vector3[] normals;
    private Vector2 builtSize;
    private float builtCell;
    public Vector2 Size => size;
    private float Clock => Application.isPlaying ? Time.time : Time.realtimeSinceStartup;

    private void OnEnable() => Rebuild();

    private void Update()
    {
        if (followTarget != null)
        {
            Vector3 target = transform.parent != null
                ? transform.parent.InverseTransformPoint(followTarget.position) : followTarget.position;
            transform.localPosition = new Vector3(target.x, transform.localPosition.y, target.z);
        }
        if (mesh == null || builtSize != size || builtCell != cellSize) Rebuild();
        UpdateSurface(Clock);
    }

    private void Rebuild()
    {
        size = new Vector2(Mathf.Clamp(size.x, 0.1f, 500f), Mathf.Clamp(size.y, 0.1f, 500f));
        cellSize = Mathf.Max(0.05f, cellSize);
        // Limit each axis to avoid accidental multi-million-vertex allocations in the inspector.
        int nx = Mathf.Clamp(Mathf.CeilToInt(size.x / cellSize), 1, 256);
        int nz = Mathf.Clamp(Mathf.CeilToInt(size.y / cellSize), 1, 256);
        if (mesh == null)
        {
            mesh = new Mesh { name = "Ocean Surface (Generated)", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
        }
        mesh.Clear();
        mesh.indexFormat = (nx + 1) * (nz + 1) > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        vertices = new Vector3[(nx + 1) * (nz + 1)];
        normals = new Vector3[vertices.Length];
        var uv = new Vector2[vertices.Length];
        for (int x = 0; x <= nx; x++)
        for (int z = 0; z <= nz; z++)
        {
            int i = x * (nz + 1) + z;
            vertices[i] = new Vector3(size.x * ((float)x / nx - 0.5f), 0f, size.y * ((float)z / nz - 0.5f));
            uv[i] = new Vector2(vertices[i].x, vertices[i].z) / 2f;
        }
        var indices = new int[nx * nz * 6];
        int t = 0;
        for (int x = 0; x < nx; x++)
        for (int z = 0; z < nz; z++)
        {
            int a = x * (nz + 1) + z, b = a + nz + 1;
            indices[t++] = a; indices[t++] = a + 1; indices[t++] = b;
            indices[t++] = a + 1; indices[t++] = b + 1; indices[t++] = b;
        }
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = indices;
        GetComponent<MeshFilter>().sharedMesh = mesh;
        builtSize = size;
        builtCell = cellSize;
        UpdateSurface(Clock);
    }

    private void PrepareWaves(float time, bool force = false)
    {
        if (!force && preparedTime == time) return;
        preparedTime = time;
        float directionOffset = 0f, strength = 1f;
        if (wind != null) wind.Sample(time, out directionOffset, out strength);
        for (int i = 0; i < 5; i++)
        {
            // Keep phase directions and speeds fixed. Wind changes component weights instead
            // of rotating phases across the whole world or multiplying accumulated time.
            float angle = (directionDegrees + DirectionOffsets[i]) * Mathf.Deg2Rad;
            waveDirections[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float alignment = 0.5f + 0.5f * Mathf.Cos((DirectionOffsets[i] - directionOffset) * Mathf.Deg2Rad);
            waveNumbers[i] = 2f * Mathf.PI / (Mathf.Max(0.1f, wavelength) * LengthRatios[i]);
            waveAmplitudes[i] = Mathf.Max(0f, amplitude) * AmplitudeRatios[i] * strength * Mathf.Lerp(0.25f, 1f, alignment * alignment);
            wavePhases[i] = -Mathf.Sqrt(9.81f * waveNumbers[i]) * time * speed + i * 1.73f;
        }
    }

    private float Evaluate(Vector2 point, float time, out Vector2 slope)
    {
        PrepareWaves(time);
        // Moving the finite water patch must not drag the wave phase with it.
        point += new Vector2(transform.localPosition.x, transform.localPosition.z);
        float height = 0f;
        slope = Vector2.zero;
        for (int i = 0; i < 5; i++)
        {
            Vector2 dir = waveDirections[i];
            float k = waveNumbers[i];
            float a = waveAmplitudes[i];
            float phase = k * Vector2.Dot(dir, point) + wavePhases[i];
            height += a * Mathf.Sin(phase);
            slope += dir * (a * k * Mathf.Cos(phase));
        }
        return height;
    }

    private void UpdateSurface(float time)
    {
        PrepareWaves(time, true);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i];
            v.y = Evaluate(new Vector2(v.x, v.z), time, out Vector2 slope);
            vertices[i] = v;
            normals[i] = new Vector3(-slope.x, 1f, -slope.y).normalized;
        }
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.RecalculateBounds();
    }

    /// <summary>World-space height for future buoyancy. Analytic waves approximate the tessellated surface.</summary>
    public float GetHeight(Vector3 worldPosition) => GetHeight(worldPosition, Clock);

    public float GetHeight(Vector3 worldPosition, float time)
    {
        Vector3 p = transform.InverseTransformPoint(worldPosition);
        p.y = Evaluate(new Vector2(p.x, p.z), time, out _);
        return transform.TransformPoint(p).y;
    }

    private void OnDisable()
    {
        if (mesh == null) return;
        if (GetComponent<MeshFilter>().sharedMesh == mesh) GetComponent<MeshFilter>().sharedMesh = null;
        if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        mesh = null;
    }
}
