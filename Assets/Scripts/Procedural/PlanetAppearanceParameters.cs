using System;
using UnityEngine;

namespace Planet.Procedural
{
    public enum PlanetSurfaceKind
    {
        Terrestrial, CrateredRock, DustyRock, SulfurVolcanic, FracturedIce,
        GasGiant, IceGiant, OpaqueHaze, StellarPhotosphere
    }

    /// <summary>Optical appearance, in body-fixed coordinates. Zero/default keeps the Terra generator.</summary>
    [Serializable]
    public sealed class PlanetAppearanceParameters
    {
        public PlanetSurfaceKind surfaceKind;
        public Color baseColor = Color.gray;
        public Color secondaryColor = Color.white;
        public Color accentColor = new Color(0.4f, 0.25f, 0.15f);
        [Min(0.1f)] public float regionFrequency = 3f;
        [Min(0f)] public float reliefMetres = 1000f;
        [Min(0.1f)] public float craterFrequency = 18f;
        [Min(0f)] public float craterDepthMetres = 1200f;
        [Range(0f, 1f)] public float craterStrength = 0.7f;
        [Range(0f, 1f)] public float fractureStrength;
        [Min(0.1f)] public float fractureFrequency = 14f;
        [Range(0.002f, 0.15f)] public float fractureWidth = 0.025f;
        [Range(0f, 1f)] public float bandContrast = 0.5f;
        [Min(1f)] public float bandCount = 14f;
        [Range(0f, 1f)] public float turbulence = 0.4f;
        [Tooltip("Absolute sine of latitude at which frost begins. 1 disables the cap.")]
        [Range(0f, 1f)] public float polarCapStart = 1f;
        [Tooltip("One illustrative large feature. It is not an observed geographic map or ephemeris.")]
        public Vector2 featureLongitudeLatitude = new Vector2(30f, -22f);
        public Vector2 featureAngularSize = new Vector2(12f, 8f);
        [Range(0f, 1f)] public float featureStrength;
        public Color featureColor = Color.white;
        [Tooltip("Visible cloud/haze altitude above the catalog's solid reference surface; not ground relief.")]
        [Min(0f)] public float opticalSurfaceHeightMetres;
        [Min(0f)] public float emissionStrength;
        [Range(0f, 1f)] public float limbDarkening;
        [Range(0f, 1f)] public float landSmoothness = 0.12f;
        public Color atmosphereColor = new Color(0.28f, 0.62f, 1f);
        [Range(0f, 2f)] public float atmosphereDensity = 0.22f;
        public Color ringColor = new Color(0.85f, 0.79f, 0.66f);
        [Range(0f, 1f)] public float ringOpacity = 0.8f;
        [Tooltip("Known limitations and the observational interpretation of this preset.")]
        [TextArea(2, 6)] public string interpretation;

        public void Validate()
        {
            regionFrequency = Mathf.Max(0.1f, regionFrequency);
            reliefMetres = Mathf.Max(0f, reliefMetres);
            craterFrequency = Mathf.Max(0.1f, craterFrequency);
            craterDepthMetres = Mathf.Max(0f, craterDepthMetres);
            craterStrength = Mathf.Clamp01(craterStrength);
            fractureFrequency = Mathf.Max(0.1f, fractureFrequency);
            fractureStrength = Mathf.Clamp01(fractureStrength);
            fractureWidth = Mathf.Clamp(fractureWidth, 0.002f, 0.15f);
            polarCapStart = Mathf.Clamp01(polarCapStart);
            bandCount = Mathf.Max(1f, bandCount);
            bandContrast = Mathf.Clamp01(bandContrast);
            turbulence = Mathf.Clamp01(turbulence);
            featureAngularSize.x = Mathf.Clamp(featureAngularSize.x, 0.5f, 80f);
            featureAngularSize.y = Mathf.Clamp(featureAngularSize.y, 0.5f, 80f);
            featureLongitudeLatitude.y = Mathf.Clamp(featureLongitudeLatitude.y, -90f, 90f);
            featureStrength = Mathf.Clamp01(featureStrength);
            opticalSurfaceHeightMetres = Mathf.Max(0f, opticalSurfaceHeightMetres);
            emissionStrength = Mathf.Max(0f, emissionStrength);
            limbDarkening = Mathf.Clamp01(limbDarkening);
            landSmoothness = Mathf.Clamp01(landSmoothness);
            atmosphereDensity = Mathf.Clamp(atmosphereDensity, 0f, 2f);
            ringOpacity = Mathf.Clamp01(ringOpacity);
        }
    }
}
