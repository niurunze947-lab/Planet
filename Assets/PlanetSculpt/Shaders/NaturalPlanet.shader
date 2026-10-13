Shader "Yongxian/Natural Planet"
{
    Properties
    {
        _PlanetRadius("Base planet radius (local units)", Float) = 0.5
        _GroundColor("Ochre mineral deposits", Color) = (0.6862745, 0.5098039, 0.1843137, 1)
        _DarkRock("Dark weathered rock", Color) = (0.36, 0.31, 0.25, 1)
        _LightRock("Pale exposed rock", Color) = (0.47, 0.41, 0.33, 1)
        _RustRock("Rust brown mineral", Color) = (0.43, 0.35, 0.27, 1)
        _Rock("Mountain rock", Color) = (0.40, 0.34, 0.27, 1)
        [Toggle] _SnowEnabled("Enable snow", Float) = 0
        _Snow("Snow", Color) = (0.86, 0.9, 0.92, 1)
        _SnowLineMeters("Average snow elevation (game m)", Float) = 4000
        _SnowVariationMeters("Local snow elevation variation", Float) = 1800
        _RockBumpStrength("Rock relief detail", Range(0, 0.02)) = 0.0008
        _DetailStrength("Surface variation", Range(0, 1)) = 0.28
        [HideInInspector] _BaseMap("Base Map", 2D) = "white" {}
        [HideInInspector] _BaseColor("Base Color", Color) = (1,1,1,1)
        [HideInInspector] _Cutoff("Cutoff", Float) = 0.5
        [HideInInspector] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "NaturalPlanetForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _PlanetRadius, _SnowLineMeters, _DetailStrength, _SnowVariationMeters, _RockBumpStrength, _SnowEnabled;
                half4 _GroundColor, _Rock, _Snow, _DarkRock, _LightRock, _RustRock;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 elevation : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half3 normalOS : TEXCOORD3;
                float height : TEXCOORD4;
                half fog : TEXCOORD5;
            };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.positionOS = v.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.normalOS = v.normalOS;
                // Authoritative meters from the height field, independent of scale and triangle sag.
                o.height = v.elevation.x;
                o.fog = ComputeFogFactor(pos.positionCS.z);
                return o;
            }
            float Hash(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1,0,0)), f.x),
                                 lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x),
                                 lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            float Fbm(float3 p)
            {
                return Noise(p) * 0.57 + Noise(p * 2.07 + 17.3) * 0.29 + Noise(p * 4.13 + 41.1) * 0.14;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 radial = normalize(i.positionOS);
                float3 warp = float3(Noise(radial * 4.1 + 7.0), Noise(radial * 4.1 + 29.0), Noise(radial * 4.1 + 63.0)) - 0.5;
                float3 p = radial + warp * 0.16;
                float geology = Fbm(p * 7.0 + 4.7);
                float mineral = Fbm(p * 22.0 + 31.0);
                float grain = Fbm(p * 110.0);
                float strata = Noise(p * float3(55.0, 130.0, 70.0) + warp * 8.0);
                float3 normalOS = normalize(i.normalOS);
                float slope = saturate(1.0 - dot(normalOS, radial));
                float h = i.height;
                // Geology is present everywhere, including untouched terrain. The same
                // object-space fields continue across the brush edge without a colored ring.
                float mountain = smoothstep(0.0, 1200.0, max(h, 0.0));
                // Closely related warm rock tones blend gradually, without biome patches.
                half3 ground = lerp(_DarkRock.rgb, _LightRock.rgb, smoothstep(0.1, 0.9, geology));
                ground = lerp(ground, _RustRock.rgb, smoothstep(0.2, 0.8, mineral) * 0.25);
                ground = lerp(ground, _GroundColor.rgb, smoothstep(0.3, 0.85, geology) * (1.0 - mineral) * 0.10);
                float exposed = smoothstep(0.03, 0.30, slope) * mountain;
                half3 layeredRock = lerp(_Rock.rgb * 0.95, _LightRock.rgb, smoothstep(0.1, 0.9, strata));
                ground = lerp(ground, layeredRock, exposed * 0.25);
                ground *= 1.0 + ((grain - 0.5) * 0.8 + (strata - 0.5) * 0.22) * _DetailStrength;

                // Variable local climate and snow deposits, not a single contour line.
                // Cooler pole-facing slopes accumulate lower; steep faces shed snow.
                float3 poleward = float3(0, radial.y >= 0.0 ? 1.0 : -1.0, 0);
                poleward -= radial * dot(poleward, radial);
                float aspect = dot(normalOS - radial, poleward);
                float climate = Fbm(p * 18.0 + 109.0);
                float drift = Fbm(p * 66.0 + 52.0);
                float localSnowLine = _SnowLineMeters + (climate - 0.5) * _SnowVariationMeters * 4.0 - aspect * 1000.0;
                float snow = smoothstep(-450.0, 850.0, h - localSnowLine + (drift - 0.5) * 1700.0);
                snow *= smoothstep(900.0, 1800.0, h);
                snow *= 1.0 - smoothstep(0.22, 0.62, slope);
                // Wind-scoured islands of rock remain even on high ridges, interrupting
                // the continuous white crest that a pure elevation mask would produce.
                snow *= lerp(0.08, 1.0, smoothstep(0.32, 0.66, drift));
                snow *= 1.0 - smoothstep(0.55, 0.78, Noise(p * 95.0 + 16.0)) * 0.8;
                snow *= saturate(_SnowEnabled);
                half3 dirtySnow = lerp(_Snow.rgb * half3(0.66, 0.72, 0.77), _Snow.rgb, smoothstep(0.22, 0.75, grain));
                dirtySnow = lerp(dirtySnow, _LightRock.rgb, (1.0 - snow) * 0.16);
                ground = lerp(ground, dirtySnow, snow);

                // Fine rock relief through screen-space surface gradients; no UV seams.
                float3 baseNormal = NormalizeNormalPerPixel(i.normalWS);
                float worldRadius = _PlanetRadius * length(mul((float3x3)GetObjectToWorldMatrix(), radial));
                float detailHeight = (grain * 0.75 + strata * 0.25) * worldRadius * _RockBumpStrength * lerp(1.0, 0.18, snow);
                float3 dx = ddx(i.positionWS), dy = ddy(i.positionWS);
                float3 r1 = cross(dy, baseNormal), r2 = cross(baseNormal, dx);
                float determinant = dot(dx, r1);
                float3 gradient = sign(determinant) * (ddx(detailHeight) * r1 + ddy(detailHeight) * r2);
                float3 detailNormal = normalize(max(abs(determinant), 1e-8) * baseNormal - gradient);
                // Analytic directional lighting remains stable at solar-system scales.
                // Keep a small fill so the unlit side remains editable while orbiting.
                Light sun = GetMainLight();
                half diffuse = saturate(dot(detailNormal, sun.direction));
                half3 lit = ground * (0.16h + diffuse * max(sun.color, half3(0.7h, 0.7h, 0.7h)));
                half4 color = half4(lit, 1);
                return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
