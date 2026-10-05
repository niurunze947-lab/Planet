Shader "Planet/StellarDistant"
{
    Properties { [HDR] _Radiance ("Uniform Stellar Radiance", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Radiance;
            CBUFFER_END
            float4 Vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            float4 Frag() : SV_Target { return float4(_Radiance.rgb,1); }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Unlit/DepthOnly"
    }
}
