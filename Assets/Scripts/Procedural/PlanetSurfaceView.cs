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
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
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
                if (surfaceMaterial.HasProperty("_SeaLevel")) surfaceMaterial.SetFloat("_SeaLevel", definition.physical.seaLevelMetres);
                if (surfaceMaterial.HasProperty("_OceanDepth")) surfaceMaterial.SetFloat("_OceanDepth", definition.generation.oceanDepthMetres);
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
            _runtimeMaterial.SetFloat("_SeaLevel", definition.physical.seaLevelMetres);
            _runtimeMaterial.SetFloat("_OceanDepth", definition.generation.oceanDepthMetres);
            return _runtimeMaterial;
        }
    }
}
