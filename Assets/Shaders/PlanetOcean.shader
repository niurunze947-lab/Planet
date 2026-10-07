Shader "Planet/Ocean"
{
    Properties
    {
        _SeaLevel ("Sea level (metres)", Float) = 0
        _ShallowColor ("Shallow water", Color) = (0.035, 0.32, 0.39, 1)
        _DeepColor ("Deep water", Color) = (0.025, 0.065, 0.24, 1)
        _FoamColor ("Breaker foam", Color) = (0.86, 0.93, 1, 1)
        _ChunkEnabled ("Runtime chunk", Float) = 0
        _ChunkOriginCR ("Camera relative origin", Vector) = (0,0,0,0)
        _ReferenceAxes ("Reference axes", Vector) = (1,1,1,0)
        _OceanProjection ("0 globe, 1 distant, 2 local", Float) = 0
        _ChunkSpacing ("Chunk vertex spacing (m)", Float) = 1
        _PlanetRadiusMetres ("Planet radius (m)", Float) = 6371000

        // FFT flow layers. Textures, tile size, peak phase speed and 3 sigma height are written by
        // Tools > Planet > Build Ocean Wave Textures; the tile sizes are also republished at runtime
        // by OceanWaveLayers so the chunk phase origins cannot drift from them.
        _WaveNormal0 ("Swell flow normal", 2D) = "gray" {}
        _WaveNormal1 ("Wind sea flow normal", 2D) = "gray" {}
        _WaveNormal2 ("Chop flow normal", 2D) = "gray" {}
        _WavePatch0 ("Swell tile (m)", Float) = 512
        _WavePatch1 ("Wind sea tile (m)", Float) = 128
        _WavePatch2 ("Chop tile (m)", Float) = 32
        _WaveFlow0 ("Swell phase speed (m/s)", Float) = 8.6
        _WaveFlow1 ("Wind sea phase speed (m/s)", Float) = 5.7
        _WaveFlow2 ("Chop phase speed (m/s)", Float) = 3.4
        _WaveAmplitude0 ("Swell 3 sigma (m)", Float) = 1.2
        _WaveAmplitude1 ("Wind sea 3 sigma (m)", Float) = 0.52
        _WaveAmplitude2 ("Chop 3 sigma (m)", Float) = 0.19
        _WaveNormalStrength ("Ripple tilt strength", Range(0, 3)) = 1
        _WaveHeightStrength ("Geometric swell strength", Range(0, 3)) = 1
        _WhitecapStrength ("Open water whitecaps", Range(0, 2)) = 0.55

        // Nearshore train, driven by the same swell: shoaling, refraction and depth-limited breaking.
        _ShoreWaveStrength ("Nearshore wave strength", Range(0, 3)) = 1
        _ShoreShelfDepth ("Shoaling shelf depth (m)", Float) = 26
        _ShoreFoamStrength ("Breaker foam strength", Range(0, 2)) = 0.9

        // Per chunk, written by OceanWaveLayers.WriteBlock.
        _WaveFrameU ("Wave frame U", Vector) = (1,0,0,0)
        _WaveFrameV ("Wave frame V", Vector) = (0,0,1,0)
        _WaveOrigin0 ("Swell phase origin", Vector) = (0,0,0,0)
        _WaveOrigin1 ("Wind sea phase origin", Vector) = (0,0,0,0)
        _WaveOrigin2 ("Chop phase origin", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-10" }
        Pass
        {
            Name "Ocean"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "PlanetSurfaceCommon.hlsl"
            #include "PlanetOceanWaves.hlsl"

            float4 _ShallowColor, _DeepColor, _FoamColor;
            float _OceanProjection;
            float _ChunkSpacing;
            float _WaveNormalStrength, _WaveHeightStrength, _WhitecapStrength;
            float _ShoreWaveStrength, _ShoreShelfDepth, _ShoreFoamStrength;
            float _WavePatch0, _WavePatch1, _WavePatch2;
            float _WaveFlow0, _WaveFlow1, _WaveFlow2;
            float _WaveAmplitude0, _WaveAmplitude1, _WaveAmplitude2;
            float4 _WaveFrameU, _WaveFrameV, _WaveOrigin0, _WaveOrigin1, _WaveOrigin2;
            TEXTURE2D(_WaveNormal0); SAMPLER(sampler_WaveNormal0);
            TEXTURE2D(_WaveNormal1); SAMPLER(sampler_WaveNormal1);
            TEXTURE2D(_WaveNormal2); SAMPLER(sampler_WaveNormal2);

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float3 directionOS : TEXCOORD1;
                float4 sample : TEXCOORD2;
                float4 shore : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float3 localPosition : TEXCOORD2;
                float3 bodyDirection : TEXCOORD3;
                float3 shoreDirection : TEXCOORD4;
                float depth : TEXCOORD5;
                float bedSlope : TEXCOORD6;
            };

            /// Per-fragment wave state shared by the flow layers and the nearshore train.
            struct OceanWaveContext
            {
                float3 localPositionMetres;
                float3 bodyDirection;
                float3 shoreDirection;
                float3 wind;
                float3 frameU;
                float3 frameV;
                float depthMetres;
                float bedSlope;
            };

            /// One flow layer, refracted towards the shore as the water shallows. The exponent says how
            /// early a wavelength feels the bottom: swell first, chop last.
            PlanetWaveLayer OceanFlowLayer(TEXTURE2D_PARAM(waveTex, waveSampler), OceanWaveContext context,
                float2 origin, float patchMetres, float phaseSpeed, float amplitudeMetres, float refractionExponent)
            {
                float3 travel = PlanetWaveTravel(context.wind, context.shoreDirection, context.depthMetres,
                    _ShoreShelfDepth, refractionExponent);
                return PlanetWaveFFTLayer(TEXTURE2D_ARGS(waveTex, waveSampler), context.localPositionMetres,
                    context.frameU, context.frameV, origin,
                    PlanetWaveDirection2D(travel, context.frameU, context.frameV),
                    patchMetres, _Time.y * phaseSpeed, amplitudeMetres);
            }

            /// Vertex stage variant: the same layer, sampled at the top mip, for geometric displacement.
            PlanetWaveLayer OceanFlowLayerVertex(TEXTURE2D_PARAM(waveTex, waveSampler), OceanWaveContext context,
                float2 origin, float patchMetres, float phaseSpeed, float amplitudeMetres, float refractionExponent)
            {
                float3 travel = PlanetWaveTravel(context.wind, context.shoreDirection, context.depthMetres,
                    _ShoreShelfDepth, refractionExponent);
                return PlanetWaveFFTLayerVertex(TEXTURE2D_ARGS(waveTex, waveSampler), context.localPositionMetres,
                    context.frameU, context.frameV, origin,
                    PlanetWaveDirection2D(travel, context.frameU, context.frameV),
                    patchMetres, _Time.y * phaseSpeed, amplitudeMetres);
            }

            /// The same swell reaching the coast, as a shoaling, refracting, depth-limited train.
            PlanetShoreWave OceanShoreWave(OceanWaveContext context)
            {
                return PlanetShoreWaveTrain(context.depthMetres, context.bedSlope, context.shoreDirection,
                    _WaveAmplitude0 * 1.3333, _WaveFlow0, _ShoreShelfDepth, _Time.y, _ShoreWaveStrength);
            }

            OceanWaveContext OceanWaveContextFor(float3 localPosition, float3 bodyDirection, float4 shore, float depth)
            {
                OceanWaveContext context;
                context.localPositionMetres = localPosition;
                context.bodyDirection = bodyDirection;
                context.wind = PlanetPrevailingWind(bodyDirection);
                context.shoreDirection = dot(shore.xyz, shore.xyz) > 1e-8 ? normalize(shore.xyz) : context.wind;
                context.frameU = _WaveFrameU.xyz;
                context.frameV = _WaveFrameV.xyz;
                context.depthMetres = depth;
                context.bedSlope = shore.w;
                return context;
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 bodyDirection = normalize(v.directionOS + 1e-6);
                float depth = _SeaLevel - v.sample.x;
                float3 displacement = 0;
                o.bodyDirection = bodyDirection;
                o.shoreDirection = v.shore.xyz;
                o.bedSlope = v.shore.w;
                o.localPosition = v.positionOS;
                if (_ChunkEnabled > 0.5)
                {
                    OceanWaveContext context = OceanWaveContextFor(v.positionOS, bodyDirection, v.shore, depth);
                    PlanetWaveLayer swell = OceanFlowLayerVertex(TEXTURE2D_ARGS(_WaveNormal0, sampler_WaveNormal0),
                        context, _WaveOrigin0.xy, _WavePatch0, _WaveFlow0, _WaveAmplitude0, 1.0);
                    // Geometric displacement is limited to waves the chunk can actually sample; the
                    // rest stays a shading detail in the fragment normal.
                    float swellWavelength = PLANET_TWO_PI * _WaveFlow0 * _WaveFlow0 / PLANET_GRAVITY;
                    displacement = bodyDirection * (swell.height * _WaveHeightStrength *
                        PlanetWaveMeshFade(swellWavelength, _ChunkSpacing));
                    PlanetShoreWave shore = OceanShoreWave(context);
                    float shoreWavelength = PLANET_TWO_PI / max(shore.number, 1e-4);
                    displacement += bodyDirection * (shore.height * PlanetWaveMeshFade(shoreWavelength, _ChunkSpacing));
                }
                float3 displaced = v.positionOS + displacement;
                o.positionCS = PlanetClipPosition(displaced);
                o.normalWS = PlanetNormalToWorld(v.normalOS);
                o.viewWS = _ChunkEnabled > 0.5 ? -PlanetRelativePosition(displaced) :
                    _WorldSpaceCameraPos - TransformObjectToWorld(displaced);
                o.depth = depth;
                return o;
            }

            half4 Frag(Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                // Exact geometric coastline: dry triangles contribute no water, even over mountains.
                clip(i.depth - 0.001);
                // Transparent water is composited once across the astronomical/local camera stack.
                float range = length(i.viewWS) / max(_ChunkUnitsPerMetre, 1e-12);
                if (_OceanProjection > 1.5) clip(4500.0 - range);
                else if (_OceanProjection > 0.5) clip(range - 4500.0);
                float3 up = normalize(i.normalWS);
                float3 view = normalize(i.viewWS);
                float3 tilt = 0;
                float whitecaps = 0;
                float breakerFoam = 0;
                if (_ChunkEnabled > 0.5)
                {
                    float4 shore = float4(i.shoreDirection, i.bedSlope);
                    OceanWaveContext context = OceanWaveContextFor(i.localPosition, normalize(i.bodyDirection), shore, i.depth);
                    PlanetWaveLayer swell = OceanFlowLayer(TEXTURE2D_ARGS(_WaveNormal0, sampler_WaveNormal0),
                        context, _WaveOrigin0.xy, _WavePatch0, _WaveFlow0, _WaveAmplitude0, 1.0);
                    PlanetWaveLayer windSea = OceanFlowLayer(TEXTURE2D_ARGS(_WaveNormal1, sampler_WaveNormal1),
                        context, _WaveOrigin1.xy, _WavePatch1, _WaveFlow1, _WaveAmplitude1, 0.6);
                    PlanetWaveLayer chop = OceanFlowLayer(TEXTURE2D_ARGS(_WaveNormal2, sampler_WaveNormal2),
                        context, _WaveOrigin2.xy, _WavePatch2, _WaveFlow2, _WaveAmplitude2, 0.3);
                    PlanetShoreWave shoreWave = OceanShoreWave(context);
                    tilt = (swell.tilt + windSea.tilt + chop.tilt) * _WaveNormalStrength + shoreWave.tilt;
                    whitecaps = (swell.foam + windSea.foam + chop.foam) * _WhitecapStrength;
                    breakerFoam = shoreWave.foam * _ShoreFoamStrength;
                }
                float3 normal = normalize(up + PlanetNormalToWorld(tilt));
                float fresnel = 0.02 + 0.98 * pow(1 - saturate(abs(dot(normal, view))), 5);
                float transmission = exp(-i.depth / 32.0);
                Light sun = GetMainLight();
                float day = smoothstep(-0.08, 0.2, dot(up, sun.direction));
                float3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, 1 - exp(-i.depth / 180.0));
                float3 color = water * (0.035 + day * sun.color * 0.85);
                // Two lobes: the wide one is the ripple glitter, the tight one the sun path.
                float facingAngle = saturate(dot(normal, normalize(sun.direction + view)));
                float specular = pow(facingAngle, 64) * 0.45 + pow(facingAngle, 512) * 0.9;
                color += sun.color * specular * day;
                color += float3(0.12, 0.24, 0.34) * fresnel * day * 0.5;
                float foam = saturate(whitecaps + breakerFoam);
                color = lerp(color, _FoamColor.rgb * (0.06 + day * sun.color * 1.05), foam);
                float alpha = saturate(1 - transmission * (1 - fresnel));
                alpha = max(alpha, foam * 0.92);
                alpha *= smoothstep(0, 0.15, i.depth);
                if (!IS_FRONT_VFACE(facing, true, false)) { color *= 0.65; alpha = min(alpha, 0.65); }
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
