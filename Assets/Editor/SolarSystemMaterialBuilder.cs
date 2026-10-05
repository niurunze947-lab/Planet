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
    /// <summary>Per-body definitions and GUID-preserving caches for the formal SolarSystem scene.</summary>
    public static class SolarSystemMaterialBuilder
    {
        public const string Folder = "Assets/Resources/Solar/BodyMaterials";
        public static bool IsBuilding { get; private set; }
        public static string BuildStatus { get; private set; } = "Idle";
        public static string LastError { get; private set; }
        private static int nextBody;
        private static System.Diagnostics.Stopwatch timer;

        [MenuItem("Tools/Planet/Build Solar System Materials", false, 22)]
        public static void BuildAll()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Build materials outside Play mode.");
            if (IsBuilding) return;
            RequireScene();
            nextBody = 0; LastError = null; IsBuilding = true;
            timer = System.Diagnostics.Stopwatch.StartNew();
            BuildStatus = "Building 0/" + SolarSystemCatalog.Bodies.Length;
            EditorApplication.update += BuildNext;
        }

        private static void BuildNext()
        {
            if (!IsBuilding) return;
            try
            {
                Body[] bodies = SolarSystemCatalog.Bodies;
                BuildBody(bodies[nextBody].Name);
                nextBody++;
                BuildStatus = "Built " + nextBody + "/" + bodies.Length + ": " + bodies[nextBody - 1].Name;
                if (nextBody < bodies.Length) return;
                EditorSceneManager.SaveScene(RequireScene());
                AssetDatabase.SaveAssets();
                BuildStatus = "Complete: " + bodies.Length + " bodies in " + timer.Elapsed.TotalSeconds.ToString("F2") + " s";
                Debug.Log("[Planet] Solar materials " + BuildStatus);
                StopBuilding();
            }
            catch (Exception error)
            {
                LastError = error.ToString(); BuildStatus = "Failed: " + error.Message;
                Debug.LogException(error); StopBuilding();
            }
        }

        private static void StopBuilding()
        {
            IsBuilding = false;
            EditorApplication.update -= BuildNext;
        }

        public static string DefinitionPath(string bodyName)
        {
            return bodyName == "Earth" ? PlanetFrameworkBuilder.DefinitionPath : Folder + "/" + bodyName + ".asset";
        }

        /// <summary>Existing definitions keep authored appearance settings; physical catalog fields synchronize.</summary>
        public static void BuildBody(string bodyName)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Build materials outside Play mode.");
            Scene scene = RequireScene();
            Body body = SolarSystemCatalog.Find(bodyName);
            if (body == null) throw new ArgumentException("Unknown body: " + bodyName);
            CelestialBody view = FindBody(scene, bodyName);
            if (view == null) throw new InvalidOperationException("Scene is missing " + bodyName);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources/Solar", "BodyMaterials");
            if (bodyName == "Sun") { StellarFrameworkBuilder.ConfigureSun(); return; }

            if (bodyName == "Earth")
            {
                // Retain the original Terra asset GUIDs and the documented ocean/cloud configuration.
                PlanetFrameworkBuilder.ConfigureSolarSystemEarth(view.transform.root.gameObject);
                PlanetSurfaceView earth = view.GetComponent<PlanetSurfaceView>();
                ConfigureSurfaceMaterial(earth.surfaceMaterial, earth.definition);
                EditorUtility.SetDirty(earth.surfaceMaterial);
                PlanetTextureLodSet lodSet = AssetDatabase.LoadAssetAtPath<PlanetTextureLodSet>(EarthTextureLodBuilder.SetPath);
                if (lodSet != null)
                {
                    PlanetTextureLodController lod = view.GetComponent<PlanetTextureLodController>();
                    if (lod == null) lod = view.gameObject.AddComponent<PlanetTextureLodController>();
                    lod.lodSet = lodSet; EditorUtility.SetDirty(lod);
                    SolarSystemVisualCoordinator.EnsureInScene();
                }
                AssetDatabase.SaveAssets();
                return;
            }

            string prefix = Folder + "/" + bodyName;
            PlanetDefinition definition = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(DefinitionPath(bodyName));
            if (definition == null)
            {
                definition = SolarSystemAppearanceCatalog.CreateDefault(body);
                AssetDatabase.CreateAsset(definition, DefinitionPath(bodyName));
            }
            SolarSystemAppearanceCatalog.SynchronizePhysical(definition, body);
            definition.RecalculateTerrainStatistics(20);
            EditorUtility.SetDirty(definition);
            Cubemap albedo = EnsureCube(definition, prefix + "_Albedo.cubemap", 256, false);
            Cubemap cloud = definition.features.hasClouds ? EnsureCube(definition, prefix + "_CloudDensity.cubemap", 128, true) : null;
            Material surface = EnsureMaterial(prefix + "Surface.mat", "Planet/ProceduralSurface");
            surface.SetTexture("_AlbedoCube", albedo); surface.SetTexture("_CloudDensityCube", cloud);
            ConfigureSurfaceMaterial(surface, definition);
            surface.SetFloat("_NightAmbient", 0.009f);
            surface.SetFloat("_RimStrength", 0f);
            surface.SetFloat("_OceanEnabled", definition.features.hasOcean ? 1f : 0f);
            surface.SetFloat("_SeaLevel", definition.physical.seaLevelMetres);
            surface.SetFloat("_CloudShadowEnabled", cloud != null ? 1f : 0f);
            surface.SetFloat("_CloudShadowStrength", 0.32f);
            surface.SetFloat("_RingOuterRatio", 0f);

            if (body.Rings.HasValue)
            {
                Texture2D profile = EnsureRingProfile(definition, body, prefix + "_RingProfile.asset");
                Material ring = EnsureMaterial(prefix + "Ring.mat", "Planet/ProceduralRing");
                ring.SetTexture("_RingProfile", profile);
                ring.SetFloat("_PlanetRadiusRatio", (float)(body.RadiusKm / body.Rings.Value.outerKm));
                view.ringMaterial = ring;
                surface.SetTexture("_RingProfile", profile);
                surface.SetFloat("_RingOuterRatio", (float)(body.Rings.Value.outerKm / body.RadiusKm));
                EditorUtility.SetDirty(ring);
            }

            PlanetSurfaceView planet = view.GetComponent<PlanetSurfaceView>();
            if (planet == null) planet = view.gameObject.AddComponent<PlanetSurfaceView>();
            planet.definition = definition; planet.albedoCubemap = albedo; planet.cloudDensityCubemap = cloud;
            planet.surfaceMaterial = surface; planet.meshResolution = 64; planet.generateOnEnable = true;
            view.BuildVisuals();

            PlanetAtmosphere atmosphere = view.GetComponent<PlanetAtmosphere>();
            if (definition.features.hasAtmosphere)
            {
                if (atmosphere == null) atmosphere = view.gameObject.AddComponent<PlanetAtmosphere>();
                atmosphere.enabled = true;
                atmosphere.planetDefinition = definition;
                atmosphere.atmosphereMaterial = EnsureMaterial(prefix + "Atmosphere.mat", "Planet/Atmosphere");
                atmosphere.atmosphereMaterial.SetFloat("_MieStrength", 0.30f);
                atmosphere.atmosphereMaterial.SetFloat("_SoftEdge", 0.42f);
                atmosphere.atmosphereColor = definition.appearance.atmosphereColor;
                atmosphere.sunScatterColor = definition.appearance.atmosphereColor;
                atmosphere.density = definition.appearance.atmosphereDensity;
                atmosphere.horizonFalloff = 3.6f; atmosphere.dayScatter = 0.8f; atmosphere.nightScatter = 0.004f;
                atmosphere.twilightStrength = bodyName == "Mars" ? 0.03f : 0.02f;
                atmosphere.autoSyncPlanetRadius = true; atmosphere.planetRadius = view.RadiusUnits;
                atmosphere.Refresh();
                EditorUtility.SetDirty(atmosphere.atmosphereMaterial);
                EditorUtility.SetDirty(atmosphere);
            }
            else if (atmosphere != null) atmosphere.enabled = false;

            PlanetCloudLayer cloudLayer = view.GetComponent<PlanetCloudLayer>();
            if (cloud != null)
            {
                if (cloudLayer == null) cloudLayer = view.gameObject.AddComponent<PlanetCloudLayer>();
                Material cloudMaterial = EnsureMaterial(prefix + "Clouds.mat", "Planet/CloudVolume");
                cloudMaterial.SetTexture("_DensityCube", cloud);
                cloudLayer.enabled = true; cloudLayer.definition = definition; cloudLayer.densityCubemap = cloud;
                cloudLayer.cloudMaterial = cloudMaterial;
                cloudLayer.innerRadius = 1.005f; cloudLayer.outerRadius = 1.009f;
                cloudLayer.planetRadius = view.RadiusUnits; cloudLayer.Refresh();
                EditorUtility.SetDirty(cloudMaterial); EditorUtility.SetDirty(cloudLayer);
            }
            else if (cloudLayer != null) cloudLayer.enabled = false;
            EditorUtility.SetDirty(surface); EditorUtility.SetDirty(planet); EditorUtility.SetDirty(view);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Rebuilds keep every body's appearance. Cached rebuilds do not rebake textures.</summary>
        public static void ConfigureSolarSystem(GameObject root)
        {
            foreach (Body body in SolarSystemCatalog.Bodies) BuildBody(body.Name);
            if (root != null)
            {
                PlanetMeshLodBuilder.ConfigureSceneMeshes(root.scene);
                SolarSystemVisualCoordinator.EnsureInScene();
            }
        }

        private static void ConfigureSurfaceMaterial(Material material, PlanetDefinition d)
        {
            material.SetFloat("_EmissionStrength", d.appearance.emissionStrength);
            material.SetFloat("_LimbDarkening", d.appearance.limbDarkening);
            material.SetFloat("_LandSmoothness", d.appearance.landSmoothness);
            Vector3 axes = d.physical.shapeScale; axes.y *= 1f - d.physical.flattening;
            material.SetVector("_ReferenceAxes", new Vector4(axes.x, axes.y, axes.z, 0f));
        }

        private static Material EnsureMaterial(string path, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("Missing shader: " + shaderName);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            return material;
        }

        private static Cubemap EnsureCube(PlanetDefinition d, string path, int size, bool cloud)
        {
            string key = "PlanetBake:" + d.ContentFingerprint();
            Cubemap old = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (old != null && old.width == size && importer != null && importer.userData == key) return old;
            Cubemap next = cloud ? PlanetSurfaceBaker.BakeCloudDensity(d, size) : PlanetSurfaceBaker.BakeAlbedo(d, size);
            next.name = Path.GetFileNameWithoutExtension(path);
            if (old != null)
            {
                EditorUtility.CopySerialized(next, old); Object.DestroyImmediate(next); EditorUtility.SetDirty(old);
            }
            else { AssetDatabase.CreateAsset(next, path); old = next; }
            AssetDatabase.SaveAssets();
            importer = AssetImporter.GetAtPath(path); importer.userData = key; AssetDatabase.WriteImportSettingsIfDirty(path);
            // WriteImportSettingsIfDirty writes the meta file, but cached importer state can still
            // expose the previous fingerprint until the native asset is imported again.
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return old;
        }

        private static Texture2D EnsureRingProfile(PlanetDefinition d, Body body, string path)
        {
            RingSet rings = body.Rings.Value;
            string key = "PlanetRing:" + Hash128.Compute(d.ContentFingerprint() + ":" + JsonUtility.ToJson(rings));
            Texture2D old = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (old != null && old.width == 2048 && importer != null && importer.userData == key) return old;
            Texture2D next = new Texture2D(2048, 1, TextureFormat.RGBA32, false, true)
            {
                name = Path.GetFileNameWithoutExtension(path), wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            Color[] pixels = new Color[next.width];
            Color color = d.appearance.ringColor.linear;
            double[] bands = rings.bands ?? new[] { rings.innerKm, rings.outerKm };
            for (int i = 0; i < pixels.Length; i++)
            {
                double radius = (i + 0.5) / pixels.Length * rings.outerKm;
                bool inRing = false;
                for (int band = 0; band < bands.Length; band += 2)
                    if (radius >= bands[band] && radius <= bands[band + 1]) { inRing = true; break; }
                float density = d.appearance.ringOpacity;
                if (body.Name == "Saturn")
                {
                    if (radius < 74500) density *= 0.10f;
                    else if (radius < 92000) density *= 0.38f;
                    else if (radius < 117580) density *= 1f;
                    else if (radius < 140180) density *= 0.68f;
                    else density *= 0.45f;
                }
                float striae = 0.86f + 0.09f * Mathf.Sin((float)radius * 0.033f) + 0.05f * Mathf.Sin((float)radius * 0.12f);
                pixels[i] = new Color(color.r * striae, color.g * striae, color.b * striae, inRing ? density * striae : 0f);
            }
            next.SetPixels(pixels); next.Apply(false, false);
            if (old != null)
            {
                EditorUtility.CopySerialized(next, old); Object.DestroyImmediate(next); EditorUtility.SetDirty(old);
            }
            else { AssetDatabase.CreateAsset(next, path); old = next; }
            AssetDatabase.SaveAssets();
            importer = AssetImporter.GetAtPath(path); importer.userData = key; AssetDatabase.WriteImportSettingsIfDirty(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return old;
        }

        private static Scene RequireScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || (scene.path != PlanetFrameworkBuilder.ScenePath && !string.IsNullOrEmpty(scene.path)))
                throw new InvalidOperationException("Open SolarSystem.unity before building its materials.");
            return scene;
        }
        private static CelestialBody FindBody(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (CelestialBody body in root.GetComponentsInChildren<CelestialBody>(true))
                    if (body.bodyName == name) return body;
            return null;
        }
    }
}
