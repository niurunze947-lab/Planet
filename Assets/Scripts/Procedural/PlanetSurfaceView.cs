using UnityEngine;
using UnityEngine.Rendering;

namespace Planet.Procedural
{
    /// <summary>
    /// Scene-facing adapter for the star-system body. The mesh is a derived cache; its source of
    /// truth is the definition and sampler, so an editor bake and a runtime rebuild are identical.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PlanetSurfaceView : MonoBehaviour
    {
        [Header("Authoritative source")]
        public PlanetDefinition definition;

        [Header("Derived visual assets")]
        public Cubemap albedoCubemap;
        public Cubemap cloudDensityCubemap;
        public Material surfaceMaterial;

        [Header("Globe mesh")]
        [Range(8, 192)] public int meshResolution = 96;
        public bool generateOnEnable = true;

        private Mesh _mesh;
        private Transform _surface;
        private string _fingerprint;
        private int _builtResolution;
        private float _radiusUnits = 1f;
        private Material _runtimeMaterial;

        public Transform SurfaceTransform { get { return _surface; } }
        public Mesh Mesh { get { return _mesh; } }

        private void OnEnable()
        {
            if (generateOnEnable && definition != null) BuildSurface(_radiusUnits);
        }

        private void OnDisable()
        {
            if (_runtimeMaterial != null)
            {
                if (Application.isPlaying) Destroy(_runtimeMaterial);
                else DestroyImmediate(_runtimeMaterial);
                _runtimeMaterial = null;
            }
        }

        public void BuildSurface(float radiusUnits)
        {
            if (definition == null) return;
            definition.ValidateParameters();
            _radiusUnits = Mathf.Max(radiusUnits, 1e-6f);
            EnsureSurfaceObject();

            string fingerprint = definition.ContentFingerprint();
            int resolution = Mathf.Clamp(meshResolution, 8, 192);
            if (_mesh == null || _fingerprint != fingerprint || _builtResolution != resolution)
            {
                PlanetSurfaceSampler sampler = definition.CreateSampler();
                PlanetMeshData data = CubeSphere.Generate(sampler, resolution);
                Mesh next = data.CreateMesh(definition.displayName + "_CubeSphere_" + resolution);
                next.hideFlags = HideFlags.DontSave;
                if (_mesh != null)
                {
                    if (Application.isPlaying) Destroy(_mesh);
                    else DestroyImmediate(_mesh);
                }
                _mesh = next;
                _fingerprint = fingerprint;
                _builtResolution = resolution;
            }

            _surface.GetComponent<MeshFilter>().sharedMesh = _mesh;
            MeshRenderer renderer = _surface.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ResolveMaterial();
            // The solar-system directional shadow atlas is not useful at astronomical scale and
            // introduces unstable cascades across the planet disc. Surface form is lit by the
            // shader's direct term plus the deterministic cloud-shadow field.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _surface.localScale = Vector3.one * _radiusUnits;
            _surface.localPosition = Vector3.zero;
            _surface.localRotation = Quaternion.identity;
        }

        public void SetRadius(float radiusUnits)
        {
            _radiusUnits = Mathf.Max(radiusUnits, 1e-6f);
            if (_surface != null) _surface.localScale = Vector3.one * _radiusUnits;
        }

        public void RefreshMaterial()
        {
            if (_surface == null) return;
            MeshRenderer renderer = _surface.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = ResolveMaterial();
        }

        private void EnsureSurfaceObject()
        {
            _surface = transform.Find("Surface");
            if (_surface == null)
            {
                GameObject go = new GameObject("Surface");
                go.transform.SetParent(transform, false);
                _surface = go.transform;
            }
            if (_surface.GetComponent<MeshFilter>() == null) _surface.gameObject.AddComponent<MeshFilter>();
            if (_surface.GetComponent<MeshRenderer>() == null) _surface.gameObject.AddComponent<MeshRenderer>();
        }

        private Material ResolveMaterial()
        {
            if (surfaceMaterial != null)
            {
                if (surfaceMaterial.HasProperty("_AlbedoCube") && albedoCubemap != null)
                    surfaceMaterial.SetTexture("_AlbedoCube", albedoCubemap);
                if (surfaceMaterial.HasProperty("_CloudDensityCube"))
                    surfaceMaterial.SetTexture("_CloudDensityCube", cloudDensityCubemap);
                if (surfaceMaterial.HasProperty("_SeaLevel")) surfaceMaterial.SetFloat("_SeaLevel", definition.physical.seaLevelMetres);
                if (surfaceMaterial.HasProperty("_OceanEnabled")) surfaceMaterial.SetFloat("_OceanEnabled", definition.features.hasOcean ? 1f : 0f);
                if (surfaceMaterial.HasProperty("_WaterCoverage")) surfaceMaterial.SetFloat("_WaterCoverage", definition.generation.waterCoverage);
                if (surfaceMaterial.HasProperty("_CloudShadowEnabled")) surfaceMaterial.SetFloat("_CloudShadowEnabled", definition.features.hasClouds && cloudDensityCubemap != null ? 1f : 0f);
                ApplyAppearance(surfaceMaterial);
                return surfaceMaterial;
            }

            if (_runtimeMaterial == null)
            {
                Shader shader = Shader.Find("Planet/ProceduralSurface");
                if (shader == null)
                {
                    Debug.LogError("[Planet] Shader Planet/ProceduralSurface is missing.", this);
                    return null;
                }
                _runtimeMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
            if (albedoCubemap != null) _runtimeMaterial.SetTexture("_AlbedoCube", albedoCubemap);
            if (cloudDensityCubemap != null) _runtimeMaterial.SetTexture("_CloudDensityCube", cloudDensityCubemap);
            _runtimeMaterial.SetFloat("_SeaLevel", definition.physical.seaLevelMetres);
            _runtimeMaterial.SetFloat("_OceanEnabled", definition.features.hasOcean ? 1f : 0f);
            _runtimeMaterial.SetFloat("_WaterCoverage", definition.generation.waterCoverage);
            _runtimeMaterial.SetFloat("_CloudShadowEnabled", definition.features.hasClouds && cloudDensityCubemap != null ? 1f : 0f);
            ApplyAppearance(_runtimeMaterial);
            return _runtimeMaterial;
        }

        private void ApplyAppearance(Material material)
        {
            Vector3 axes = definition.physical.shapeScale;
            axes.y *= 1f - definition.physical.flattening;
            if (material.HasProperty("_ReferenceAxes")) material.SetVector("_ReferenceAxes", new Vector4(axes.x, axes.y, axes.z, 0f));
            if (material.HasProperty("_EmissionStrength")) material.SetFloat("_EmissionStrength", definition.appearance.emissionStrength);
            if (material.HasProperty("_LimbDarkening")) material.SetFloat("_LimbDarkening", definition.appearance.limbDarkening);
        }
    }
}
