#ifndef PLANET_SURFACE_COMMON_INCLUDED
#define PLANET_SURFACE_COMMON_INCLUDED
            CBUFFER_START(UnityPerMaterial)
                half4 _OceanTint;
                half4 _RimColor;
                float _SeaLevel;
                half _SeparateOcean;
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
                half _NormalEnabled;
                float _ChunkEnabled;
                float4 _ChunkOriginCR;
                float _UseVertexAlbedo;
                float4x4 _ChunkBodyToWorld;
                float _ChunkUnitsPerMetre;
                float _LocalClipDistance;
                float4 _ReferenceAxes;
                half _EmissionStrength;
                half _LimbDarkening;
                float _RingOuterRatio;
            CBUFFER_END


float3 PlanetRelativePosition(float3 localPosition)
{
    return _ChunkOriginCR.xyz + mul((float3x3)_ChunkBodyToWorld, localPosition) * _ChunkUnitsPerMetre;
}
float3 PlanetNormalToWorld(float3 normalOS)
{
    if (_ChunkEnabled > 0.5) return normalize(mul((float3x3)_ChunkBodyToWorld, normalOS));
    return TransformObjectToWorldNormal(normalOS);
}
float3 PlanetDirectionToBody(float3 directionWS)
{
    if (_ChunkEnabled > 0.5) return normalize(mul(directionWS, (float3x3)_ChunkBodyToWorld));
    return normalize(TransformWorldToObjectDir(directionWS));
}
float4 PlanetClipPosition(float3 localPosition)
{
    if (_ChunkEnabled > 0.5)
        return mul(UNITY_MATRIX_P, float4(mul((float3x3)UNITY_MATRIX_V, PlanetRelativePosition(localPosition)), 1.0));
    return TransformObjectToHClip(localPosition);
}
#endif
