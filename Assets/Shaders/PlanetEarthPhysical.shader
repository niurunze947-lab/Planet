Shader "Planet/EarthPhysical"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _NormalMap ("Terrain Normal", 2D) = "bump" {}
        _OceanMask ("Ocean Mask (A)", 2D) = "black" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)

        [Header(Land Relief)]
        _LandNormalStrength ("Normal Strength", Range(0, 2)) = 0.75
        _LandAOStrength ("Terrain Micro-Shadow", Range(0, 1)) = 0.35

        [Header(Ocean)]
        _OceanTint ("Deep Water Tint", Color) = (0.55, 0.72, 0.95, 1)
        _OceanRoughness ("Ocean Roughness", Range(0.05, 0.95)) = 0.38
        _OceanF0 ("Ocean Fresnel F0", Range(0.005, 0.12)) = 0.02
        _OceanSpecular ("Ocean Specular", Range(0, 4)) = 1.4
        _GrazingSheen ("Limb Grazing Sheen", Range(0, 2)) = 0.35

        [Header(Aerial Perspective)]
        _AtmosphereTint ("Inscatter Color", Color) = (0.35, 0.55, 0.95, 1)
        _AtmosphereHaze ("Aerial Perspective", Range(0, 1)) = 0.22
        _HazeFalloff ("Haze Falloff", Range(0.5, 6)) = 2.6

        [Header(Ambient)]
        _AmbientStrength ("Night Ambient", Range(0, 1)) = 0.05
        _TerminatorSoftness ("Terminator Softness", Range(0.01, 0.5)) = 0.1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 300

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

            TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_OceanMask); SAMPLER(sampler_OceanMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half4  _OceanTint;
                half4  _AtmosphereTint;
                half   _LandNormalStrength;
                half   _LandAOStrength;
                half   _OceanRoughness;
                half   _OceanF0;
                half   _OceanSpecular;
                half   _GrazingSheen;
                half   _AtmosphereHaze;
                half   _HazeFalloff;
                half   _AmbientStrength;
                half   _TerminatorSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS     : SV_POSITION;
                float3 positionWS     : TEXCOORD0;
                float3 normalWS       : TEXCOORD1;
                float3 tangentWS      : TEXCOORD2;
                float3 bitangentWS    : TEXCOORD3;
                float3 sphereNormalWS : TEXCOORD4;
                float2 uv             : TEXCOORD5;
                float4 shadowCoord    : TEXCOORD6;
            };

            // Broad ocean sun ball for planetary viewing distance.
            // One pixel covers many km of sea: wave facets average out, so this must stay
            // a wide, soft GGX lobe. Fresnel keeps nadir at F0 (~2%) and lifts the sheen
            // only toward the limb. Never apply a wave normal map here.
            float3 OceanSpecular(float NdotL, float NdotV, float NdotH, float VdotH, float roughness, float f0)
            {
                float a  = max(roughness * roughness, 0.0025);
                float a2 = a * a;
                float d  = NdotH * NdotH * (a2 - 1.0) + 1.0;
                float D  = a2 / (PI * d * d + 1e-5);
                float k  = a * 0.5;
                float G  = (NdotL / (NdotL * (1.0 - k) + k)) * (NdotV / (NdotV * (1.0 - k) + k));
                float F  = f0 + (1.0 - f0) * pow(1.0 - VdotH, 5.0);
                return D * G * F * 0.25 / max(NdotV, 1e-3);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS     = positionInputs.positionCS;
                output.positionWS     = positionInputs.positionWS;
                output.normalWS       = normalInputs.normalWS;
                output.tangentWS      = normalInputs.tangentWS;
                output.bitangentWS    = normalInputs.bitangentWS;
                // Sphere normal from the undisplaced direction — stable even with terrain bumps.
                output.sphereNormalWS = normalize(TransformObjectToWorldNormal(normalize(input.positionOS.xyz + 1e-5)));
                output.uv             = TRANSFORM_TEX(input.uv, _BaseMap);
                output.shadowCoord    = GetShadowCoord(positionInputs);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 sphereNormal  = normalize(input.sphereNormalWS);
                half3 terrainNormal = normalize(input.normalWS);
                half3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                Light  sun          = GetMainLight(input.shadowCoord);
                half3  sunDirection = normalize(sun.direction);

                // Soft planetary terminator.
                half sunFacing  = saturate(dot(sphereNormal, sunDirection));
                half directDay  = smoothstep(-_TerminatorSoftness, _TerminatorSoftness, sunFacing);
                half shadowAtt  = sun.shadowAttenuation;

                // Ocean mask: alpha of the derived metallic/smoothness map.
                half ocean = saturate((SAMPLE_TEXTURE2D(_OceanMask, sampler_OceanMask, input.uv).a - 0.12h) / 0.73h);

                // Land: tangent-space relief. Ocean: perfectly smooth sphere (no wave texture).
                half3 tangentNormal = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv), _LandNormalStrength);
                half3 detailNormal = normalize(
                    input.tangentWS * tangentNormal.x +
                    input.bitangentWS * tangentNormal.y +
                    terrainNormal * tangentNormal.z);
                half3 N = normalize(lerp(detailNormal, sphereNormal, ocean));

                half NdotL = saturate(dot(N, sunDirection));
                half NdotV = saturate(dot(N, viewDirection));

                // Slope-based micro-occlusion on land: steep faces facing away from the sun
                // darken. Works in normalized normal space so it cannot blow up like metre-scale
                // height comparison.
                half slopeAO = saturate(0.65h + 0.35h * saturate(dot(terrainNormal, sphereNormal)));
                half landAO = lerp(1.0h, slopeAO * (0.75h + 0.25h * NdotL), (1.0h - ocean) * _LandAOStrength);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                albedo = lerp(albedo, albedo * _OceanTint.rgb, ocean);

                half diffuseTerm = NdotL * directDay * landAO * shadowAtt;
                half3 ambient = _AmbientStrength * (0.35h + 0.65h * sunFacing);
                half3 color = albedo * (ambient + diffuseTerm * sun.color);

                // --- Ocean specular: soft sun ball + Fresnel limb sheen ---
                half3 H = normalize(sunDirection + viewDirection);
                half NdotH = saturate(dot(N, H));
                half VdotH = saturate(dot(viewDirection, H));

                half3 oceanSpec = OceanSpecular(
                    max(NdotL, 1e-3), max(NdotV, 1e-3), NdotH, VdotH,
                    _OceanRoughness, _OceanF0);

                half horizon = 1.0h - saturate(dot(sphereNormal, viewDirection));
                half grazing = horizon * horizon;

                half3 spec = oceanSpec * _OceanSpecular * directDay * shadowAtt * sun.color;
                spec *= ocean;
                spec += ocean * grazing * _GrazingSheen * directDay * sunFacing * sun.color;
                color += spec;

                // --- Aerial perspective: only near the limb, never over the whole disc ---
                half airPath = pow(horizon, _HazeFalloff);
                half sunlit = 0.15h + 0.85h * sunFacing;
                half haze = saturate(airPath * _AtmosphereHaze * sunlit);
                color = lerp(color, _AtmosphereTint.rgb * (0.2h + 0.8h * sunlit), haze);

                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
