using System;
using System.Text;
using Planet.Solar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Planet.EditorTools
{
    /// <summary>
    /// Bakes every body's scene-unit radius into the body itself.
    ///
    /// Background: the render radius used to be re-derived from
    /// <c>SolarSystemCatalog.RadiusKm</c> and <c>SolarSystemSimulation.kmPerUnit</c> on every frame.
    /// It is now <see cref="CelestialBody.sceneRadiusUnits"/> - a serialized property of the body -
    /// and this menu is the only thing that writes it.
    ///
    /// The catalog stays the single source of truth for *physical* size (DEVELOPMENT-WORKFLOW §4).
    /// The baked value is a derived cache, and it records the <c>kmPerUnit</c> it came from, so the
    /// scene scale drifting away from the baked numbers is detectable rather than silent: the bake
    /// reports it, and <see cref="CelestialBody"/> warns at runtime.
    ///
    /// Owned by the SolarSystem worktree. It deliberately does not touch procedural definitions,
    /// materials or texture caches - those belong to planet-definition / SolarSystemMaterialBuilder.
    /// </summary>
    public static class BodyScaleBaker
    {
        [MenuItem("Tools/Planet/Bake Body Scales", false, 23)]
        public static void MenuBake()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Bake body scales outside Play mode.");

            Scene scene = SceneManager.GetActiveScene();
            SolarSystemSimulation sim = FindSimulation(scene);
            if (sim == null)
                throw new InvalidOperationException(
                    "No SolarSystemSimulation in scene '" + scene.name + "'. Open SolarSystem.unity first.");

            CelestialBody[] bodies = FindBodies(sim);
            if (bodies.Length == 0)
                throw new InvalidOperationException("The simulation has no CelestialBody children to bake.");

            float kmPerUnit = sim.KmPerUnit;
            int changed = 0;
            var report = new StringBuilder();

            foreach (CelestialBody body in bodies)
            {
                if (body == null) continue;
                if (body.BakeScale(kmPerUnit))
                {
                    changed++;
                    report.AppendLine(string.Format("  {0,-10} {1:G9} scene units", body.bodyName, body.sceneRadiusUnits));
                }
                // BuildVisuals re-reads the baked scale and rewrites the surface / ring transforms.
                body.BuildVisuals();
                EditorUtility.SetDirty(body);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log(string.Format(
                "[Planet] Baked {0} body scales at kmPerUnit={1:N0} ({2} changed) in {3}.\n{4}",
                bodies.Length, kmPerUnit, changed, scene.name, report.Length > 0 ? report.ToString() : string.Empty));
        }

        /// <summary>
        /// Read-only report: which bodies would change if the bake ran, and which were baked against
        /// a different scene scale. Use it to audit the scene before committing.
        /// </summary>
        [MenuItem("Tools/Planet/Validate Body Scales", false, 24)]
        public static void MenuValidate()
        {
            Scene scene = SceneManager.GetActiveScene();
            SolarSystemSimulation sim = FindSimulation(scene);
            if (sim == null)
            {
                Debug.LogWarning("[Planet] No SolarSystemSimulation in scene '" + scene.name + "'.");
                return;
            }

            float kmPerUnit = sim.KmPerUnit;
            CelestialBody[] bodies = FindBodies(sim);
            var report = new StringBuilder();
            int stale = 0, drifted = 0, unbaked = 0;

            foreach (CelestialBody body in bodies)
            {
                if (body == null) continue;
                Body def = SolarSystemCatalog.Find(body.bodyName);
                if (def == null)
                {
                    report.AppendLine("  " + body.bodyName + ": not in SolarSystemCatalog");
                    continue;
                }

                float expected = (float)(def.RadiusKm / kmPerUnit);
                if (body.sceneRadiusUnits <= 0f)
                {
                    unbaked++;
                    report.AppendLine(string.Format("  {0,-10} NOT BAKED (would become {1:G9})", body.bodyName, expected));
                }
                else if (!Mathf.Approximately(body.sceneRadiusUnits, expected))
                {
                    stale++;
                    report.AppendLine(string.Format("  {0,-10} {1:G9} != catalog {2:G9}",
                        body.bodyName, body.sceneRadiusUnits, expected));
                }

                if (body.bakedKmPerUnit > 0f && !Mathf.Approximately(body.bakedKmPerUnit, kmPerUnit))
                {
                    drifted++;
                    report.AppendLine(string.Format("  {0,-10} baked for kmPerUnit={1:N0}, simulation uses {2:N0}",
                        body.bodyName, body.bakedKmPerUnit, kmPerUnit));
                }
            }

            string summary = string.Format(
                "[Planet] Body scale audit for '{0}' at kmPerUnit={1:N0}: {2} bodies, {3} stale, {4} unbaked, {5} baked against another scale.",
                scene.name, kmPerUnit, bodies.Length, stale, unbaked, drifted);

            if (stale + unbaked + drifted > 0) Debug.LogWarning(summary + "\n" + report);
            else Debug.Log(summary + " All baked values match the catalog.");
        }

        private static SolarSystemSimulation FindSimulation(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                SolarSystemSimulation sim = root.GetComponentInChildren<SolarSystemSimulation>(true);
                if (sim != null) return sim;
            }
            return null;
        }

        private static CelestialBody[] FindBodies(SolarSystemSimulation sim)
        {
            Transform root = sim.bodiesRoot != null ? sim.bodiesRoot : sim.transform;
            return root.GetComponentsInChildren<CelestialBody>(true);
        }
    }
}
