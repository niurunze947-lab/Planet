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
                float _ProceduralDetailMaterialAmplitude;
                float _ProceduralDetailBaseWavelength;
                half _ProceduralDetailGain;
                uint _ProceduralDetailSeed;
                float _ProceduralDetailOctaves;
                float _ProceduralDetailCoastScale;
                half _ProceduralDetailHasOcean;
                float _TerrainDetailSpacing;
                float _TerrainDetailRadiusMetres;
                float _TerrainVisualReliefScale;
                float _TerrainOpticalHeight;
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

uint PlanetDetailMix(uint z)
{
    z += 0x9E3779B9u;
    z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
    z = (z ^ (z >> 13)) * 0xC2B2AE35u;
    return z ^ (z >> 16);
}

uint PlanetDetailHash(int3 lattice, uint octaveSeed)
{
    uint h = PlanetDetailMix(octaveSeed ^ ((uint)lattice.x * 0x8DA6B343u));
    h = PlanetDetailMix(h ^ ((uint)lattice.y * 0xD8163841u));
    return PlanetDetailMix(h ^ ((uint)lattice.z * 0xCB1AB31Fu));
}

float3 PlanetDetailGradientVector(uint hash)
{
    switch (hash % 12u)
    {
        case 0: return float3(1, 1, 0);
        case 1: return float3(-1, 1, 0);
        case 2: return float3(1, -1, 0);
        case 3: return float3(-1, -1, 0);
        case 4: return float3(1, 0, 1);
        case 5: return float3(-1, 0, 1);
        case 6: return float3(1, 0, -1);
        case 7: return float3(-1, 0, -1);
        case 8: return float3(0, 1, 1);
        case 9: return float3(0, -1, 1);
        case 10: return float3(0, 1, -1);
        default: return float3(0, -1, -1);
    }
}

// xyz = analytic derivative, w = height. Both share one simplex traversal.
float4 PlanetDetailCorner(float3 x, int3 lattice, uint octaveSeed)
{
    float t = 0.5 - dot(x, x);
    if (t <= 0.0) return 0.0;
    float3 gradient = PlanetDetailGradientVector(PlanetDetailHash(lattice, octaveSeed));
    float value = dot(gradient, x);
    float t2 = t * t;
    float t3 = t2 * t;
    float t4 = t2 * t2;
    return float4(t4 * gradient - 8.0 * t3 * value * x, t4 * value);
}

float4 PlanetDetailNoiseGradient(float3 highPosition, float3 lowPosition, uint octaveSeed)
{
    const float F3 = 1.0 / 3.0;
    const float G3 = 1.0 / 6.0;
    // Keep the planetary integer address out of the local fraction. Combining high and
    // low in a float before floor() discards sub-metre motion on an Earth-sized body.
    int3 highCell = (int3)floor(highPosition);
    lowPosition += highPosition - (float3)highCell;
    int sumHigh = highCell.x + highCell.y + highCell.z;
    int skewHigh = (int)((uint)abs(sumHigh) / 3u);
    if (sumHigh < 0) skewHigh = -skewHigh;
    int remainder = sumHigh - skewHigh * 3;
    float skewLow = (lowPosition.x + lowPosition.y + lowPosition.z + remainder) * F3;
    int3 localCell = (int3)floor(lowPosition + skewLow);
    int3 i = highCell + skewHigh + localCell;
    float t = (localCell.x + localCell.y + localCell.z + remainder) * G3;
    float3 x0 = lowPosition - (float3)localCell + t;
    int3 o1, o2;
    if (x0.x >= x0.y)
    {
        if (x0.y >= x0.z) { o1 = int3(1, 0, 0); o2 = int3(1, 1, 0); }
        else if (x0.x >= x0.z) { o1 = int3(1, 0, 0); o2 = int3(1, 0, 1); }
        else { o1 = int3(0, 0, 1); o2 = int3(1, 0, 1); }
    }
    else
    {
        if (x0.y < x0.z) { o1 = int3(0, 0, 1); o2 = int3(0, 1, 1); }
        else if (x0.x < x0.z) { o1 = int3(0, 1, 0); o2 = int3(0, 1, 1); }
        else { o1 = int3(0, 1, 0); o2 = int3(1, 1, 0); }
    }
    float4 sum = PlanetDetailCorner(x0, i, octaveSeed);
    sum += PlanetDetailCorner(x0 - (float3)o1 + G3, i + o1, octaveSeed);
    sum += PlanetDetailCorner(x0 - (float3)o2 + 2.0 * G3, i + o2, octaveSeed);
    sum += PlanetDetailCorner(x0 - 1.0 + 3.0 * G3, i + 1, octaveSeed);
    return 76.0 * sum;
}

void PlanetDetailCoordinates(float3 localPosition, float3 directionOS, float radialOffset,
    float wavelength, out float3 highPosition, out float3 lowPosition)
{
    float3 direction = normalize(directionOS);
    highPosition = _DetailOriginHigh.xyz / wavelength;
    lowPosition = (_DetailOriginLow.xyz + localPosition - direction * radialOffset) / wavelength;
}

float4 PlanetDetailFieldGradient(float3 localPosition, float3 directionOS, float radialOffset, float pixelFootprint)
{
    float amplitude = _ProceduralDetailAmplitude;
    float wavelength = max(_ProceduralDetailBaseWavelength, 1.0);
    float4 result = 0.0;
    float spacing = max(_TerrainDetailSpacing, 0.05);
    float footprint = max(pixelFootprint, 0.001);
    int octaves = clamp((int)_ProceduralDetailOctaves, 0, 24);
    [loop]
    for (int octave = 0; octave < octaves; octave++)
    {
        if (amplitude < 0.005 || wavelength <= 2.0 * footprint) break;
        if (max(max(abs(_DetailOriginHigh.x), abs(_DetailOriginHigh.y)), abs(_DetailOriginHigh.z)) / wavelength > 5e8) break;
        float t = saturate((wavelength - 2.0 * spacing) / (2.0 * spacing));
        float geometryWeight = t * t * (3.0 - 2.0 * t);
        float screenWeight = smoothstep(2.0 * footprint, 4.0 * footprint, wavelength);
        float materialWeight = (1.0 - geometryWeight) * screenWeight;
        if (materialWeight > 0.001)
        {
            uint octaveSeed = PlanetDetailMix(_ProceduralDetailSeed + (uint)(octave + 1) * 0x9E3779B9u);
            float3 highPosition, lowPosition;
            PlanetDetailCoordinates(localPosition, directionOS, radialOffset, wavelength, highPosition, lowPosition);
            float4 sample = PlanetDetailNoiseGradient(highPosition, lowPosition, octaveSeed);
            result.xyz += sample.xyz * (amplitude * materialWeight / wavelength);
            result.w += sample.w * amplitude * materialWeight;
        }
        amplitude *= _ProceduralDetailGain;
        wavelength *= 0.5;
    }
    return result;
}
#endif
