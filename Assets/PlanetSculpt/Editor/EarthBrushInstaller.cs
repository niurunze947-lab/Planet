using System;
using System.IO;
using System.Linq;
using Planet.Procedural;
using Planet.Solar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Yongxian.PlanetSculpt.Editor
{
    [InitializeOnLoad]
    public static class EarthBrushInstaller
    {
        const string Request = "Library/EarthBrush.request";
        const string Report = "Library/EarthBrushReport.json";
        static EarthBrushInstaller() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
            string command;
            try { command = File.ReadAllText(Request).Trim(); }
            catch (IOException) { return; } // The external request writer may still hold the file.
            if (command == "playtest" && (!EditorApplication.isPlaying || Time.timeSinceLevelLoad < 3f)) return;
            File.Delete(Request);
            try
            {
                if (command == "install") Install();
                else if (command == "validate") Validate();
                else if (command == "playtest") PlayTest();
                else if (command == "quota") ValidateQuota();
                else if (command == "play") EditorApplication.isPlaying = true;
                else if (command == "stop") EditorApplication.isPlaying = false;
                else if (command == "probe") WriteReport("probe");
                else throw new InvalidOperationException("Unknown Earth brush command: " + command);
            }
            catch (Exception error)
            {
                File.WriteAllText(Report, JsonUtility.ToJson(new Result { status = "FAIL", detail = error.ToString(), project = Application.dataPath }, true));
                Debug.LogException(error);
            }
        }

        static CelestialBody Earth()
        {
            var candidates = Object.FindObjectsOfType<CelestialBody>(true).Where(x => x.bodyName == "Earth" && x.GetComponent<PlanetSurfaceView>() != null && x.gameObject.scene.isLoaded).ToArray();
            if (candidates.Length != 1) throw new InvalidOperationException("Expected exactly one Earth, found " + candidates.Length);
            return candidates[0];
        }

        [MenuItem("Tools/Planet/Install Earth Brush")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Install outside Play mode.");
            var earth = Earth();
            var surface = earth.GetComponent<PlanetSurfaceView>();
            if (surface.SurfaceTransform == null) throw new InvalidOperationException("Earth surface is not built.");
            var scene = earth.gameObject.scene;
            string backup = "Library/BeforeEarthBrush-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity";
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Cannot back up the current scene.");
            ConfigureRock(earth, surface);
            var target = surface.SurfaceTransform.gameObject;
            var terrain = target.GetComponent<PlanetTerrain>();
            if (terrain == null) terrain = Undo.AddComponent<PlanetTerrain>(target);
            terrain.initializeOnAwake = false;
            terrain.preserveSourceMesh = false;
            terrain.useNaturalSurface = true;
            terrain.radius = 1f;
            terrain.enabled = false;
            target.GetComponent<MeshCollider>().enabled = false;
            var brush = target.GetComponent<PlanetBrush>();
            if (brush == null) brush = Undo.AddComponent<PlanetBrush>(target);
            brush.frameOnStart = false;
            brush.enabled = false;
            brush.viewCamera = Object.FindObjectOfType<SolarSystemSimulation>().ViewCamera;
            var controller = earth.GetComponent<EarthSculptController>();
            if (controller == null) controller = Undo.AddComponent<EarthSculptController>(earth.gameObject);
            EditorUtility.SetDirty(terrain);
            EditorUtility.SetDirty(brush);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(target.GetComponent<MeshCollider>());
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Cannot save installed scene.");
            Selection.activeGameObject = earth.gameObject;
            WriteReport("installed", backup);
        }

        static void ConfigureRock(CelestialBody earth, PlanetSurfaceView surface)
        {
            const string definitionPath = "Assets/PlanetSculpt/Resources/EarthRockDefinition.asset";
            const string materialPath = "Assets/PlanetSculpt/Resources/EarthRockSurface.mat";
            var rock = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(definitionPath);
            if (rock == null)
            {
                rock = Object.Instantiate(surface.definition);
                rock.name = "EarthRockDefinition";
                AssetDatabase.CreateAsset(rock, definitionPath);
            }
            rock.displayName = "Earth - Bare Rock";
            rock.features.hasOcean = false;
            rock.features.hasAtmosphere = false;
            rock.features.hasClouds = false;
            rock.features.hasEcology = false;
            rock.features.hasIceCaps = false;
            rock.physical.flattening = 0f;
            rock.physical.shapeScale = Vector3.one;
            rock.physical.atmosphereHeightMetres = 0f;
            rock.physical.seaLevelMetres = 0f;
            rock.appearance.surfaceKind = PlanetSurfaceKind.CrateredRock;
            rock.appearance.reliefMetres = 0f;
            rock.appearance.craterDepthMetres = 0f;
            rock.appearance.craterStrength = 0f;
            rock.appearance.featureStrength = 0f;
            rock.appearance.opticalSurfaceHeightMetres = 0f;
            rock.appearance.baseColor = new Color(0.4f, 0.34f, 0.27f);
            rock.appearance.secondaryColor = new Color(0.47f, 0.41f, 0.33f);
            rock.appearance.accentColor = new Color(0.43f, 0.35f, 0.27f);
            rock.generation.waterCoverage = 0f;
            rock.generation.cloudCoverage = 0f;
            rock.detail.enabled = false;
            rock.ValidateParameters();
            EditorUtility.SetDirty(rock);
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Resources.Load<Material>("PlanetSurface")) { name = "EarthRockSurface" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetFloat("_PlanetRadius", 1f);
            material.SetFloat("_SnowEnabled", 0f);
            EditorUtility.SetDirty(material);
            surface.definition = rock;
            surface.surfaceMaterial = material;
            surface.albedoCubemap = null;
            surface.cloudDensityCubemap = null;
            surface.ClearVisualTextures();
            var clouds = earth.GetComponent<PlanetCloudLayer>();
            if (clouds != null) { clouds.definition = rock; EditorUtility.SetDirty(clouds); }
            var atmosphere = earth.GetComponent<PlanetAtmosphere>();
            if (atmosphere != null) { atmosphere.planetDefinition = rock; EditorUtility.SetDirty(atmosphere); }
            surface.BuildSurface(earth.RadiusUnits);
            EditorUtility.SetDirty(surface);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Planet/Validate Brush Stroke Limit")]
        public static void ValidateQuota()
        {
            var go = new GameObject("Brush quota validation");
            go.SetActive(false);
            try
            {
                var terrain = go.AddComponent<PlanetTerrain>();
                terrain.subdivisions = 2;
                terrain.useNaturalSurface = false;
                terrain.Initialize();
                var brush = go.AddComponent<PlanetBrush>();
                var direction = terrain.SurfaceMesh.vertices[0].normalized;
                Require(brush.RemainingStrokes == 15, "New brush must have 15 strokes.");
                brush.PaintAt(Vector3.zero, 0.05f);
                brush.PaintAt(direction, 0f, false, true);
                Require(brush.RemainingStrokes == 15 && terrain.UndoCount == 0, "Invalid and no-op input must not consume a stroke or undo entry.");
                for (int i = 0; i < 15; i++)
                {
                    bool lower = i % 2 == 1;
                    brush.PaintAt(direction, 0.05f, lower);
                    brush.PaintAt(direction, 0.05f, lower);
                    Require(brush.StrokesUsed == i + 1, "Held input must consume only one stroke.");
                    brush.EndStroke();
                }
                var last = terrain.SurfaceMesh.vertices;
                brush.PaintAt(direction, 0.05f);
                brush.PaintAt(direction, 0.05f, true);
                brush.PaintAt(direction, 0.05f, false, true);
                Require(last.SequenceEqual(terrain.SurfaceMesh.vertices), "All drawing modes must stop after stroke 15.");
                brush.UndoLastStroke();
                Require(brush.RemainingStrokes == 0, "Undo must not refund strokes.");
                brush.enabled = false;
                brush.enabled = true;
                terrain.ResetTerrain();
                brush.PaintAt(direction, 0.05f);
                Require(brush.RemainingStrokes == 0 && terrain.CopyHeights().All(h => h == 0f), "Reset or toggling the brush must not bypass the limit.");
                WriteReport("quota-passed", "15 strokes; held drawing counted once; no-op, exhaustion, undo, reset and re-enable checked.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [MenuItem("Tools/Planet/Validate Earth Brush")]
        public static void Validate()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Use the playtest in Play mode.");
            if (!PlanetSculptValidation.RunChecks()) throw new Exception("Original brush regression suite failed.");
            var source = Earth().GetComponent<PlanetSurfaceView>().SurfaceTransform.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] original = source.vertices;
            Vector2[] originalUv = source.uv;
            Vector2[] originalUv2 = source.uv2;
            GameObject test = new GameObject("Earth brush preservation test");
            try
            {
                test.AddComponent<MeshFilter>().sharedMesh = source;
                test.AddComponent<MeshRenderer>();
                var terrain = test.AddComponent<PlanetTerrain>();
                terrain.initializeOnAwake = false;
                terrain.preserveSourceMesh = true;
                terrain.useNaturalSurface = false;
                terrain.radius = 1f;
                terrain.mountainBrush = false;
                terrain.Initialize();
                Require(terrain.SurfaceMesh != source, "Source mesh must not be edited in place.");
                Require(original.SequenceEqual(terrain.SurfaceMesh.vertices), "Initial geography must be preserved exactly.");
                Require(originalUv.SequenceEqual(terrain.SurfaceMesh.uv) && originalUv2.SequenceEqual(terrain.SurfaceMesh.uv2), "Texture channels must be retained.");
                Vector3 direction = original[0].normalized;
                terrain.BeginStroke();
                terrain.ApplyBrush(direction, 4f * Mathf.Deg2Rad, 0.01f, false);
                Require(terrain.SurfaceMesh.vertices[0].magnitude > original[0].magnitude + 0.009f, "Raise must deform the original Earth mesh.");
                Require(terrain.UndoStroke(), "Earth stroke must be undoable.");
                Require(original.SequenceEqual(terrain.SurfaceMesh.vertices), "Undo must restore original Earth geometry exactly.");
                terrain.BeginStroke();
                terrain.ApplyBrush(direction, 4f * Mathf.Deg2Rad, -0.01f, false);
                Require(terrain.SurfaceMesh.vertices[0].magnitude < original[0].magnitude - 0.009f, "Lower must excavate Earth.");
                terrain.ApplyBrush(direction, 4f * Mathf.Deg2Rad, 0.5f, true);
                Require(original.SequenceEqual(source.vertices), "Original source mesh was mutated.");
                Require(terrain.SurfaceCollider.sharedMesh == terrain.SurfaceMesh, "Collision must use the sculpted mesh.");
            }
            finally { Object.DestroyImmediate(test); }
            WriteReport("validation-passed", "Original brush suite; Earth source geometry/UV preservation, raise, lower, smooth, undo and collider.");
        }

        static void PlayTest()
        {
            var earth = Earth();
            var controller = earth.GetComponent<EarthSculptController>();
            Require(controller != null && controller.IsSculpting, "Earth brush did not enter sculpt mode.");
            var terrain = controller.Terrain;
            var brush = controller.Brush;
            var surface = earth.GetComponent<PlanetSurfaceView>();
            var material = surface.SurfaceTransform.GetComponent<MeshRenderer>().sharedMaterial;
            Vector3[] before = terrain.SurfaceMesh.vertices;
            Vector3 direction = before[0].normalized;
            brush.PaintAt(direction, 1f / 60f);
            brush.EndStroke();
            Require(!before.SequenceEqual(terrain.SurfaceMesh.vertices), "Live brush did not raise Earth.");
            brush.UndoLastStroke();
            Require(before.SequenceEqual(terrain.SurfaceMesh.vertices), "Live undo failed.");
            Require(surface.SurfaceTransform.GetComponent<MeshRenderer>().sharedMaterial == material, "Earth material was replaced.");
            controller.ExitSculptMode();
            Require(!controller.IsSculpting && !brush.enabled, "Exit failed.");
            controller.EnterSculptMode();
            Require(controller.IsSculpting && brush.enabled, "Re-entry failed.");
            WriteReport("playtest-passed", "Live Earth raise and undo, material retained, exit/re-entry passed.");
        }

        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void WriteReport(string status, string detail = "")
        {
            var earth = Earth();
            File.WriteAllText(Report, JsonUtility.ToJson(new Result {
                status = status, detail = detail, project = Application.dataPath,
                scene = earth.gameObject.scene.path, earth = earth.name, playing = EditorApplication.isPlaying,
                installed = earth.GetComponent<EarthSculptController>() != null
            }, true));
        }
        [Serializable] class Result
        {
            public string status, detail, project, scene, earth;
            public bool playing, installed;
        }
    }
}
