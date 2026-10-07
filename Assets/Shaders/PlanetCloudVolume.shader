Shader "Planet/CloudVolume"
{
    Properties
    {
        _DensityCube ("Cloud Density", Cube) = "black" {}
        _InnerRadius ("Inner Radius", Range(0.8, 1)) = 0.975
        _Density ("Density", Range(0, 2)) = 0.72
        _SilverLining ("Silver Lining", Range(0, 2)) = 0.42
        _ShadowStrength ("Shadow Strength", Range(0, 2)) = 0.5
        _SunDirection ("Sun Direction", Vector) = (0.45, -0.3, 0.84, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-20" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            Name "CloudVolume"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURECUBE(_DensityCube); SAMPLER(sampler_DensityCube);
            CBUFFER_START(UnityPerMaterial)
                half _InnerRadius;
                half _Density;
                half _SilverLining;
                half _ShadowStrength;
                float4 _SunDirection;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 directionOS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS);
                output.positionCS = p.positionCS;
                output.directionOS = normalize(input.positionOS.xyz);
                output.normalWS = n.normalWS;
                output.positionWS = p.positionWS;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half3 direction = normalize(input.directionOS);
                half density = SAMPLE_TEXTURECUBE(_DensityCube, sampler_DensityCube, direction).r * _Density;
                half3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                half3 sunDirection = normalize(_SunDirection.xyz);
                half sunAmount = saturate(dot(normalize(input.normalWS), sunDirection));
                half forward = pow(saturate(dot(viewDirection, sunDirection)), 8.0h);
                half silver = (0.35h + 0.65h * sunAmount) * _SilverLining * forward;
                half horizon = pow(saturate(1.0h - dot(normalize(input.normalWS), viewDirection)), 1.4h);
                // Approximate optical path through the shell. The path becomes longer toward the
                // limb, which gives the cloud layer visible thickness instead of a flat decal.
                half pathLength = lerp(0.55h, 1.75h, horizon);
                half alpha = saturate(density * pathLength * (0.30h + 0.58h * sunAmount + 0.22h * horizon));
                half3 color = lerp(half3(0.38h, 0.42h, 0.48h), half3(1, 0.97h, 0.90h), sunAmount * 0.68h + silver);
                return half4(color + silver, alpha);
            }
            ENDHLSL
        }

    }
}
