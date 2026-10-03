Shader "Planet/StarfieldPhysical"
{
    Properties
    {
        _MainTex ("Star Map", 2D) = "black" {}
        _Tint ("Tint", Color) = (0.85, 0.88, 1.0, 1.0)
        _Exposure ("Exposure", Float) = 0.38
        _Rotation ("Rotation", Range(-6.283, 6.283)) = 0
        _SunDirection ("Sun Direction", Vector) = (0.45, -0.30, 0.84, 0)
        _DayFadePower ("Day Fade Power", Range(0.5, 8)) = 2.2
        _DayIntensity ("Day Star Intensity", Range(0, 1)) = 0.012
        _HorizonGlow ("Twilight Horizon Glow", Range(0, 1)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }
        Cull Off
        ZWrite Off
        // URP draws the skybox AFTER opaque geometry. ZTest Always would paint stars
        // over every planet. LEqual keeps stars only where depth is still at the far plane.
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _Tint;
            half _Exposure;
            half _Rotation;
            float4 _SunDirection;
            half _DayFadePower;
            half _DayIntensity;
            half _HorizonGlow;

            struct Attributes
            {
                float4 vertex : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction   : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.vertex);
                output.direction  = normalize(input.vertex.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float longitude = atan2(direction.x, direction.z) + _Rotation;
                float latitude  = asin(clamp(direction.y, -1.0, 1.0));
                float2 uv = float2(longitude / (2.0 * UNITY_PI) + 0.5,
                                   latitude / UNITY_PI + 0.5);
                uv.x = frac(uv.x);

                half4 starColor = tex2D(_MainTex, uv);

                // Day hemisphere: atmospheric / daylight glare washes the stars out.
                // Stars remain on the night side. cos = 1 looking at the sun, -1 anti-sun.
                float3 sunDir = normalize(_SunDirection.xyz);
                float cosSun = dot(direction, sunDir);

                // Smooth day weight: full stars opposite the sun, almost none toward it.
                float dayWeight = pow(saturate(cosSun * 0.5 + 0.5), _DayFadePower);
                half starIntensity = lerp(1.0h, _DayIntensity, dayWeight);

                half3 result = starColor.rgb * _Tint.rgb * exp2(_Exposure) * starIntensity;

                // Subtle warm airglow near the sun so the day sky is not pure black.
                float3 glow = _Tint.rgb * _HorizonGlow * dayWeight * exp2(_Exposure) * 0.35;
                result += glow;

                return half4(result, 1.0h);
            }
            ENDHLSL
        }
    }
}
