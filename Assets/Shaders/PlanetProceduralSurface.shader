Shader "Planet/ProceduralSurface"
{
    Properties
    {
        _ChunkEnabled ("Runtime Chunk", Float) = 0
        _ChunkOriginCR ("Camera Relative Chunk Origin", Vector) = (0, 0, 0, 0)
        _UseVertexAlbedo ("Use Local Surface Color", Float) = 0
        _AlbedoCube ("Procedural Albedo", Cube) = "white" {}
        _NormalCube ("Body-space Detail Normal", Cube) = "white" {}
        _NormalEnabled ("Detail Normal Enabled", Float) = 0
        _CloudDensityCube ("Cloud Density", Cube) = "black" {}
        _SeaLevel ("Sea Level (m)", Float) = 0
        _OceanEnabled ("Ocean Enabled", Float) = 1
        _SeparateOcean ("Separate ocean geometry", Float) = 0
        _LocalClipDistance ("Local camera composition distance", Float) = 0
        _ProceduralDetailEnabled ("Procedural Detail Enabled", Float) = 0
        _ProceduralDetailAmplitude ("Procedural Detail Amplitude", Float) = 0
        _ProceduralDetailMaterialAmplitude ("Procedural Detail Material Amplitude", Float) = 0
        _ProceduralDetailBaseWavelength ("Procedural Detail Base Wavelength", Float) = 4096
        _ProceduralDetailGain ("Procedural Detail Gain", Range(0.3, 0.75)) = 0.55
        _ProceduralDetailSeed ("Procedural Detail Seed", Float) = 0
        _ProceduralDetailOctaves ("Procedural Detail Octaves", Float) = 0
        _ProceduralDetailCoastScale ("Procedural Detail Coast Scale", Float) = 1
        _ProceduralDetailHasOcean ("Procedural Detail Has Ocean", Float) = 0
        _TerrainDetailSpacing ("Terrain Detail Vertex Spacing", Float) = 1
        _TerrainDetailRadiusMetres ("Terrain Detail Radius Metres", Float) = 6371000
        _TerrainVisualReliefScale ("Terrain Visual Relief Scale", Float) = 1
        _TerrainOpticalHeight ("Terrain Optical Height", Float) = 0
        _DetailOriginHigh ("Detail Origin High", Vector) = (0, 0, 0, 0)
        _DetailOriginLow ("Detail Origin Low", Vector) = (0, 0, 0, 0)
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
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURECUBE(_AlbedoCube); SAMPLER(sampler_AlbedoCube);
            TEXTURECUBE(_NormalCube); SAMPLER(sampler_NormalCube);
            TEXTURECUBE(_CloudDensityCube); SAMPLER(sampler_CloudDensityCube);
            TEXTURE2D(_RingProfile); SAMPLER(sampler_RingProfile);
            float _PlanetGridStrength;

            #include "PlanetSurfaceCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float3 directionOS : TEXCOORD1;
                float4 surfaceData : TEXCOORD2;
                half4 color : COLOR;
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
                float3 viewVectorWS : TEXCOORD6;
                float3 localPositionOS : TEXCOORD7;
                half3 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = PlanetClipPosition(input.positionOS.xyz);
                output.viewVectorWS = _ChunkEnabled > 0.5 ? -PlanetRelativePosition(input.positionOS.xyz) : _WorldSpaceCameraPos.xyz - positionInputs.positionWS;
                output.color = input.color.rgb;
                output.positionWS = positionInputs.positionWS;
                output.localPositionOS = input.positionOS.xyz;
                output.normalWS = PlanetNormalToWorld(input.normalOS);
                output.directionOS = normalize(input.directionOS);
                output.surfaceData = input.surfaceData;
                float3 shapeNormal = normalize(output.directionOS / (_ReferenceAxes.xyz * _ReferenceAxes.xyz));
                output.sphereNormalWS = PlanetNormalToWorld(shapeNormal);
                output.shadowCoord = GetShadowCoord(positionInputs);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_LocalClipDistance > 0) clip(_LocalClipDistance - length(input.viewVectorWS));
                float3 directionOS = normalize(input.directionOS);
                half3 radialNormal = normalize(input.sphereNormalWS);
                half3 terrainNormal = normalize(input.normalWS);
                float height = input.surfaceData.x;
                float pixelFootprint = max(length(ddx(input.localPositionOS)), length(ddy(input.localPositionOS)));
                half detailMask = _ProceduralDetailHasOcean > 0.5h
                    ? smoothstep(0.0h, max(_ProceduralDetailCoastScale, 1.0), abs(height - _SeaLevel)) : 1.0h;
                float4 detailSample = 0;
                if (_ProceduralDetailEnabled > 0.5h && _ChunkEnabled > 0.5)
                    detailSample = PlanetDetailFieldGradient(input.localPositionOS, directionOS, input.surfaceData.w, pixelFootprint);
                float proceduralDetail = detailSample.w;
                if (_NormalEnabled > 0.5h)
                {
                    float3 detailOS = normalize(SAMPLE_TEXTURECUBE(_NormalCube, sampler_NormalCube, directionOS).rgb * 2.0 - 1.0);
                    terrainNormal = PlanetNormalToWorld(detailOS);
                }
                if (_ProceduralDetailEnabled > 0.5h && _ChunkEnabled > 0.5)
                {
                    float3 baseNormalOS = PlanetDirectionToBody(terrainNormal);
                    float3 tangentGradient = detailSample.xyz - directionOS * dot(detailSample.xyz, directionOS);
                    terrainNormal = PlanetNormalToWorld(normalize(baseNormalOS - tangentGradient * (0.65h * detailMask)));
                }
                half slope = saturate(input.surfaceData.y / 42.0h);
                // The mesh carries the smooth, derived liquid coverage in TEXCOORD2.z. It is
                // computed from sea level minus terrain height, so it remains correct when the
                // terrain range or water inventory changes.
                half ocean = saturate(input.surfaceData.z);
                ocean *= _OceanEnabled * (1.0h - _SeparateOcean);
                half3 normal = normalize(lerp(terrainNormal, radialNormal, ocean));
                half3 viewDirection = normalize(input.viewVectorWS);
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
                half3 albedo = lerp(SAMPLE_TEXTURECUBE(_AlbedoCube, sampler_AlbedoCube, directionOS).rgb, input.color, _UseVertexAlbedo);
                if (_ProceduralDetailEnabled > 0.5h && _ChunkEnabled > 0.5)
                {
                    half detailTone = saturate(0.5h + 0.5h * proceduralDetail /
                        max(_ProceduralDetailMaterialAmplitude, 0.001h));
                    albedo *= lerp(1.0h, lerp(0.88h, 1.10h, detailTone), detailMask);
                }
                // The old globe albedo includes blue ocean. The solid seafloor is sediment/rock;
                // water color and specular now belong exclusively to the separate ocean renderer.
                if (_SeparateOcean > 0.5 && input.surfaceData.x < _SeaLevel)
                    albedo = lerp(half3(0.32, 0.27, 0.17), half3(0.075, 0.09, 0.085),
                        saturate((_SeaLevel - input.surfaceData.x) / 1500.0));
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
                    float3 lightDirectionOS = PlanetDirectionToBody(lightDirection);
                    float3 cloudDirection = normalize(directionOS + lightDirectionOS * _CloudShadowOffset);
                    cloudDensity = SAMPLE_TEXTURECUBE(_CloudDensityCube, sampler_CloudDensityCube, cloudDirection).r;
                }
                half cloudShadow = 1.0h - cloudDensity * _CloudShadowStrength * saturate(twilight);
                if (_RingOuterRatio > 1.0)
                {
                    // Ray/plane intersection in radius-normalized body space; independent of floating origin.
                    float3 localLight = PlanetDirectionToBody(lightDirection);
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
                if (_ProceduralDetailEnabled > 0.5h && _ChunkEnabled > 0.5)
                    smoothness = saturate(smoothness + proceduralDetail /
                        max(_ProceduralDetailMaterialAmplitude, 0.001h) * 0.025h * detailMask);
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
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PlanetSurfaceCommon.hlsl"
            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION { return PlanetClipPosition(positionOS.xyz); }
            half4 DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
