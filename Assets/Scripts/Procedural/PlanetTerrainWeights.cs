using Unity.Mathematics;

namespace Planet.Procedural
{
    /// <summary>Base terrain weights plus a top snow layer, shared by rendering and ecology placement.</summary>
    public struct PlanetTerrainWeights
    {
        public float grass;
        public float sand;
        public float rock;
        public float snow;

        public float4 Channels => new float4(grass, sand, rock, snow);

        /// <summary>Grass, desert and snow scatter masks; rock never emits vegetation.</summary>
        public float3 VegetationWeights => new float3(
            grass * (1f - snow), sand * (1f - snow), snow);

        public static PlanetTerrainWeights FromClimate(bool hasEcology, bool hasIceCaps, bool hasOcean,
            float heightMetres, float seaLevelMetres, float temperature, float moisture, float slopeDegrees,
            float snowLineTemperature, float snowBlendRange, float snowSlopeStartDegrees,
            float snowSlopeFullDegrees, float desertMoistureStart, float desertMoistureFull,
            float rockSlopeStartDegrees, float rockSlopeFullDegrees,
            float rockLineStartMetres, float rockLineFullMetres)
        {
            float elevation = math.max(0f, heightMetres - seaLevelMetres);
            float steepRock = Smooth(rockSlopeStartDegrees, rockSlopeFullDegrees, slopeDegrees);
            float highRock = Smooth(rockLineStartMetres, rockLineFullMetres, elevation) * 0.72f;
            float rock = math.max(steepRock, highRock);
            float sand;
            float grass;

            if (hasOcean && heightMetres < seaLevelMetres)
            {
                grass = 0f;
                sand = 1f - rock;
            }
            else
            {
                float warm = Smooth(0.22f, 0.56f, temperature);
                float dry = 1f - Smooth(desertMoistureStart, desertMoistureFull, moisture);
                sand = warm * dry;
                float shoreline = 1f - Smooth(0f, 280f, elevation);
                sand = math.max(sand, shoreline * warm * 0.55f);
                grass = hasEcology ? 1f - sand : 0f;
                if (!hasEcology) sand = 1f;
                grass *= 1f - rock;
                sand *= 1f - rock;
            }

            float baseTotal = grass + sand + rock;
            if (baseTotal > 1e-5f)
            {
                grass /= baseTotal;
                sand /= baseTotal;
                rock /= baseTotal;
            }
            else
            {
                grass = 0f;
                sand = 0f;
                rock = 1f;
            }

            float snowTemperature = 1f - Smooth(snowLineTemperature - snowBlendRange,
                snowLineTemperature + snowBlendRange, temperature);
            float slopeMask = 1f - Smooth(snowSlopeStartDegrees, snowSlopeFullDegrees, slopeDegrees);
            float snow = hasIceCaps && (!hasOcean || heightMetres >= seaLevelMetres)
                ? math.saturate(snowTemperature * slopeMask)
                : 0f;

            return new PlanetTerrainWeights { grass = grass, sand = sand, rock = rock, snow = snow };
        }

        private static float Smooth(float start, float end, float value)
        {
            float t = math.saturate((value - start) / math.max(end - start, 1e-4f));
            return t * t * (3f - 2f * t);
        }
    }
}
