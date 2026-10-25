Shader "WhiteWhale/Interaction/MeshOutline"
{
    Properties
    {
        [HDR] _OutlineColor ("Outline Color", Color) = (2, 1.5, 0.3, 1)
        _OutlineWidth ("Outline Width (Metres)", Range(0.001, 0.05)) = 0.015
        [HideInInspector] _MeshCenter ("Mesh Center", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float4 _MeshCenter;
                float _OutlineWidth;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                float3 direction = mul((float3x3)unity_ObjectToWorld, input.positionOS - _MeshCenter.xyz);
                positionWS += direction * rsqrt(max(dot(direction, direction), 0.000001)) * _OutlineWidth;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }
    }
}
