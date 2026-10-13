using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Yongxian.PlanetSculpt.Editor
{
    public static class PlanetSculptValidation
    {
        [MenuItem("Tools/Yongxian/Validate Custom Planet Brush")]
        public static void Run()
        {
            bool passed = RunChecks();
            if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        }

        public static bool RunChecks()
        {
            GameObject planet = null;
            Mesh mesh = null;
            int result = 0;
            try
            {
                planet = new GameObject("Planet brush validation");
                var terrain = planet.AddComponent<PlanetTerrain>();
                terrain.Initialize();
                terrain.mountainBrush = false;
                mesh = terrain.SurfaceMesh;
                Require(terrain.VertexCount == 40962, "Subdivision level 6 should have 40962 welded vertices.");
                Require(mesh.triangles.Length == 245760, "Expected 81920 triangles.");
                Require(Mathf.Abs(terrain.MetersToLocalHeight(4000f) / terrain.radius - 0.08f) < 1e-6f, "4000 game meters should display at 8 percent of radius.");
                const float brushRadius = 4.5f * Mathf.Deg2Rad;
                Vector3[] original = mesh.vertices;
                int[] triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = original[triangles[i]], b = original[triangles[i + 1]], c = original[triangles[i + 2]];
                    Require(Vector3.Dot(Vector3.Cross(b - a, c - a), a) > 0f, "Face must point outward.");
                }
                Vector3 direction = original[0].normalized;
                terrain.BeginStroke();
                Require(terrain.ApplyBrush(direction, brushRadius, terrain.MetersToLocalHeight(4000f), false), "Raise should change vertices.");
                Require(Mathf.Abs(terrain.LocalHeightToMeters(terrain.CopyHeights()[0]) - 4000f) < 0.1f, "Center receives a 4000 m elevation.");
                Require(Mathf.Abs(mesh.uv2[0].x - 4000f) < 0.1f, "Shader elevation channel must match physical height.");
                var raised = mesh.vertices;
                for (int i = 0; i < original.Length; i++)
                    if (Vector3.Dot(original[i].normalized, direction) < Mathf.Cos(brushRadius))
                        Require(Vector3.Distance(original[i], raised[i]) < 0.00001f, "Outside brush must remain unchanged.");
                Physics.SyncTransforms();
                Require(terrain.SurfaceCollider.Raycast(new Ray(direction * 2f, -direction), out RaycastHit hit, 3f), "Ray must hit deformed collider.");
                Require(Mathf.Abs(hit.point.magnitude - (terrain.radius + terrain.MetersToLocalHeight(4000f))) < 0.000002f, "Collider must follow visible exaggerated surface.");
                Require(Mathf.Abs(terrain.GetHeightMeters(hit) - 4000f) < 2f, "Hover readout must use barycentric altitude.");
                float beforeSmooth = terrain.CopyHeights()[0];
                terrain.ApplyBrush(direction, brushRadius, 1f, true);
                Require(terrain.CopyHeights()[0] < beforeSmooth, "Smoothing must reduce the peak.");
                Require(terrain.UndoStroke(), "Stroke must be undoable.");
                foreach (float h in terrain.CopyHeights()) Require(h == 0f, "Undo must restore all heights.");
                foreach (var uv in mesh.uv2) Require(uv.x == 0f, "Undo must also reset shader snow and ground data.");
                terrain.ApplyBrush(direction, brushRadius, -10f, false);
                Require(Mathf.Abs(terrain.LocalHeightToMeters(terrain.CopyHeights()[0]) + 2000f) < 0.1f, "2000 m depth clamp must hold.");
                terrain.ApplyBrush(direction, brushRadius, 10f, false);
                Require(Mathf.Abs(terrain.LocalHeightToMeters(terrain.CopyHeights()[0]) - 9000f) < 0.1f, "9000 m height clamp must hold.");
                terrain.ResetTerrain();
                foreach (float h in terrain.CopyHeights()) Require(h == 0f, "Reset must restore sphere.");
                Require(terrain.UndoStroke() && terrain.CopyHeights()[0] > 0f, "Reset must be undoable.");
                terrain.ResetTerrain();
                terrain.mountainBrush = true;
                terrain.BeginStroke();
                terrain.ApplyBrush(direction, brushRadius, terrain.MetersToLocalHeight(3000f), false);
                var firstRidge = terrain.CopyHeights();
                terrain.UndoStroke();
                terrain.ApplyBrush(direction, brushRadius, terrain.MetersToLocalHeight(3000f), false);
                var secondRidge = terrain.CopyHeights();
                for (int i = 0; i < firstRidge.Length; i++)
                    Require(firstRidge[i] == secondRidge[i], "Ridge pattern must be deterministic after undo.");
                Require(terrain.GetComponent<MeshRenderer>().sharedMaterial.shader.name == "Yongxian/Natural Planet", "Natural surface must be applied.");
                ColorUtility.TryParseHtmlString("#AF822F", out Color expectedGround);
                Color actualGround = terrain.GetComponent<MeshRenderer>().sharedMaterial.GetColor("_GroundColor");
                Require(Mathf.Abs(actualGround.r - expectedGround.r) < 0.0001f && Mathf.Abs(actualGround.g - expectedGround.g) < 0.0001f && Mathf.Abs(actualGround.b - expectedGround.b) < 0.0001f, "Material must store requested #AF822F.");
                for (int i = 0; i < 25; i++)
                {
                    terrain.BeginStroke();
                    terrain.ApplyBrush(-direction, brushRadius, terrain.MetersToLocalHeight(10f), false);
                }
                Require(terrain.UndoCount == 20, "Undo history must retain at most 20 strokes.");
                for (int i = 0; i < 20; i++) Require(terrain.UndoStroke(), "Each retained stroke must undo.");
                Require(!terrain.UndoStroke(), "Empty history must safely return false.");
                terrain.ResetTerrain();
                terrain.mountainBrush = false;
                var brush = planet.AddComponent<PlanetBrush>();
                brush.PaintAt(direction, 1f / 60f);
                Require(mesh.vertices[0].magnitude - terrain.radius > terrain.radius * 0.0025f, "One click at default strength must still produce visible relief.");
                Require(Mathf.Abs(terrain.LocalHeightToMeters(terrain.CopyHeights()[0]) - 180f) < 0.1f, "Quick click at default strength must apply the reduced 180 game meters, independent of click duration.");
                brush.UndoLastStroke();
                foreach (float h in terrain.CopyHeights()) Require(h == 0f, "UI undo action must restore the whole click.");
                Vector3 to = Quaternion.AngleAxis(18f, Vector3.Cross(direction, Vector3.forward).normalized) * direction;
                brush.PaintAt(direction, 1f / 60f);
                brush.PaintAt(to, 1f / 60f);
                brush.EndStroke();
                Vector3 midpoint = Vector3.Slerp(direction, to, 0.5f);
                int nearest = 0;
                for (int i = 1; i < original.Length; i++)
                    if (Vector3.Dot(original[i].normalized, midpoint) > Vector3.Dot(original[nearest].normalized, midpoint)) nearest = i;
                Require(terrain.LocalHeightToMeters(terrain.CopyHeights()[nearest]) > 75f, "Fast drag must fill its middle rather than leave two disconnected dots.");
                brush.UndoLastStroke();
                foreach (float h in terrain.CopyHeights()) Require(h == 0f, "One undo must remove the entire interpolated drag.");
                terrain.ApplyBrush(direction, brushRadius, terrain.MetersToLocalHeight(4000f), false);
                terrain.ApplyBrush(to, brushRadius, terrain.MetersToLocalHeight(3000f), false);
                terrain.ApplyBrush(-direction, brushRadius, terrain.MetersToLocalHeight(-500f), false);
                var beforeLower = terrain.CopyHeights();
                brush.SetLowerMode(true);
                brush.PaintAt(direction, 1f / 60f);
                Require(Mathf.Abs(terrain.LocalHeightToMeters(terrain.CopyHeights()[0]) - 3820f) < 0.1f, "Lower mode must gently reduce the mountain by 180 game meters.");
                brush.PaintAt(to, 1f / 60f);
                brush.EndStroke();
                var afterLower = terrain.CopyHeights();
                bool loweredDestination = false;
                for (int i = 0; i < beforeLower.Length; i++)
                {
                    Require(afterLower[i] <= beforeLower[i], "Lower mode must never raise terrain.");
                    if (Vector3.Dot(original[i].normalized, to) > 0.9999f && afterLower[i] < beforeLower[i]) loweredDestination = true;
                }
                Require(loweredDestination, "Dragging must lower terrain along its path.");
                brush.UndoLastStroke();
                var restored = terrain.CopyHeights();
                for (int i = 0; i < restored.Length; i++) Require(restored[i] == beforeLower[i], "One undo must restore the entire lowered stroke.");
                terrain.ResetTerrain();
                brush.PaintAt(direction, 1f / 60f);
                Require(Mathf.Abs(mesh.uv2[0].x + 180f) < 0.1f, "Lower mode must dig into an untouched sphere.");
                brush.PaintAt(direction, 0.1f);
                Require(mesh.uv2[0].x < -190f, "Holding must deepen an existing basin.");
                brush.PaintAt(to, 1f / 60f);
                Require(terrain.LocalHeightToMeters(terrain.CopyHeights()[nearest]) < -75f, "Fast dragging must dig a continuous trench across the original surface.");
                brush.UndoLastStroke();
                foreach (float h in terrain.CopyHeights()) Require(h == 0f, "One undo must restore the entire trench to the original sphere.");
                terrain.ApplyBrush(direction, brushRadius, terrain.MetersToLocalHeight(90f), false);
                brush.PaintAt(direction, 1f / 60f);
                Require(Mathf.Abs(mesh.uv2[0].x + 90f) < 0.1f, "Lowering must pass through zero height without stopping.");
                brush.EndStroke();
                for (int i = 0; i < 15; i++) terrain.ApplyBrush(direction, brushRadius, terrain.MetersToLocalHeight(-180f), false);
                Require(Mathf.Abs(mesh.uv2[0].x + 2000f) < 0.1f, "Repeated lowering must respect the configured depth limit.");
                Physics.SyncTransforms();
                Require(terrain.SurfaceCollider.Raycast(new Ray(direction * 2f, -direction), out RaycastHit loweredHit, 3f), "Lowered terrain must remain hittable.");
                Require(Mathf.Abs(loweredHit.point.magnitude - (terrain.radius + terrain.MetersToLocalHeight(-2000f))) < 0.000002f, "Collider must follow the excavated surface.");
                brush.SetLowerMode(false);
                File.WriteAllText("Library/PlanetSculptValidation.txt", "PASS: raise/lower modes; dig untouched ground; deepen basins; cross zero height; continuous trenches; depth limit; whole-stroke undo; geometry/collider; altitude UV; topology; smoothing; 20-stroke history; palette.");
                Debug.Log("Planet brush validation PASSED.");
            }
            catch (Exception error)
            {
                result = 1;
                File.WriteAllText("Library/PlanetSculptValidation.txt", "FAIL: " + error);
                Debug.LogException(error);
            }
            finally
            {
                if (planet != null) UnityEngine.Object.DestroyImmediate(planet);
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
            return result == 0;
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
