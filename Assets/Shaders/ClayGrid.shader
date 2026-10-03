// White-model clay with a latitude/longitude graticule.
//
// A perfectly uniform white sphere looks identical no matter how it spins, so the rotation this
// project is supposed to demonstrate is invisible on it. The graticule fixes that without any
// texture: it is derived from the UV sphere's own coordinates, so it is exactly a technical
// white-model surface, and setting _GridStrength to 0 gives back a plain clay ball.
Shader "Planet/ClayGrid"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.82, 0.824, 0.84, 1)
        _GridColor ("Grid Color", Color) = (0.36, 0.42, 0.54, 1)
        _GridStrength ("Grid Strength", Range(0, 1)) = 0.30
        _LongitudeLines ("Longitude Divisions", Range(2, 180)) = 36
        _LatitudeLines ("Latitude Divisions", Range(1, 90)) = 18
        _WrapLighting ("Night Side Wrap", Range(0, 1)) = 0.12
        _Smoothness ("Smoothness", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ClayForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _GridColor;
                float  _GridStrength;
                float  _LongitudeLines;
                float  _LatitudeLines;
                float  _WrapLighting;
                float  _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.positionWS  = pos.positionWS;
                OUT.normalWS    = GetVertexNormalInputs(IN.normalOS).normalWS;
                OUT.uv          = IN.uv;
                OUT.shadowCoord = GetShadowCoord(pos);
                return OUT;
            }

            // Anti-aliased line weight: 1 exactly on a division, 0 between them.
            // fwidth keeps the line one pixel wide at every zoom level, which matters because this
            // scene spans from 0.006-unit planets to a 7,500-unit Kuiper belt.
            float GridLine(float coord, float divisions, float widthPixels)
            {
                float scaled = coord * divisions;
                float fromLine = abs(frac(scaled) - 0.5) * 2.0;
                float aa = max(fwidth(scaled) * widthPixels, 1e-6);
                return smoothstep(1.0 - aa, 1.0, fromLine);
            }

            float BandLine(float coord, float centre, float widthPixels)
            {
                float aa = max(fwidth(coord) * widthPixels, 1e-6);
                return 1.0 - smoothstep(0.0, aa, abs(coord - centre));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);

                // ---- graticule -------------------------------------------------------------
                float minor = max(GridLine(IN.uv.x, _LongitudeLines, 1.0),
                                  GridLine(IN.uv.y, _LatitudeLines, 1.0));
                // Equator and prime meridian a touch heavier, so "which way is up" is readable.
                float equator = BandLine(IN.uv.y, 0.5, 1.8);
                float meridian = BandLine(IN.uv.x, 0.0, 1.8) + BandLine(IN.uv.x, 1.0, 1.8);
                float grid = saturate(minor * 0.7 + equator + meridian) * _GridStrength;

                half3 albedo = lerp(_BaseColor.rgb, _GridColor.rgb, grid);

                // ---- lighting: one directional light, wrapped so the night side is not a hole --
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float ndl = dot(N, mainLight.direction);
                float wrapped = saturate((ndl + _WrapLighting) / (1.0 + _WrapLighting));

                half3 diffuse = albedo * mainLight.color * (wrapped * mainLight.distanceAttenuation);

                // Flat ambient, matching RenderSettings.ambientLight in the scene.
                half3 ambient = albedo * SampleSH(N);

                return half4(diffuse + ambient, 1.0);
            }
            ENDHLSL
        }

        // No ShadowCaster pass: the scene's single directional light has shadows disabled, because
        // shadow cascades over a 1e10 km scene are meaningless.
    }

    Fallback Off
}
