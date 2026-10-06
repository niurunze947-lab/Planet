using Unity.Mathematics;
using UnityEngine;

namespace Planet.Procedural
{
    /// <summary>
    /// Immutable, blittable snapshot shared by CPU queries, mesh generation and Burst texture baking.
    /// No UVs, mesh LOD, global Random state, texture reads or Unity objects enter the sampling path.
    /// </summary>
    public struct PlanetSurfaceSampler
    {
        public readonly float radiusMetres;
        public readonly float flattening;
        public readonly float seaLevelMetres;
        public readonly float minHeightMetres;
        public readonly float maxHeightMetres;
        public readonly float waterDepthRangeMetres;
        public readonly bool hasOcean;
        public readonly bool hasClouds;
        public readonly bool hasEcology;
        public readonly bool hasIceCaps;
        public const float NormalEncodingScale = 16f;

        private readonly float3 seedOffset;
        private readonly float continentFrequency, continentThreshold, domainWarp;
        private readonly int continentOctaves, mountainOctaves;
        private readonly float oceanDepth, plateauHeight, mountainFrequency, mountainHeight;
        private readonly float detailFrequency, detailHeight, slopeStep;
        private readonly float temperatureOffset, moistureOffset, altitudeCooling;
        private readonly float cloudFrequency, cloudCoverage, cloudSoftness, cloudDetailFrequency;
        private readonly float cloudOceanDensity, cloudLandDensity, cloudMoistureInfluence, cloudAltitudeBias;
        private readonly float cloudTerrainInfluence, cloudOrographicInfluence, cloudUpwindDistance;
        private readonly float visualReliefScale;
        private readonly float3 deepOcean, shallowOcean, beach, desert, grassland;
        private readonly float3 forest, rainforest, tundra, rock, snow;
        private readonly PlanetAppearanceSampler appearance;
        private readonly float3 referenceAxes;

        public PlanetSurfaceSampler(PlanetDefinition definition)
        {
            PlanetPhysicalParameters p = definition.physical;
            PlanetGenerationParameters g = definition.generation;
            PlanetPalette c = definition.palette;
            radiusMetres = (float)p.radiusMetres;
            flattening = p.flattening;
            seaLevelMetres = p.seaLevelMetres;
            minHeightMetres = g.terrainMinimumMetres < g.terrainMaximumMetres
                ? g.terrainMinimumMetres : -g.oceanDepthMetres;
            maxHeightMetres = g.terrainMaximumMetres > minHeightMetres
                ? g.terrainMaximumMetres : g.plateauHeightMetres + g.mountainHeightMetres + g.detailHeightMetres;
            waterDepthRangeMetres = math.max(seaLevelMetres - minHeightMetres, 1f);
            hasOcean = definition.features != null && definition.features.hasOcean;
            hasClouds = definition.features != null && definition.features.hasClouds;
            hasEcology = definition.features == null || definition.features.hasEcology;
            hasIceCaps = definition.features == null || definition.features.hasIceCaps;
            uint seed = unchecked((uint)g.seed);
            seedOffset = new float3(SeedUnit(seed ^ 0x68bc21ebu), SeedUnit(seed ^ 0x02e5be93u),
                SeedUnit(seed ^ 0x967a889bu)) * 240f + 17f;
            appearance = new PlanetAppearanceSampler(definition.appearance, seedOffset);
            referenceAxes = new float3(p.shapeScale.x, p.shapeScale.y * (1f - flattening), p.shapeScale.z);
            continentFrequency = g.continentFrequency;
            continentThreshold = g.continentThreshold;
            domainWarp = g.domainWarp;
            continentOctaves = g.continentOctaves;
            mountainOctaves = g.mountainOctaves;
            oceanDepth = g.oceanDepthMetres;
            plateauHeight = g.plateauHeightMetres;
            mountainFrequency = g.mountainFrequency;
            mountainHeight = g.mountainHeightMetres;
            detailFrequency = g.detailFrequency;
            detailHeight = g.detailHeightMetres;
            slopeStep = g.slopeSampleDistanceMetres;
            temperatureOffset = g.temperatureOffset;
            moistureOffset = g.moistureOffset;
            altitudeCooling = g.altitudeCoolingPerKilometre;
            cloudFrequency = g.cloudFrequency;
            cloudCoverage = g.cloudCoverage;
            cloudSoftness = g.cloudSoftness;
            cloudDetailFrequency = g.cloudDetailFrequency;
            cloudOceanDensity = g.cloudOceanDensity;
            cloudLandDensity = g.cloudLandDensity;
            cloudMoistureInfluence = g.cloudMoistureInfluence;
            cloudAltitudeBias = g.cloudAltitudeBias;
            cloudTerrainInfluence = g.cloudTerrainInfluence;
            cloudOrographicInfluence = g.cloudOrographicInfluence;
            cloudUpwindDistance = g.cloudUpwindDistanceMetres;
            visualReliefScale = g.visualReliefScale;
            deepOcean = Linear(c.deepOcean); shallowOcean = Linear(c.shallowOcean);
            beach = Linear(c.beach); desert = Linear(c.desert); grassland = Linear(c.grassland);
            forest = Linear(c.forest); rainforest = Linear(c.rainforest); tundra = Linear(c.tundra);
            rock = Linear(c.rock); snow = Linear(c.snow);
        }

        public float EvaluateHeight(float3 direction)
        {
            float3 d = math.normalizesafe(direction, new float3(0f, 1f, 0f));
            if (appearance.kind != PlanetSurfaceKind.Terrestrial) return appearance.Height(d);
            float3 p = d * 2.1f + seedOffset;
            float3 warp = new float3(noise.snoise(p), noise.snoise(p + 31.73f), noise.snoise(p - 19.31f));
            float3 w = d + warp * domainWarp;
            float continental = Fbm(w * continentFrequency + seedOffset, continentOctaves, 0.51f);
            continental += 0.11f * noise.snoise(d * 0.9f + seedOffset + 107.3f);
            float coast = continental - continentThreshold;

            if (coast < 0f)
                return -oceanDepth * math.pow(math.saturate(-coast * 3.4f), 0.72f);

            float interior = Smooth(0f, 0.065f, coast);
            float plateau = plateauHeight * math.pow(math.saturate(coast * 3.2f), 1.35f);
            float belt = Smooth(0.27f, 0.75f, noise.snoise(w * 4.7f + seedOffset - 47f) * 0.5f + 0.5f);
            float ridge = Ridged(w * mountainFrequency + seedOffset, mountainOctaves);
            float relief = mountainHeight * belt * math.pow(ridge, 2.1f) * interior;
            float detail = detailHeight * Fbm(d * detailFrequency + seedOffset, 4, 0.5f) * interior;
            return math.max(0f, plateau + relief + detail);
        }

        public PlanetSurfaceSample Evaluate(float3 direction)
        {
            float3 d = math.normalizesafe(direction, new float3(0f, 1f, 0f));
            float h = EvaluateHeight(d);
            float3 helper = math.abs(d.y) < 0.95f ? new float3(0f, 1f, 0f) : new float3(0f, 0f, 1f);
            float3 tangent = math.normalize(math.cross(helper, d));
            float3 bitangent = math.cross(d, tangent);
            float step = slopeStep / radiusMetres;
            float dhT = (EvaluateHeight(d + tangent * step) - EvaluateHeight(d - tangent * step)) / (2f * slopeStep);
            float dhB = (EvaluateHeight(d + bitangent * step) - EvaluateHeight(d - bitangent * step)) / (2f * slopeStep);
            float3 referenceNormal = ReferenceNormal(d);
            float3 normal = math.normalize(referenceNormal - tangent * dhT * visualReliefScale - bitangent * dhB * visualReliefScale);
            float slope = math.degrees(math.atan(math.sqrt(dhT * dhT + dhB * dhB)));
            EvaluateClimate(d, h, out float temperature, out float moisture);
            float waterDepth = hasOcean ? math.max(0f, seaLevelMetres - h) : 0f;
            float waterContent = hasOcean ? math.saturate(waterDepth / waterDepthRangeMetres) : 0f;
            float ocean = hasOcean ? Smooth(0f, 120f, waterDepth) : 0f;
            PlanetBiome biome = SelectBiome(h, temperature, moisture);
            return new PlanetSurfaceSample
            {
                heightMetres = h, slopeDegrees = slope, temperature = temperature, moisture = moisture,
                ocean = ocean, waterDepthMetres = waterDepth, waterContent = waterContent,
                biome = biome, normal = normal,
                albedoLinear = appearance.kind == PlanetSurfaceKind.Terrestrial
                    ? Albedo(d, h, temperature, moisture, slope, ocean) : appearance.Albedo(d)
            };
        }

        /// <summary>
        /// Globe albedo cache path. It uses the same height, climate, water and biome functions as
        /// Evaluate(), but omits tangent finite differences because a color texel does not need a
        /// geometric normal. This removes four extra height evaluations per baked texel.
        /// </summary>
        public float3 EvaluateAlbedoOnly(float3 direction)
        {
            float3 d = math.normalizesafe(direction, new float3(0f, 1f, 0f));
            if (appearance.kind != PlanetSurfaceKind.Terrestrial) return appearance.Albedo(d);
            float h = EvaluateHeight(d);
            EvaluateClimate(d, h, out float temperature, out float moisture);
            float waterDepth = hasOcean ? math.max(0f, seaLevelMetres - h) : 0f;
            float ocean = hasOcean ? Smooth(0f, 120f, waterDepth) : 0f;
            return Albedo(d, h, temperature, moisture, 0f, ocean);
        }

        private void EvaluateClimate(float3 d, float h, out float temperature, out float moisture)
        {
            float elevation = math.max(0f, h - seaLevelMetres);
            float latitude = math.abs(d.y);
            temperature = math.saturate(1.02f - math.pow(latitude, 1.35f) -
                elevation * altitudeCooling * 0.001f + temperatureOffset +
                Fbm(d * 3.7f + seedOffset + 70f, 3, 0.5f) * 0.06f);
            float dryBelt = math.exp(-math.pow((latitude - 0.43f) / 0.17f, 2f));
            moisture = math.saturate(0.63f + Fbm(d * 3.1f + seedOffset - 81f, 4, 0.55f) * 0.48f -
                dryBelt * 0.36f + moistureOffset);
        }

        /// <summary>Body-fixed statistical cloud field; terrestrial clouds share terrain and biome climate.</summary>
        public float EvaluateCloudDensity(float3 direction)
        {
            if (!hasClouds) return 0f;
            float3 d = math.normalizesafe(direction, new float3(0f, 1f, 0f));
            float baseNoise = Fbm(d * cloudFrequency + seedOffset + 151f, 5, 0.55f) * 0.5f + 0.5f;
            float detail = Fbm(d * cloudDetailFrequency + seedOffset - 33f, 3, 0.52f) * 0.5f + 0.5f;
            float shaped = baseNoise * 0.82f + detail * 0.18f;
            float edge = math.saturate((shaped - (1f - cloudCoverage)) / math.max(cloudSoftness, 0.001f));
            float baseDensity = edge * edge * (3f - 2f * edge);
            if (appearance.kind != PlanetSurfaceKind.Terrestrial || cloudTerrainInfluence <= 0f)
                return baseDensity;
            float h = EvaluateHeight(d);
            float elevation = math.saturate((h - seaLevelMetres) / math.max(maxHeightMetres - seaLevelMetres, 1f));
            // Use the coastline, not seabed depth: shallow water still supplies moisture.
            float water = hasOcean ? Smooth(-150f, 150f, seaLevelMetres - h) : 0f;
            EvaluateClimate(d, h, out float temperature, out float moisture);
            float surfaceFactor = math.lerp(cloudLandDensity, cloudOceanDensity, water);
            surfaceFactor *= math.max(0f, 1f + cloudMoistureInfluence * (moisture - 0.5f) * 1.6f);
            surfaceFactor *= math.max(0f, 1f + cloudAltitudeBias * elevation);
            surfaceFactor *= math.lerp(0.9f, 1.08f, temperature);
            if (cloudOrographicInfluence > 0f)
            {
                float latitude = math.abs(d.y);
                float westerlies = Smooth(0.35f, 0.55f, latitude) * (1f - Smooth(0.8f, 0.95f, latitude));
                // Unnormalised tangent tends to zero at the poles; there is no arbitrary polar seam.
                float3 wind = new float3(d.z, 0f, -d.x) * math.lerp(-1f, 1f, westerlies);
                float3 upwind = math.normalizesafe(d - wind * (cloudUpwindDistance / radiusMetres), d);
                float upwindHeight = EvaluateHeight(upwind);
                float delta = (math.max(h, seaLevelMetres) - math.max(upwindHeight, seaLevelMetres)) / 2000f;
                surfaceFactor *= math.max(0f, 1f + cloudOrographicInfluence * math.clamp(delta, -1f, 1f));
            }
            return math.saturate(baseDensity * math.lerp(1f, surfaceFactor, cloudTerrainInfluence));
        }

        /// <summary>Normalized solid terrain position, including the seabed below the separate ocean.</summary>
        public float3 SurfacePosition(float3 direction, float heightMetres)
        {
            float3 d = math.normalizesafe(direction, new float3(0f, 1f, 0f));
            float reference = math.rsqrt(math.csum(d * d / (referenceAxes * referenceAxes)));
            return d * (reference + DisplacementMetres(heightMetres) / radiusMetres);
        }

        /// <summary>Display displacement from the reference shape. Water level stays fixed under exaggeration.</summary>
        public float DisplacementMetres(float heightMetres)
        {
            float relief = hasOcean
                ? seaLevelMetres + (heightMetres - seaLevelMetres) * visualReliefScale
                : heightMetres * visualReliefScale;
            return relief + appearance.opticalHeight;
        }

        public float3 OceanPosition(float3 direction)
        {
            float3 d = math.normalizesafe(direction, new float3(0, 1, 0));
            float reference = math.rsqrt(math.csum(d * d / (referenceAxes * referenceAxes)));
            return d * (reference + seaLevelMetres / radiusMetres);
        }

        public float3 ReferenceNormal(float3 direction)
        {
            return math.normalize(direction / (referenceAxes * referenceAxes));
        }

        public float NormalizeHeight(float heightMetres)
        {
            return math.saturate((heightMetres - minHeightMetres) / (maxHeightMetres - minHeightMetres));
        }

        private PlanetBiome SelectBiome(float h, float temperature, float moisture)
        {
            switch (appearance.kind)
            {
                case PlanetSurfaceKind.CrateredRock:
                case PlanetSurfaceKind.DustyRock: return PlanetBiome.Rock;
                case PlanetSurfaceKind.FracturedIce: return PlanetBiome.Ice;
                case PlanetSurfaceKind.SulfurVolcanic: return PlanetBiome.Volcanic;
                case PlanetSurfaceKind.GasGiant:
                case PlanetSurfaceKind.IceGiant: return PlanetBiome.CloudTop;
                case PlanetSurfaceKind.OpaqueHaze: return PlanetBiome.Haze;
                case PlanetSurfaceKind.StellarPhotosphere: return PlanetBiome.Photosphere;
            }
            if (hasOcean && h < seaLevelMetres) return PlanetBiome.Ocean;
            if (hasIceCaps && temperature < 0.16f) return PlanetBiome.Ice;
            if (!hasEcology) return PlanetBiome.Alpine;
            if (hasOcean && h - seaLevelMetres < 100f) return PlanetBiome.Coast;
            if (h - seaLevelMetres > 3600f) return PlanetBiome.Alpine;
            if (temperature < 0.35f) return PlanetBiome.Tundra;
            if (moisture < 0.34f && temperature > 0.4f) return PlanetBiome.Desert;
            if (moisture < 0.52f) return PlanetBiome.Grassland;
            if (temperature > 0.72f && moisture > 0.65f) return PlanetBiome.Rainforest;
            return PlanetBiome.Forest;
        }

        private float3 Albedo(float3 d, float h, float temperature, float moisture, float slope, float ocean)
        {
            float e = math.max(0f, h - seaLevelMetres);
            float shelf = 1f - Smooth(30f, 950f, seaLevelMetres - h);
            float3 water = math.lerp(deepOcean, shallowOcean, shelf * 0.68f);
            float fertile = Smooth(0.34f, 0.62f, moisture);
            float3 land = hasEcology ? math.lerp(desert, grassland, fertile) : rock;
            if (hasEcology)
            {
                land = math.lerp(land, forest, Smooth(0.48f, 0.65f, moisture));
                land = math.lerp(land, rainforest, Smooth(0.65f, 0.8f, moisture) * Smooth(0.64f, 0.82f, temperature));
                land = math.lerp(land, tundra, 1f - Smooth(0.2f, 0.4f, temperature));
            }
            float rockAmount = math.max(Smooth(2100f, 4500f, e), Smooth(5f, 18f, slope) * 0.5f);
            land = math.lerp(land, rock, rockAmount * 0.78f);
            float snowAmount = hasIceCaps ? 1f - Smooth(0.12f, 0.25f, temperature) : 0f;
            land = math.lerp(land, snow, snowAmount);
            if (hasOcean)
                land = math.lerp(land, beach, (1f - Smooth(15f, 160f, e)) * (1f - snowAmount) * 0.52f);

            // Multiscale color variation follows the same planet-fixed 3D domain as the height field.
            float variation = Fbm(d * 95f + seedOffset + 21f, 5, 0.53f);
            float fine = Fbm(d * 670f + seedOffset - 9f, 3, 0.53f);
            land *= math.clamp(1f + variation * 0.38f + fine * 0.13f, 0.7f, 1.25f);
            float3 result = math.lerp(land, water, ocean);
            // Sea ice continues across the coastline; polar caps never depend on a mesh pole vertex.
            float seaIce = hasIceCaps ? (1f - Smooth(0.065f, 0.14f, temperature)) * ocean : 0f;
            return math.lerp(result, snow * (0.94f + fine * 0.04f), seaIce);
        }

        private static float Fbm(float3 p, int octaves, float persistence)
        {
            float result = 0f, amplitude = 1f, weight = 0f;
            for (int i = 0; i < octaves; i++)
            {
                result += noise.snoise(p) * amplitude;
                weight += amplitude;
                p = p * 2.03f + new float3(7.13f, 3.71f, 5.97f);
                amplitude *= persistence;
            }
            return result / weight;
        }

        private static float Ridged(float3 p, int octaves)
        {
            float result = 0f, amplitude = 1f, weight = 0f, feedback = 1f;
            for (int i = 0; i < octaves; i++)
            {
                float ridge = 1f - math.abs(noise.snoise(p));
                ridge = ridge * ridge * feedback;
                result += ridge * amplitude;
                weight += amplitude;
                feedback = math.saturate(ridge * 1.7f);
                p = p * 2.13f + new float3(2.7f, 9.3f, 1.6f);
                amplitude *= 0.52f;
            }
            return result / weight;
        }

        private static float Smooth(float a, float b, float value)
        {
            float t = math.saturate((value - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        private static float SeedUnit(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            return (x & 0x00ffffffu) / 16777215f;
        }

        private static float3 Linear(Color color)
        {
            Color linear = color.linear;
            return new float3(linear.r, linear.g, linear.b);
        }
    }
}
