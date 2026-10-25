Shader "WhiteWhale/Interaction/SpriteOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HDR] _OutlineColor ("Outline Color", Color) = (2, 1.5, 0.3, 1)
        _OutlineWidth ("Outline Width (Texture Pixels)", Range(1, 6)) = 2
        [HideInInspector] _Color ("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _SpriteUVRect ("Sprite UV Rect", Vector) = (0, 0, 1, 1)
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest LEqual
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
                half4 _OutlineColor;
                half4 _Color;
                float4 _SpriteUVRect;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                SetUpSpriteInstanceProperties();
                float3 position = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                output.positionCS = TransformObjectToHClip(position);
                output.uv = input.uv;
                output.color = input.color * _Color * unity_SpriteColor;
                return output;
            }

            half SampleAlpha(float2 uv)
            {
                float inside = step(_SpriteUVRect.x, uv.x) * step(_SpriteUVRect.y, uv.y)
                    * step(uv.x, _SpriteUVRect.z) * step(uv.y, _SpriteUVRect.w);
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a * inside;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                float2 offset = _MainTex_TexelSize.xy * _OutlineWidth;
                half minimumAlpha = 1;
                [unroll] for (int y = -1; y <= 1; y++)
                {
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        minimumAlpha = min(minimumAlpha, SampleAlpha(input.uv + float2(x, y) * offset));
                    }
                }

                // 沿不透明区域的内侧描边，贴图没有留白时也不会被矩形边界截断。
                half edge = 1 - smoothstep(0.1, 0.6, minimumAlpha);
                color.rgb = lerp(color.rgb, _OutlineColor.rgb, edge * _OutlineColor.a);
                return color;
            }
            ENDHLSL
        }
    }
}
