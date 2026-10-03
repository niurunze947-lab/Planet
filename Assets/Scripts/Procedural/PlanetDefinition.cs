using System;
using UnityEngine;

namespace Planet.Procedural
{
    [Serializable]
    public sealed class PlanetPhysicalParameters
    {
        [Min(1000f)] public double radiusMetres = 6371000.0;
        [Min(0f)] public double massKilograms = 5.9722e24;
        [Tooltip("Signed sidereal period. Negative values mean retrograde rotation.")]
        public double rotationPeriodSeconds = 86164.09056;
        [Range(0f, 0.2f)] public float flattening = 1f / 298.257f;
        [Tooltip("IAU pole orientation, in the same equatorial frame as the solar catalog.")]
        public double northPoleRightAscensionDegrees;
        public double northPoleDeclinationDegrees = 90.0;
        public float seaLevelMetres;
        [Min(0f)] public float atmosphereHeightMetres = 80000f;

        public double SurfaceGravity { get { return 6.67430e-11 * massKilograms / (radiusMetres * radiusMetres); } }
    }

    [Serializable]
    public sealed class PlanetGenerationParameters
    {
        public int seed = 271828;
        [Header("Continents — frequencies are cycles per unit sphere")]
        [Range(0.5f, 5f)] public float continentFrequency = 1.65f;
        [Range(-0.5f, 0.5f)] public float continentThreshold = 0.08f;
        [Range(0f, 0.8f)] public float domainWarp = 0.32f;
        [Range(2, 8)] public int continentOctaves = 6;
        [Min(0f)] public float oceanDepthMetres = 7200f;
        [Min(0f)] public float plateauHeightMetres = 1600f;

        [Header("Relief")]
        [Min(0.1f)] public float mountainFrequency = 12f;
        [Min(0f)] public float mountainHeightMetres = 6200f;
        [Range(2, 8)] public int mountainOctaves = 6;
        [Min(0.1f)] public float detailFrequency = 140f;
        [Min(0f)] public float detailHeightMetres = 220f;
        [Tooltip("Fixed physical distance for slope/normal evaluation, independent of mesh resolution.")]
        [Min(10f)] public float slopeSampleDistanceMetres = 2200f;

        [Header("Climate")]
        [Range(-0.3f, 0.3f)] public float temperatureOffset;
        [Range(-0.3f, 0.3f)] public float moistureOffset = 0.02f;
        [Min(0f)] public float altitudeCoolingPerKilometre = 0.085f;
    }

    [Serializable]
    public sealed class PlanetPalette
    {
        public Color deepOcean = new Color(0.025f, 0.065f, 0.24f);
        public Color shallowOcean = new Color(0.035f, 0.32f, 0.39f);
        public Color beach = new Color(0.72f, 0.66f, 0.46f);
        public Color desert = new Color(0.79f, 0.64f, 0.40f);
        public Color grassland = new Color(0.42f, 0.46f, 0.22f);
        public Color forest = new Color(0.25f, 0.35f, 0.16f);
        public Color rainforest = new Color(0.13f, 0.27f, 0.13f);
        public Color tundra = new Color(0.48f, 0.48f, 0.35f);
        public Color rock = new Color(0.42f, 0.39f, 0.33f);
        public Color snow = new Color(0.89f, 0.93f, 0.94f);
    }

    /// <summary>Authoritative physical and procedural input. Meshes and textures are derived caches.</summary>
    [CreateAssetMenu(menuName = "Planet/Planet Definition", fileName = "PlanetDefinition")]
    public sealed class PlanetDefinition : ScriptableObject
    {
        public const int GeneratorVersion = 1;
        public string displayName = "Procedural Terra";
        public PlanetPhysicalParameters physical = new PlanetPhysicalParameters();
        public PlanetGenerationParameters generation = new PlanetGenerationParameters();
        public PlanetPalette palette = new PlanetPalette();

        public PlanetSurfaceSampler CreateSampler()
        {
            ValidateParameters();
            return new PlanetSurfaceSampler(this);
        }

        public string ContentFingerprint()
        {
            ValidateParameters();
            return Hash128.Compute(GeneratorVersion + ":" + JsonUtility.ToJson(physical) + ":" +
                JsonUtility.ToJson(generation) + ":" + JsonUtility.ToJson(palette)).ToString();
        }

        public void ValidateParameters()
        {
            if (physical == null) physical = new PlanetPhysicalParameters();
            if (generation == null) generation = new PlanetGenerationParameters();
            if (palette == null) palette = new PlanetPalette();
            physical.radiusMetres = Math.Max(1000.0, physical.radiusMetres);
            physical.massKilograms = Math.Max(0.0, physical.massKilograms);
            physical.flattening = Mathf.Clamp(physical.flattening, 0f, 0.2f);
            physical.atmosphereHeightMetres = Mathf.Max(0f, physical.atmosphereHeightMetres);
            generation.continentFrequency = Mathf.Max(0.1f, generation.continentFrequency);
            generation.continentOctaves = Mathf.Clamp(generation.continentOctaves, 2, 8);
            generation.mountainOctaves = Mathf.Clamp(generation.mountainOctaves, 2, 8);
            generation.mountainFrequency = Mathf.Max(0.1f, generation.mountainFrequency);
            generation.detailFrequency = Mathf.Max(0.1f, generation.detailFrequency);
            generation.domainWarp = Mathf.Clamp(generation.domainWarp, 0f, 0.8f);
            generation.oceanDepthMetres = Mathf.Max(1f, generation.oceanDepthMetres);
            generation.plateauHeightMetres = Mathf.Max(0f, generation.plateauHeightMetres);
            generation.mountainHeightMetres = Mathf.Max(0f, generation.mountainHeightMetres);
            generation.detailHeightMetres = Mathf.Max(0f, generation.detailHeightMetres);
            generation.slopeSampleDistanceMetres = Mathf.Clamp(generation.slopeSampleDistanceMetres,
                10f, (float)physical.radiusMetres * 0.02f);
        }

        private void OnValidate() { ValidateParameters(); }
    }
}
