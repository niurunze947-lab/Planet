Shader "Planet/ProceduralSurface"
{
    Properties
    {
        _AlbedoCube ("Procedural Albedo", Cube) = "white" {}
        _SeaLevel ("Sea Level (m)", Float) = 0
        _OceanDepth ("Ocean Depth (m)", Float) = 7200
        _OceanTint ("Ocean Tint", Color) = (0.82, 0.94, 1.0, 1)
        _OceanSmoothness ("Ocean Smoothness", Range(0, 1)) = 0.92
        _LandSmoothness ("Land Smoothness", Range(0, 1)) = 0.18
        _NightAmbient ("Night Ambient", Range(0, 0.2)) = 0.012
        _RimColor ("Atmosphere Rim", Color) = (0.16, 0.42, 0.95, 1)
        _RimStrength ("Atmosphere Rim Strength", Range(0, 2)) = 0.42
        _RimPower ("Atmosphere Rim Power", Range(0.5, 8)) = 2.7
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 350

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURECUBE(_AlbedoCube); SAMPLER(sampler_AlbedoCube);

            CBUFFER_START(UnityPerMaterial)
                half4 _OceanTint;
                half4 _RimColor;
                half _SeaLevel;
                half _OceanDepth;
                half _OceanSmoothness;
                half _LandSmoothness;
                half _NightAmbient;
                half _RimStrength;
                half _RimPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float3 directionOS : TEXCOORD1;
                float4 surfaceData : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 directionOS : TEXCOORD2;
                float4 surfaceData : TEXCOORD3;
                float3 sphereNormalWS : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.directionOS = normalize(input.directionOS);
                output.surfaceData = input.surfaceData;
                output.sphereNormalWS = normalize(TransformObjectToWorldNormal(output.directionOS));
                output.shadowCoord = GetShadowCoord(positionInputs);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 directionOS = normalize(input.directionOS);
                half3 radialNormal = normalize(input.sphereNormalWS);
                half3 terrainNormal = normalize(input.normalWS);
                half height = input.surfaceData.x;
                half ocean = saturate((-height + _SeaLevel) / max(_OceanDepth, 1.0h));
                half3 normal = normalize(lerp(terrainNormal, radialNormal, ocean));
                half3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                Light mainLight = GetMainLight(input.shadowCoord);
                half3 lightDirection = normalize(mainLight.direction);
                half ndotl = dot(normal, lightDirection);
                half wrappedDiffuse = saturate((ndotl + 0.10h) / 1.10h);
                half sunFacing = saturate(dot(radialNormal, lightDirection));
                half3 albedo = SAMPLE_TEXTURECUBE(_AlbedoCube, sampler_AlbedoCube, directionOS).rgb;
                albedo = lerp(albedo, albedo * _OceanTint.rgb, ocean * 0.38h);

                half3 color = albedo * (_NightAmbient + wrappedDiffuse * mainLight.color * mainLight.shadowAttenuation);
                half smoothness = lerp(_LandSmoothness, _OceanSmoothness, ocean);
                half3 halfVector = normalize(lightDirection + viewDirection);
                half specular = pow(saturate(dot(normal, halfVector)), lerp(24.0h, 128.0h, smoothness));
                specular *= lerp(0.025h, 0.70h, ocean) * sunFacing * mainLight.shadowAttenuation;
                color += specular * mainLight.color;

                half horizon = pow(saturate(1.0h - dot(radialNormal, viewDirection)), _RimPower);
                half limbLight = lerp(0.18h, 1.0h, sunFacing);
                color += horizon * _RimColor.rgb * (_RimStrength * limbLight);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
