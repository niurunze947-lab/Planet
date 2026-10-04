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
        public const string CloudDensityPath = "Assets/Resources/Solar/ProceduralTerra_CloudDensity.cubemap";
        public const string MaterialPath = "Assets/Resources/Solar/ProceduralTerraSurface.mat";
        public const string CloudMaterialPath = "Assets/Resources/Solar/ProceduralTerraClouds.mat";
        public const string AtmosphereMaterialPath = "Assets/Settings/Atmosphere_Earth.mat";
        public const string ScenePath = "Assets/Scenes/SolarSystem.unity";
        private const int AlbedoSize = 256;
        private const int CloudSize = 128;
        private const int MeshResolution = 96;

        [MenuItem("Tools/Planet/Refresh Procedural Planet Visuals", false, 21)]
        public static void RefreshVisuals()
        {
            if (Application.isPlaying) throw new System.InvalidOperationException("Refresh the saved scene outside Play mode.");
            PlanetDefinition definition = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(DefinitionPath);
            Cubemap albedo = AssetDatabase.LoadAssetAtPath<Cubemap>(AlbedoPath);
            Cubemap clouds = AssetDatabase.LoadAssetAtPath<Cubemap>(CloudDensityPath);
            if (definition == null || albedo == null || clouds == null)
                throw new System.InvalidOperationException("Run Build Procedural Planet Example to generate the source assets first.");
            Scene scene = EditorSceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new System.InvalidOperationException("Open SolarSystem.unity before refreshing its visuals.");
            ConfigureSolarSystemEarth(scene, definition, albedo, clouds,
                EnsureMaterial(albedo, clouds, definition), EnsureCloudMaterial(clouds, definition), EnsureAtmosphereMaterial());
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Planet] Visuals refreshed using existing cubemaps: " + ScenePath);
        }

        [MenuItem("Tools/Planet/Build Procedural Planet Example", false, 20)]
        public static void BuildExample()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            EnsureFolders();
            PlanetDefinition definition = EnsureDefinition();
            AssetDatabase.SaveAssets();
            Cubemap albedo = EnsureAlbedo(definition);
            AssetDatabase.SaveAssets();
            Cubemap cloudDensity = EnsureCloudDensity(definition);
            AssetDatabase.SaveAssets();
            Material material = EnsureMaterial(albedo, cloudDensity, definition);
            Material cloudMaterial = EnsureCloudMaterial(cloudDensity, definition);
            Material atmosphereMaterial = EnsureAtmosphereMaterial();

            Scene scene = EditorSceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ConfigureSolarSystemEarth(scene, definition, albedo, cloudDensity, material, cloudMaterial, atmosphereMaterial);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Planet] Procedural planet framework example built: " + ScenePath +
                " (" + timer.Elapsed.TotalSeconds.ToString("F2") + " s)");
        }

        /// <summary>Called by the Solar System scene builder so a rebuilt scene keeps the example.</summary>
        public static void ConfigureSolarSystemEarth(GameObject solarRoot)
        {
            EnsureFolders();
            PlanetDefinition definition = EnsureDefinition();
            Cubemap albedo = EnsureAlbedo(definition);
            Cubemap cloudDensity = EnsureCloudDensity(definition);
            Material material = EnsureMaterial(albedo, cloudDensity, definition);
            Material cloudMaterial = EnsureCloudMaterial(cloudDensity, definition);
            Material atmosphereMaterial = EnsureAtmosphereMaterial();
            Scene scene = solarRoot != null ? solarRoot.scene : SceneManager.GetActiveScene();
            ConfigureSolarSystemEarth(scene, definition, albedo, cloudDensity, material, cloudMaterial, atmosphereMaterial);
        }

        private static void ConfigureSolarSystemEarth(Scene scene, PlanetDefinition definition, Cubemap albedo,
            Cubemap cloudDensity, Material material, Material cloudMaterial, Material atmosphereMaterial)
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
                    view.cloudDensityCubemap = cloudDensity;
                    view.surfaceMaterial = material;
                    view.meshResolution = MeshResolution;
                    view.generateOnEnable = true;
                    bodies[i].BuildVisuals();
                    PlanetAtmosphere atmosphere = bodies[i].GetComponent<PlanetAtmosphere>();
                    if (atmosphere == null) atmosphere = bodies[i].gameObject.AddComponent<PlanetAtmosphere>();
                    atmosphere.planetDefinition = definition;
                    atmosphere.atmosphereMaterial = atmosphereMaterial;
                    atmosphere.autoSyncPlanetRadius = true;
                    atmosphere.planetRadius = bodies[i].RadiusUnits;
                    atmosphere.shellRadius = 1.0125f;
                    atmosphere.density = 0.22f;
                    atmosphere.horizonFalloff = 3.2f;
                    atmosphere.dayScatter = 0.9f;
                    atmosphere.nightScatter = 0.025f;
                    atmosphere.twilightColor = new Color(1f, 0.65f, 0.40f, 1f);
                    atmosphere.twilightStrength = 0.08f;
                    atmosphere.Refresh();

                    PlanetCloudLayer clouds = bodies[i].GetComponent<PlanetCloudLayer>();
                    if (clouds == null) clouds = bodies[i].gameObject.AddComponent<PlanetCloudLayer>();
                    clouds.definition = definition;
                    clouds.densityCubemap = cloudDensity;
                    clouds.cloudMaterial = cloudMaterial;
                    clouds.planetRadius = bodies[i].RadiusUnits;
                    clouds.innerRadius = 1.005f;
                    clouds.outerRadius = 1.009f;
                    clouds.density = 0.74f;
                    clouds.silverLining = 0.46f;
                    clouds.shadowStrength = 0.56f;
                    clouds.Refresh();
                    EditorUtility.SetDirty(view);
                    EditorUtility.SetDirty(atmosphere);
                    EditorUtility.SetDirty(clouds);
                    EditorUtility.SetDirty(bodies[i]);
                    ConfigureSunAndSky(scene);
                    return;
                }
            }
            ConfigureSunAndSky(scene);
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
            definition.RecalculateTerrainStatistics(28);
            definition.ValidateParameters();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static Cubemap EnsureAlbedo(PlanetDefinition definition)
        {
            Cubemap old = AssetDatabase.LoadAssetAtPath<Cubemap>(AlbedoPath);
            string expected = definition.ContentFingerprint();
            if (IsCurrent(old, AlbedoPath, expected, AlbedoSize)) return old;
            Cubemap baked = PlanetSurfaceBaker.BakeAlbedo(definition, AlbedoSize);
            if (baked == null) return old;
            return SaveCubemap(baked, old, AlbedoPath, expected);
        }

        private static Cubemap EnsureCloudDensity(PlanetDefinition definition)
        {
            Cubemap old = AssetDatabase.LoadAssetAtPath<Cubemap>(CloudDensityPath);
            string expected = definition.ContentFingerprint();
            if (IsCurrent(old, CloudDensityPath, expected, CloudSize)) return old;
            Cubemap baked = PlanetSurfaceBaker.BakeCloudDensity(definition, CloudSize);
            if (baked == null) return old;
            return SaveCubemap(baked, old, CloudDensityPath, expected);
        }

        private static bool IsCurrent(Cubemap cube, string path, string fingerprint, int size)
        {
            if (cube == null || cube.width != size) return false;
            AssetImporter importer = AssetImporter.GetAtPath(path);
            return importer != null && importer.userData == "PlanetBake:" + fingerprint;
        }

        private static Cubemap SaveCubemap(Cubemap baked, Cubemap existing, string path, string fingerprint)
        {
            // Generate successfully first, then copy into the existing asset to preserve its GUID
            // and every scene/material reference. Unity normalizes native asset names on import,
            // so the cache fingerprint belongs in importer metadata, not in the Cubemap name.
            baked.name = Path.GetFileNameWithoutExtension(path);
            Cubemap result = existing;
            if (existing != null)
            {
                EditorUtility.CopySerialized(baked, existing);
                Object.DestroyImmediate(baked);
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(baked, path);
                result = baked;
            }
            AssetDatabase.SaveAssets();
            AssetImporter importer = AssetImporter.GetAtPath(path);
            importer.userData = "PlanetBake:" + fingerprint;
            AssetDatabase.WriteImportSettingsIfDirty(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return result;
        }

        private static Material EnsureMaterial(Cubemap albedo, Cubemap cloudDensity, PlanetDefinition definition)
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
            material.SetTexture("_CloudDensityCube", cloudDensity);
            material.SetFloat("_SeaLevel", definition.physical.seaLevelMetres);
            material.SetFloat("_OceanEnabled", definition.features.hasOcean ? 1f : 0f);
            material.SetFloat("_WaterCoverage", definition.generation.waterCoverage);
            material.SetFloat("_CloudShadowEnabled", definition.features.hasClouds ? 1f : 0f);
            material.SetFloat("_CloudShadowStrength", 0.32f);
            material.SetFloat("_CloudShadowOffset", 0.035f);
            material.SetFloat("_OceanSmoothness", 0.42f);
            material.SetFloat("_LandSmoothness", 0.12f);
            material.SetColor("_RimColor", new Color(0.11f, 0.38f, 0.95f, 1f));
            material.SetFloat("_RimStrength", definition.features.hasAtmosphere ? 0.10f : 0f);
            material.SetFloat("_RimPower", 2.7f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureCloudMaterial(Cubemap density, PlanetDefinition definition)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(CloudMaterialPath);
            Shader shader = Shader.Find("Planet/CloudVolume");
            if (shader == null) throw new System.InvalidOperationException("Planet/CloudVolume shader did not compile.");
            if (material == null)
            {
                material = new Material(shader) { name = "ProceduralTerraClouds" };
                AssetDatabase.CreateAsset(material, CloudMaterialPath);
            }
            else material.shader = shader;
            material.SetTexture("_DensityCube", density);
            material.SetFloat("_Density", definition.features.hasClouds ? 0.74f : 0f);
            material.SetFloat("_SilverLining", 0.46f);
            material.SetFloat("_ShadowStrength", 0.56f);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 10;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureAtmosphereMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(AtmosphereMaterialPath);
            Shader shader = Shader.Find("Planet/Atmosphere");
            if (shader == null) throw new System.InvalidOperationException("Planet/Atmosphere shader did not compile.");
            if (material == null)
            {
                material = new Material(shader) { name = "Atmosphere_Earth" };
                AssetDatabase.CreateAsset(material, AtmosphereMaterialPath);
            }
            else material.shader = shader;
            material.SetFloat("_Density", 0.22f);
            material.SetFloat("_RimPower", 2.8f);
            material.SetFloat("_DayScatter", 1.05f);
            material.SetFloat("_NightScatter", 0.025f);
            material.SetFloat("_MieStrength", 0.48f);
            material.SetFloat("_SoftEdge", 0.42f);
            material.SetColor("_TwilightColor", new Color(1f, 0.65f, 0.40f, 1f));
            material.SetFloat("_TwilightStrength", 0.08f);
            material.SetFloat("_ForwardGlow", 0.65f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureSunAndSky(Scene scene)
        {
            Material sky = RenderSettings.skybox;
            Shader starShader = Shader.Find("Planet/StarfieldPhysical");
            if (sky != null && starShader != null)
            {
                Texture2D starMap = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Sky/starmap_2020_4k.exr");
                sky.shader = starShader;
                if (starMap != null) sky.SetTexture("_MainTex", starMap);
                sky.SetColor("_Tint", new Color(0.85f, 0.88f, 1f, 1f));
                sky.SetFloat("_Exposure", 0.16f);
                EditorUtility.SetDirty(sky);
            }
            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                Light[] lights = roots[r].GetComponentsInChildren<Light>(true);
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i].type != LightType.Directional) continue;
                    lights[i].shadows = LightShadows.None;
                    PlanetLightingController controller = lights[i].GetComponent<PlanetLightingController>();
                    if (controller == null) controller = lights[i].gameObject.AddComponent<PlanetLightingController>();
                    controller.sun = lights[i];
                    controller.starfieldMaterial = RenderSettings.skybox;
                    controller.maximumSurfaceExposure = 16f;
                    EditorUtility.SetDirty(lights[i]);
                    EditorUtility.SetDirty(controller);
                }
            }
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
