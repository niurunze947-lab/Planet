using Unity.Mathematics;
using UnityEngine;

namespace Planet.Procedural
{
    /// <summary>Blittable, deterministic optical fields. No textures, arrays, time or Unity objects.</summary>
    public struct PlanetAppearanceSampler
    {
        public readonly PlanetSurfaceKind kind;
        public readonly float opticalHeight;
        private readonly float3 offset, primary, secondary, accent, featureColor;
        private readonly float regionFrequency, relief, craterFrequency, craterDepth, craterStrength;
        private readonly float fractureFrequency, fractureWidth, fractureStrength;
        private readonly float bandCount, contrast, turbulence, capStart, featureStrength;
        private readonly float3 featureCentre, featureEast, featureNorth;
        private readonly float2 featureSize;

        public PlanetAppearanceSampler(PlanetAppearanceParameters p, float3 seedOffset)
        {
            kind = p.surfaceKind;
            opticalHeight = p.opticalSurfaceHeightMetres;
            offset = seedOffset;
            primary = Linear(p.baseColor); secondary = Linear(p.secondaryColor);
            accent = Linear(p.accentColor); featureColor = Linear(p.featureColor);
            regionFrequency = p.regionFrequency; relief = p.reliefMetres;
            craterFrequency = p.craterFrequency; craterDepth = p.craterDepthMetres;
            craterStrength = p.craterStrength;
            fractureFrequency = p.fractureFrequency; fractureWidth = p.fractureWidth;
            fractureStrength = p.fractureStrength;
            bandCount = p.bandCount; contrast = p.bandContrast; turbulence = p.turbulence;
            capStart = p.polarCapStart; featureStrength = p.featureStrength;
            float lon = math.radians(p.featureLongitudeLatitude.x);
            float lat = math.radians(p.featureLongitudeLatitude.y);
            featureCentre = new float3(math.cos(lat) * math.cos(lon), math.sin(lat), math.cos(lat) * math.sin(lon));
            featureEast = new float3(-math.sin(lon), 0f, math.cos(lon));
            featureNorth = math.cross(featureEast, featureCentre);
            featureSize = math.radians(new float2(p.featureAngularSize.x, p.featureAngularSize.y));
        }

        public float Height(float3 d)
        {
            if (IsOpticalShell) return 0f;
            float regional = Fbm(d * regionFrequency + offset) * relief;
            if (craterStrength > 0f)
            {
                float q = noise.cellular(d * craterFrequency + offset + 41f).x;
                // A smooth excavation bowl and raised rim from the same field used by Albedo.
                float bowl = 1f - math.smoothstep(0.12f, 0.58f, q);
                float rim = math.exp(-math.pow((q - 0.60f) / 0.085f, 2f));
                regional += craterDepth * craterStrength * (-bowl + rim * 0.30f);
            }
            if (fractureStrength > 0f)
                regional -= Fractures(d) * relief * 0.22f;
            return regional;
        }

        public bool IsOpticalShell
        {
            get { return kind == PlanetSurfaceKind.GasGiant || kind == PlanetSurfaceKind.IceGiant ||
                kind == PlanetSurfaceKind.OpaqueHaze || kind == PlanetSurfaceKind.StellarPhotosphere; }
        }

        public float3 Albedo(float3 d)
        {
            if (kind == PlanetSurfaceKind.GasGiant || kind == PlanetSurfaceKind.IceGiant)
                return Gas(d);
            if (kind == PlanetSurfaceKind.StellarPhotosphere)
            {
                float grain = noise.cellular(d * 105f + offset).x;
                float3 color = primary * (0.65f + 0.35f * math.smoothstep(0.08f, 0.80f, grain));
                return math.lerp(color, featureColor, Feature(d) * featureStrength);
            }
            if (kind == PlanetSurfaceKind.OpaqueHaze)
            {
                float flow = Fbm(d * 6f + offset + new float3(0f, d.y * 18f, 0f));
                return math.lerp(primary, secondary, math.saturate(0.5f + flow * contrast));
            }

            float region = math.smoothstep(-0.23f, 0.24f, Fbm(d * regionFrequency + offset));
            float3 land = math.lerp(primary, secondary, region);
            if (kind == PlanetSurfaceKind.SulfurVolcanic)
            {
                float q = noise.cellular(d * craterFrequency + offset + 41f).x;
                float caldera = 1f - math.smoothstep(0.09f, 0.23f, q);
                float sulfur = math.exp(-math.pow((q - 0.28f) / 0.065f, 2f));
                float active = math.smoothstep(-0.05f, 0.32f, noise.snoise(d * 9f + offset + 84f));
                land = math.lerp(land, accent, sulfur * active * 0.72f);
                land = math.lerp(land, new float3(0.018f, 0.012f, 0.008f), caldera * active * 0.90f);
            }
            else if (craterStrength > 0f)
            {
                float q = noise.cellular(d * craterFrequency + offset + 41f).x;
                float floor = 1f - math.smoothstep(0.10f, 0.50f, q);
                float rim = math.exp(-math.pow((q - 0.60f) / 0.10f, 2f));
                land *= 1f - floor * 0.22f * craterStrength + rim * 0.22f * craterStrength;
            }
            if (fractureStrength > 0f) land = math.lerp(land, accent, Fractures(d));
            if (capStart < 0.999f)
            {
                float capLatitude = math.abs(d.y);
                float edge = Fbm(d * 14f + offset + 57f) * 0.018f;
                land = math.lerp(land, accent, math.smoothstep(capStart, math.min(1f, capStart + 0.055f), capLatitude + edge));
            }
            land *= 1f + Fbm(d * 64f + offset - 21f) * 0.13f;
            return math.lerp(land, featureColor, Feature(d) * featureStrength);
        }

        private float3 Gas(float3 d)
        {
            float latitude = math.asin(math.clamp(d.y, -1f, 1f));
            // Continuous 3D turbulence avoids longitude seams and a pinched UV pole.
            float eddies = Fbm(d * 24f + offset);
            float wave = math.sin(latitude * bandCount + eddies * turbulence * 2.6f);
            float band = math.smoothstep(-0.55f, 0.60f, wave);
            float3 color = math.lerp(primary, secondary, 0.5f + (band - 0.5f) * contrast);
            color *= 1f + eddies * turbulence * 0.22f;
            float feature = Feature(d);
            float swirl = 0.87f + 0.13f * math.sin(feature * 27f + eddies * 5f);
            color = math.lerp(color, featureColor * swirl, feature * featureStrength);
            if (kind == PlanetSurfaceKind.IceGiant)
            {
                float streak = math.pow(math.saturate(noise.snoise(d * new float3(18f, 60f, 18f) + offset - 9f)), 8f);
                color = math.lerp(color, accent, streak * turbulence * 0.45f);
            }
            return math.max(color, new float3(0f));
        }

        private float Fractures(float3 d)
        {
            float2 cells = noise.cellular(d * fractureFrequency + offset - 71f);
            return (1f - math.smoothstep(fractureWidth * 0.2f, fractureWidth, cells.y - cells.x)) * fractureStrength;
        }

        private float Feature(float3 d)
        {
            if (featureStrength <= 0f || math.dot(d, featureCentre) <= 0f) return 0f;
            float2 angles = new float2(math.atan2(math.dot(d, featureEast), math.dot(d, featureCentre)),
                math.atan2(math.dot(d, featureNorth), math.dot(d, featureCentre)));
            float2 q = angles / featureSize;
            return 1f - math.smoothstep(0.65f, 1f, math.length(q));
        }

        private static float Fbm(float3 p)
        {
            return (noise.snoise(p) + noise.snoise(p * 2.03f + 17f) * 0.5f +
                noise.snoise(p * 4.13f - 11f) * 0.25f) / 1.75f;
        }

        private static float3 Linear(Color c)
        {
            Color l = c.linear;
            return new float3(l.r, l.g, l.b);
        }
    }
}
