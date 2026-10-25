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
        [HideInInspector] _BaseMap("Base Map", 2D) = "white" {}
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
            #include "Assets/Shaders/Ocean/RegionClip.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _CrestColor, _FoamColor, _SpecColor;
            float _Smoothness, _CrestHeight, _FoamAmount, _RippleStrength, _TableClip;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                half3 normalWS:TEXCOORD1;
                float3 positionOS:TEXCOORD2;
                half fog:TEXCOORD3;
                half3 vertexLighting:TEXCOORD4;
            };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.positionOS = input.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
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
            half4 Frag(Varyings i):SV_Target
            {
                if (_TableClip > 0.5) ApplyOceanRegionClip(i.positionWS);
                float2 p = i.positionOS.xz;
                float t = _Time.y;
                half2 ripple = half2(
                    sin(dot(p, float2(1.91, 0.73)) + t * 0.9) + 0.45 * sin(dot(p, float2(-2.3, 3.1)) - t * 1.2),
                    cos(dot(p, float2(0.63, 2.17)) - t * 0.75) + 0.4 * cos(dot(p, float2(3.3, -1.7)) + t));
                half3 n = normalize(i.normalWS + half3(ripple.x, 0, ripple.y) * _RippleStrength);
                half3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half fresnel = pow(1.0h - saturate(dot(n, view)), 4.0h);
                half height = saturate(i.positionOS.y / max(0.01, _CrestHeight) * 0.5 + 0.5);
                half variation = sin(dot(p, float2(0.22, 0.31))) * 0.035h;
                half3 albedo = lerp(_BaseColor.rgb, _CrestColor.rgb, saturate(height * 0.65h + fresnel * 0.35h + variation));
                half breakup = Noise(p * 0.7 + float2(t * 0.06, -t * 0.04));
                half foam = smoothstep(0.8h, 0.98h, height) * smoothstep(0.65h, 0.88h, breakup) * _FoamAmount;
                albedo = lerp(albedo, _FoamColor.rgb, foam);
                InputData data = (InputData)0;
                data.positionWS = i.positionWS;
                data.positionCS = i.positionCS;
                data.normalWS = n;
                data.viewDirectionWS = view;
                data.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                data.fogCoord = i.fog;
                data.vertexLighting = i.vertexLighting;
                data.bakedGI = SampleSH(n);
                data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                data.shadowMask = half4(1,1,1,1);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.specular = _SpecColor.rgb;
                surface.smoothness = lerp(_Smoothness, 0.45h, foam);
                surface.occlusion = 1;
                surface.alpha = 1;
                half4 color = UniversalFragmentPBR(data, surface);
                color.rgb = MixFog(color.rgb, i.fog);
                return color;
            }
            ENDHLSL
        }
        UsePass "WhiteWhale/Clipped Lit/ShadowCaster"
        UsePass "WhiteWhale/Clipped Lit/DepthOnly"
        UsePass "WhiteWhale/Clipped Lit/DepthNormals"
    }
}
