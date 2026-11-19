Shader "WhiteWhale/Ocean Water"
{
    Properties
    {
        _BaseColor("Deep Ocean", Color) = (0.018, 0.13, 0.21, 1)
        _CrestColor("Lit Waves", Color) = (0.055, 0.38, 0.43, 1)
        _FoamColor("Crest Foam", Color) = (0.65, 0.84, 0.8, 1)
        _Smoothness("Smoothness", Range(0,1)) = 0.82
        _SpecColor("Specular", Color) = (0.45,0.58,0.62,1)
        _CrestHeight("Wave Color Height", Float) = 0.6
        _FoamAmount("Sparse Foam", Range(0,1)) = 0.16
        _RippleStrength("Fine Ripple Strength", Range(0,0.2)) = 0.035
        [HideInInspector] _TableClip("Table Clip", Float) = 0
        [HideInInspector] _Cull("Cull", Float) = 2
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [ToggleUI] _UseBaseMap("Use Base Map", Float) = 0
        [ToggleUI] _ProceduralWater("Procedural Pixel Water", Float) = 0
        _PixelDeepColor("Pixel Water Deep", Color) = (0.009, 0.087, 0.105, 1)
        _PixelLightColor("Pixel Water Light", Color) = (0.019, 0.255, 0.28, 1)
        _PixelRippleColor("Pixel Ripple Color", Color) = (0.055, 0.40, 0.43, 1)
        _PixelRippleStrength("Pixel Ripple Strength", Range(0,1)) = 0.45
        _PixelCellSize("Pixel Cell Size", Range(0.025,0.35)) = 0.11
        _PixelSkyReflection("Sky Reflection", Range(0,1)) = 0.8
        _PixelSkyHorizon("Sky Reflection Horizon", Color) = (0.36, 0.52, 0.58, 1)
        _PixelSkyZenith("Sky Reflection Zenith", Color) = (0.09, 0.23, 0.36, 1)
        _PixelSunGlint("Sun Reflection", Range(0,2)) = 0.9
        _SunsetGlintStrength("Sunset Glint Strength", Range(0,2)) = 0
        _SunsetGlintColor("Sunset Glint Color", Color) = (1.0, 0.58, 0.19, 1)
        _ShipReflectionStrength("Ship Reflection Strength", Range(0,1)) = 0
        _PixelReflectionRipple("Reflection Ripple", Range(0,0.15)) = 0.045
        _WaterTransmission("Underwater Visibility", Range(0,1)) = 0
        _WaterTransmissionDepth("Underwater Half Visibility Distance", Range(0.2,6)) = 1.35
        _WaterTransmissionTint("Underwater Tint", Color) = (0.45, 0.84, 0.92, 1)
        _PixelFill("Water Fill", Range(0,0.5)) = 0.16
        _PixelWaveLightContrast("Wave Light Contrast", Range(0,1)) = 0
        _ContactFoamColor("Hull Foam Color", Color) = (0.52, 0.71, 0.70, 1)
        _ContactFoamWidth("Hull Foam Width", Range(0.06,0.6)) = 0.30
        _ContactFoamStrength("Hull Foam Strength", Range(0,1)) = 0.8
        [HideInInspector] _Cutoff("Cutoff", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Assets/Shaders/Ocean/RegionClip.hlsl"
            #include "Assets/Shaders/Ocean/OceanGpuWaves.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _CrestColor, _FoamColor, _SpecColor;
            float _Smoothness, _CrestHeight, _FoamAmount, _RippleStrength, _TableClip, _UseBaseMap;
            float4 _BaseMap_ST;
            half4 _PixelDeepColor, _PixelLightColor, _PixelRippleColor;
            float _ProceduralWater, _PixelRippleStrength, _PixelCellSize;
            half4 _PixelSkyHorizon, _PixelSkyZenith;
            float _PixelSkyReflection, _PixelSunGlint, _PixelReflectionRipple, _PixelFill;
            half4 _WaterTransmissionTint;
            float _WaterTransmission, _WaterTransmissionDepth;
            half4 _SunsetGlintColor;
            float _SunsetGlintStrength;
            float _ShipReflectionStrength;
            float _PixelWaveLightContrast;
            half4 _ContactFoamColor;
            float _ContactFoamWidth, _ContactFoamStrength;
            CBUFFER_END
            float _OceanFoamSourceCount;
            float4 _OceanFoamHulls[8];    // local XZ center, half width, half length
            float4 _OceanFoamHullAxes[8]; // forward XZ, waterline height, signed strength
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_OceanShipReflectionTexture);
            SAMPLER(sampler_OceanShipReflectionTexture);
            float4x4 _OceanShipReflectionVP;
            float _OceanShipReflectionReady;
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                half3 normalWS:TEXCOORD1;
                float3 positionOS:TEXCOORD2;
                half fog:TEXCOORD3;
                half3 vertexLighting:TEXCOORD4;
                float2 uv:TEXCOORD5;
                float2 restXZ:TEXCOORD6;
            };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 tangentX, tangentZ;
                float3 displacement = OceanDisplace(input.positionOS.xz + _OceanPatchOffset.xy, tangentX, tangentZ);
                float3 positionOS = input.positionOS.xyz + displacement;
                float3 normalOS = normalize(cross(tangentZ, tangentX));
                VertexPositionInputs p = GetVertexPositionInputs(positionOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.positionOS = positionOS;
                o.normalWS = TransformObjectToWorldNormal(normalOS);
                o.restXZ = input.uv + _OceanPatchOffset.xy;
                o.uv = o.restXZ * _BaseMap_ST.xy + _BaseMap_ST.zw;
                o.fog = ComputeFogFactor(p.positionCS.z);
                o.vertexLighting = VertexLighting(p.positionWS, o.normalWS);
                return o;
            }
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453);
            }
            float Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1,0)), f.x),
                    lerp(Hash(cell + float2(0,1)), Hash(cell + 1), f.x), f.y);
            }
            float WaterHash(float2 p)
            {
                float3 h = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
                h += dot(h, h.yzx + 33.33);
                return frac((h.x + h.y) * h.z);
            }
            float WaterNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 blend = frac(p);
                blend = blend * blend * (3.0 - 2.0 * blend);
                float lower = lerp(WaterHash(cell), WaterHash(cell + float2(1, 0)), blend.x);
                float upper = lerp(WaterHash(cell + float2(0, 1)), WaterHash(cell + 1.0), blend.x);
                return lerp(lower, upper, blend.y);
            }
            half3 PixelWaterAlbedo(float2 restXZ, float waveHeight, float time, float pixelFootprint,
                float sunsetBand)
            {
                // Smooth color and stepped ripples share the same undeformed ocean coordinates.
                float broad = WaterNoise(restXZ * 0.23 + float2(-0.09, 0.035) * time);
                float patches = WaterNoise(restXZ * 0.58 + float2(17.2, -8.9) + float2(0.04, 0.08) * time);
                float tint = smoothstep(0.20, 0.78, lerp(broad, patches, 0.32) + waveHeight * 0.08);
                half3 water = lerp(_PixelDeepColor.rgb, _PixelLightColor.rgb, tint);

                float cellSize = max(0.025, _PixelCellSize);
                float2 pixelXZ = (floor(restXZ / cellSize) + 0.5) * cellSize;
                float2 alongAcross = float2(dot(pixelXZ, float2(0.985, 0.174)),
                                           dot(pixelXZ, float2(-0.174, 0.985)));
                float2 rippleUV = alongAcross * float2(1.0, 4.2) + float2(31.9, 12.4);
                rippleUV += float2(-0.33, 0.12) * time;
                float ribbon = smoothstep(0.88, 0.97, WaterNoise(rippleUV));
                float distanceFade = 1.0 - smoothstep(0.65, 2.1, pixelFootprint / cellSize);
                half coverage = ribbon * distanceFade * _PixelRippleStrength * lerp(0.55, 1.0, tint);
                // Give the sun's reflection priority while keeping short blue ripples in darker water.
                coverage *= 1.0 - 0.94 * smoothstep(0.10, 0.68, sunsetBand);
                return lerp(water, _PixelRippleColor.rgb, coverage);
            }
            struct WaterReflection
            {
                float3 normal;
                float skyElevation;
                float grazing;
                float alignment;
                float directLight;
                float lowSun;
                float sunsetBand;
            };
            WaterReflection EvaluateWaterReflection(half3 normalWS, half3 viewWS, float2 restXZ,
                float time, float pixelFootprint, Light sun)
            {
                float rippleFade = 1.0 - smoothstep(0.12, 0.44, pixelFootprint);
                float3 normal = normalWS;
                [branch]
                if (_PixelReflectionRipple * rippleFade > 0.001)
                {
                    // Different directions, scales and offsets prevent the two normal channels from forming a grid.
                    float2 flowA = float2(dot(restXZ, float2(1.62, 0.91)), dot(restXZ, float2(-1.14, 1.88)));
                    float2 flowB = float2(dot(restXZ, float2(0.88, -2.06)), dot(restXZ, float2(1.71, 1.09)));
                    float2 ripple = float2(WaterNoise(flowA + float2(0.17, -0.22) * time),
                        WaterNoise(flowB + float2(19.73, -8.12) + float2(-0.25, 0.11) * time)) * 2.0 - 1.0;
                    normal = normalize(normalWS + float3(ripple.x, 0.0, ripple.y) * (_PixelReflectionRipple * rippleFade));
                }
                float3 reflected = reflect(-viewWS, normal);
                WaterReflection result;
                result.normal = normal;
                result.skyElevation = saturate(reflected.y);
                result.grazing = 1.0 - saturate(dot(normal, viewWS));
                result.alignment = saturate(dot(reflected, sun.direction));
                result.directLight = saturate(dot(normal, sun.direction) * 4.0) *
                    sun.distanceAttenuation * sun.shadowAttenuation;
                result.lowSun = smoothstep(0.005, 0.07, sun.direction.y) *
                    (1.0 - smoothstep(0.38, 0.68, sun.direction.y));
                result.sunsetBand = smoothstep(0.86, 0.995, result.alignment) * result.directLight * result.lowSun;
                return result;
            }
            half3 PixelWaterReflection(half3 bodyColor, float3 positionWS, float2 restXZ,
                float time, float pixelFootprint, Light sun, WaterReflection reflection)
            {
                half3 sky = lerp(_PixelSkyHorizon.rgb, _PixelSkyZenith.rgb, reflection.skyElevation);
                float grazing = reflection.grazing;
                float fresnel = (0.045 + 0.955 * grazing * grazing * grazing) * _PixelSkyReflection;
                half3 color = lerp(bodyColor, sky, fresnel);
                [branch]
                if (_OceanShipReflectionReady > 0.5 && _ShipReflectionStrength > 0.0 && _TableClip < 0.5)
                {
                    float3 distorted = positionWS + float3(reflection.normal.x, 0, reflection.normal.z) * 0.65;
                    float4 reflectionClip = mul(_OceanShipReflectionVP, float4(distorted, 1));
                    float2 reflectionUV = reflectionClip.xy / reflectionClip.w * 0.5 + 0.5;
                    #if UNITY_UV_STARTS_AT_TOP
                        reflectionUV.y = 1.0 - reflectionUV.y;
                    #endif
                    half4 ship = SAMPLE_TEXTURE2D(_OceanShipReflectionTexture, sampler_OceanShipReflectionTexture, reflectionUV);
                    float reflectionWeight = _ShipReflectionStrength * (0.45 + 0.45 * grazing);
                    color = lerp(color, ship.rgb, ship.a * reflectionWeight);
                }

                // The specular lobe tracks the actual main light, including scene shadows.
                float alignment = reflection.alignment;
                float glint = 0.28 * pow(alignment, 24.0) + 0.72 * pow(alignment, 110.0);
                float sunset = reflection.lowSun * saturate(_SunsetGlintStrength);
                half3 highlight = sun.color * (glint * reflection.directLight * _PixelSunGlint * (1.0 - 0.65 * sunset));
                [branch]
                if (_SunsetGlintStrength > 0.0 && reflection.sunsetBand > 0.001)
                {
                    // Keep glitter inside the light's reflection lobe and stretch the fragments across it.
                    float2 sunXZ = sun.direction.xz * rsqrt(max(dot(sun.direction.xz, sun.direction.xz), 0.001));
                    float2 across = float2(sunXZ.y, -sunXZ.x);
                    float2 sparkleUV = float2(dot(restXZ, across) * 1.55, dot(restXZ, sunXZ) * 4.45);
                    float flicker = WaterNoise(sparkleUV + float2(0.88, -1.24) * time);
                    float fragments = smoothstep(0.84, 0.95, flicker);
                    float pinpoints = smoothstep(0.93, 0.99, flicker);
                    float sparkleFade = 1.0 - smoothstep(0.13, 0.48, pixelFootprint);
                    float core = smoothstep(0.86, 0.995, alignment);
                    float glitter = reflection.sunsetBand * (0.37 * fragments + 1.15 * pinpoints * core) * sparkleFade;
                    highlight += _SunsetGlintColor.rgb * sun.color * (glitter * _SunsetGlintStrength);
                }
                return color + highlight;
            }
            half3 CompositeUnderwater(half3 waterColor, float3 waterPositionWS, float2 screenUV)
            {
                float rawDepth = SampleSceneDepth(screenUV);
                #if UNITY_REVERSED_Z
                    if (rawDepth < 0.000001) return waterColor;
                #else
                    if (rawDepth > 0.999999) return waterColor;
                #endif
                float backgroundEyeDepth = unity_OrthoParams.w > 0.5
                    ? LinearDepthToEyeDepth(rawDepth) : LinearEyeDepth(rawDepth, _ZBufferParams);
                float surfaceEyeDepth = -TransformWorldToView(waterPositionWS).z;
                float thickness = backgroundEyeDepth - surfaceEyeDepth;
                if (thickness <= 0.001 || thickness > _WaterTransmissionDepth * 8.0) return waterColor;

                // The opaque color buffer contains submerged geometry before the water is drawn.
                float visibility = _WaterTransmission * exp2(-thickness / _WaterTransmissionDepth);
                half3 submerged = SampleSceneColor(screenUV) * _WaterTransmissionTint.rgb;
                return lerp(waterColor, submerged, visibility);
            }
            half HullContactFoam(float3 positionOS, float time, float4 hull, float4 axes)
            {
                float2 delta = positionOS.xz - hull.xy;
                float2 forward = axes.xy;
                float2 across = float2(forward.y, -forward.x);
                float2 hullXZ = float2(dot(delta, across), dot(delta, forward));
                float2 halfSize = max(hull.zw, 0.05);
                float waterlineDelta = positionOS.y - axes.z;
                if (any(abs(hullXZ) > halfSize + _ContactFoamWidth * 1.6) ||
                    waterlineDelta < -0.26 || waterlineDelta > 0.75)
                    return 0.0h;

                float edgeDistance;
                if (axes.w < 0.0)
                {
                    float corner = min(halfSize.x, halfSize.y) * 0.25;
                    float2 q = abs(hullXZ) - halfSize + corner;
                    edgeDistance = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - corner;
                }
                else
                {
                    float2 scaled = hullXZ / halfSize;
                    float radius = length(scaled);
                    if (radius < 0.72) return 0.0h;
                    edgeDistance = radius * (radius - 1.0) / max(length(scaled / halfSize), 0.001);
                }
                if (edgeDistance < -0.26 || edgeDistance > _ContactFoamWidth * 1.45)
                    return 0.0h;

                float breakup = WaterNoise(hullXZ * 7.0 + float2(-0.7, 1.15) * time);
                float outerEdge = _ContactFoamWidth * lerp(0.52, 1.28, breakup);
                float band = smoothstep(-0.26, -0.04, edgeDistance) *
                    (1.0 - smoothstep(outerEdge * 0.42, outerEdge, edgeDistance));
                float patches = smoothstep(0.24, 0.60, breakup);
                float immersion = smoothstep(-0.26, -0.06, waterlineDelta);
                return band * patches * immersion * abs(axes.w);
            }
            half OceanContactFoam(float3 positionOS, float time)
            {
                if (_ContactFoamStrength <= 0.0) return 0.0h;
                half coverage = 0.0h;
                [loop]
                for (int i = 0; i < (int)_OceanFoamSourceCount; i++)
                    coverage = max(coverage, HullContactFoam(positionOS, time, _OceanFoamHulls[i], _OceanFoamHullAxes[i]));
                return coverage * _ContactFoamStrength;
            }
            half4 Frag(Varyings i):SV_Target
            {
                if (_TableClip > 0.5) ApplyOceanRegionClip(i.positionWS);
                half3 n = normalize(i.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half3 albedo;
                half foam = 0.0h;
                float2 footprint = fwidth(i.restXZ);
                float pixelFootprint = max(footprint.x, footprint.y);
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light sun = (Light)0;
                WaterReflection reflection = (WaterReflection)0;
                [branch]
                if (_ProceduralWater > 0.5)
                {
                    sun = GetMainLight(shadowCoord);
                    reflection = EvaluateWaterReflection(n, view, i.restXZ, _Time.y, pixelFootprint, sun);
                    albedo = PixelWaterAlbedo(i.restXZ, i.positionOS.y, _Time.y, pixelFootprint,
                        reflection.sunsetBand * saturate(_SunsetGlintStrength));
                }
                else if (_UseBaseMap > 0.5)
                    albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                else
                {
                    float2 p = i.positionOS.xz + _OceanPatchOffset.xy;
                    float t = _Time.y;
                    half2 ripple = half2(
                        sin(dot(p, float2(1.91, 0.73)) + t * 0.9) + 0.45 * sin(dot(p, float2(-2.3, 3.1)) - t * 1.2),
                        cos(dot(p, float2(0.63, 2.17)) - t * 0.75) + 0.4 * cos(dot(p, float2(3.3, -1.7)) + t));
                    n = normalize(n + half3(ripple.x, 0, ripple.y) * _RippleStrength);
                    half fresnel = pow(1.0h - saturate(dot(n, view)), 4.0h);
                    half height = saturate(i.positionOS.y / max(0.01, _CrestHeight) * 0.5 + 0.5);
                    half variation = sin(dot(p, float2(0.22, 0.31))) * 0.035h;
                    albedo = lerp(_BaseColor.rgb, _CrestColor.rgb, saturate(height * 0.65h + fresnel * 0.35h + variation));
                    half breakup = Noise(p * 0.7 + float2(t * 0.06, -t * 0.04));
                    foam = smoothstep(0.8h, 0.98h, height) * smoothstep(0.65h, 0.88h, breakup) * _FoamAmount;
                    albedo = lerp(albedo, _FoamColor.rgb, foam);
                }
                InputData data = (InputData)0;
                data.positionWS = i.positionWS;
                data.positionCS = i.positionCS;
                data.normalWS = n;
                data.viewDirectionWS = view;
                data.shadowCoord = shadowCoord;
                data.fogCoord = i.fog;
                data.vertexLighting = i.vertexLighting;
                data.bakedGI = SampleSH(n);
                data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                data.shadowMask = half4(1,1,1,1);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                // A flat fill keeps the stylized palette readable in the room projection's light.
                surface.emission = _ProceduralWater > 0.5 ? albedo * _PixelFill : 0.0h;
                surface.specular = _SpecColor.rgb;
                surface.smoothness = lerp(_Smoothness, 0.45h, foam);
                surface.occlusion = 1;
                surface.alpha = 1;
                half4 color = UniversalFragmentPBR(data, surface);
                [branch]
                if (_ProceduralWater > 0.5)
                {
                    // Use level water as the reference so overall sea brightness does not follow the sun angle.
                    float facing = saturate(dot(n, sun.direction)) - saturate(sun.direction.y);
                    float sunStrength = saturate(dot(sun.color, float3(0.2126, 0.7152, 0.0722))) *
                        sun.distanceAttenuation * sun.shadowAttenuation;
                    float waveShade = 1.0 + clamp(facing * 2.1, -0.52, 0.32) * _PixelWaveLightContrast * sunStrength;
                    half3 bodyColor = color.rgb * waveShade;
                    [branch]
                    if (_WaterTransmission > 0.0)
                        bodyColor = CompositeUnderwater(bodyColor, i.positionWS, data.normalizedScreenSpaceUV);
                    color.rgb = PixelWaterReflection(bodyColor, i.positionWS, i.restXZ, _Time.y,
                        pixelFootprint, sun, reflection);
                }
                half contactFoam = OceanContactFoam(i.positionOS, _Time.y);
                if (contactFoam > 0.0h)
                {
                    if (_ProceduralWater <= 0.5) sun = GetMainLight(shadowCoord);
                    half foamLight = lerp(0.78h, 1.0h, sun.shadowAttenuation * saturate(dot(n, sun.direction)));
                    color.rgb = lerp(color.rgb, _ContactFoamColor.rgb * foamLight, contactFoam);
                }
                color.rgb = MixFog(color.rgb, i.fog);
                return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            #include "Assets/Shaders/Ocean/OceanGpuWaves.hlsl"
            #include "Assets/Shaders/Ocean/RegionClip.hlsl"
            float _TableClip;
            struct NormalVaryings
            {
                float4 positionCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                half3 normalWS:TEXCOORD1;
            };
            NormalVaryings DepthNormalsVert(float3 restPositionOS:POSITION)
            {
                NormalVaryings output;
                float3 tangentX, tangentZ;
                float3 positionOS = restPositionOS +
                    OceanDisplace(restPositionOS.xz + _OceanPatchOffset.xy, tangentX, tangentZ);
                output.positionWS = TransformObjectToWorld(positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(cross(tangentZ, tangentX));
                return output;
            }
            half4 DepthNormalsFrag(NormalVaryings input):SV_Target
            {
                if (_TableClip > 0.5) ApplyOceanRegionClip(input.positionWS);
                float3 normalWS = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(normalWS);
                    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
                #else
                    return half4(normalWS, 0);
                #endif
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Ocean/OceanGpuWaves.hlsl"
            #include "Assets/Shaders/Ocean/RegionClip.hlsl"
            float _TableClip;
            struct DepthAttributes { float4 positionOS:POSITION; };
            struct DepthVaryings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; };
            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output;
                float3 tangentX, tangentZ;
                float3 positionOS = input.positionOS.xyz +
                    OceanDisplace(input.positionOS.xz + _OceanPatchOffset.xy, tangentX, tangentZ);
                output.positionCS = TransformObjectToHClip(positionOS);
                output.positionWS = TransformObjectToWorld(positionOS);
                return output;
            }
            half4 DepthFrag(DepthVaryings input):SV_Target
            {
                if (_TableClip > 0.5) ApplyOceanRegionClip(input.positionWS);
                return 0;
            }
            ENDHLSL
        }
    }
}
