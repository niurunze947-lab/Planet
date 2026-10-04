using UnityEngine;
using UnityEngine.Rendering;

namespace Planet.Procedural
{
    /// <summary>
    /// A shell-based cloud approximation. Density and view-dependent optical path provide globe
    /// shading; the same density cubemap feeds the terrain's deterministic cloud shadows.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PlanetCloudLayer : MonoBehaviour
    {
        public PlanetDefinition definition;
        public Cubemap densityCubemap;
        public Material cloudMaterial;
        [Min(1e-9f)] public float planetRadius = 1f;
        [Range(1.001f, 1.15f)] public float innerRadius = 1.018f;
        [Range(1.002f, 1.18f)] public float outerRadius = 1.045f;
        [Range(0f, 2f)] public float density = 0.72f;
        [Range(0f, 2f)] public float silverLining = 0.42f;
        [Range(0f, 2f)] public float shadowStrength = 0.5f;

        private GameObject _shell;
        private MeshRenderer _renderer;
        private Material _fallbackMaterial;
        private MaterialPropertyBlock _properties;
        private Vector3 _sunDirection = new Vector3(0.45f, -0.3f, 0.84f);

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

        private void OnEnable()
        {
            EnsureShell();
            ApplyProperties();
        }

        private void LateUpdate()
        {
            if (_shell == null || _renderer == null) EnsureShell();
            ApplyProperties();
        }

        private void OnDisable()
        {
            if (_shell != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(_shell); else Destroy(_shell);
#else
                Destroy(_shell);
#endif
            }
            _shell = null;
            _renderer = null;
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

        private void EnsureShell()
        {
            if (_shell != null && _renderer != null)
            {
                if (cloudMaterial != null && _renderer.sharedMaterial != cloudMaterial)
                {
                    _renderer.sharedMaterial = cloudMaterial;
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
            Transform existing = transform.Find("CloudVolume");
            if (existing != null) _shell = existing.gameObject;
            else
            {
                _shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _shell.name = "CloudVolume";
                _shell.transform.SetParent(transform, false);
                Collider collider = _shell.GetComponent<Collider>();
                if (collider != null)
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying) DestroyImmediate(collider); else Destroy(collider);
#else
                    Destroy(collider);
#endif
                }
                _shell.hideFlags = HideFlags.DontSave;
            }
            _renderer = _shell.GetComponent<MeshRenderer>();
            if (_renderer == null) return;
            _shell.GetComponent<MeshFilter>().sharedMesh = PlanetShellMesh.Sphere;
            // Cloud shadows are sampled deterministically by Planet/ProceduralSurface. A mesh
            // ShadowCaster here would use the solar-system shadow atlas and flicker at this scale.
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            if (cloudMaterial != null) _renderer.sharedMaterial = cloudMaterial;
            else if (_renderer.sharedMaterial == null)
            {
                Shader shader = Shader.Find("Planet/CloudVolume");
                if (shader == null) return;
                _fallbackMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                _renderer.sharedMaterial = _fallbackMaterial;
            }
        }

        private void ApplyProperties()
        {
            if (_shell == null || _renderer == null) return;
            IPlanetRadiusProvider body = GetComponent<IPlanetRadiusProvider>();
            if (body != null)
            {
                if (body.RadiusUnits <= 0f)
                {
                    _shell.SetActive(false);
                    return;
                }
                planetRadius = body.RadiusUnits;
            }
            bool visible = (definition == null || definition.features == null || definition.features.hasClouds) &&
                planetRadius > 0f && densityCubemap != null;
            _shell.SetActive(visible);
            if (!visible) return;
            _shell.transform.localPosition = Vector3.zero;
            _shell.transform.localRotation = Quaternion.identity;
            float diameter = planetRadius * Mathf.Max(outerRadius, 1f) * 2f;
            float flattening = definition != null ? definition.physical.flattening : 0f;
            _shell.transform.localScale = new Vector3(diameter, diameter - planetRadius * flattening * 2f, diameter);
            if (_properties == null) _properties = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_properties);
            _properties.SetTexture("_DensityCube", densityCubemap);
            _properties.SetFloat("_InnerRadius", innerRadius / Mathf.Max(outerRadius, 1.001f));
            _properties.SetFloat("_Density", density);
            _properties.SetFloat("_SilverLining", silverLining);
            _properties.SetFloat("_ShadowStrength", shadowStrength);
            _properties.SetVector("_SunDirection", new Vector4(_sunDirection.x, _sunDirection.y, _sunDirection.z, 0f));
            _renderer.SetPropertyBlock(_properties);
        }
    }
}
