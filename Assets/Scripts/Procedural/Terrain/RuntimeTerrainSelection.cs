using System.Collections.Generic;
using Unity.Mathematics;

namespace Planet.Procedural.Terrain
{
    /// <summary>Budgeted screen-space quadtree. Selection never starts from or enumerates the finest global grid.</summary>
    public static class RuntimeTerrainSelection
    {
        public static HashSet<TerrainNodeKey> Select(RuntimeTerrainSource source, PlanetViewState view,
            int maxLeaves, int maxLevel, double pixelTolerance, bool focused, HashSet<TerrainNodeKey> previousSplits = null)
        {
            var macroCache = new Dictionary<float3, float>();
            maxLeaves = math.max(6, maxLeaves);
            maxLevel = math.clamp(maxLevel, 0, TerrainLattice.MaxLevel);
            var leaves = new HashSet<TerrainNodeKey>();
            var scores = new Dictionary<TerrainNodeKey, double>();
            double3 cameraDirection = math.normalizesafe(view.positionBfMetres, new double3(1, 0, 0));
            double clearance = math.abs(math.length(view.positionBfMetres) - math.length(source.Position(cameraDirection, 0.01, macroCache)));
            for (int face = 0; face < 6; face++)
            {
                var key = TerrainNodeKey.Root((PlanetCubeFace)face);
                leaves.Add(key); scores[key] = Score(source, key, view, focused, clearance, macroCache);
            }
            while (leaves.Count + 3 <= maxLeaves)
            {
                double bestScore = 0; TerrainNodeKey best = default;
                foreach (var key in leaves)
                {
                    if (key.level >= maxLevel) continue;
                    double threshold = pixelTolerance * (previousSplits != null && previousSplits.Contains(key) ? 0.7 : 1.0);
                    double score = scores[key] / math.max(0.1, threshold);
                    if (score > math.max(1, bestScore)) { bestScore = score; best = key; }
                }
                if (bestScore <= 1) break;
                leaves.Remove(best); scores.Remove(best);
                for (int i = 0; i < 4; i++)
                {
                    var child = best.Child(i); leaves.Add(child); scores[child] = Score(source, child, view, focused, clearance, macroCache);
                }
            }
            return leaves;
        }

        private static double Score(RuntimeTerrainSource source, TerrainNodeKey key, PlanetViewState view, bool focused, double clearance, Dictionary<float3, float> macroCache)
        {
            if (!focused) return 0;
            TerrainLattice.NodeUv(key, out double2 min, out double2 max);
            double3 d = TerrainLattice.Direction(key.face, (min + max) * 0.5);
            double refRadius = source.ReferenceRadius(d);
            double edge = TerrainLattice.NodeSize(source.radius, key.level);
            double spacing = edge / TerrainLattice.N;
            double3 center = source.Position(d, spacing, macroCache);
            // A planet-wide relief bound on every centimetre patch defeats frustum culling.
            double localRelief = math.min(source.ReliefBound, edge * 0.5 + source.detail.ResidualBound(spacing) * source.reliefScale);
            double bound = edge * 0.85 + localRelief;
            double3 relative = center - view.positionBfMetres;
            double range = math.length(relative);
            if (key.level > 1)
            {
                // Conservative tangent-plane horizon and view cone; coarse parents remain complete coverage.
                double observerRadius = math.length(view.positionBfMetres);
                double horizonMargin = source.ReliefBound * 3 + edge;
                if (observerRadius > refRadius && math.dot(view.positionBfMetres, d) + horizonMargin < refRadius) return 0;
                float3 forward = math.mul(view.rotationBf, new float3(0, 0, 1));
                double diagonalFov = math.atan(math.tan(math.radians(view.verticalFovDegrees) * 0.5) * math.sqrt(1 + view.aspect * view.aspect));
                if (math.dot(relative, (double3)forward) + bound < range * math.cos(diagonalFov + 0.2)) return 0;
            }
            double distance = math.max(range - bound, math.max(0.01, clearance * 0.25));
            double pixelsPerMetre = math.max(1, view.pixelHeight) /
                (2 * math.tan(math.radians(math.clamp(view.verticalFovDegrees, 1f, 170f)) * 0.5) * distance);
            double curvature = spacing * spacing / (8 * source.radius);
            double macroSlope = math.min(1, source.ReliefBound / source.radius * 2);
            double detailError = source.solid ? source.detail.ResidualBound(spacing) * source.reliefScale : 0;
            // Error estimate plus a vertex footprint limit. Budgets cap refinement even at an extreme camera pose.
            return math.max(curvature + spacing * macroSlope + detailError, spacing / 12) * pixelsPerMetre;
        }

        public static HashSet<TerrainNodeKey> WithAncestors(IEnumerable<TerrainNodeKey> leaves)
        {
            var result = new HashSet<TerrainNodeKey>();
            foreach (var leaf in leaves)
            {
                var key = leaf; result.Add(key);
                while (key.level > 0) { key = key.Parent; result.Add(key); }
            }
            return result;
        }
    }
}
