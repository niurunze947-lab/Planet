Shader "Planet/Ocean"
{
    Properties
    {
        _SeaLevel ("Sea level (metres)", Float) = 0
        _ShallowColor ("Shallow water", Color) = (0.035, 0.32, 0.39, 1)
        _DeepColor ("Deep water", Color) = (0.025, 0.065, 0.24, 1)
        _ChunkEnabled ("Runtime chunk", Float) = 0
        _ChunkOriginCR ("Camera relative origin", Vector) = (0,0,0,0)
        _ReferenceAxes ("Reference axes", Vector) = (1,1,1,0)
        _OceanProjection ("0 globe, 1 distant, 2 local", Float) = 0
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
            float4 _ShallowColor, _DeepColor;
            float _OceanProjection;
            struct Attributes { float3 positionOS:POSITION; float3 normalOS:NORMAL; float4 sample:TEXCOORD2; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float3 viewWS:TEXCOORD1; float depth:TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = PlanetClipPosition(v.positionOS);
                o.normalWS = PlanetNormalToWorld(v.normalOS);
                o.viewWS = _ChunkEnabled > 0.5 ? -PlanetRelativePosition(v.positionOS) :
                    _WorldSpaceCameraPos - TransformObjectToWorld(v.positionOS);
                o.depth = _SeaLevel - v.sample.x;
                return o;
            }
            half4 Frag(Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC):SV_Target
            {
                // Exact geometric coastline: dry triangles contribute no water, even over mountains.
                clip(i.depth - 0.001);
                // Transparent water is composited once across the astronomical/local camera stack.
                float range = length(i.viewWS) / max(_ChunkUnitsPerMetre, 1e-12);
                if (_OceanProjection > 1.5) clip(4500.0 - range);
                else if (_OceanProjection > 0.5) clip(range - 4500.0);
                float3 normal = normalize(i.normalWS);
                float3 view = normalize(i.viewWS);
                float fresnel = 0.02 + 0.98 * pow(1 - saturate(abs(dot(normal, view))), 5);
                float transmission = exp(-i.depth / 32.0);
                Light sun = GetMainLight();
                float day = smoothstep(-0.08, 0.2, dot(normal, sun.direction));
                float3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, 1 - exp(-i.depth / 180.0));
                float3 color = water * (0.035 + day * sun.color * 0.85);
                float specular = pow(saturate(dot(normal, normalize(sun.direction + view))), 64) * day;
                color += sun.color * specular * 0.45;
                color += float3(0.12, 0.24, 0.34) * fresnel * day * 0.5;
                float alpha = saturate(1 - transmission * (1 - fresnel));
                alpha *= smoothstep(0, 0.15, i.depth);
                if (!IS_FRONT_VFACE(facing, true, false)) { color *= 0.65; alpha = min(alpha, 0.65); }
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
