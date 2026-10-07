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

float PlanetDetailGradient(uint hash, float3 x)
{
    switch (hash % 12u)
    {
        case 0: return x.x + x.y;
        case 1: return -x.x + x.y;
        case 2: return x.x - x.y;
        case 3: return -x.x - x.y;
        case 4: return x.x + x.z;
        case 5: return -x.x + x.z;
        case 6: return x.x - x.z;
        case 7: return -x.x - x.z;
        case 8: return x.y + x.z;
        case 9: return -x.y + x.z;
        case 10: return x.y - x.z;
        default: return -x.y - x.z;
    }
}

float PlanetDetailCorner(float3 x, int3 lattice, uint octaveSeed)
{
    float t = 0.5 - dot(x, x);
    if (t <= 0.0) return 0.0;
    t *= t;
    return t * t * PlanetDetailGradient(PlanetDetailHash(lattice, octaveSeed), x);
}

float PlanetDetailNoise(float3 p, uint octaveSeed)
{
    const float F3 = 1.0 / 3.0;
    const float G3 = 1.0 / 6.0;
    float s = (p.x + p.y + p.z) * F3;
    int3 i = (int3)floor(p + s);
    float t = (i.x + i.y + i.z) * G3;
    float3 x0 = p - ((float3)i - t);
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
    float sum = PlanetDetailCorner(x0, i, octaveSeed);
    sum += PlanetDetailCorner(x0 - (float3)o1 + G3, i + o1, octaveSeed);
    sum += PlanetDetailCorner(x0 - (float3)o2 + 2.0 * G3, i + o2, octaveSeed);
    sum += PlanetDetailCorner(x0 - 1.0 + 3.0 * G3, i + 1, octaveSeed);
    return 76.0 * sum;
}

float3 PlanetDetailCoordinates(float3 localPosition, float3 directionOS, float height, float wavelength)
{
    float3 direction = normalize(directionOS);
    float3 axes = max(_ReferenceAxes.xyz, 0.001);
    float referenceRadius = _TerrainDetailRadiusMetres * rsqrt(dot(direction / axes, direction / axes));
    float displacement = _ProceduralDetailHasOcean > 0.5h
        ? _SeaLevel + (height - _SeaLevel) * _TerrainVisualReliefScale
        : height * _TerrainVisualReliefScale;
    displacement += _TerrainOpticalHeight;
    float radialOffset = referenceRadius + displacement - _TerrainDetailRadiusMetres;
    float3 origin = _DetailOriginHigh.xyz / wavelength +
        (_DetailOriginLow.xyz + localPosition - direction * radialOffset) / wavelength;
    return origin;
}

float PlanetDetailField(float3 localPosition, float3 directionOS, float height)
{
    float amplitude = _ProceduralDetailAmplitude;
    float wavelength = max(_ProceduralDetailBaseWavelength, 1.0);
    float result = 0.0;
    float spacing = max(_TerrainDetailSpacing, 0.05);
    int octaves = clamp((int)_ProceduralDetailOctaves, 0, 24);
    [loop]
    for (int octave = 0; octave < octaves; octave++)
    {
        if (amplitude < 0.005) break;
        float t = saturate((wavelength - 2.0 * spacing) / (2.0 * spacing));
        float geometryWeight = t * t * (3.0 - 2.0 * t);
        float materialWeight = 1.0 - geometryWeight;
        if (materialWeight > 0.001)
        {
            uint octaveSeed = PlanetDetailMix(_ProceduralDetailSeed + (uint)(octave + 1) * 0x9E3779B9u);
            float3 coordinates = PlanetDetailCoordinates(localPosition, directionOS, height, wavelength);
            result += PlanetDetailNoise(coordinates, octaveSeed) * amplitude * materialWeight;
        }
        amplitude *= _ProceduralDetailGain;
        wavelength *= 0.5;
    }
    return result;
}

float3 PlanetDetailNormal(float3 localPosition, float3 directionOS, float height, float3 normalOS)
{
    float3 up = normalize(directionOS);
    float3 helper = abs(up.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0);
    float3 east = normalize(cross(helper, up));
    float3 north = cross(up, east);
    float step = max(_TerrainDetailSpacing * 0.5, 0.25);
    float angularStep = step / max(_TerrainDetailRadiusMetres, 1.0);
    float slopeEast = (PlanetDetailField(localPosition + east * step, normalize(up + east * angularStep), height) -
        PlanetDetailField(localPosition - east * step, normalize(up - east * angularStep), height)) / (2.0 * step);
    float slopeNorth = (PlanetDetailField(localPosition + north * step, normalize(up + north * angularStep), height) -
        PlanetDetailField(localPosition - north * step, normalize(up - north * angularStep), height)) / (2.0 * step);
    float3 gradient = east * slopeEast + north * slopeNorth;
    return normalize(normalOS - gradient);
}
#endif
