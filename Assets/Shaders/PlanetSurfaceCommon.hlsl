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
                half _ProceduralDetailEnabled;
                float _ProceduralDetailAmplitude;
                float _ProceduralDetailBaseWavelength;
                half _ProceduralDetailGain;
                float _ProceduralDetailSeed;
                float _ChunkSpacing;
                float4 _DetailOriginHigh;
                float4 _DetailOriginLow;
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

float PlanetDetailHash(float3 p, float seed)
{
    p = frac(p * 0.1031 + seed * 0.000017);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float PlanetDetailNoise(float3 p, float seed)
{
    float3 cell = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = PlanetDetailHash(cell, seed);
    float n100 = PlanetDetailHash(cell + float3(1, 0, 0), seed);
    float n010 = PlanetDetailHash(cell + float3(0, 1, 0), seed);
    float n110 = PlanetDetailHash(cell + float3(1, 1, 0), seed);
    float n001 = PlanetDetailHash(cell + float3(0, 0, 1), seed);
    float n101 = PlanetDetailHash(cell + float3(1, 0, 1), seed);
    float n011 = PlanetDetailHash(cell + float3(0, 1, 1), seed);
    float n111 = PlanetDetailHash(cell + float3(1, 1, 1), seed);
    float x00 = lerp(n000, n100, f.x), x10 = lerp(n010, n110, f.x);
    float x01 = lerp(n001, n101, f.x), x11 = lerp(n011, n111, f.x);
    return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z) * 2.0 - 1.0;
}

float PlanetDetailField(float3 localPositionMeters)
{
    float3 p = _DetailOriginHigh.xyz / max(_ProceduralDetailBaseWavelength, 0.001) +
        (_DetailOriginLow.xyz + localPositionMeters) / max(_ProceduralDetailBaseWavelength, 0.001);
    float amplitude = _ProceduralDetailAmplitude;
    float wavelength = max(_ProceduralDetailBaseWavelength, 1.0);
    float result = 0.0;
    [unroll]
    for (int octave = 0; octave < 8; octave++)
    {
        wavelength *= octave == 0 ? 1.0 : 0.5;
        float spacing = max(_ChunkSpacing, 0.05);
        float fade = saturate((wavelength - spacing) / max(spacing, 0.001));
        fade = fade * fade * (3.0 - 2.0 * fade);
        result += PlanetDetailNoise(p * (_ProceduralDetailBaseWavelength / wavelength),
            _ProceduralDetailSeed + octave * 17.0) * amplitude * fade;
        amplitude *= _ProceduralDetailGain;
    }
    return result;
}

float3 PlanetDetailNormal(float3 localPositionMeters, float3 normalOS)
{
    float step = max(_ChunkSpacing * 0.5, 0.25);
    float e = max(_ProceduralDetailAmplitude * 0.02, 0.002);
    float dx = (PlanetDetailField(localPositionMeters + float3(step, 0, 0)) -
        PlanetDetailField(localPositionMeters - float3(step, 0, 0))) / (2.0 * step);
    float dy = (PlanetDetailField(localPositionMeters + float3(0, step, 0)) -
        PlanetDetailField(localPositionMeters - float3(0, step, 0))) / (2.0 * step);
    float dz = (PlanetDetailField(localPositionMeters + float3(0, 0, step)) -
        PlanetDetailField(localPositionMeters - float3(0, 0, step))) / (2.0 * step);
    return normalize(normalOS - float3(dx, dy, dz) * e);
}
#endif
