using System;
using System.IO;
using Planet.Procedural;
using Planet.Solar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Planet.EditorTools
{
    /// <summary>Builds and assigns persistent planet meshes. Formal SolarSystem play mode never calls CubeSphere.Generate.</summary>
    public static class PlanetMeshLodBuilder
    {
        public const string Folder = "Assets/Resources/Solar/BodyMaterials/MeshLod";
        private static readonly int[] Resolutions = { 16, 32, 64, 96 };
        private static readonly string[] Labels = { "System", "Orbit", "Near", "Surface" };

        [MenuItem("Tools/Planet/Build Planet Mesh LODs", false, 25)]
        public static void BuildAll()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Build planet meshes outside Play mode.");
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != PlanetFrameworkBuilder.ScenePath)
                throw new InvalidOperationException("Open SolarSystem.unity before building planet mesh LODs.");
            EnsureFolder(Folder);
            int built = ConfigureSceneMeshes(scene);
            SolarSystemVisualCoordinator.EnsureInScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Planet Mesh LOD] Built " + built + " planet mesh sets at " + Folder + ".");
        }

        public static int ConfigureSceneMeshes(Scene scene)
        {
            EnsureFolder(Folder);
            int built = 0;
            foreach (CelestialBody body in FindBodies(scene))
            {
                PlanetSurfaceView view = body.GetComponent<PlanetSurfaceView>();
                if (view == null || view.definition == null) continue;
                PlanetMeshLodSet set = BuildBody(body.bodyName, view.definition);
                view.meshLodSet = set;
                view.allowRuntimeMeshGeneration = false;
                PlanetMeshLodController controller = body.GetComponent<PlanetMeshLodController>();
                if (controller == null) controller = body.gameObject.AddComponent<PlanetMeshLodController>();
                controller.lodSet = set;
                view.BuildSurface(body.RadiusUnits);
                EditorUtility.SetDirty(view);
                EditorUtility.SetDirty(controller);
                built++;
            }
            return built;
        }

        public static PlanetMeshLodSet BuildBody(string bodyName, PlanetDefinition definition)
        {
            string setPath = Folder + "/" + bodyName + "MeshLods.asset";
            PlanetMeshLodSet set = AssetDatabase.LoadAssetAtPath<PlanetMeshLodSet>(setPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<PlanetMeshLodSet>();
                AssetDatabase.CreateAsset(set, setPath);
            }
            set.definition = definition;
            set.tiers = new PlanetMeshTier[Resolutions.Length];
            string fingerprint = definition.ContentFingerprint();
            for (int i = 0; i < Resolutions.Length; i++)
            {
                int resolution = Resolutions[i];
                string meshPath = Folder + "/" + bodyName + "_r" + resolution + ".asset";
                string key = "PlanetMesh:" + fingerprint + ":" + resolution;
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                AssetImporter importer = AssetImporter.GetAtPath(meshPath);
                if (mesh == null || importer == null || importer.userData != key)
                {
                    PlanetSurfaceSampler sampler = definition.CreateSampler();
                    PlanetMeshData data = CubeSphere.Generate(sampler, resolution);
                    Mesh next = data.CreateMesh(bodyName + "_CubeSphere_" + resolution);
                    next.name = bodyName + "_CubeSphere_" + resolution;
                    next.hideFlags = HideFlags.None;
                    if (mesh == null)
                    {
                        AssetDatabase.CreateAsset(next, meshPath);
                        mesh = next;
                    }
                    else
                    {
                        EditorUtility.CopySerialized(next, mesh);
                        Object.DestroyImmediate(next);
                        EditorUtility.SetDirty(mesh);
                    }
                    AssetDatabase.SaveAssets();
                    importer = AssetImporter.GetAtPath(meshPath);
                    importer.userData = key;
                    AssetDatabase.WriteImportSettingsIfDirty(meshPath);
                    AssetDatabase.ImportAsset(meshPath, ImportAssetOptions.ForceUpdate);
                }
                set.tiers[i] = new PlanetMeshTier { label = Labels[i], resolution = resolution, mesh = mesh };
            }
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return set;
        }

        private static CelestialBody[] FindBodies(Scene scene)
        {
            var list = new System.Collections.Generic.List<CelestialBody>();
            foreach (GameObject root in scene.GetRootGameObjects())
                list.AddRange(root.GetComponentsInChildren<CelestialBody>(true));
            return list.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
