using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Planet.Procedural;
using Planet.Solar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Planet.EditorTools
{
    public static class EarthTextureLodBuilder
    {
        public const string Folder = "Assets/Resources/Solar/BodyMaterials/EarthLod";
        public const string SetPath = Folder + "/EarthTextureLods.asset";
        public static bool IsBuilding { get; private set; }
        public static string Status { get; private set; } = "Idle";
        public static string LastError { get; private set; }
        private sealed class Bake
        {
            public string path;
            public int size;
            public PlanetSurfaceBaker.MapKind kind;
        }
        private static readonly Queue<Bake> jobs = new Queue<Bake>();
        private static Task<Color[][]> pending;
        private static Bake current;
        private static PlanetDefinition definition;
        private static string sourceFingerprint;
        private static System.Diagnostics.Stopwatch timer;
        private const string ResumeKey = "Planet.EarthTextureLod.BuildPending";

        [InitializeOnLoadMethod]
        private static void ResumeAfterReload()
        {
            if (SessionState.GetBool(ResumeKey, false)) EditorApplication.update += ResumeWhenEditing;
        }

        private static void ResumeWhenEditing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            EditorApplication.update -= ResumeWhenEditing;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == PlanetFrameworkBuilder.ScenePath) Build();
        }

        [MenuItem("Tools/Planet/Build Earth Texture LODs", false, 23)]
        public static void Build()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Bake Earth textures outside Play mode.");
            if (IsBuilding) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != PlanetFrameworkBuilder.ScenePath)
                throw new InvalidOperationException("Open SolarSystem.unity first.");
            definition = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(PlanetFrameworkBuilder.DefinitionPath);
            if (definition == null) throw new InvalidOperationException("Build the Terra example first.");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(SolarSystemMaterialBuilder.Folder, "EarthLod");
            definition.RecalculateTerrainStatistics(28); EditorUtility.SetDirty(definition);
            sourceFingerprint = definition.ContentFingerprint();
            PlanetTextureLodSet set = AssetDatabase.LoadAssetAtPath<PlanetTextureLodSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<PlanetTextureLodSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }
            set.definition = definition;
            set.tiers = new PlanetTextureTier[4];
            int[] sizes = {128, 256, 512, 1024};
            int[] cloudSizes = {64, 128, 256, 512};
            // Runtime chunks carry resolved geometric normals. A globe normal map would overwrite them.
            int[] normalSizes = {0, 0, 0, 0};
            string[] labels = {"System", "Orbit", "Near", "Surface"};
            jobs.Clear();
            for (int i = 0; i < sizes.Length; i++)
            {
                string a = i == 1 ? PlanetFrameworkBuilder.AlbedoPath : Folder + "/Earth_Albedo_" + sizes[i] + ".cubemap";
                string c = i == 1 ? PlanetFrameworkBuilder.CloudDensityPath : Folder + "/Earth_Cloud_" + cloudSizes[i] + ".cubemap";
                string n = normalSizes[i] == 0 ? null : Folder + "/Earth_Normal_" + normalSizes[i] + ".cubemap";
                set.tiers[i] = new PlanetTextureTier {label = labels[i], albedoSize = sizes[i], cloudSize = cloudSizes[i], normalSize = normalSizes[i],
                    albedoResource = ResourcePath(a), cloudResource = ResourcePath(c), normalResource = n != null ? ResourcePath(n) : null};
                Enqueue(a, sizes[i], PlanetSurfaceBaker.MapKind.Albedo, i == 1);
                Enqueue(c, cloudSizes[i], PlanetSurfaceBaker.MapKind.CloudDensity, i == 1);
                if (n != null) Enqueue(n, normalSizes[i], PlanetSurfaceBaker.MapKind.Normal, false);
            }
            EditorUtility.SetDirty(set); AssetDatabase.SaveAssets();
            foreach (CelestialBody body in Object.FindObjectsOfType<CelestialBody>())
            {
                if (body.bodyName != "Earth") continue;
                PlanetTextureLodController controller = body.GetComponent<PlanetTextureLodController>();
                if (controller == null) controller = body.gameObject.AddComponent<PlanetTextureLodController>();
                controller.lodSet = set; EditorUtility.SetDirty(controller);
            }
            SolarSystemVisualCoordinator.EnsureInScene();
            timer = System.Diagnostics.Stopwatch.StartNew(); IsBuilding = true; LastError = null;
            SessionState.SetBool(ResumeKey, true);
            EditorApplication.update += Step;
        }

        private static string ResourcePath(string path) { return path.Substring("Assets/Resources/".Length).Replace(".cubemap", ""); }
        private static string Key(Bake bake)
        {
            // Preserve the existing Terra cube cache key for the original 256/128 assets.
            return (bake.path == PlanetFrameworkBuilder.AlbedoPath || bake.path == PlanetFrameworkBuilder.CloudDensityPath)
                ? "PlanetBake:" + sourceFingerprint
                : "PlanetLod:1:" + sourceFingerprint + ":" + bake.kind + ":" + bake.size;
        }
        private static void Enqueue(string path, int size, PlanetSurfaceBaker.MapKind kind, bool original)
        {
            Bake job = new Bake {path = path, size = size, kind = kind};
            Cubemap existing = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (existing != null && existing.width == size && importer != null && importer.userData == Key(job)) return;
            jobs.Enqueue(job);
        }
        private static void Step()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                if (definition.ContentFingerprint() != sourceFingerprint)
                    throw new InvalidOperationException("Terra changed during baking. Run Build Earth Texture LODs again to use the new definition.");
                if (pending != null)
                {
                    if (!pending.IsCompleted) return;
                    if (pending.IsFaulted) throw pending.Exception;
                    Cubemap next = new Cubemap(current.size, TextureFormat.RGBA32, true, true)
                    { name = System.IO.Path.GetFileNameWithoutExtension(current.path), wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
                    for (int face = 0; face < 6; face++) next.SetPixels(pending.Result[face], (CubemapFace)face);
                    next.Apply(true, true); // Derived caches do not need persistent CPU pixel copies.
                    Cubemap existing = AssetDatabase.LoadAssetAtPath<Cubemap>(current.path);
                    if (existing != null) { EditorUtility.CopySerialized(next, existing); Object.DestroyImmediate(next); EditorUtility.SetDirty(existing); }
                    else AssetDatabase.CreateAsset(next, current.path);
                    AssetDatabase.SaveAssets();
                    AssetImporter importer = AssetImporter.GetAtPath(current.path); importer.userData = Key(current);
                    AssetDatabase.WriteImportSettingsIfDirty(current.path);
                    AssetDatabase.ImportAsset(current.path, ImportAssetOptions.ForceUpdate);
                    pending = null;
                }
                if (jobs.Count == 0)
                {
                    EditorApplication.update -= Step; IsBuilding = false;
                    SessionState.SetBool(ResumeKey, false);
                    EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
                    Status = "Complete in " + timer.Elapsed.TotalSeconds.ToString("F2") + " s";
                    Debug.Log("[Planet LOD] " + Status); return;
                }
                current = jobs.Dequeue(); Status = "Baking " + current.kind + " " + current.size + " (" + jobs.Count + " remaining)";
                pending = PlanetSurfaceBaker.BakeFacesAsync(definition, current.size, current.kind);
            }
            catch (Exception error)
            {
                LastError = error.ToString(); Status = "Failed: " + error.Message;
                IsBuilding = false; SessionState.SetBool(ResumeKey, false); EditorApplication.update -= Step; Debug.LogException(error);
            }
        }
    }
}
