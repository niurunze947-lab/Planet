using UnityEngine;
using UnityEngine.Rendering;
using Planet.Procedural.Terrain;

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

        [Header("Runtime terrain")]
        [Tooltip("Generate bounded, view-dependent terrain chunks at runtime instead of loading whole-planet meshes.")]
        public bool useRuntimeChunks = true;

        [Header("Legacy offline mesh cache")]
        public PlanetMeshLodSet meshLodSet;
        [Tooltip("Fallback for isolated prototypes only. Formal SolarSystem assets must have meshLodSet assigned.")]
        public bool allowRuntimeMeshGeneration;

        [Header("Globe mesh")]
        [Range(8, 192)] public int meshResolution = 96;
        public bool generateOnEnable = true;

        private Mesh _mesh;
        private Transform _surface;
        private string _fingerprint;
        private int _builtResolution;
        private float _radiusUnits = 1f;
        private Material _runtimeMaterial;
        private MaterialPropertyBlock _visualTextures;
        private RuntimeTerrainRenderer _terrain;
        private bool _bodyVisible = true;
        private bool _nearTerrainActive;
        private Transform _ocean;
        private Mesh _oceanMesh;
        private Material _oceanMaterial;
        public Transform OceanTransform { get { return _ocean; } }
        public Material OceanMaterial { get { return _oceanMaterial; } }

        public Transform SurfaceTransform { get { return _surface; } }
        public Mesh Mesh { get { return _mesh; } }
        public RuntimeTerrainRenderer TerrainRenderer
        {
            get { if (_terrain == null) _terrain = GetComponent<RuntimeTerrainRenderer>(); return _terrain; }
        }

        private void OnEnable()
        {
            if (generateOnEnable && definition != null) BuildSurface(_radiusUnits);
        }

        private void OnDisable()
        {
            if (_terrain != null) _terrain.enabled = false;
            ReleaseOcean();
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

            if (useRuntimeChunks)
            {
                // A fixed globe is the complete far-field surface. Chunks replace it only after
                // the six root faces cover the body, so streaming cannot open visual holes.
                string farFingerprint = definition.ContentFingerprint();
                const int farResolution = 64;
                if (_mesh == null || _fingerprint != farFingerprint || _builtResolution != farResolution)
                {
                    Mesh next = CubeSphere.Generate(definition.CreateSampler(), farResolution)
                        .CreateMesh(definition.displayName + "_FarGlobe");
                    next.hideFlags = HideFlags.DontSave;
                    if (_mesh != null && (_mesh.hideFlags & HideFlags.DontSave) != 0)
                    {
                        if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
                    }
                    _mesh = next;
                    _fingerprint = farFingerprint;
                    _builtResolution = farResolution;
                    ReleaseOcean();
                }
                BuildOcean();
                _surface.localScale = Vector3.one * _radiusUnits;
                _surface.localPosition = Vector3.zero;
                _surface.localRotation = Quaternion.identity;
                _surface.GetComponent<MeshFilter>().sharedMesh = _mesh;
                MeshRenderer globe = _surface.GetComponent<MeshRenderer>();
                globe.sharedMaterial = ResolveMaterial();
                globe.shadowCastingMode = ShadowCastingMode.Off;
                globe.receiveShadows = false;
                globe.enabled = _bodyVisible;
                if (_terrain == null) _terrain = GetComponent<RuntimeTerrainRenderer>();
                if (_terrain == null) _terrain = gameObject.AddComponent<RuntimeTerrainRenderer>();
                _terrain.enabled = true;
                _terrain.Configure(this, _surface, _radiusUnits, ResolveMaterial());
                _terrain.SetStreamingRequested(false);
                return;
            }
            if (_terrain != null) _terrain.enabled = false;

            string fingerprint = definition.ContentFingerprint();
            int resolution = Mathf.Clamp(meshResolution, 8, 192);
            if (meshLodSet != null && meshLodSet.MeshAt(0) != null)
            {
                SetMeshAsset(meshLodSet.MeshAt(0), 0);
            }
            else if (allowRuntimeMeshGeneration && (_mesh == null || _fingerprint != fingerprint || _builtResolution != resolution))
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
            else if (_mesh == null)
            {
                Debug.LogError("[Planet] Offline mesh LOD set is missing for " + definition.displayName + ". Run Tools/Planet/Build Planet Mesh LODs.", this);
                return;
            }

            _surface.GetComponent<MeshFilter>().sharedMesh = _mesh;
            MeshRenderer renderer = _surface.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ResolveMaterial();
            if (_visualTextures != null) renderer.SetPropertyBlock(_visualTextures);
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
            BuildOcean();
        }

        public void SetRadius(float radiusUnits)
        {
            _radiusUnits = Mathf.Max(radiusUnits, 1e-6f);
            if (_surface != null) _surface.localScale = Vector3.one * _radiusUnits;
            if (_terrain != null) _terrain.SetRadius(_radiusUnits);
            if (_ocean != null) _ocean.localScale = Vector3.one * _radiusUnits;
        }

        /// <summary>
        /// Binds an offline mesh LOD without making the runtime mesh generator own or destroy the
        /// imported asset. This is the compatibility boundary used by PlanetMeshLodController.
        /// </summary>
        public void SetMeshAsset(Mesh mesh, int tier)
        {
            if (useRuntimeChunks) return;
            if (mesh == null) return;
            EnsureSurfaceObject();
            if (_mesh != null && _mesh != mesh && (_mesh.hideFlags & HideFlags.DontSave) != 0)
            {
                if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
            }
            _mesh = mesh;
            _fingerprint = definition != null ? definition.ContentFingerprint() : string.Empty;
            _builtResolution = -Mathf.Abs(tier + 1);
            _surface.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = _surface.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ResolveMaterial();
            if (_visualTextures != null) renderer.SetPropertyBlock(_visualTextures);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            _surface.localScale = Vector3.one * _radiusUnits;
        }

        public void RefreshMaterial()
        {
            if (_surface == null) return;
            MeshRenderer renderer = _surface.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = ResolveMaterial();
        }

        /// <summary>Per-view LOD binding. Never writes selected textures into shared material assets.</summary>
        public void SetVisualTextures(Cubemap albedo, Cubemap normal, Cubemap clouds)
        {
            if (_surface == null) return;
            if (_visualTextures == null) _visualTextures = new MaterialPropertyBlock();
            _visualTextures.Clear();
            _visualTextures.SetTexture("_AlbedoCube", albedo != null ? albedo : albedoCubemap);
            _visualTextures.SetFloat("_NormalEnabled", normal != null ? 1f : 0f);
            _visualTextures.SetFloat("_CloudShadowEnabled", 0f);
            if (normal != null) _visualTextures.SetTexture("_NormalCube", normal);
            if (clouds != null) _visualTextures.SetTexture("_CloudDensityCube", clouds);
            _surface.GetComponent<MeshRenderer>().SetPropertyBlock(_visualTextures);
        }

        public void ClearVisualTextures()
        {
            _visualTextures = null;
            if (_surface != null) _surface.GetComponent<MeshRenderer>().SetPropertyBlock(null);
        }

        public void SetRenderersVisible(bool visible)
        {
            _bodyVisible = visible;
            if (_surface != null) _surface.GetComponent<MeshRenderer>().enabled =
                visible && (!_nearTerrainActive || _terrain == null || !_terrain.CoverageReady);
            if (_terrain != null) _terrain.SetVisible(visible && _nearTerrainActive && _terrain.CoverageReady);
            if (_ocean != null) _ocean.GetComponent<MeshRenderer>().enabled =
                visible && (!_nearTerrainActive || _terrain == null || !_terrain.CoverageReady);
        }

        /// <summary>Prewarm near terrain behind the globe, then switch once root coverage exists.</summary>
        public void SetTerrainState(bool prewarm, bool active)
        {
            if (!useRuntimeChunks || _terrain == null || _surface == null) return;
            _nearTerrainActive = active;
            _terrain.SetStreamingRequested(prewarm);
            bool chunksVisible = _bodyVisible && active && _terrain.CoverageReady;
            _terrain.SetVisible(chunksVisible);
            _surface.GetComponent<MeshRenderer>().enabled = _bodyVisible && !chunksVisible;
            if (_ocean != null) _ocean.GetComponent<MeshRenderer>().enabled = _bodyVisible && !chunksVisible;
        }

        public void CopyVisualProperties(MaterialPropertyBlock target)
        {
            target.Clear();
            if (_surface != null) _surface.GetComponent<MeshRenderer>().GetPropertyBlock(target);
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
                if (surfaceMaterial.HasProperty("_CloudShadowEnabled")) surfaceMaterial.SetFloat("_CloudShadowEnabled", 0f);
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
            _runtimeMaterial.SetFloat("_CloudShadowEnabled", 0f);
            ApplyAppearance(_runtimeMaterial);
            return _runtimeMaterial;
        }

        private void ApplyAppearance(Material material)
        {
            if (material.HasProperty("_SeparateOcean")) material.SetFloat("_SeparateOcean", definition.features.hasOcean ? 1 : 0);
            Vector3 axes = definition.physical.shapeScale;
            axes.y *= 1f - definition.physical.flattening;
            if (material.HasProperty("_ReferenceAxes")) material.SetVector("_ReferenceAxes", new Vector4(axes.x, axes.y, axes.z, 0f));
            if (material.HasProperty("_EmissionStrength")) material.SetFloat("_EmissionStrength", definition.appearance.emissionStrength);
            if (material.HasProperty("_LimbDarkening")) material.SetFloat("_LimbDarkening", definition.appearance.limbDarkening);
        }

        private void BuildOcean()
        {
            if (!definition.features.hasOcean) { ReleaseOcean(); return; }
            if (_ocean != null) return;
            Material template = Resources.Load<Material>("Solar/OceanSurface");
            if (template == null || _mesh == null) return;
            _oceanMaterial = new Material(template) { name = "Ocean " + definition.displayName, hideFlags = HideFlags.DontSave };
            _oceanMaterial.SetFloat("_SeaLevel", definition.physical.seaLevelMetres);
            _oceanMaterial.SetColor("_ShallowColor", definition.palette.shallowOcean);
            _oceanMaterial.SetColor("_DeepColor", definition.palette.deepOcean);
            var directions = new System.Collections.Generic.List<Vector3>();
            var samples = new System.Collections.Generic.List<Vector4>();
            _mesh.GetUVs(1, directions); _mesh.GetUVs(2, samples);
            var vertices = new Vector3[directions.Count];
            var normals = new Vector3[directions.Count];
            PlanetSurfaceSampler sampler = definition.CreateSampler();
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = sampler.OceanPosition((Unity.Mathematics.float3)directions[i]);
                normals[i] = sampler.ReferenceNormal((Unity.Mathematics.float3)directions[i]);
            }
            _oceanMesh = new Mesh { name = definition.displayName + "_Ocean", hideFlags = HideFlags.DontSave,
                indexFormat = _mesh.indexFormat };
            _oceanMesh.vertices = vertices; _oceanMesh.normals = normals;
            _oceanMesh.SetUVs(2, samples); _oceanMesh.triangles = _mesh.triangles; _oceanMesh.RecalculateBounds();
            _ocean = new GameObject("Ocean") { hideFlags = HideFlags.DontSave }.transform;
            _ocean.SetParent(transform, false); _ocean.localScale = Vector3.one * _radiusUnits;
            _ocean.gameObject.AddComponent<MeshFilter>().sharedMesh = _oceanMesh;
            var renderer = _ocean.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _oceanMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }

        private void ReleaseOcean()
        {
            if (Application.isPlaying)
            { if (_ocean != null) Destroy(_ocean.gameObject); Destroy(_oceanMesh); Destroy(_oceanMaterial); }
            else
            { if (_ocean != null) DestroyImmediate(_ocean.gameObject); DestroyImmediate(_oceanMesh); DestroyImmediate(_oceanMaterial); }
            _ocean = null; _oceanMesh = null; _oceanMaterial = null;
        }
    }
}
