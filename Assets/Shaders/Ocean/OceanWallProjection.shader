Shader "WhiteWhale/Ocean Wall Projection"
{
    Properties
    {
        [MainTexture] _MainTex("Ocean Feed", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 edge = min(uv, 1.0 - uv);
                half footprint = smoothstep(0.009, 0.036, edge.x) * smoothstep(0.016, 0.064, edge.y);
                float2 center = (uv - 0.5) * float2(1.35, 1.0);
                half exposure = 1.0 - 0.34 * dot(center, center);

                float2 blur = _MainTex_TexelSize.xy * 1.25;
                half3 feed = 0.60 * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                feed += 0.10 * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(blur.x, 0)).rgb;
                feed += 0.10 * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(blur.x, 0)).rgb;
                feed += 0.10 * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, blur.y)).rgb;
                feed += 0.10 * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(0, blur.y)).rgb;

                half3 projected = feed * half3(1.06, 1.09, 1.15) + half3(0.032, 0.036, 0.043);
                half imageAlpha = 0.87 * footprint * exposure;
                half edgeLight = 0.18 * (1.0 - footprint)
                    * smoothstep(0.0, 0.023, edge.x) * smoothstep(0.0, 0.041, edge.y);
                half alpha = imageAlpha + edgeLight;
                half3 color = (projected * imageAlpha + half3(0.38, 0.48, 0.61) * edgeLight)
                    / max(alpha, 0.001);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
