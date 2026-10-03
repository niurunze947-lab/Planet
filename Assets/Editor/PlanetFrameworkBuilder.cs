using System.IO;
using Planet.Procedural;
using Planet.Solar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Planet.EditorTools
{
    /// <summary>Creates the derived example assets and wires the Earth body in SolarSystem.unity.</summary>
    public static class PlanetFrameworkBuilder
    {
        public const string DefinitionPath = "Assets/Resources/Solar/ProceduralTerra.asset";
        public const string AlbedoPath = "Assets/Resources/Solar/ProceduralTerra_Albedo.cubemap";
        public const string MaterialPath = "Assets/Resources/Solar/ProceduralTerraSurface.mat";
        public const string ScenePath = "Assets/Scenes/SolarSystem.unity";
        private const int AlbedoSize = 256;
        private const int MeshResolution = 96;

        [MenuItem("Tools/Planet/Build Procedural Planet Example", false, 20)]
        public static void BuildExample()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolders();
            PlanetDefinition definition = EnsureDefinition();
            Cubemap albedo = EnsureAlbedo(definition);
            Material material = EnsureMaterial(albedo, definition);

            Scene scene = EditorSceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ConfigureSolarSystemEarth(scene, definition, albedo, material);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Planet] Procedural planet framework example built: " + ScenePath);
        }

        /// <summary>Called by the Solar System scene builder so a rebuilt scene keeps the example.</summary>
        public static void ConfigureSolarSystemEarth(GameObject solarRoot)
        {
            EnsureFolders();
            PlanetDefinition definition = EnsureDefinition();
            Cubemap albedo = EnsureAlbedo(definition);
            Material material = EnsureMaterial(albedo, definition);
            Scene scene = solarRoot != null ? solarRoot.scene : SceneManager.GetActiveScene();
            ConfigureSolarSystemEarth(scene, definition, albedo, material);
        }

        private static void ConfigureSolarSystemEarth(Scene scene, PlanetDefinition definition, Cubemap albedo, Material material)
        {
            if (!scene.IsValid()) return;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                CelestialBody[] bodies = roots[r].GetComponentsInChildren<CelestialBody>(true);
                for (int i = 0; i < bodies.Length; i++)
                {
                    if (!string.Equals(bodies[i].bodyName, "Earth", System.StringComparison.OrdinalIgnoreCase)) continue;
                    PlanetSurfaceView view = bodies[i].GetComponent<PlanetSurfaceView>();
                    if (view == null) view = bodies[i].gameObject.AddComponent<PlanetSurfaceView>();
                    view.definition = definition;
                    view.albedoCubemap = albedo;
                    view.surfaceMaterial = material;
                    view.meshResolution = MeshResolution;
                    view.generateOnEnable = true;
                    bodies[i].BuildVisuals();
                    EditorUtility.SetDirty(view);
                    EditorUtility.SetDirty(bodies[i]);
                    return;
                }
            }
            Debug.LogWarning("[Planet] Could not find Earth in the active SolarSystem scene.");
        }

        private static PlanetDefinition EnsureDefinition()
        {
            PlanetDefinition definition = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(DefinitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<PlanetDefinition>();
                definition.displayName = "Procedural Terra";
                definition.physical.radiusMetres = 6371000.0;
                definition.physical.massKilograms = 5.9722e24;
                definition.physical.rotationPeriodSeconds = 86164.09056;
                definition.physical.northPoleDeclinationDegrees = 90.0;
                definition.generation.seed = 271828;
                AssetDatabase.CreateAsset(definition, DefinitionPath);
            }
            definition.ValidateParameters();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static Cubemap EnsureAlbedo(PlanetDefinition definition)
        {
            Cubemap old = AssetDatabase.LoadAssetAtPath<Cubemap>(AlbedoPath);
            string expected = definition.ContentFingerprint();
            if (old != null && old.name.Contains(expected.Substring(0, 8))) return old;
            if (old != null) AssetDatabase.DeleteAsset(AlbedoPath);
            Cubemap baked = PlanetSurfaceBaker.BakeAlbedo(definition, AlbedoSize);
            baked.name = definition.displayName + "_ProceduralAlbedo_" + expected.Substring(0, 8);
            AssetDatabase.CreateAsset(baked, AlbedoPath);
            return AssetDatabase.LoadAssetAtPath<Cubemap>(AlbedoPath);
        }

        private static Material EnsureMaterial(Cubemap albedo, PlanetDefinition definition)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Shader shader = Shader.Find("Planet/ProceduralSurface");
            if (shader == null) throw new System.InvalidOperationException("Planet/ProceduralSurface shader did not compile.");
            if (material == null)
            {
                material = new Material(shader) { name = "ProceduralTerraSurface" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else material.shader = shader;
            material.SetTexture("_AlbedoCube", albedo);
            material.SetFloat("_SeaLevel", definition.physical.seaLevelMetres);
            material.SetFloat("_OceanDepth", definition.generation.oceanDepthMetres);
            material.SetColor("_RimColor", new Color(0.11f, 0.38f, 0.95f, 1f));
            material.SetFloat("_RimStrength", 0.44f);
            material.SetFloat("_RimPower", 2.7f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Solar");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
