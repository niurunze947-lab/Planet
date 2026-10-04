Shader "Planet/StarfieldPhysical"
{
    Properties
    {
        _MainTex ("Star Map", 2D) = "black" {}
        _Tint ("Tint", Color) = (0.85, 0.88, 1.0, 1.0)
        _Exposure ("Exposure", Float) = 0.38
        _Rotation ("Rotation", Range(-6.283, 6.283)) = 0
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
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            half4 _Tint;
            half _Exposure;
            half _Rotation;
            // Global, runtime-only EV offset. The lighting controller never edits the sky asset.
            float _PlanetSkyExposureEV;

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

            half3 SampleStarMapSeamSafe(float2 uv)
            {
                // atan2 jumps by one full turn at +/-pi. Implicit derivatives therefore select
                // the coarsest mip on that pixel quad and draw a vertical dark stripe. Reduce
                // longitude gradients to the shortest periodic difference BEFORE wrapping UVs.
                float2 dx = ddx(uv);
                float2 dy = ddy(uv);
                dx.x -= round(dx.x);
                dy.x -= round(dy.x);
                uv.x = frac(uv.x);
                uv.y = clamp(uv.y, 0.5 * _MainTex_TexelSize.y, 1.0 - 0.5 * _MainTex_TexelSize.y);
                return tex2Dgrad(_MainTex, uv, dx, dy).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float longitude = atan2(direction.x, direction.z) + _Rotation;
                float latitude  = asin(clamp(direction.y, -1.0, 1.0));
                float2 uv = float2(longitude / (2.0 * UNITY_PI) + 0.5,
                                   latitude / UNITY_PI + 0.5);
                half3 starColor = SampleStarMapSeamSafe(uv);

                // The star map is a continuous celestial background. Its intensity is controlled
                // by the exposure required to display the visible planet surface, so the whole
                // sky remains spatially coherent instead of one half being painted grey.
                half3 result = starColor * _Tint.rgb * exp2(_Exposure + _PlanetSkyExposureEV);

                return half4(result, 1.0h);
            }
            ENDHLSL
        }
    }
}
