Shader "Planet/ProceduralSurface"
{
    Properties
    {
        _AlbedoCube ("Procedural Albedo", Cube) = "white" {}
        _CloudDensityCube ("Cloud Density", Cube) = "black" {}
        _SeaLevel ("Sea Level (m)", Float) = 0
        _OceanEnabled ("Ocean Enabled", Float) = 1
        _CloudShadowEnabled ("Cloud Shadows Enabled", Float) = 1
        _WaterCoverage ("Water Coverage", Range(0, 1)) = 0.65
        _CloudShadowStrength ("Cloud Shadow Strength", Range(0, 1)) = 0.32
        _CloudShadowOffset ("Cloud Shadow Offset", Range(0, 0.2)) = 0.035
        _OceanTint ("Ocean Tint", Color) = (0.82, 0.94, 1.0, 1)
        _OceanSmoothness ("Ocean Smoothness", Range(0, 1)) = 0.42
        _LandSmoothness ("Land Smoothness", Range(0, 1)) = 0.12
        _NightAmbient ("Night Ambient", Range(0, 0.2)) = 0.028
        _RimColor ("Atmosphere Rim", Color) = (0.16, 0.42, 0.95, 1)
        _RimStrength ("Atmosphere Rim Strength", Range(0, 2)) = 0.42
        _RimPower ("Atmosphere Rim Power", Range(0.5, 8)) = 2.7
        _ReferenceAxes ("Reference Shape Axes", Vector) = (1, 1, 1, 0)
        _EmissionStrength ("Photospheric Emission", Float) = 0
        _LimbDarkening ("Photospheric Limb Darkening", Range(0, 1)) = 0
        _RingProfile ("Ring Optical Profile", 2D) = "black" {}
        _RingOuterRatio ("Ring Outer Radius / Body Radius", Float) = 0
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURECUBE(_AlbedoCube); SAMPLER(sampler_AlbedoCube);
            TEXTURECUBE(_CloudDensityCube); SAMPLER(sampler_CloudDensityCube);
            TEXTURE2D(_RingProfile); SAMPLER(sampler_RingProfile);
            float _PlanetGridStrength;

            CBUFFER_START(UnityPerMaterial)
                half4 _OceanTint;
                half4 _RimColor;
                half _SeaLevel;
                half _OceanEnabled;
                half _CloudShadowEnabled;
                half _WaterCoverage;
                half _CloudShadowStrength;
                half _CloudShadowOffset;
                half _OceanSmoothness;
                half _LandSmoothness;
                half _NightAmbient;
                half _RimStrength;
                half _RimPower;
                float4 _ReferenceAxes;
                half _EmissionStrength;
                half _LimbDarkening;
                float _RingOuterRatio;
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
                float3 shapeNormal = normalize(output.directionOS / (_ReferenceAxes.xyz * _ReferenceAxes.xyz));
                output.sphereNormalWS = normalize(TransformObjectToWorldNormal(shapeNormal));
                output.shadowCoord = GetShadowCoord(positionInputs);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 directionOS = normalize(input.directionOS);
                half3 radialNormal = normalize(input.sphereNormalWS);
                half3 terrainNormal = normalize(input.normalWS);
                half height = input.surfaceData.x;
                half slope = saturate(input.surfaceData.y / 42.0h);
                // The mesh carries the smooth, derived liquid coverage in TEXCOORD2.z. It is
                // computed from sea level minus terrain height, so it remains correct when the
                // terrain range or water inventory changes.
                half ocean = saturate(input.surfaceData.z);
                ocean *= _OceanEnabled;
                half3 normal = normalize(lerp(terrainNormal, radialNormal, ocean));
                half3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                // Planetary bodies are far outside the useful range of URP shadow cascades in
                // this solar-system scene. Sampling that shadow map caused the visible flickering
                // half-disc and a moving cascade seam. Use the stable directional light term and
                // keep cloud shadows in the procedural cubemap below.
                Light mainLight = GetMainLight();
                half3 lightDirection = normalize(mainLight.direction);
                half ndotl = dot(normal, lightDirection);
                half radialLight = dot(radialNormal, lightDirection);
                // Use the spheroid normal for the day/night transition. Terrain normals still
                // shape the lit side, but cannot turn the global terminator into a hard straight
                // shadow boundary or amplify a Cube Sphere face seam.
                half twilight = smoothstep(-0.32h, 0.30h, radialLight);
                half reliefDiffuse = saturate(ndotl);
                half direct = twilight * (0.16h + 0.84h * reliefDiffuse);
                half sunFacing = saturate(dot(radialNormal, lightDirection));
                half3 albedo = SAMPLE_TEXTURECUBE(_AlbedoCube, sampler_AlbedoCube, directionOS).rgb;
                // Optional, antialiased angular reference grid, controlled by the existing L key.
                float2 angles = float2(atan2(directionOS.z, directionOS.x), asin(clamp(directionOS.y, -1, 1)));
                float2 gridCoord = angles * (12.0 / PI);
                float2 gridDistance = abs(frac(gridCoord + 0.5) - 0.5);
                float2 gridWidth = max(fwidth(gridCoord), 0.0001);
                half grid = 1.0h - min(smoothstep(gridWidth.x * 0.5, gridWidth.x * 1.5, gridDistance.x),
                    smoothstep(gridWidth.y * 0.5, gridWidth.y * 1.5, gridDistance.y));
                albedo *= 1.0h - grid * _PlanetGridStrength;
                if (_EmissionStrength > 0.0h)
                {
                    half mu = saturate(dot(radialNormal, viewDirection));
                    return half4(albedo * _EmissionStrength * (1.0h - _LimbDarkening * (1.0h - mu)), 1.0h);
                }
                albedo = lerp(albedo, albedo * _OceanTint.rgb, ocean * 0.38h);
                // Steep terrain loses direct sky fill and appears more rock-like. This gives
                // mountains readable form even where the albedo palette is deliberately broad.
                albedo *= lerp(1.0h, 0.72h, slope * (1.0h - ocean));

                half cloudDensity = 0.0h;
                if (_CloudShadowEnabled > 0.5h)
                {
                    float3 lightDirectionOS = normalize(TransformWorldToObjectDir(lightDirection));
                    float3 cloudDirection = normalize(directionOS + lightDirectionOS * _CloudShadowOffset);
                    cloudDensity = SAMPLE_TEXTURECUBE(_CloudDensityCube, sampler_CloudDensityCube, cloudDirection).r;
                }
                half cloudShadow = 1.0h - cloudDensity * _CloudShadowStrength * saturate(twilight);
                if (_RingOuterRatio > 1.0)
                {
                    // Ray/plane intersection in radius-normalized body space; independent of floating origin.
                    float3 localLight = normalize(TransformWorldToObjectDir(lightDirection));
                    if (abs(localLight.y) > 0.0001)
                    {
                        float referenceRadius = rsqrt(dot(directionOS / _ReferenceAxes.xyz, directionOS / _ReferenceAxes.xyz));
                        float3 localPoint = directionOS * referenceRadius;
                        float distanceToRing = -localPoint.y / localLight.y;
                        float ringRadius = length((localPoint + distanceToRing * localLight).xz) / _RingOuterRatio;
                        if (distanceToRing > 0.0 && ringRadius <= 1.0)
                        {
                            half opacity = SAMPLE_TEXTURE2D(_RingProfile, sampler_RingProfile, float2(ringRadius, 0.5)).a;
                            cloudShadow *= 1.0h - opacity * 0.82h;
                        }
                    }
                }
                half3 color = albedo * (_NightAmbient + direct * mainLight.color * cloudShadow);
                half smoothness = lerp(_LandSmoothness, _OceanSmoothness, ocean);
                half3 halfVector = normalize(lightDirection + viewDirection);
                // Ocean highlights stay wide at planetary scale. The old exponent of 128 made
                // the cubemap read as sharp, tiled wave normals from orbit.
                half specular = pow(saturate(dot(normal, halfVector)), lerp(7.0h, 30.0h, smoothness));
                specular *= lerp(0.018h, 0.20h, ocean) * sunFacing * saturate(twilight) *
                    cloudShadow;
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
