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
                else if (command == "refresh") AssetDatabase.Refresh();
                else if (command == "navigation") NavigationTest();
                else if (command == "contact") NavigationTest(2);
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
            terrain.preserveSourceMesh = true;
            terrain.useNaturalSurface = true;
            terrain.radius = 1f;
            terrain.enabled = false;
            target.GetComponent<MeshCollider>().enabled = false;
            var brush = target.GetComponent<PlanetBrush>();
            if (brush == null) brush = Undo.AddComponent<PlanetBrush>(target);
            brush.frameOnStart = false;
            brush.orbitCamera = false;
            brush.paintEnabled = false;
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
            var original = AssetDatabase.LoadAssetAtPath<PlanetDefinition>("Assets/Resources/Solar/ProceduralTerra.asset");
            if (original == null) throw new InvalidOperationException("Original Earth definition is missing.");
            if (rock == null)
            {
                rock = Object.Instantiate(original);
                AssetDatabase.CreateAsset(rock, definitionPath);
            }
            else EditorUtility.CopySerialized(original, rock);
            rock.name = "EarthRockDefinition";
            rock.displayName = "Earth - exposed terrain";
            // Preserve the original seed, sea level, ellipsoid, relief and detail parameters.
            // Features only remove visible water/air/vegetation; they do not flatten the ground.
            rock.features.hasOcean = false;
            rock.features.hasAtmosphere = false;
            rock.features.hasClouds = false;
            rock.features.hasEcology = false;
            rock.features.hasIceCaps = false;
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

        static void NavigationTest(double startingHeight = 100)
        {
            Require(EditorApplication.isPlaying, "Navigation test requires Play mode.");
            var earth = Earth();
            var controller = earth.GetComponent<EarthSculptController>();
            var sim = SolarSystemSimulation.Instance;
            var flight = sim.ViewCamera.GetComponent<Planet.Interaction.UnifiedFlightController>();
            var bridge = controller.Bridge;
            Require(flight.enabled && !controller.Brush.orbitCamera, "Original flight must own navigation.");
            Require(sim.ViewCamera.GetComponent<ObserverRenderRig>().LocalCamera != null, "Metre camera missing.");
            var original = AssetDatabase.LoadAssetAtPath<PlanetDefinition>("Assets/Resources/Solar/ProceduralTerra.asset");
            var rock = earth.GetComponent<PlanetSurfaceView>().definition;
            Require(JsonUtility.ToJson(original.physical) == JsonUtility.ToJson(rock.physical), "Original physical shape changed.");
            Require(JsonUtility.ToJson(original.generation) == JsonUtility.ToJson(rock.generation), "Original terrain generation changed.");
            Require(JsonUtility.ToJson(original.detail) == JsonUtility.ToJson(rock.detail), "Original detail parameters changed.");
            Require(!rock.features.hasOcean && !rock.features.hasAtmosphere && !rock.features.hasClouds, "Water or atmosphere still enabled.");
            var baseline = CubeSphere.Generate(original.CreateSampler(), 64).CreateMesh("Baseline geometry check");
            try { Require(baseline.vertices.SequenceEqual(controller.Terrain.SurfaceMesh.vertices), "Bare rock must preserve original globe vertices."); }
            finally { Object.DestroyImmediate(baseline); }
            var vertices = controller.Terrain.SurfaceMesh.vertices;
            Vector3 direction = vertices[vertices.Length / 3].normalized;
            var d = Unity.Mathematics.math.normalize((Unity.Mathematics.double3)(Unity.Mathematics.float3)direction);
            double ground = bridge.SampleCollisionRadiusMetres(d);
            controller.Brush.PaintAt(direction, 0.05f); controller.Brush.EndStroke();
            Require(bridge.SampleCollisionRadiusMetres(d) > ground + 0.01, "Edited terrain missing from flight collision.");
            controller.Brush.UndoLastStroke();
            Require(Math.Abs(bridge.SampleCollisionRadiusMetres(d) - ground) < 0.01, "Undo did not restore collision.");
            Require(bridge.SweepSphere(d * (ground + 100), d * (ground - 100), 0.5, out var resolved, out _), "Swept collision missed ground.");
            Require(Unity.Mathematics.math.length(resolved) >= ground + 0.49, "Sweep entered terrain.");
            int index = sim.FocusIndex;
            Vec3d center = sim.PositionWorldMetresAt(index);
            Vec3d offset = SolarSystemSimulation.RotateMetres(earth.transform.rotation, new Vec3d(d.x * (ground + startingHeight), d.y * (ground + startingHeight), d.z * (ground + startingHeight)));
            Vector3 inward = -(earth.transform.rotation * (Vector3)(Unity.Mathematics.float3)d);
            flight.SetPose(new Unity.Mathematics.double3(center.x + offset.x, center.y + offset.y, center.z + offset.z), Quaternion.LookRotation(inward), Unity.Mathematics.double3.zero);
            flight.ApplyNavigationScroll(-1200f);
            double deadline = EditorApplication.timeSinceStartup + 2;
            EditorApplication.CallbackFunction check = null;
            check = () => {
                if (EditorApplication.timeSinceStartup < deadline) return;
                EditorApplication.update -= check;
                try {
                    bridge.Sync();
                    Require(bridge.Near && bridge.ClearanceMetres < startingHeight - 0.05 && bridge.ClearanceMetres >= 0.49, "Close scroll failed: " + bridge.ClearanceMetres);
                    Require(bridge.Pointer(sim.ViewCamera, new Vector3(sim.ViewCamera.pixelWidth / 2f, sim.ViewCamera.pixelHeight / 2f), out _, out _), "Near-ground brush ray missed surface.");
                    WriteReport("navigation-passed", "Original globe/parameters retained; flight enabled; edited collision/undo/sweep passed; wheel moved from " + startingHeight + "m to " + bridge.ClearanceMetres.ToString("F3") + "m; near brush ray passed.");
                } catch(Exception e) { File.WriteAllText(Report, JsonUtility.ToJson(new Result {status="FAIL", detail=e.ToString()}, true)); Debug.LogException(e); }
            };
            EditorApplication.update += check;
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
