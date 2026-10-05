using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Planet.Procedural
{
    /// <summary>Independent emissive stellar renderer. It consumes observation state, never a planet sampler.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class StellarSurfaceView : MonoBehaviour
    {
        public StellarDefinition definition;
        public Texture2D blackbodyLut;
        public Material photosphereMaterial;
        public Material distantMaterial;
        public bool IsDetailed { get; private set; }
        public Transform SurfaceTransform { get { return surface; } }
        public Mesh Mesh { get { return filter != null ? filter.sharedMesh : null; } }
        private Transform surface;
        private MeshFilter filter;
        private MeshRenderer surfaceRenderer;
        private MaterialPropertyBlock properties;
        private float radius, rigidSpin;

        private void OnEnable()
        {
            IPlanetRadiusProvider provider = GetComponent<IPlanetRadiusProvider>();
            if (provider != null) radius = provider.RadiusUnits;
            if (definition != null) BuildSurface(radius);
        }
        public void BuildSurface(float radiusUnits)
        {
            if (definition == null) return;
            definition.ValidateParameters();
            surface = transform.Find("Surface");
            if (surface == null) { var go = new GameObject("Surface"); go.transform.SetParent(transform, false); surface = go.transform; }
            filter = surface.GetComponent<MeshFilter>(); if (filter == null) filter = surface.gameObject.AddComponent<MeshFilter>();
            surfaceRenderer = surface.GetComponent<MeshRenderer>(); if (surfaceRenderer == null) surfaceRenderer = surface.gameObject.AddComponent<MeshRenderer>();
            surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off; surfaceRenderer.receiveShadows = false;
            surfaceRenderer.lightProbeUsage = LightProbeUsage.Off; surfaceRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            SetRadius(radiusUnits); SetDetailed(IsDetailed);
        }
        public void SetRadius(float radiusUnits)
        {
            radius = Mathf.Max(radiusUnits, 1e-9f);
            if (surface != null) { surface.localPosition = Vector3.zero; surface.localScale = Vector3.one * radius; }
        }
        public void SetRigidSpin(float degrees)
        {
            rigidSpin = degrees;
            if (surface != null) surface.localRotation = Quaternion.AngleAxis(-rigidSpin, Vector3.up);
        }
        private void SetDetailed(bool detailed)
        {
            IsDetailed = detailed;
            if (surfaceRenderer == null) return;
            filter.sharedMesh = StellarIcosphere.Get(detailed ? 6 : 3);
            surfaceRenderer.sharedMaterial = detailed ? photosphereMaterial : distantMaterial;
        }
        public void UpdateObservation(bool focusedOnStar, double simulationDays, double convectionSeconds)
        {
            if (definition == null) return;
            if (surfaceRenderer == null) BuildSurface(radius);
            if (IsDetailed != focusedOnStar || surfaceRenderer.sharedMaterial == null) SetDetailed(focusedOnStar);
            if (!IsDetailed) { surfaceRenderer.SetPropertyBlock(null); return; }
            if (properties == null) properties = new MaterialPropertyBlock();
            double equatorial = 2.0 * Math.PI / definition.equatorialRotationDays;
            double polar = 2.0 * Math.PI / definition.polarRotationDays;
            properties.SetFloat("_EquatorialPhase", (float)Math.IEEERemainder(simulationDays * equatorial, 2.0 * Math.PI));
            properties.SetFloat("_RotationShear", (float)(simulationDays * (polar - equatorial)));
            properties.SetFloat("_ActivityDays", (float)simulationDays);
            properties.SetFloat("_FocusedObservation", focusedOnStar ? 1f : 0f);
            properties.SetFloat("_FocusExposureEV", focusedOnStar ? definition.focusedExposureEV : 0f);
            properties.SetColor("_FocusSpectralResponse", definition.focusedSpectralResponse);
            properties.SetFloat("_FocusSpectralMix", focusedOnStar ? definition.focusedSpectralMix : 0f);
            properties.SetFloat("_FocusedGranulationBoost", focusedOnStar ? definition.focusedGranulationBoost : 1f);
            properties.SetFloat("_FocusLimbGlow", focusedOnStar ? definition.focusedLimbGlow : 0f);
            properties.SetVector("_ConvectionPhase", new Vector4((float)(convectionSeconds / definition.granuleLifetimeSeconds),
                (float)(convectionSeconds / definition.supergranuleLifetimeSeconds), 0, 0));
            surfaceRenderer.SetPropertyBlock(properties);
        }
    }
}
