using UnityEngine;

namespace Planet.Procedural
{
/// <summary>
/// Adds a thin, transparent atmosphere shell around a displaced planet mesh.
/// The shell is generated in edit mode and is intentionally not serialized as a second mesh.
/// This keeps the scene lightweight while still making the effect visible in the editor and play mode.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class PlanetAtmosphere : MonoBehaviour
{
    [Header("Optional planet feature")]
    public PlanetDefinition planetDefinition;
    [Header("Shell")]
    public Material atmosphereMaterial;
    [Tooltip("When enabled, a sibling CelestialBody supplies the current floating-origin radius.")]
    public bool autoSyncPlanetRadius = true;
    [Min(1e-9f)] public float planetRadius = 1f;
    [Tooltip("Used when the definition has no atmosphere height or automatic scaling is disabled.")]
    [Range(1.001f, 1.20f)] public float shellRadius = 1.0125f;

    [Header("Scattering")]
    [Range(0f, 2f)] public float density = 0.62f;
    [Range(0.5f, 8f)] public float horizonFalloff = 2.4f;
    [Range(0f, 2f)] public float dayScatter = 0.8f;
    [Range(0f, 1f)] public float nightScatter = 0.025f;
    public Color atmosphereColor = new Color(0.28f, 0.62f, 1f, 1f);
    public Color sunScatterColor = new Color(0.55f, 0.78f, 1f, 1f);
    public Color twilightColor = new Color(1f, 0.65f, 0.40f, 1f);
    [Range(0f, 1f)] public float twilightStrength = 0.08f;

    private GameObject _shell;
    private MeshRenderer _renderer;
    private MaterialPropertyBlock _properties;
    private Vector3 _sunDirection = new Vector3(0.45f, -0.30f, 0.84f);
    private Material _fallbackMaterial;

    private void OnEnable()
    {
        EnsureShell();
        ApplyProperties();
    }

    private void OnDisable()
    {
        if (_shell != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(_shell);
            else Destroy(_shell);
#else
            Destroy(_shell);
#endif
        }
        _shell = null;
        _renderer = null;
        if (_fallbackMaterial != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(_fallbackMaterial);
            else Destroy(_fallbackMaterial);
#else
            Destroy(_fallbackMaterial);
#endif
            _fallbackMaterial = null;
        }
    }

    private void LateUpdate()
    {
        if (_shell == null || _renderer == null) EnsureShell();
        ApplyProperties();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            EnsureShell();
            ApplyProperties();
        };
    }
#endif

    public void SetSunDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude > 0.0001f) _sunDirection = direction.normalized;
        ApplyProperties();
    }

    public void Refresh()
    {
        EnsureShell();
        ApplyProperties();
    }

    private void EnsureShell()
    {
        if (_shell != null && _renderer != null)
        {
            if (atmosphereMaterial != null && _renderer.sharedMaterial != atmosphereMaterial)
            {
                _renderer.sharedMaterial = atmosphereMaterial;
                if (_fallbackMaterial != null)
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying) DestroyImmediate(_fallbackMaterial); else Destroy(_fallbackMaterial);
#else
                    Destroy(_fallbackMaterial);
#endif
                    _fallbackMaterial = null;
                }
            }
            return;
        }

        Transform existing = transform.Find("AtmosphereShell");
        if (existing != null)
        {
            _shell = existing.gameObject;
            _renderer = _shell.GetComponent<MeshRenderer>();
        }
        else
        {
            _shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _shell.name = "AtmosphereShell";
            _shell.transform.SetParent(transform, false);
            Collider collider = _shell.GetComponent<Collider>();
            if (collider != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(collider);
                else Destroy(collider);
#else
                Destroy(collider);
#endif
            }
            _shell.hideFlags = HideFlags.DontSave;
            _renderer = _shell.GetComponent<MeshRenderer>();
        }

        if (_renderer == null) return;
        _shell.GetComponent<MeshFilter>().sharedMesh = Planet.Procedural.PlanetShellMesh.Sphere;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

        if (atmosphereMaterial != null)
        {
            _renderer.sharedMaterial = atmosphereMaterial;
        }
        else if (_renderer.sharedMaterial == null)
        {
            Shader shader = Shader.Find("Planet/Atmosphere");
            if (shader == null)
            {
                if (!_warnedMissingShader)
                {
                    Debug.LogWarning("[PlanetAtmosphere] Shader 'Planet/Atmosphere' is not available yet.", this);
                    _warnedMissingShader = true;
                }
                return;
            }
            _fallbackMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            _renderer.sharedMaterial = _fallbackMaterial;
        }
    }

    private bool _warnedMissingShader;

    private void ApplyProperties()
    {
        if (_shell == null || _renderer == null) return;
        if (autoSyncPlanetRadius)
        {
            IPlanetRadiusProvider body = GetComponent<IPlanetRadiusProvider>();
            if (body != null)
            {
                // SolarSystemSimulation writes RadiusUnits after the body is built. Keep the
                // temporary shell hidden instead of rendering a unit-radius shell for one frame.
                if (body.RadiusUnits <= 0f)
                {
                    _shell.SetActive(false);
                    return;
                }
                planetRadius = body.RadiusUnits;
            }
        }
        if (planetRadius <= 0f || (planetDefinition != null &&
            (!planetDefinition.features.hasAtmosphere || planetDefinition.physical.atmosphereHeightMetres <= 0f)))
        {
            _shell.SetActive(false);
            return;
        }
        _shell.SetActive(true);
        float effectiveShellRadius = shellRadius;
        if (planetDefinition != null && planetDefinition.physical.radiusMetres > 0.0)
        {
            // Atmosphere height is physical data, so Earth uses roughly 80 km / 6371 km = 1.0126.
            effectiveShellRadius = 1f + (float)(planetDefinition.physical.atmosphereHeightMetres /
                planetDefinition.physical.radiusMetres);
        }
        effectiveShellRadius = Mathf.Clamp(effectiveShellRadius, 1.000001f, 1.20f);
        _shell.transform.localPosition = Vector3.zero;
        _shell.transform.localRotation = Quaternion.identity;
        float diameter = planetRadius * effectiveShellRadius * 2f;
        float flattening = planetDefinition != null ? planetDefinition.physical.flattening : 0f;
        _shell.transform.localScale = new Vector3(diameter, diameter - planetRadius * flattening * 2f, diameter);

        if (_properties == null) _properties = new MaterialPropertyBlock();
        _renderer.GetPropertyBlock(_properties);
        _properties.SetColor("_AtmosphereColor", atmosphereColor);
        _properties.SetColor("_SunColor", sunScatterColor);
        _properties.SetFloat("_Density", density);
        _properties.SetFloat("_RimPower", horizonFalloff);
        _properties.SetFloat("_DayScatter", dayScatter);
        _properties.SetFloat("_NightScatter", nightScatter);
        _properties.SetColor("_TwilightColor", twilightColor);
        _properties.SetFloat("_TwilightStrength", twilightStrength);
        _properties.SetFloat("_ForwardGlow", 0.65f);
        _properties.SetVector("_SunDirection", new Vector4(_sunDirection.x, _sunDirection.y, _sunDirection.z, 0f));
        _renderer.SetPropertyBlock(_properties);
    }
}
}
