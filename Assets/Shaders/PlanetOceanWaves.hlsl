#ifndef PLANET_OCEAN_WAVES_INCLUDED
#define PLANET_OCEAN_WAVES_INCLUDED

// Shared ocean wave model for Planet/Ocean. Two contributions meet here:
//
//  1. FFT flow layers - tileable patches baked from a Phillips spectrum by
//     Tools/Planet/Build Ocean Wave Textures. They are sampled on the chunk's cube-face
//     tangent frame and scrolled with the prevailing wind, so the free surface carries a real
//     deep-water spectrum instead of hand-authored noise.
//  2. A nearshore wave train - the shoaling/refraction/breaking solution of the same swell.
//     Its phase coordinate is the bed distance reconstructed from the local bed gradient, so
//     crests follow the bathymetric contours and shorten and steepen as the water shallows.
//
// Both return a body-space tangential tilt to add to the surface normal plus a geometric height,
// so the vertex displacement and the shaded normal are the same wave field.

#define PLANET_GRAVITY 9.80665
#define PLANET_TWO_PI 6.283185307179586
// Depth-limited breaking height, H_max = breakerIndex x depth (McCowan).
#define PLANET_BREAKER_INDEX 0.78
// Slope floor for the phase coordinate: keeps the travelled distance finite on flat beds.
#define PLANET_SHORE_SLOPE_FLOOR 0.004

struct PlanetWaveLayer
{
    float3 tilt;    // body-space tangential perturbation of the up vector
    float height;   // metres, signed
    float foam;     // 0..1 whitecaps
};

struct PlanetShoreWave
{
    float3 tilt;     // body-space tangential perturbation of the up vector
    float height;    // metres, signed
    float foam;      // 0..1 breaker foam
    float breaking;  // 0..1 how close the crest is to the depth-limited limit
    float number;    // local wavenumber, for the mesh budget fade
};

/// Zonal prevailing wind, same convention as PlanetSurfaceSampler's orographic wind: easterly
/// trades inside |lat| < 0.35, westerlies up to 0.9, polar easterlies beyond. The tangent shrinks
/// towards the poles but its direction stays continuous, so no arbitrary polar seam is introduced.
float3 PlanetPrevailingWind(float3 bodyDirection)
{
    float latitude = abs(bodyDirection.y);
    float westerlies = smoothstep(0.35, 0.55, latitude) * (1.0 - smoothstep(0.8, 0.95, latitude));
    float3 zonal = float3(bodyDirection.z, 0.0, -bodyDirection.x) * lerp(-1.0, 1.0, westerlies);
    float lengthSquared = dot(zonal, zonal);
    return lengthSquared > 1e-8 ? zonal * rsqrt(lengthSquared) : float3(1.0, 0.0, 0.0);
}

/// Bends a travel direction from the open-sea wind towards the shore normal as the water shallows.
float3 PlanetWaveTravel(float3 wind, float3 shoreDirection, float depthMetres, float shelfDepth, float exponent)
{
    float shallow = 1.0 - saturate((depthMetres - shelfDepth * 0.15) / max(shelfDepth * 0.85, 1.0));
    float bend = pow(shallow, max(exponent, 0.05));
    float3 travel = lerp(wind, shoreDirection, bend);
    float lengthSquared = dot(travel, travel);
    return lengthSquared > 1e-8 ? travel * rsqrt(lengthSquared) : wind;
}

/// Tile-space lookup of one flow layer. <paramref name="origin"/> is the chunk's phase origin in
/// tiles, computed in double on the CPU and reduced modulo one tile, so neighbouring chunks cannot
/// drift apart; everything the shader adds to it is short and stays exact in float.
float2 PlanetWaveUv(float3 localPositionMetres, float3 frameU, float3 frameV, float2 origin,
    float2 travel2D, float patchMetres, float scrollMetres)
{
    float invPatch = 1.0 / max(patchMetres, 1.0);
    return origin + float2(dot(localPositionMetres, frameU), dot(localPositionMetres, frameV)) * invPatch
        + travel2D * (scrollMetres * invPatch);
}

PlanetWaveLayer PlanetWaveLayerDecode(float4 packed, float3 frameU, float3 frameV, float amplitudeMetres)
{
    float2 tilt = packed.rg * 2.0 - 1.0;
    PlanetWaveLayer layer;
    layer.tilt = frameU * tilt.x + frameV * tilt.y;
    layer.height = (packed.b * 2.0 - 1.0) * amplitudeMetres;
    layer.foam = packed.a;
    return layer;
}

/// Shaded lookup: implicit derivatives, so the ripple keeps its mip chain with distance.
PlanetWaveLayer PlanetWaveFFTLayer(
    TEXTURE2D_PARAM(waveTex, waveSampler),
    float3 localPositionMetres, float3 frameU, float3 frameV, float2 origin,
    float2 travel2D, float patchMetres, float scrollMetres, float amplitudeMetres)
{
    float2 uv = PlanetWaveUv(localPositionMetres, frameU, frameV, origin, travel2D, patchMetres, scrollMetres);
    return PlanetWaveLayerDecode(SAMPLE_TEXTURE2D(waveTex, waveSampler, uv), frameU, frameV, amplitudeMetres);
}

/// Vertex-stage lookup: no derivatives exist there, so the top mip is requested explicitly.
PlanetWaveLayer PlanetWaveFFTLayerVertex(
    TEXTURE2D_PARAM(waveTex, waveSampler),
    float3 localPositionMetres, float3 frameU, float3 frameV, float2 origin,
    float2 travel2D, float patchMetres, float scrollMetres, float amplitudeMetres)
{
    float2 uv = PlanetWaveUv(localPositionMetres, frameU, frameV, origin, travel2D, patchMetres, scrollMetres);
    return PlanetWaveLayerDecode(SAMPLE_TEXTURE2D_LOD(waveTex, waveSampler, uv, 0.0), frameU, frameV, amplitudeMetres);
}

/// Projected 2D direction of a body-space direction inside a cube-face tangent frame.
float2 PlanetWaveDirection2D(float3 direction, float3 frameU, float3 frameV)
{
    float2 projected = float2(dot(direction, frameU), dot(direction, frameV));
    return projected * rsqrt(max(dot(projected, projected), 1e-8));
}

/// Nearshore wave train. <paramref name="bedSlope"/> is the magnitude of the seabed height
/// gradient, <paramref name="shoreDirection"/> points uphill (towards land), and the phase
/// coordinate is depth / slope, which reconstructs travelled distance at the local wavelength.
PlanetShoreWave PlanetShoreWaveTrain(
    float depthMetres, float bedSlope, float3 shoreDirection,
    float offshoreHeightMetres, float deepPhaseSpeed, float shelfDepth, float time, float strength)
{
    PlanetShoreWave wave;
    wave.tilt = float3(0.0, 0.0, 0.0);
    wave.height = 0.0;
    wave.foam = 0.0;
    wave.breaking = 0.0;
    wave.number = 0.0;
    float shelf = max(shelfDepth, 1.0);
    float depth = max(depthMetres, 0.0);
    // Shoaling only exists inside the shelf; beyond it the FFT layers own the surface.
    float shoal = 1.0 - smoothstep(shelf * 0.55, shelf, depth);
    if (shoal <= 0.0001 || strength <= 0.0) return wave;

    float deepSpeed = max(deepPhaseSpeed, 0.25);
    float deepNumber = PLANET_GRAVITY / (deepSpeed * deepSpeed);   // k0 = g / c0^2 for the peak wave
    // Exact shallow-water correction: c = c0 sqrt(tanh(k0 h)), so the angular frequency k*c is
    // invariant and the crest slows down as it runs into the beach.
    float ratio = sqrt(max(tanh(deepNumber * depth), 1e-6));
    float number = deepNumber / max(ratio, 1e-4);
    // Green's law in wave-speed form: A ~ h^-1/4 ~ c^-1/2, capped by the breaker height.
    float offshoreAmplitude = max(offshoreHeightMetres, 0.0) * 0.5;
    float amplitude = min(offshoreAmplitude * rsqrt(max(ratio, 1e-4)), PLANET_BREAKER_INDEX * 0.5 * depth);
    amplitude *= shoal * strength;
    if (amplitude <= 0.0001) return wave;

    float travelled = depth / max(bedSlope, PLANET_SHORE_SLOPE_FLOOR);
    float phase = number * travelled + deepNumber * deepSpeed * time;
    // A second harmonic sharpens the crest and flattens the trough once the wave is depth limited.
    wave.breaking = saturate(amplitude / max(PLANET_BREAKER_INDEX * 0.5 * depth, 1e-4));
    wave.number = number;
    float skew = 0.22 * wave.breaking;
    float height = amplitude * (cos(phase) + skew * cos(2.0 * phase));
    float slope = -amplitude * number * (sin(phase) + 2.0 * skew * sin(2.0 * phase));
    // For a profile z = h(s) the normal is r - dh/ds * s; s runs offshore, i.e. against the
    // uphill shore direction handed in by the terrain build.
    wave.height = height;
    wave.tilt = shoreDirection * slope;
    float crest = saturate(cos(phase) * 0.5 + 0.5);
    float breakingFoam = smoothstep(0.6, 1.05, wave.breaking) * smoothstep(0.4, 0.95, crest);
    float swash = (1.0 - smoothstep(0.02, 1.2, depth)) * smoothstep(0.05, 0.7, crest);
    wave.foam = saturate((breakingFoam + 0.85 * swash) * strength);
    return wave;
}

/// Mesh budget fade: a wavelength that the chunk cannot sample becomes a shading detail only.
float PlanetWaveMeshFade(float wavelengthMetres, float vertexSpacingMetres)
{
    return 1.0 - smoothstep(wavelengthMetres * 0.12, wavelengthMetres * 0.45, vertexSpacingMetres);
}

#endif
