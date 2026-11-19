Shader "Hidden/WhiteWhale/Ocean Ship Reflection Capture"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Front
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_ReflectionBaseMap);
            SAMPLER(sampler_ReflectionBaseMap);
            float4 _ReflectionBaseMap_ST;
            half4 _ReflectionBaseColor;
            float4x4 _OceanShipReflectionVP;
            float _OceanReflectionWaterY;
            half3 _OceanReflectionLightDirection;
            half3 _OceanReflectionLightColor;
            struct Attributes { float3 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half3 normalWS:TEXCOORD1; float height:TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS);
                output.height = world.y - _OceanReflectionWaterY;
                world.y -= 2.0 * output.height;
                output.positionCS = mul(_OceanShipReflectionVP, float4(world, 1));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv * _ReflectionBaseMap_ST.xy + _ReflectionBaseMap_ST.zw;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                clip(input.height);
                half3 albedo = SAMPLE_TEXTURE2D(_ReflectionBaseMap, sampler_ReflectionBaseMap, input.uv).rgb * _ReflectionBaseColor.rgb;
                half facing = saturate(dot(normalize(input.normalWS), _OceanReflectionLightDirection));
                return half4(albedo * (0.35h + 0.55h * _OceanReflectionLightColor * facing), 1);
            }
            ENDHLSL
        }
    }
}
