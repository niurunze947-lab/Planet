using System;
using System.Collections.Generic;
using System.Text;
using Planet.Procedural;
using Planet.Solar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Planet.EditorTools
{
    /// <summary>
    /// Prints the numbers behind the two-coordinate-system design, taken from the open scene and the
    /// live float/double behaviour rather than from a table in a document.
    ///
    /// <para>Everything here is read-only. It exists because the central claim of the design -
    /// "cosmic positions need doubles, surface detail needs chunk-local floats" - is only meaningful
    /// as a measurement, and because the failure mode it prevents (a world that jitters once the
    /// camera is far enough from the origin) is invisible in a screenshot.</para>
    ///
    /// <para>Owned by the SolarSystem worktree. It touches no asset, no material and no procedural
    /// definition; it only reads the scene and reports.</para>
    /// </summary>
    public static class SpaceScaleAuditor
    {
        [MenuItem("Tools/Planet/Audit Space Scale", false, 25)]
        public static void MenuAudit()
        {
            Scene scene = SceneManager.GetActiveScene();
            SolarSystemSimulation sim = FindSimulation(scene);
            if (sim == null)
            {
                Debug.LogWarning("[Planet] No SolarSystemSimulation in scene '" + scene.name +
                                 "'. Open SolarSystem.unity first.");
                return;
            }

            SolarSpaceContext ctx = sim.GetComponent<SolarSpaceContext>();
            if (ctx == null)
            {
                Debug.LogWarning("[Planet] '" + sim.name + "' has no SolarSpaceContext, so there is no " +
                                 "frame tree to audit. Rebuild the scene (Tools/Planet/Rebuild Solar " +
                                 "System Scene) or add the component by hand.");
                return;
            }

            // The simulation resolves its scene in Build(); the frame tree needs it, and it needs the
            // positions resolved too, or every distance in the report would read as zero.
            sim.Build();
            sim.RefreshPositions();
            ctx.Initialize(sim);

            // One console entry per section. Unity's console renders multi-line messages fine, but a
            // single 200-line entry is unreadable in the log and cannot be filtered by section.
            var full = new StringBuilder();
            full.AppendLine("Space scale audit - scene '" + scene.name + "', kmPerUnit = " +
                            sim.KmPerUnit.ToString("N0") + " (1 scene unit = " +
                            SpacePrecision.FormatMetres(sim.KmPerUnit * 1000.0) + ")");

            LogSection(AppendRungs, sim, ctx, full);
            LogSection(AppendMantissaCeiling, sim, ctx, full);
            LogSection(AppendBodyTiers, sim, ctx, full);
            LogSection(AppendSurfaceLadder, sim, ctx, full);
            LogSection(AppendSeams, sim, ctx, full);
            LogSection(AppendBelts, sim, ctx, full);
            LogSection(AppendOrbits, sim, ctx, full);

            // Also written to Library/, which is gitignored and outside Assets/: a report that can be
            // diffed between two runs is worth more than one that scrolls past in the console, and
            // nothing here belongs in version control.
            string path = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.dataPath), "Library/SpaceScaleAudit.txt");
            try
            {
                System.IO.File.WriteAllText(path, full.ToString());
                Debug.Log("[Planet] Full audit report written to " + path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Planet] Could not write the audit report to " + path + ": " + e.Message);
            }
        }

        private static void LogSection(Action<StringBuilder, SolarSystemSimulation, SolarSpaceContext> section,
                                       SolarSystemSimulation sim, SolarSpaceContext ctx, StringBuilder full)
        {
            var report = new StringBuilder();
            section(report, sim, ctx);
            full.AppendLine();
            full.Append(report);
            Debug.Log("[Planet] " + report.ToString().TrimEnd());
        }

        // ======================================================================================
        // Rungs: where the origin sits and how much precision is left at each scale
        // ======================================================================================

        private static void AppendRungs(StringBuilder report, SolarSystemSimulation sim, SolarSpaceContext ctx)
        {
            report.AppendLine("COORDINATE RUNGS");
            report.AppendLine("  rung                 depth   origin in parent (m)          units/m     observer offset      float32 step there");

            foreach (SpaceFrame frame in ctx.Tree.AllFrames)
            {
                // The observer's offset from this rung's own origin: the step that matters for any
                // geometry stored in this frame.
                Vec3d observerFromFrame = ctx.ObserverInRootMetres - frame.OriginInRootMetres;
                double distance = observerFromFrame.Length;
                double step = distance > 1e-9 ? SpacePrecision.ExactUlp(distance, NumericFormat.Float32) : 0.0;

                report.AppendLine(string.Format("  {0,-20} {1,4}    {2,-27} {3,-11:G4} {4,-19} {5}",
                    frame.name, frame.Depth,
                    SpacePrecision.FormatMetres(frame.originInParentMetres.Length),
                    frame.unitsPerMetre,
                    SpacePrecision.FormatMetres(distance),
                    step > 0.0 ? SpacePrecision.FormatMetres(step) : "n/a (observer at origin)"));
            }

            report.AppendLine();
            report.AppendLine("  Simulation origin (floating origin, double km) = " + sim.OriginKm);
            report.AppendLine("  Observer in focused body frame                 = " +
                              SpacePrecision.FormatMetres(ctx.ObserverInBodyMetres.Length) + " from its centre");
            report.AppendLine("  Float32 step at the observer's own coordinates  = " +
                              (ctx.ObserverStepMetres > 0.0
                                  ? SpacePrecision.FormatMetres(ctx.ObserverStepMetres)
                                  : "n/a (the camera sits exactly on the origin)"));
            report.AppendLine("  Float32 step at the farthest rendered object   = " +
                              SpacePrecision.FormatMetres(ctx.WorstRenderStepMetres) +
                              "   <- the worst quantisation anywhere in the visible world");
            report.AppendLine(string.Format("  Origin rebases so far = {0}, last delta = {1}, peak drift = {2}",
                ctx.SurfaceOrigin.RebaseCount,
                SpacePrecision.FormatMetres(ctx.SurfaceOrigin.LastRebaseDeltaMetres),
                SpacePrecision.FormatMetres(ctx.SurfaceOrigin.PeakDriftMetres)));
            report.AppendLine();
        }

        // ======================================================================================
        // The headline number: float32 has 24 significand bits, so it can only span so far
        // ======================================================================================

        private static void AppendMantissaCeiling(StringBuilder report, SolarSystemSimulation sim, SolarSpaceContext ctx)
        {
            report.AppendLine("MANTISSA CEILING (why one format cannot cover pebble -> galaxy)");
            report.AppendLine("  format   significand   relative precision   step at 1 m   span still holding 1 m   span still holding 1 cm");
            foreach (NumericFormat format in new[] { NumericFormat.Float32, NumericFormat.Float64 })
            {
                report.AppendLine(string.Format("  {0,-8} {1,11}   {2,18:E3}   {3,-11}   {4,-21}   {5}",
                    format,
                    SpacePrecision.SignificandBits(format),
                    SpacePrecision.RelativePrecision(format),
                    SpacePrecision.FormatMetres(SpacePrecision.ExactUlp(1.0, format)),
                    SpacePrecision.FormatMetres(SpacePrecision.SpanForFeature(1.0, format)),
                    SpacePrecision.FormatMetres(SpacePrecision.SpanForFeature(0.01, format))));
            }

            report.AppendLine("  the span column is the distance from a frame origin at which the format's step");
            report.AppendLine("  first exceeds the feature; past it the feature is quantised away entirely.");

            double pebble = 0.01;
            double galaxy = 9.5e20;
            double ratio = galaxy / pebble;
            report.AppendLine();
            report.AppendLine("  span modelled here: 1 cm pebble -> Milky Way (1.0e5 ly) = " +
                              SpacePrecision.FormatMetres(galaxy));
            report.AppendLine("    span / feature = " + ratio.ToString("E2") + "  (" + Math.Log10(ratio).ToString("F1") +
                              " orders of magnitude)");
            foreach (NumericFormat format in new[] { NumericFormat.Float32, NumericFormat.Float64 })
            {
                int levels = SpacePrecision.LevelsRequired(galaxy, pebble, format);
                report.AppendLine(string.Format("    {0}: {1} nested rungs of 2^{2} subdivision, each re-centring on the last",
                    format, levels, SpacePrecision.SignificandBits(format)));
            }
            report.AppendLine("  so the answer to 'can one coordinate system hold pebble to galaxy' is no for either");
            report.AppendLine("  format on its own - and the same arithmetic says the Solar System needs exactly one rung");
            report.AppendLine("  above the body (1.0e13 m span / 1 cm feature), which is the Stellar -> Planetary step.");
            report.AppendLine();
        }

        // ======================================================================================
        // Q2: what happens to everything that is not the focused body
        // ======================================================================================

        private static void AppendBodyTiers(StringBuilder report, SolarSystemSimulation sim, SolarSpaceContext ctx)
        {
            Camera camera = sim.ViewCamera != null ? sim.ViewCamera : Camera.main;
            float fov = camera != null ? camera.fieldOfView : 50f;
            int height = camera != null ? Math.Max(camera.pixelHeight, 1) : 1080;

            report.AppendLine("FAR-FIELD POLICY (what a focus change does to the other bodies)");
            report.AppendLine("  focus = " + (sim.FocusView != null ? sim.FocusView.bodyName : "?") +
                              ", camera = " + (camera != null ? camera.name : "none") +
                              ", fov = " + fov.ToString("F0") + " deg, " + height + " px tall");
            report.AppendLine("  thresholds: skipped below " + ctx.subPixelThreshold.ToString("F2") +
                              " px, marker below " + ctx.markerThreshold.ToString("F0") + " px, proxy below 64 px");
            report.AppendLine();
            report.AppendLine("  body         radius          distance        projected      tier");

            int[] counts = new int[4];
            ctx.CountTiers(counts);

            var trials = new List<string>();
            for (int i = 0; i < sim.BodyCount; i++)
            {
                CelestialBody body = sim.ViewAt(i);
                if (body == null) continue;

                double radiusMetres = body.RadiusUnits / (sim.UnitsPerKm * 0.001);
                Vec3d bodyInStellar = new Vec3d(
                    sim.PositionKmAt(i).x * 1000.0, sim.PositionKmAt(i).y * 1000.0, sim.PositionKmAt(i).z * 1000.0);
                Vec3d originInStellar = ctx.Origin.positionInFrameMetres;
                double distanceMetres = Math.Max((bodyInStellar - originInStellar).Length, 1e-6);
                if (camera != null)
                {
                    Vector3 camWorld = camera.transform.position;
                    Vec3d camInStellar = originInStellar + new Vec3d(
                        camWorld.x / (sim.UnitsPerKm * 0.001),
                        camWorld.y / (sim.UnitsPerKm * 0.001),
                        camWorld.z / (sim.UnitsPerKm * 0.001));
                    distanceMetres = Math.Max((bodyInStellar - camInStellar).Length, 1e-6);
                }

                double diameterPixels = 2.0 * radiusMetres / distanceMetres /
                                        (2.0 * Math.Tan(fov * Math.PI / 360.0)) * height;
                BodyRenderTier tier = ctx.ClassifyBody(i, camera, fov, height);
                string name = body.bodyName + (i == sim.FocusIndex ? " *" : string.Empty);

                // Below a metre the camera is standing on the body and the projection is meaningless:
                // 2r/d goes to infinity, and printing 1.0e16 px in a table of measurements is worse
                // than printing nothing. The tier is still reported, because it is still correct.
                bool coincident = distanceMetres < 1.0;
                string distanceText = coincident ? "on the body" : SpacePrecision.FormatMetres(distanceMetres);
                string pixelText = coincident ? "     (n/a)" : string.Format("{0,10:F3} px", diameterPixels);

                report.AppendLine(string.Format("  {0,-12} {1,-13} {2,-15} {3}   {4}",
                    name,
                    SpacePrecision.FormatMetres(radiusMetres),
                    distanceText,
                    pixelText,
                    tier));

                if (tier == BodyRenderTier.Skipped) trials.Add(body.bodyName);
            }

            report.AppendLine();
            report.AppendLine(string.Format("  tiers: {0} full, {1} proxy, {2} marker, {3} skipped of {4} bodies",
                counts[0], counts[1], counts[2], counts[3], sim.BodyCount));
            if (trials.Count > 0)
            {
                report.AppendLine("  renderer switched off for: " + string.Join(", ", trials.ToArray()));
                report.AppendLine("  (radius, transform and published RadiusUnits are untouched - only the " +
                                  "MeshRenderer is disabled, so the shells and the framing maths keep working)");
            }
            report.AppendLine("  note: the 'G' key multiplies every radius by 400, which moves bodies up through " +
                              "these tiers on its own.");
            if (camera != null && Camera.main == camera && !Application.isPlaying)
                report.AppendLine("  note: the editor camera sits on the scene origin, where the floating origin also is,\n" +
                                  "  so the focused body reads as coincident. In play mode the camera's own orbit\n" +
                                  "  distance governs and the tier follows it.");
            report.AppendLine();
        }

        // ======================================================================================
        // Surface rung: chunk size and the float32 step inside a chunk
        // ======================================================================================

        private static void AppendSurfaceLadder(StringBuilder report, SolarSystemSimulation sim, SolarSpaceContext ctx)
        {
            SurfaceChunkGrid grid = ctx.SurfaceGrid;
            if (grid == null)
            {
                report.AppendLine("SURFACE CHUNK LADDER: no grid (focus body has no catalog definition)");
                report.AppendLine();
                return;
            }

            report.AppendLine("SURFACE CHUNK LADDER (focused body; chunk-local float32 coordinates)");
            report.AppendLine("  body radius = " + SpacePrecision.FormatMetres(grid.bodyRadiusMetres) +
                              ", flattening = " + grid.flattening.ToString("F5") +
                              ", maxLevel = " + grid.maxLevel + ", resolution = " + grid.resolution);
            report.AppendLine();
            report.AppendLine("  level   chunks/face   chunk edge      grid spacing    vertex float32 step   vs globe");
            double globeStep = ChunkLocalGeometry.GlobeVertexStepMetres(grid.bodyRadiusMetres);

            for (int level = 4; level <= grid.maxLevel; level++)
            {
                if (level > 4 && level < grid.maxLevel - 6 && level != 8 && level != 12) continue;

                double chunk = grid.ChunkSizeMetres(level);
                double spacing = chunk / grid.resolution;
                double step = ChunkLocalGeometry.VertexStepMetres(grid, level);
                report.AppendLine(string.Format("  {0,5}   {1,11:N0}   {2,-15} {3,-15} {4,-21} {5,8:N0}x",
                    level,
                    grid.CellsPerFace(level),
                    SpacePrecision.FormatMetres(chunk),
                    SpacePrecision.FormatMetres(spacing),
                    SpacePrecision.FormatMetres(step),
                    globeStep / step));
            }

            report.AppendLine();
            report.AppendLine("  reference: the unit-sphere globe vertex carries a float32 step of " +
                              SpacePrecision.FormatMetres(globeStep) + " at this radius,");
            report.AppendLine("  because it is stored as a fraction of the radius. Chunk-local metres are not.");
            report.AppendLine();
            report.AppendLine("  two different ceilings, and it is worth keeping them apart:");
            report.AppendLine("    precision ceiling - the level whose chunk-local float32 step first reaches the target:");
            for (int i = 0; i < PrecisionTargetsMetres.Length; i++)
            {
                int level = SpacePrecision.ChunkLevelForPrecision(grid.bodyRadiusMetres, PrecisionTargetsMetres[i]);
                report.AppendLine(string.Format("      {0,-9} -> L{1,-3} (chunk {2}, float32 step {3}, grid spacing {4})",
                    SpacePrecision.FormatMetres(PrecisionTargetsMetres[i]), level,
                    SpacePrecision.FormatMetres(grid.ChunkSizeMetres(level)),
                    SpacePrecision.FormatMetres(ChunkLocalGeometry.VertexStepMetres(grid, level)),
                    SpacePrecision.FormatMetres(grid.ChunkSizeMetres(level) / grid.resolution)));
            }

            report.AppendLine("    resolution ceiling - the level whose grid spacing reaches the target. This is far");
            report.AppendLine("    finer than the precision ceiling and is what actually sets how small a terrain");
            report.AppendLine("    feature can be resolved at all; levels past maxLevel would need a deeper index:");
            for (int i = 0; i < PrecisionTargetsMetres.Length; i++)
            {
                int level = ResolutionLevelFor(grid, PrecisionTargetsMetres[i]);
                double spacing = grid.bodyRadiusMetres * Math.PI * 0.5 / Math.Pow(2.0, level) / grid.resolution;
                report.AppendLine(string.Format("      {0,-14} -> L{1,-3} (grid spacing {2}{3})",
                    SpacePrecision.FormatMetres(PrecisionTargetsMetres[i]), level,
                    SpacePrecision.FormatMetres(spacing),
                    level > grid.maxLevel ? "  BEYOND maxLevel=" + grid.maxLevel : "  (reachable)"));
            }
            report.AppendLine();
        }

        private static readonly double[] PrecisionTargetsMetres = { 1.0, 0.01, 0.001, 0.0001 };

        /// <summary>
        /// Coarsest level whose grid spacing is at or below the target. Computed rather than searched,
        /// so it reports levels deeper than <see cref="SurfaceChunkGrid.maxLevel"/> instead of
        /// silently clamping to whatever the grid happens to allow.
        /// </summary>
        private static int ResolutionLevelFor(SurfaceChunkGrid grid, double targetMetres)
        {
            if (!(targetMetres > 0.0) || !(grid.bodyRadiusMetres > 0.0)) return 0;
            double faceArc = grid.bodyRadiusMetres * Math.PI * 0.5;
            double maxChunkSize = targetMetres * grid.resolution;

            int level = 0;
            while (level < 40 && faceArc / Math.Pow(2.0, level) > maxChunkSize) level++;
            return level;
        }

        // ======================================================================================
        // The two invariants the chunk grid has to satisfy to be usable at all
        // ======================================================================================

        private static void AppendSeams(StringBuilder report, SolarSystemSimulation sim, SolarSpaceContext ctx)
        {
            report.AppendLine("CHUNK GRID INVARIANTS");

            SurfaceChunkGrid grid = ctx.SurfaceGrid;
            if (grid == null)
            {
                report.AppendLine("  skipped: no surface grid");
                report.AppendLine();
                return;
            }

            // A coarse copy of the focused body's grid, used for the six-face neighbour sweep.
            var small = new SurfaceChunkGrid(grid.bodyRadiusMetres, grid.flattening, 2, grid.resolution);

            // 1. Two chunks that share an edge must sample that edge at the same set of points, bit for
            //    bit. This is the crack test, and it is done as a set comparison on purpose: across a
            //    cube-face boundary the shared edge can be traversed in the opposite direction, so
            //    pairing vertices by index would report a failure where the geometry is in fact
            //    watertight. Set equality is the property the mesher actually needs.
            var faces = (PlanetCubeFace[])Enum.GetValues(typeof(PlanetCubeFace));
            var directions = (ChunkNeighbourDirection[])Enum.GetValues(typeof(ChunkNeighbourDirection));

            int edges = 0, matched = 0, unmatched = 0, reversed = 0, missing = 0, crossFace = 0;
            long worstUnmatchedBits = 0;
            string firstUnmatched = "none";

            foreach (PlanetCubeFace face in faces)
            {
                int cells = small.CellsPerFace(2);
                for (int y = 0; y < cells; y++)
                {
                    for (int x = 0; x < cells; x++)
                    {
                        var key = new SurfaceChunkKey(face, 2, x, y);
                        foreach (ChunkNeighbourDirection direction in directions)
                        {
                            edges++;
                            SurfaceChunkKey neighbour;
                            if (!small.TryNeighbour(key, direction, out neighbour)) { missing++; continue; }
                            if (neighbour.face != key.face) crossFace++;

                            // Which of the four edges of key has a neighbour edge with the same points?
                            bool found = false;
                            for (int ke = 0; ke < 4 && !found; ke++)
                            {
                                Vec3d[] selfEdge = EdgeDirections(small, key, ke);
                                for (int ne = 0; ne < 4 && !found; ne++)
                                {
                                    Vec3d[] otherEdge = EdgeDirections(small, neighbour, ne);
                                    if (!SameDirectionSet(selfEdge, otherEdge)) continue;

                                    found = true;
                                    matched++;
                                    // Same points, opposite traversal order: fine for a stitched mesh,
                                    // but worth counting because it is what a naive index pairing trips on.
                                    if (BitDifference(selfEdge[0], otherEdge[0]) != 0) reversed++;
                                }
                            }

                            if (!found)
                            {
                                unmatched++;
                                long bits = NearestEdgeDifference(small, key, neighbour);
                                if (bits > worstUnmatchedBits) worstUnmatchedBits = bits;
                                if (firstUnmatched == "none") firstUnmatched = key + " ->" + direction + "-> " + neighbour;
                            }
                        }
                    }
                }
            }

            report.AppendLine("  1. seam continuity: " + edges + " chunk edges sampled at L2 over all six cube faces");
            report.AppendLine("       neighbours found:      " + (edges - missing) + " of " + edges +
                              (missing == 0 ? "  (every edge of every chunk has one)" : "  MISSING " + missing));
            report.AppendLine("       shared point sets:     " + matched + " matched exactly, " + unmatched + " unmatched");
            report.AppendLine("       of the matches, " + reversed + " traverse the shared edge in the opposite direction");
            report.AppendLine("       resolved across a cube-face boundary: " + crossFace +
                              "  <- the cases a hand-written 24-entry edge table gets wrong");
            if (unmatched > 0)
                report.AppendLine("       WORST UNMATCHED: " + worstUnmatchedBits + " ULP, first at " + firstUnmatched);

            // 2. Every vertex a chunk hands out must be reachable in its neighbour's shared edge with
            //    zero drift, which is what stops the terrain from opening a hairline gap.
            long worstSeamBits = 0;
            string worstSeamWhere = "none";
            int seamVertices = 0;
            foreach (PlanetCubeFace face in faces)
            {
                int cells = small.CellsPerFace(3);
                for (int y = 0; y < cells; y++)
                {
                    for (int x = 0; x + 1 < cells; x++)
                    {
                        var left = new SurfaceChunkKey(face, 3, x, y);
                        var right = new SurfaceChunkKey(face, 3, x + 1, y);
                        for (int iy = 0; iy <= small.resolution; iy++)
                        {
                            long bits = BitDifference(small.SampleDirection(left, small.resolution, iy),
                                                      small.SampleDirection(right, 0, iy));
                            seamVertices++;
                            if (bits > worstSeamBits) { worstSeamBits = bits; worstSeamWhere = left + " | " + right + " iy=" + iy; }
                        }
                    }
                }
            }
            report.AppendLine("  2. in-face seam drift at L3: worst " + worstSeamBits + " ULP over " +
                              seamVertices.ToString("N0") + " shared vertices, at " + worstSeamWhere);
            report.AppendLine("       the same-face case has an exact index identity, so this must be 0; anything" +
                              " else would show as a crack.");

            // 3. Equal-level neighbours must not be flagged for stitching.
            int badStitch = 0, stitchPairs = 0;
            for (int level = 1; level <= 3; level++)
            {
                int cells = grid.CellsPerFace(level);
                for (int x = 0; x + 1 < cells; x++)
                {
                    var a = new SurfaceChunkKey(PlanetCubeFace.PositiveX, level, x, 0);
                    var b = new SurfaceChunkKey(PlanetCubeFace.PositiveX, level, x + 1, 0);
                    if (SurfaceChunkGrid.NeedsStitch(a, b)) badStitch++; else stitchPairs++;
                }
            }
            report.AppendLine("  3. same-level neighbour pairs: " + stitchPairs + " checked, " + badStitch +
                              " flagged for stitching (must be 0 - equal levels share vertices exactly)");
            report.AppendLine();
        }

        /// <summary>The four edges of a chunk as direction arrays: 0 = v min, 1 = u max, 2 = v max, 3 = u min.</summary>
        private static Vec3d[] EdgeDirections(SurfaceChunkGrid grid, SurfaceChunkKey key, int edge)
        {
            var result = new Vec3d[grid.resolution + 1];
            for (int i = 0; i <= grid.resolution; i++)
            {
                switch (edge)
                {
                    case 0: result[i] = grid.SampleDirection(key, i, 0); break;
                    case 1: result[i] = grid.SampleDirection(key, grid.resolution, i); break;
                    case 2: result[i] = grid.SampleDirection(key, i, grid.resolution); break;
                    default: result[i] = grid.SampleDirection(key, 0, i); break;
                }
            }
            return result;
        }

        /// <summary>
        /// True when two edges cover exactly the same points. Compared as sets because the traversal
        /// order across a cube-face boundary is not guaranteed to agree.
        /// </summary>
        private static bool SameDirectionSet(Vec3d[] a, Vec3d[] b)
        {
            if (a.Length != b.Length) return false;
            var remaining = new List<int>(b.Length);
            for (int i = 0; i < b.Length; i++) remaining.Add(i);

            for (int i = 0; i < a.Length; i++)
            {
                int hit = -1;
                for (int j = 0; j < remaining.Count; j++)
                {
                    if (BitDifference(a[i], b[remaining[j]]) == 0) { hit = j; break; }
                }
                if (hit < 0) return false;
                remaining.RemoveAt(hit);
            }
            return true;
        }

        /// <summary>How far apart the closest pair of edges of two chunks is, in ULP. Diagnostic only.</summary>
        private static long NearestEdgeDifference(SurfaceChunkGrid grid, SurfaceChunkKey a, SurfaceChunkKey b)
        {
            long best = long.MaxValue;
            for (int ae = 0; ae < 4; ae++)
            {
                Vec3d[] ea = EdgeDirections(grid, a, ae);
                for (int be = 0; be < 4; be++)
                {
                    Vec3d[] eb = EdgeDirections(grid, b, be);
                    for (int i = 0; i < ea.Length; i++)
                    {
                        long bits = BitDifference(ea[i], eb[i]);
                        if (bits < best) best = bits;
                    }
                }
            }
            return best == long.MaxValue ? 0 : best;
        }

        /// <summary>
        /// Bit-level difference between two doubles, as a count of representable steps. Used instead
        /// of an epsilon because the invariant under test is exact equality: two chunks either agree
        /// on a shared vertex or they do not, and "close enough" still opens a crack.
        /// </summary>
        private static long BitDifference(double a, double b)
        {
            if (a == b) return 0;                       // also catches +0 == -0
            if (double.IsNaN(a) || double.IsNaN(b)) return long.MaxValue;
            long ka = BitConverter.DoubleToInt64Bits(a);
            long kb = BitConverter.DoubleToInt64Bits(b);
            if (ka < 0) ka = long.MinValue - ka;
            if (kb < 0) kb = long.MinValue - kb;
            return Math.Abs(ka - kb);
        }

        private static long BitDifference(Vec3d a, Vec3d b)
        {
            return Math.Max(BitDifference(a.x, b.x),
                   Math.Max(BitDifference(a.y, b.y), BitDifference(a.z, b.z)));
        }

        // ======================================================================================
        // Debris belts: the measured cost of the solve cadence
        // ======================================================================================

        private static void AppendBelts(StringBuilder report, SolarSystemSimulation sim, SolarSpaceContext ctx)
        {
            BeltField[] belts = sim.GetComponentsInChildren<BeltField>(true);
            if (belts.Length == 0) { report.AppendLine("DEBRIS BELTS: none in scene"); report.AppendLine(); return; }

            int total = 0;
            report.AppendLine("DEBRIS BELTS (solve/submit split)");
            report.AppendLine("  field            particles   solves since Build   last solved   last submitted   last solve ms   last submit ms");

            foreach (BeltField belt in belts)
            {
                total += belt.count;
                report.AppendLine(string.Format("  {0,-16} {1,9:N0}   {2,19:N0}   {3,11:N0}   {4,14:N0}   {5,13:F3}   {6,14:F3}",
                    string.IsNullOrEmpty(belt.fieldName) ? belt.name : belt.fieldName,
                    belt.count, belt.SolveCount, belt.LastSolvedCount, belt.LastSubmittedCount,
                    belt.LastSolveMilliseconds, belt.LastSubmitMilliseconds));
            }

            report.AppendLine();
            report.AppendLine("  " + total.ToString("N0") + " particles. Before this change every one of them was " +
                              "re-solved every frame; the cadence is now derived from");
            report.AppendLine("  angularStalenessDegrees = " + belts[0].angularStalenessDegrees.ToString("F3") +
                              " deg and the live time rate, so at 5 days/s the Kuiper belt (mean motion " +
                              "~0.005 deg/day)");
            report.AppendLine("  refreshes roughly once every 20 frames while the asteroid belt still refreshes every frame.");
            report.AppendLine("  Markers whose direction is outside the view cone, or beyond the far plane, are never submitted.");
            report.AppendLine();
        }

        private static void AppendOrbits(StringBuilder report, SolarSystemSimulation sim, SolarSpaceContext ctx)
        {
            OrbitPath[] paths = sim.orbitsRoot != null
                ? sim.orbitsRoot.GetComponentsInChildren<OrbitPath>(true)
                : new OrbitPath[0];
            CircleLine[] circles = sim.orbitsRoot != null
                ? sim.orbitsRoot.GetComponentsInChildren<CircleLine>(true)
                : new CircleLine[0];

            int vertices = 0;
            foreach (OrbitPath p in paths) vertices += p.samples;

            report.AppendLine("ORBIT LINES");
            report.AppendLine("  " + paths.Length + " OrbitPath, " + circles.Length + " CircleLine, " +
                              vertices.ToString("N0") + " line vertices");
            report.AppendLine(string.Format("  orbits root offset = {0} scene units (rigid follow of the floating origin; " +
                              "no vertex is recomputed on a focus change)",
                              sim.orbitsRoot != null ? sim.orbitsRoot.position.ToString("F3") : "n/a"));
            report.AppendLine();
        }

        // ======================================================================================

        private static SolarSystemSimulation FindSimulation(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                SolarSystemSimulation sim = root.GetComponentInChildren<SolarSystemSimulation>(true);
                if (sim != null) return sim;
            }
            return null;
        }
    }
}
