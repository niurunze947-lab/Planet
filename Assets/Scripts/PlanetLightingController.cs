using UnityEngine;
using Planet.Solar;
using Planet.Procedural;
using Unity.Mathematics;

/// <summary>
/// Synchronizes the sun direction and exposure used by the physical starfield. The exposure is
/// driven by a projected surface-luminance estimate of the focused planet. Runtime exposure is
/// a global shader value; the shared skybox material asset is never edited during adaptation.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class PlanetLightingController : MonoBehaviour
{
    public Light sun;
    public Material starfieldMaterial;
    [Range(0.02f, 1f)] public float updateInterval = 0.12f;
    [Range(0.02f, 4f)] public float referenceSurfaceExposure = 1f;
    [Range(0.02f, 4f)] public float minimumSurfaceExposure = 0.055f;
    [Range(0.02f, 16f)] public float maximumSurfaceExposure = 16f;
    [Range(0f, 1f)] public float exposureResponse = 0.18f;
    [Header("Focused Sun presentation")]
    [Tooltip("Skybox EV while the Sun fills the view. This is deliberately separate from photosphere EV.")]
    [Range(-12f, 0f)] public float sunFocusSkyExposureEV = -8f;
    [Range(0.05f, 1f)] public float sunFocusSkyCoverageThreshold = 0.35f;

    private float _nextUpdate;
    private float _lastUpdate;
    private float _skyExposureEV;
    private PlanetSurfaceView _sampledView;
    private string _sampledFingerprint;
    private Vector3[] _sampleNormals;
    private float[] _sampleAlbedo;
    private PlanetAtmosphere[] _atmospheres;
    private PlanetCloudLayer[] _clouds;
    private float _nextDiscovery;

    private static readonly int SkyExposureId = Shader.PropertyToID("_PlanetSkyExposureEV");
    private const int MeterSamples = 96;

    public float CurrentSkyExposureEV { get { return _skyExposureEV; } }
    public float MeteredSurfaceBrightness { get; private set; }

    private void OnEnable() { RefreshLighting(true); }
    private void OnDisable() { Shader.SetGlobalFloat(SkyExposureId, 0f); }

    private void LateUpdate()
    {
        if (sun == null) sun = GetComponent<Light>();
        if (Time.realtimeSinceStartup < _nextUpdate) return;
        RefreshLighting(false);
        _nextUpdate = Time.realtimeSinceStartup + Mathf.Max(0.02f, updateInterval);
    }

    public void RefreshLighting(bool immediate = false)
    {
        if (sun == null) sun = GetComponent<Light>();
        Vector3 direction = IncomingLightDirection();
        SolarSystemSimulation sim = SolarSystemSimulation.Instance;
        float coverage;
        MeteredSurfaceBrightness = EstimateSurfaceBrightness(sim, direction, out coverage);
        bool sunFocused = sim != null && sim.FocusDefinition != null && sim.FocusDefinition.IsSun;
        float targetEV;
        if (sunFocused && coverage >= sunFocusSkyCoverageThreshold)
        {
            // A close solar disc should be judged like a bright-field observation: the HDR
            // photosphere remains visible while the starfield falls almost to black.
            // Once the Sun is a meaningful focused subject, keep this dark-field exposure
            // stable across the normal 2x-5x framing range instead of letting stars reappear
            // as the user backs the camera away by a small amount.
            targetEV = sunFocusSkyExposureEV;
        }
        else
        {
            float ratio = Mathf.Clamp(MeteredSurfaceBrightness / 0.02f,
                Mathf.Max(minimumSurfaceExposure, 0.02f), Mathf.Max(maximumSurfaceExposure, 0.02f));
            targetEV = Mathf.Clamp(Mathf.Log(Mathf.Max(referenceSurfaceExposure, 0.02f) / ratio, 2f), -4f, 1.5f) * coverage;
        }
        float now = Time.realtimeSinceStartup;
        float elapsed = Mathf.Max(0f, now - _lastUpdate);
        float response = 1f - Mathf.Pow(1f - Mathf.Clamp01(exposureResponse), elapsed / 0.12f);
        _skyExposureEV = Mathf.Lerp(_skyExposureEV, targetEV, immediate ? 1f : response);
        Shader.SetGlobalFloat(SkyExposureId, _skyExposureEV);
        _lastUpdate = now;

        if (immediate || _atmospheres == null || now >= _nextDiscovery)
        {
            _atmospheres = FindObjectsOfType<PlanetAtmosphere>();
            _clouds = FindObjectsOfType<PlanetCloudLayer>();
            _nextDiscovery = now + 2f;
        }
        for (int i = 0; i < _atmospheres.Length; i++)
            if (_atmospheres[i] != null) _atmospheres[i].SetSunDirection(direction);
        for (int i = 0; i < _clouds.Length; i++)
            if (_clouds[i] != null) _clouds[i].SetSunDirection(direction);
    }

    private Vector3 IncomingLightDirection()
    {
        return sun != null ? -sun.transform.forward : new Vector3(-0.45f, 0.30f, -0.84f).normalized;
    }

    private float EstimateSurfaceBrightness(SolarSystemSimulation sim, Vector3 lightDirection, out float coverage)
    {
        coverage = 0f;
        if (sim == null || sim.FocusDefinition == null || sim.FocusView == null)
            return 0.02f;

        float radius = Mathf.Max(sim.FocusRadiusUnits, 1e-5f);
        Camera camera = sim.ViewCamera != null ? sim.ViewCamera : Camera.main;
        if (camera == null) return 0.02f;
        Transform body = sim.FocusView.transform;
        Vector3 offset = camera.transform.position - body.position;
        float distance = offset.magnitude;
        Vector3 viewport = camera.WorldToViewportPoint(body.position);
        if (viewport.z <= 0f || viewport.x < -0.2f || viewport.x > 1.2f || viewport.y < -0.2f || viewport.y > 1.2f)
            return 0.02f;
        float angularRadius = Mathf.Asin(Mathf.Clamp01(radius / Mathf.Max(distance, radius)));
        float screenRadius = Mathf.Tan(angularRadius) / Mathf.Max(Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad), 0.001f);
        coverage = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(screenRadius));

        PlanetSurfaceView surface = body.GetComponent<PlanetSurfaceView>();
        if (sim.FocusDefinition.IsSun)
        {
            StellarSurfaceView star = body.GetComponent<StellarSurfaceView>();
            return star != null && star.definition != null ? star.definition.displayRadianceScale : 0.95f;
        }
        Vector3 viewLocal = body.InverseTransformDirection(offset.normalized);
        Vector3 sunLocal = body.InverseTransformDirection(lightDirection);
        float lightLuminance = sun != null ? Luminance(sun.color.linear) * sun.intensity : 1.35f;
        if (surface == null || surface.definition == null)
        {
            float phase = Mathf.Clamp01(Vector3.Dot(viewLocal, sunLocal) * 0.5f + 0.5f);
            return 0.18f * lightLuminance * phase + 0.002f;
        }

        string fingerprint = surface.definition.ContentFingerprint();
        if (_sampledView != surface || _sampledFingerprint != fingerprint || _sampleNormals == null)
        {
            PlanetSurfaceSampler sampler = surface.definition.CreateSampler();
            _sampleNormals = new Vector3[MeterSamples];
            _sampleAlbedo = new float[MeterSamples];
            for (int i = 0; i < MeterSamples; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / MeterSamples;
                float r = Mathf.Sqrt(1f - y * y);
                float a = i * 2.39996323f;
                float3 d = new float3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));
                PlanetSurfaceSample sample = sampler.Evaluate(d);
                _sampleNormals[i] = (Vector3)math.normalize(math.lerp(sample.normal, d, sample.ocean));
                _sampleAlbedo[i] = math.dot(sample.albedoLinear, new float3(0.2126f, 0.7152f, 0.0722f));
            }
            _sampledView = surface;
            _sampledFingerprint = fingerprint;
        }

        float weightedLight = 0f, totalWeight = 0f;
        for (int i = 0; i < MeterSamples; i++)
        {
            float weight = Mathf.Max(0f, Vector3.Dot(_sampleNormals[i], viewLocal));
            float irradiance = Mathf.Max(0f, Vector3.Dot(_sampleNormals[i], sunLocal)) * lightLuminance;
            weightedLight += weight * _sampleAlbedo[i] * (irradiance + 0.012f);
            totalWeight += weight;
        }
        return weightedLight / Mathf.Max(totalWeight, 0.001f);
    }

    private static float Luminance(Color color)
    {
        return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
    }
}
