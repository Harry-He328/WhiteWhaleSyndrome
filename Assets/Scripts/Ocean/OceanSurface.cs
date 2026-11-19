using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>独立海洋的水面。网格与浮力采样共用波形；保持水面水平，仅绕 Y 轴旋转。</summary>
[ExecuteAlways, DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class OceanSurface : MonoBehaviour
{
    private const int BaseWaveCount = 5;
    private const int MaxWaveCount = 32;
    private const int MaxContactFoamSources = 8; // Matches the arrays in OceanWater.shader.
    private static readonly ProfilerMarker SurfaceUpdateMarker = new ProfilerMarker("OceanSurface.UpdateSurface");

    [SerializeField] private Vector2 size = new Vector2(40f, 40f);
    [SerializeField, Min(0.05f)] private float cellSize = 0.25f;
    [SerializeField, Min(0f)] private float amplitude = 0.25f;
    [SerializeField, Min(0.1f)] private float wavelength = 6f;
    [SerializeField, Min(0f)] private float speed = 1.2f;
    [SerializeField] private float directionDegrees = 25f;
    [SerializeField, Range(BaseWaveCount, MaxWaveCount), Tooltip("Wave components evaluated per vertex. Set to 5 for the original sea.")]
    private int waveCount = BaseWaveCount;
    [SerializeField, Range(0f, 0.8f), Tooltip("Higher values squeeze vertices toward the wave crests.")]
    private float crestSteepness = 0.68f;
    [SerializeField, Range(0f, 0.9f), Tooltip("Controls the drop in wave strength between groups.")]
    private float groupContrast = 0.65f;
    [SerializeField, Min(2f), Tooltip("Distance between wave groups, measured in main wavelengths.")]
    private float groupSpacing = 3.1f;
    [SerializeField] private Transform followTarget;
    [SerializeField] private OceanWind wind;
    private readonly List<OceanContactFoamSource> contactFoamSources = new List<OceanContactFoamSource>();
    private readonly Vector4[] foamHulls = new Vector4[MaxContactFoamSources];
    private readonly Vector4[] foamAxes = new Vector4[MaxContactFoamSources];
    private readonly Vector2[] waveDirections = new Vector2[MaxWaveCount];
    private readonly float[] waveNumbers = new float[MaxWaveCount];
    private readonly Vector2[] waveVectors = new Vector2[MaxWaveCount];
    private readonly float[] waveAmplitudes = new float[MaxWaveCount];
    private readonly float[] wavePhases = new float[MaxWaveCount];
    private readonly float[] horizontalAmplitudes = new float[MaxWaveCount];
    private readonly Vector4[] gpuWaveX = new Vector4[MaxWaveCount];
    private readonly Vector4[] gpuWaveZ = new Vector4[MaxWaveCount];
    private static readonly float[] DirectionOffsets = { -48f, -13f, 23f, 61f, 104f };
    private static readonly float[] LengthRatios = { 1f, 0.73f, 0.49f, 0.34f, 0.23f };
    private static readonly float[] AmplitudeRatios = { 1f, 0.52f, 0.27f, 0.12f, 0.055f };
    private float preparedTime = float.NaN;
    private int activeWaveCount;
    private Vector2 groupWaveVector;
    private float groupPhase;
    private float groupDepth;
    private float maxHeight;
    private float maxHorizontalDisplacement;
    private Mesh mesh;
    private MeshRenderer surfaceRenderer;
    private MaterialPropertyBlock waveProperties;
    private Vector2 builtSize;
    private float builtCell;
    public Vector2 Size => size;
    private float Clock => Application.isPlaying ? Time.time : Time.realtimeSinceStartup;

    private void OnEnable()
    {
        surfaceRenderer = GetComponent<MeshRenderer>();
        waveProperties ??= new MaterialPropertyBlock();
        Rebuild();
        UpdateSurface(Clock);
    }

    private void OnValidate() => preparedTime = float.NaN;

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
        }
        mesh.Clear();
        mesh.indexFormat = (nx + 1) * (nz + 1) > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        var vertices = new Vector3[(nx + 1) * (nz + 1)];
        var uv = new Vector2[vertices.Length];
        for (int x = 0; x <= nx; x++)
        for (int z = 0; z <= nz; z++)
        {
            int i = x * (nz + 1) + z;
            vertices[i] = new Vector3(size.x * ((float)x / nx - 0.5f), 0f, size.y * ((float)z / nz - 0.5f));
            uv[i] = new Vector2(vertices[i].x, vertices[i].z);
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
    }

    private void PrepareWaves(float time, bool force = false)
    {
        int count = Mathf.Clamp(waveCount, BaseWaveCount, MaxWaveCount);
        if (!force && preparedTime == time && activeWaveCount == count) return;
        preparedTime = time;
        activeWaveCount = count;
        float directionOffset = 0f, strength = 1f;
        if (wind != null) wind.Sample(time, out directionOffset, out strength);
        float mainWaveNumber = 2f * Mathf.PI / Mathf.Max(0.1f, wavelength);
        float speedScale = Mathf.Max(0f, speed);
        float mainAngle = directionDegrees * Mathf.Deg2Rad;
        float groupNumber = mainWaveNumber / Mathf.Max(2f, groupSpacing);
        groupWaveVector = new Vector2(Mathf.Cos(mainAngle), Mathf.Sin(mainAngle)) * groupNumber;
        // The envelope travels at half the main crest speed, so crests pass through a group.
        groupPhase = -groupNumber * (0.5f * Mathf.Sqrt(9.81f / mainWaveNumber) * speedScale) * time;
        groupDepth = Mathf.Clamp(groupContrast, 0f, 0.9f) * 0.5f;
        float derivativeBound = 0f;
        maxHeight = 0f;
        // Spread the additional wave energy across components so the count is not a height multiplier.
        float detailAmplitude = 0.52f / Mathf.Sqrt(Mathf.Max(8, activeWaveCount - BaseWaveCount));
        for (int i = 0; i < activeWaveCount; i++)
        {
            float offset, lengthRatio, amplitudeRatio, phaseOffset;
            if (i < BaseWaveCount)
            {
                offset = DirectionOffsets[i];
                lengthRatio = LengthRatios[i];
                amplitudeRatio = AmplitudeRatios[i];
                phaseOffset = i * 1.73f;
            }
            else
            {
                // Fixed low-discrepancy sequences spread waves without changing their phases
                // or directions when the slider changes or when the ship moves.
                float detail = i - BaseWaveCount + 1;
                offset = Mathf.Lerp(-90f, 90f, Mathf.Repeat(detail * 0.618034f, 1f));
                lengthRatio = Mathf.Lerp(0.28f, 0.86f, Mathf.Repeat(detail * 0.414214f, 1f));
                amplitudeRatio = detailAmplitude * Mathf.Lerp(0.7f, 1.15f, lengthRatio);
                phaseOffset = 2f * Mathf.PI * Mathf.Repeat(detail * 0.732051f, 1f);
            }
            // Keep phase directions and speeds fixed. Wind changes component weights instead
            // of rotating phases across the whole world or multiplying accumulated time.
            float angle = (directionDegrees + offset) * Mathf.Deg2Rad;
            waveDirections[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float alignment = 0.5f + 0.5f * Mathf.Cos((offset - directionOffset) * Mathf.Deg2Rad);
            waveNumbers[i] = mainWaveNumber / lengthRatio;
            waveVectors[i] = waveDirections[i] * waveNumbers[i];
            waveAmplitudes[i] = Mathf.Max(0f, amplitude) * amplitudeRatio * strength * Mathf.Lerp(0.25f, 1f, alignment * alignment);
            wavePhases[i] = -Mathf.Sqrt(9.81f * waveNumbers[i]) * time * speedScale + phaseOffset;
            derivativeBound += waveAmplitudes[i] * (waveNumbers[i] + groupDepth * groupNumber);
            maxHeight += waveAmplitudes[i];
        }
        // Keep the horizontal displacement derivative below 1, including the envelope.
        // This keeps the surface from folding and makes the height query invertible.
        float horizontalScale = Mathf.Clamp(crestSteepness, 0f, 0.8f) / Mathf.Max(0.2f, derivativeBound);
        maxHorizontalDisplacement = maxHeight * horizontalScale;
        for (int i = 0; i < activeWaveCount; i++)
            horizontalAmplitudes[i] = waveAmplitudes[i] * horizontalScale;
    }

    private Vector3 EvaluateDisplacement(Vector2 point)
    {
        float envelopePhase = Vector2.Dot(groupWaveVector, point) + groupPhase;
        float envelope = 1f - groupDepth + groupDepth * Mathf.Cos(envelopePhase);
        Vector3 displacement = Vector3.zero;
        for (int i = 0; i < activeWaveCount; i++)
        {
            Vector2 dir = waveDirections[i];
            Vector2 waveVector = waveVectors[i];
            float phase = Vector2.Dot(waveVector, point) + wavePhases[i];
            float sin = Mathf.Sin(phase);
            float horizontal = horizontalAmplitudes[i] * Mathf.Cos(phase);
            displacement += new Vector3(dir.x * horizontal, waveAmplitudes[i] * sin, dir.y * horizontal);
        }
        return displacement * envelope;
    }

    private void UpdateSurface(float time)
    {
        using var marker = SurfaceUpdateMarker.Auto();
        PrepareWaves(time, true);
        // Sample in a stable frame so the patch following the ship does not drag the waves.
        Vector2 patchOffset = PatchOffset;
        surfaceRenderer.GetPropertyBlock(waveProperties);
        waveProperties.SetVector("_OceanPatchOffset", new Vector4(patchOffset.x, patchOffset.y, 0f, 0f));
        waveProperties.SetFloat("_OceanWaveCount", activeWaveCount);
        waveProperties.SetVector("_OceanGroup", new Vector4(groupWaveVector.x, groupWaveVector.y, groupPhase, groupDepth));
        for (int i = 0; i < activeWaveCount; i++)
        {
            gpuWaveX[i] = new Vector4(waveDirections[i].x, waveDirections[i].y, waveNumbers[i], wavePhases[i]);
            gpuWaveZ[i] = new Vector4(waveAmplitudes[i], horizontalAmplitudes[i], 0f, 0f);
        }
        waveProperties.SetVectorArray("_OceanWaveX", gpuWaveX);
        waveProperties.SetVectorArray("_OceanWaveZ", gpuWaveZ);
        UpdateContactFoam();
        surfaceRenderer.SetPropertyBlock(waveProperties);
        // The wave envelope never exceeds one, so the summed amplitudes bound the entire mesh.
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(
            size.x + 2f * maxHorizontalDisplacement,
            2f * maxHeight + 0.01f,
            size.y + 2f * maxHorizontalDisplacement));
    }

    internal void RegisterContactFoam(OceanContactFoamSource source)
    {
        if (contactFoamSources.Contains(source)) return;
        contactFoamSources.Add(source);
        RefreshContactFoamPreview();
    }

    internal void UnregisterContactFoam(OceanContactFoamSource source)
    {
        if (contactFoamSources.Remove(source)) RefreshContactFoamPreview();
    }

    private void RefreshContactFoamPreview()
    {
        // ExecuteAlways.Update may not run again after an object registers in a still scene.
        if (Application.isPlaying || !isActiveAndEnabled || surfaceRenderer == null || mesh == null) return;
        surfaceRenderer.GetPropertyBlock(waveProperties);
        UpdateContactFoam();
        surfaceRenderer.SetPropertyBlock(waveProperties);
    }

    private void UpdateContactFoam()
    {
        int count = 0;
        foreach (OceanContactFoamSource source in contactFoamSources)
        {
            if (count == MaxContactFoamSources) break;
            if (source == null || !source.TryGetShaderData(transform, out Vector4 hull, out Vector4 axes)) continue;
            // Skip objects whose footprint cannot touch this finite water patch.
            float radius = new Vector2(hull.z, hull.w).magnitude + maxHorizontalDisplacement + 1f;
            if (Mathf.Abs(hull.x) > size.x * 0.5f + radius || Mathf.Abs(hull.y) > size.y * 0.5f + radius) continue;
            foamHulls[count] = hull;
            foamAxes[count] = axes;
            count++;
        }
        waveProperties.SetFloat("_OceanFoamSourceCount", count);
        if (count == 0) return;
        waveProperties.SetVectorArray("_OceanFoamHulls", foamHulls);
        waveProperties.SetVectorArray("_OceanFoamHullAxes", foamAxes);
    }

    private Vector2 PatchOffset
    {
        get
        {
            // Express the patch's translation in the same axes as its mesh vertices.
            Vector3 origin = transform.parent != null ? transform.parent.position : Vector3.zero;
            Vector3 offset = transform.InverseTransformVector(transform.position - origin);
            return new Vector2(offset.x, offset.z);
        }
    }

    /// <summary>World-space wave height for ship motion and ocean picking.</summary>
    public float GetHeight(Vector3 worldPosition) => GetHeight(worldPosition, Clock);

    public float GetHeight(Vector3 worldPosition, float time)
    {
        Vector3 p = transform.InverseTransformPoint(worldPosition);
        PrepareWaves(time);
        Vector2 target = new Vector2(p.x, p.z) + PatchOffset;
        Vector2 rest = target;
        // Undo the horizontal displacement before reading height at the requested XZ.
        // Fixed-point iteration converges because PrepareWaves bounds the displacement derivative.
        const float toleranceSquared = 0.000001f;
        for (int i = 0; i < 64; i++)
        {
            Vector3 displacement = EvaluateDisplacement(rest);
            Vector2 next = target - new Vector2(displacement.x, displacement.z);
            if ((next - rest).sqrMagnitude <= toleranceSquared)
            {
                p.y = displacement.y;
                return transform.TransformPoint(p).y;
            }
            rest = next;
        }
        p.y = EvaluateDisplacement(rest).y;
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
