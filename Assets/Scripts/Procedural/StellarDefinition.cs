using UnityEngine;

namespace Planet.Procedural
{
    /// <summary>A celestial body with radiative/dynamic photosphere parameters, independent of planet biomes.</summary>
    [CreateAssetMenu(menuName = "Planet/Stellar Definition", fileName = "StellarDefinition")]
    public sealed class StellarDefinition : CelestialDefinition
    {
        public const int GeneratorVersion = 1;
        [Header("Photosphere and blackbody radiation")]
        public float effectiveTemperatureKelvin = 5778f;
        public float granuleTemperatureContrastKelvin = 260f;
        public float limbCoolingKelvin = 380f;
        [Range(0f, 1f)] public float limbLinear = 0.48f;
        [Range(0f, 1f)] public float limbQuadratic = 0.18f;
        [Tooltip("Fixed display normalization for HDR inspection, not physical automatic exposure.")]
        public float displayRadianceScale = 0.95f;
        [Header("Focused observation presentation")]
        [Tooltip("Exposure applied only to the detailed photosphere when Sun is the camera focus.")]
        [Range(-8f, 0f)] public float focusedExposureEV = -0.45f;
        [Tooltip("Warm H-alpha-like observation response. Physical blackbody temperature remains unchanged.")]
        public Color focusedSpectralResponse = new Color(1f, 0.12f, 0.018f, 1f);
        [Range(0f, 1f)] public float focusedSpectralMix = 0.58f;
        [Range(0.5f, 3f)] public float focusedGranulationBoost = 2.0f;
        [Range(0f, 2f)] public float focusedLimbGlow = 0.5f;
        [Header("Convection: physical spatial scales, real-time inspection clock")]
        public int seed = 314159;
        public float granuleDiameterKilometres = 900f;
        public float supergranuleDiameterKilometres = 30000f;
        public float granuleLifetimeSeconds = 600f;
        public float supergranuleLifetimeSeconds = 86400f;
        public float normalReliefMetres = 100f;
        [Header("Differential rotation / activity: simulation days")]
        public float equatorialRotationDays = 27f;
        public float polarRotationDays = 37f;
        [Range(0, 16)] public int spotCount = 8;
        public float spotLifetimeDays = 12f;
        public float spotAngularRadiusDegrees = 2f;
        public float umbraTemperatureKelvin = 3800f;
        public float penumbraTemperatureKelvin = 4800f;
        [Header("Reserved observer/outer-layer inputs; no outer-layer rendering in this iteration")]
        public StellarOuterLayerOptions outerLayers = new StellarOuterLayerOptions();

        public void ValidateParameters()
        {
            if (physical == null) physical = new PlanetPhysicalParameters();
            physical.radiusMetres = System.Math.Max(1000, physical.radiusMetres);
            physical.massKilograms = System.Math.Max(0, physical.massKilograms);
            effectiveTemperatureKelvin = Mathf.Clamp(effectiveTemperatureKelvin, 4500f, 8000f);
            granuleDiameterKilometres = Mathf.Clamp(granuleDiameterKilometres, 200f, 2000f);
            supergranuleDiameterKilometres = Mathf.Max(10000f, supergranuleDiameterKilometres);
            granuleLifetimeSeconds = Mathf.Max(30f, granuleLifetimeSeconds);
            supergranuleLifetimeSeconds = Mathf.Max(3600f, supergranuleLifetimeSeconds);
            equatorialRotationDays = Mathf.Max(1f, equatorialRotationDays);
            polarRotationDays = Mathf.Max(equatorialRotationDays, polarRotationDays);
            limbLinear = Mathf.Clamp01(limbLinear); limbQuadratic = Mathf.Clamp(limbQuadratic, 0, 1f - limbLinear);
            spotCount = Mathf.Clamp(spotCount, 0, 16);
            spotLifetimeDays = Mathf.Max(1f, spotLifetimeDays);
            spotAngularRadiusDegrees = Mathf.Clamp(spotAngularRadiusDegrees, 0.25f, 6f);
            umbraTemperatureKelvin = Mathf.Clamp(umbraTemperatureKelvin, 3000f, 4500f);
            penumbraTemperatureKelvin = Mathf.Clamp(penumbraTemperatureKelvin, umbraTemperatureKelvin, effectiveTemperatureKelvin);
            displayRadianceScale = Mathf.Max(0.001f, displayRadianceScale);
            focusedExposureEV = Mathf.Clamp(focusedExposureEV, -8f, 0f);
            focusedSpectralMix = Mathf.Clamp01(focusedSpectralMix);
            focusedGranulationBoost = Mathf.Clamp(focusedGranulationBoost, 0.5f, 3f);
            focusedLimbGlow = Mathf.Clamp(focusedLimbGlow, 0f, 2f);
        }
        public override string ContentFingerprint()
        {
            ValidateParameters();
            return Hash128.Compute("Stellar:" + GeneratorVersion + ":" + StellarRadiation.SpectrumVersion + ":" + JsonUtility.ToJson(this)).ToString();
        }
        private void OnValidate() { ValidateParameters(); }
    }

    [System.Serializable]
    public sealed class StellarOuterLayerOptions
    {
        public bool chromosphere, corona, flares, prominences;
        public float chromosphereHeightMetres = 2000000f;
        public float coronaOuterRadiusRatio = 3f;
        [Tooltip("Interface placeholder for a future observer atmosphere integrator; currently unused.")]
        public bool observerAtmosphereEnabled;
    }
}
