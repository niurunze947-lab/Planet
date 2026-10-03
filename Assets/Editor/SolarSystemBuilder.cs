using System.IO;
using Planet.Solar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Planet.EditorTools
{
    /// <summary>
    /// Regenerates the white-model palette and rebuilds Assets/Scenes/SolarSystem.unity from
    /// <see cref="SolarSystemCatalog"/>. Nothing here is required at runtime; it exists so the
    /// scene can be reproduced from source instead of being a hand-assembled binary blob.
    /// </summary>
    public static class SolarSystemBuilder
    {
        public const string ScenePath = "Assets/Scenes/SolarSystem.unity";
        public const string PostProfilePath = "Assets/Settings/SolarSystemPost.asset";
        public const string SkyboxPath = "Assets/Settings/Skybox_Starfield.mat";
        public const string MaterialFolder = "Assets/Resources/Solar";

        // =====================================================================================
        // Menu entries
        // =====================================================================================

        [MenuItem("Tools/Planet/Rebuild Solar System Scene", false, 10)]
        public static void MenuRebuildScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            RebuildScene(true);
        }

        [MenuItem("Tools/Planet/Rebuild White-Model Materials", false, 11)]
        public static void MenuRebuildMaterials()
        {
            BuildMaterials();
            Debug.Log("[Solar] White-model material palette rebuilt in " + MaterialFolder);
        }

        // =====================================================================================
        // Scene
        // =====================================================================================

        public static void RebuildScene(bool openAfterwards)
        {
            BuildMaterials();
            VolumeProfile profile = BuildPostProfile();
            Material skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            if (skybox == null) Debug.LogWarning("[Solar] Skybox material missing at " + SkyboxPath);

            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject root = SolarSystemFactory.BuildAll(skybox, profile);
            PlanetFrameworkBuilder.ConfigureSolarSystemEarth(root);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            bool ok = EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            AddSceneToBuildSettings(ScenePath);

            Debug.Log("[Solar] Solar system scene rebuilt at " + ScenePath +
                      " (saved=" + ok + ", " + root.transform.childCount + " root children).");
            if (!openAfterwards) EditorSceneManager.CloseScene(scene, true);
        }

        [MenuItem("Tools/Planet/Open Solar System Scene", false, 12)]
        public static void OpenScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }

        private static int _captureIndex;
        private static Camera _captureCam;
        private static RenderTexture _captureRt;
        private static Texture2D _captureTex;
        private static bool _capturing;

        private const int CaptureWidth = 1600;
        private const int CaptureHeight = 900;
        private const string CaptureFolder = "Assets/Screenshots";

        /// <summary>
        /// Captures every pose in <see cref="SolarSystemPoses"/> to Assets/Screenshots. Play mode only.
        ///
        /// One pose per editor tick, deliberately. Belt markers go out through
        /// Graphics.DrawMeshInstanced, whose submissions survive until the end of the frame - doing
        /// all eleven poses inside a single call stacks eleven copies of each belt at eleven
        /// different floating origins, which renders as a smear of concentric rings. Yielding a
        /// frame between poses is what keeps each capture honest.
        /// </summary>
        [MenuItem("Tools/Planet/Capture Solar System Poses", false, 30)]
        public static void CapturePoses()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Solar] Enter play mode before capturing poses.");
                return;
            }
            if (_capturing)
            {
                Debug.LogWarning("[Solar] A capture run is already in progress.");
                return;
            }

            _captureCam = Camera.main;
            if (_captureCam == null) { Debug.LogError("[Solar] No main camera."); return; }

            Directory.CreateDirectory(CaptureFolder);
            _captureRt = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
            _captureRt.antiAliasing = 4;
            _captureTex = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false);
            _captureIndex = 0;
            _capturing = true;
            EditorApplication.update += CaptureStep;
            Debug.Log("[Solar] capturing " + SolarSystemPoses.All.Length + " poses...");
        }

        private static void CaptureStep()
        {
            if (!_capturing) return;

            if (!Application.isPlaying || _captureIndex >= SolarSystemPoses.All.Length || _captureCam == null)
            {
                EditorApplication.update -= CaptureStep;
                _capturing = false;
                if (_captureTex != null) UnityEngine.Object.DestroyImmediate(_captureTex);
                if (_captureRt != null) { _captureRt.Release(); UnityEngine.Object.DestroyImmediate(_captureRt); }
                _captureTex = null;
                _captureRt = null;
                // No AssetDatabase.Refresh() here: it can reload the domain and drop the editor out
                // of play mode. Use Tools > Planet > Reimport Solar Screenshots afterwards.
                Debug.Log("[Solar] pose capture finished (" + _captureIndex + " files in " + CaptureFolder + ").");
                return;
            }

            string name = SolarSystemPoses.All[_captureIndex].Name;
            string applied = SolarSystemPoses.Apply(name);

            var prev = _captureCam.targetTexture;
            _captureCam.targetTexture = _captureRt;
            _captureCam.Render();
            _captureCam.targetTexture = prev;

            RenderTexture.active = _captureRt;
            _captureTex.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
            _captureTex.Apply();
            RenderTexture.active = null;

            string path = CaptureFolder + "/solar_" + name + ".png";
            File.WriteAllBytes(path, _captureTex.EncodeToPNG());
            Debug.Log("[Solar] captured " + path + "  (" + applied + ")");

            _captureIndex++;
        }

        [MenuItem("Tools/Planet/Reimport Solar Screenshots", false, 31)]
        public static void ReimportScreenshots()
        {
            AssetDatabase.Refresh();
        }

        private static void AddSceneToBuildSettings(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
                if (scenes[i].path == path) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // =====================================================================================
        // Materials
        // =====================================================================================

        public static void BuildMaterials()
        {
            EnsureFolder(MaterialFolder);

            // ---- matte white clay: every body, no texture, no metallic ----------------------
            Material clay = Create("WhiteClay", "Planet/ClayGrid");
            clay.SetColor("_BaseColor", new Color(0.820f, 0.824f, 0.840f, 1f));
            clay.SetColor("_GridColor", new Color(0.330f, 0.390f, 0.520f, 1f));
            clay.SetFloat("_GridStrength", SolarMaterials.GridStrength);
            clay.SetFloat("_LongitudeLines", 36f);   // one line every 10 degrees
            clay.SetFloat("_LatitudeLines", 18f);
            clay.SetFloat("_WrapLighting", 0.12f);
            clay.SetFloat("_Smoothness", 0.12f);
            Save(clay);

            // ---- rings: same clay, a touch darker so they read against the planet -----------
            Material ring = Create("WhiteRing", "Planet/ClayGrid");
            ring.SetColor("_BaseColor", new Color(0.760f, 0.762f, 0.775f, 1f));
            ring.SetColor("_GridColor", new Color(0.330f, 0.390f, 0.520f, 1f));
            ring.SetFloat("_GridStrength", 0.0f);    // a graticule on a ring mesh would be nonsense
            ring.SetFloat("_WrapLighting", 0.12f);
            ring.SetFloat("_Smoothness", 0.05f);
            Save(ring);

            // ---- the Sun: emissive so it feeds bloom and reads as the light source ----------
            Material sun = Create("WhiteSun", "Universal Render Pipeline/Lit");
            sun.SetColor("_BaseColor", new Color(1f, 0.985f, 0.955f, 1f));
            sun.SetFloat("_Metallic", 0f);
            sun.SetFloat("_Smoothness", 0f);
            sun.EnableKeyword("_EMISSION");
            sun.SetColor("_EmissionColor", new Color(1f, 0.965f, 0.90f) * 4.0f);
            sun.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            Save(sun);

            // URP/Lit leaves unused shader keywords on a reused material; clear the ones the clay
            // shader does not declare so switching shaders back and forth cannot leave stale state.
            clay.DisableKeyword("_EMISSION");
            ring.DisableKeyword("_EMISSION");

            // ---- debris markers: unlit so a 100 km rock is not lost to the terminator -------
            Material marker = Create("BeltMarker", "Universal Render Pipeline/Unlit");
            marker.SetColor("_BaseColor", new Color(0.520f, 0.560f, 0.640f, 1f));
            marker.SetFloat("_Surface", 0f);          // opaque
            marker.SetFloat("_Blend", 0f);
            marker.SetFloat("_ZWrite", 1f);
            marker.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            marker.SetShaderPassEnabled("ShadowCaster", false);
            marker.enableInstancing = true;           // required by Graphics.DrawMeshInstanced
            Save(marker);

            // ---- orbit lines: unlit, vertex-coloured, alpha blended -------------------------
            Material line = Create("OrbitLine", "Planet/OrbitLine");
            line.SetColor("_Tint", Color.white);
            line.renderQueue = (int)RenderQueue.Transparent;
            line.SetShaderPassEnabled("ShadowCaster", false);
            Save(line);

            SolarMaterials.ClearCache();
        }

        private static Material Create(string name, string shaderName)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError("[Solar] Shader not found: " + shaderName);
                return null;
            }

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = shader;
                return existing;
            }

            var mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void Save(Material m)
        {
            if (m == null) return;
            EditorUtility.SetDirty(m);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // =====================================================================================
        // Post-processing
        // =====================================================================================

        public static VolumeProfile BuildPostProfile()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, PostProfilePath);
            }

            Bloom bloom = profile.Has<Bloom>() ? profile.components.Find(c => c is Bloom) as Bloom : profile.Add<Bloom>(true);
            bloom.active = true;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.85f;          // the Sun is a 4x HDR emitter; bloom sells it
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.95f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.62f;
            bloom.tint.overrideState = true;
            bloom.tint.value = new Color(0.92f, 0.95f, 1f);

            Tonemapping tone = profile.Has<Tonemapping>() ? profile.components.Find(c => c is Tonemapping) as Tonemapping : profile.Add<Tonemapping>(true);
            tone.active = true;
            tone.mode.overrideState = true;
            tone.mode.value = TonemappingMode.ACES;

            Vignette vig = profile.Has<Vignette>() ? profile.components.Find(c => c is Vignette) as Vignette : profile.Add<Vignette>(true);
            vig.active = true;
            vig.intensity.overrideState = true;
            vig.intensity.value = 0.22f;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
