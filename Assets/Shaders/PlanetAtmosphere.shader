Shader "Planet/Atmosphere"
{
    Properties
    {
        _AtmosphereColor ("Rayleigh Color", Color) = (0.30, 0.58, 1.0, 1.0)
        _SunColor ("Sun Scatter Color", Color) = (0.85, 0.78, 0.55, 1.0)
        _Density ("Optical Density", Range(0, 3)) = 0.85
        _RimPower ("Limb Falloff", Range(0.5, 8)) = 3.2
        _DayScatter ("Day Scatter", Range(0, 3)) = 1.0
        _NightScatter ("Night Scatter", Range(0, 1)) = 0.03
        _MieStrength ("Mie Forward Scatter", Range(0, 2)) = 0.4
        _MiePower ("Mie Lobe", Range(2, 64)) = 16
        _SoftEdge ("Planet Edge Softening", Range(0, 1)) = 0.35
        _TwilightColor ("Twilight Color", Color) = (1.0, 0.65, 0.40, 1.0)
        _TwilightStrength ("Twilight Strength", Range(0, 2)) = 0.08
        _ForwardGlow ("Forward Glow", Range(0, 3)) = 0.65
        _SunDirection ("Sun Direction", Vector) = (0.45, -0.30, 0.84, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Back

        Pass
        {
            Name "AtmosphericScattering"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4  _AtmosphereColor;
                half4  _SunColor;
                half   _Density;
                half   _RimPower;
                half   _DayScatter;
                half   _NightScatter;
                half   _MieStrength;
                half   _MiePower;
                half   _SoftEdge;
                half4  _TwilightColor;
                half   _TwilightStrength;
                half   _ForwardGlow;
                float4 _SunDirection;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS  = positionInputs.positionWS;
                output.normalWS    = normalInputs.normalWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float3 normalDirection = normalize(input.normalWS);
                float3 sunDirection = normalize(_SunDirection.xyz);

                // Optical path through the shell grows toward the geometric limb.
                float facing = abs(dot(normalDirection, viewDirection));
                float limb = pow(saturate(1.0 - facing), _RimPower);

                // Lighting of the air parcel itself.
                float sunDot = dot(normalDirection, sunDirection);
                // Soft terminator across the atmosphere — no hard day/night cut.
                float dayFactor = smoothstep(-0.15, 0.25, sunDot);
                float scatter = lerp(_NightScatter, _DayScatter, dayFactor);

                // Rayleigh-ish blue inscatter, brighter on the sunlit limb.
                float3 rayleigh = _AtmosphereColor.rgb * scatter;

                // Mie forward scatter: warm glow looking toward the sun through the limb.
                float cosVS = saturate(dot(-viewDirection, sunDirection));
                float mie = pow(cosVS, _MiePower) * _MieStrength * dayFactor;
                float twilight = exp(-pow(sunDot / 0.10, 2.0));
                float twilightLimb = pow(saturate(1.0 - facing), 1.55) * twilight;

                // Thin haze over the disc that softens high-contrast surface detail.
                // Keep this tiny — it is a softener, not a fog that hides the terrain.
                float discHaze = facing * facing * (0.008 + 0.022 * dayFactor) * _SoftEdge;

                // Confine warmth to a narrow terminator arc and low intensity. In particular,
                // a negative sunDot must not be clamped to zero and tint the entire night limb.
                float alpha = saturate((limb * (0.025 + 0.62 * scatter) + discHaze) * _Density);
                float3 color = rayleigh + _SunColor.rgb * mie * _ForwardGlow * 0.15;
                color = lerp(color, _TwilightColor.rgb, twilightLimb * _TwilightStrength);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
