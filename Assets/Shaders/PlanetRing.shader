Shader "Planet/ProceduralRing"
{
    Properties
    {
        _RingProfile ("Radial Color / Optical Opacity", 2D) = "white" {}
        _PlanetRadiusRatio ("Body Radius / Ring Outer Radius", Float) = 0.42
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_RingProfile); SAMPLER(sampler_RingProfile);
            CBUFFER_START(UnityPerMaterial)
                float _PlanetRadiusRatio;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.positionOS = input.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float radius = length(i.positionOS.xz);
                half4 optical = SAMPLE_TEXTURE2D(_RingProfile, sampler_RingProfile, float2(radius, 0.5));
                Light sun = GetMainLight();
                float3 ray = normalize(TransformWorldToObjectDir(sun.direction));
                float t = dot(-i.positionOS, ray);
                float3 closest = i.positionOS + ray * max(0, t);
                // Analytic planet shadow, avoiding the astronomical-scale URP shadow atlas.
                float occulted = t > 0 && dot(closest, closest) < _PlanetRadiusRatio * _PlanetRadiusRatio ? 1 : 0;
                half diffuse = 0.20h + 0.80h * abs(dot(normalize(i.normalWS), sun.direction));
                half3 color = optical.rgb * (0.035h + diffuse * sun.color) * lerp(1.0h, 0.10h, occulted);
                return half4(color, optical.a);
            }
            ENDHLSL
        }
    }
}
