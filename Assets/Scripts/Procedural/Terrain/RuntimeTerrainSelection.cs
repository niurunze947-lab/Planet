using System.Collections.Generic;
using Unity.Mathematics;

namespace Planet.Procedural.Terrain
{
    /// <summary>Budgeted screen-space quadtree. Selection never starts from or enumerates the finest global grid.</summary>
    public static class RuntimeTerrainSelection
    {
        public static HashSet<TerrainNodeKey> Select(RuntimeTerrainSource source, PlanetViewState view,
            int maxLeaves, int maxLevel, double pixelTolerance, bool focused, HashSet<TerrainNodeKey> previousSplits = null,
            TerrainNeighbourhoodPolicy? neighbourhood = null, double prefetchSeconds = 2)
        {
            var macroCache = new Dictionary<float3, float>();
            maxLeaves = math.max(6, maxLeaves);
            maxLevel = math.clamp(maxLevel, 0, TerrainLattice.MaxLevel);
            var leaves = new HashSet<TerrainNodeKey>();
            var candidates = new CandidateHeap();
            double3 cameraDirection = math.normalizesafe(view.positionBfMetres, new double3(1, 0, 0));
            double clearance = math.abs(math.length(view.positionBfMetres) - math.length(source.Position(cameraDirection, 0.01, macroCache)));
            double predicted = TerrainNeighbourhoodPolicy.PredictedClearance(view, clearance, prefetchSeconds);
            double3 ahead = math.normalizesafe(TerrainNeighbourhoodPolicy.LookAhead(view, prefetchSeconds), cameraDirection);
            TerrainNeighbourhoodPolicy policy = neighbourhood ?? TerrainNeighbourhoodPolicy.Default;
            for (int face = 0; face < 6; face++)
            {
                var key = TerrainNodeKey.Root((PlanetCubeFace)face);
                leaves.Add(key);
                Enqueue(candidates, source, key, view, focused, clearance, predicted, ahead, macroCache, policy, previousSplits, pixelTolerance, maxLevel);
            }
            while (leaves.Count + 3 <= maxLeaves)
            {
                if (!candidates.TryPop(out TerrainNodeKey best)) break;
                leaves.Remove(best);
                for (int i = 0; i < 4; i++)
                {
                    var child = best.Child(i); leaves.Add(child);
                    Enqueue(candidates, source, child, view, focused, clearance, predicted, ahead, macroCache, policy, previousSplits, pixelTolerance, maxLevel);
                }
            }
            return leaves;
        }

        private static void Enqueue(CandidateHeap heap, RuntimeTerrainSource source, TerrainNodeKey key,
            PlanetViewState view, bool focused, double clearance, double predicted, double3 ahead,
            Dictionary<float3, float> cache, TerrainNeighbourhoodPolicy policy, HashSet<TerrainNodeKey> previous, double tolerance, int maxLevel)
        {
            if (key.level >= maxLevel) return;
            bool retain = previous != null && previous.Contains(key);
            double score = Score(source, key, view, focused, clearance, predicted, ahead, cache, policy, retain) /
                math.max(0.1, tolerance * (retain ? 0.7 : 1));
            if (score > 1) heap.Add(key, score);
        }

        private static double Score(RuntimeTerrainSource source, TerrainNodeKey key, PlanetViewState view, bool focused,
            double clearance, double predicted, double3 ahead, Dictionary<float3, float> macroCache,
            TerrainNeighbourhoodPolicy policy, bool retain)
        {
            if (!focused) return 0;
            TerrainLattice.NodeUv(key, out double2 min, out double2 max);
            double3 d = TerrainLattice.Direction(key.face, (min + max) * 0.5);
            double refRadius = source.ReferenceRadius(d);
            double edge = TerrainLattice.NodeSize(source.radius, key.level);
            double spacing = edge / TerrainLattice.N;
            double3 cameraDirection = cameraDirectionFor(view);
            bool underfoot = TerrainLattice.NodeAt(cameraDirection, key.level).Equals(key);
            double surfaceDistance = TerrainNeighbourhoodPolicy.SurfaceDistanceToNode(cameraDirection, key, source.radius);
            double futureDistance = TerrainNeighbourhoodPolicy.SurfaceDistanceToNode(ahead, key, source.radius);
            // Reserve the contact path before broad visual/preload demand can spend the entire budget.
            if (view.localCameraActive && underfoot && spacing > math.max(0.01, clearance * 0.02))
                return 1e20 / (key.level + 1);
            int requiredLevel = view.localCameraActive && source.solid
                ? policy.RequiredLevel(source.radius, predicted, surfaceDistance, retain) : 0;
            if (key.level < requiredLevel)
                return 1e18 / (key.level + 1);
            if (view.localCameraActive && source.solid &&
                key.level < policy.RequiredLevel(source.radius, predicted, futureDistance, retain))
                return 1e16 / (key.level + 1);
            double3 center = source.Position(d, spacing, macroCache);
            // A planet-wide relief bound on every centimetre patch defeats frustum culling.
            double localRelief = math.min(source.ReliefBound, edge * 0.5 + source.detail.ResidualBound(spacing) * source.reliefScale);
            double bound = edge * 0.85 + localRelief;
            double3 relative = center - view.positionBfMetres;
            double range = math.length(relative);
            double protectedRange = 128.0 + math.min(10000.0, math.length(view.velocityBfMetresPerSecond) * 2.0);
            bool collisionNeighbourhood = view.localCameraActive &&
                (underfoot || range - bound < protectedRange);
            if (key.level > 1 && !collisionNeighbourhood)
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
            // Reserve the path below the observer before spending the remaining leaves on
            // the horizon. Otherwise coarse far patches exhaust the budget at ground level.
            double pixelsPerMetre = math.max(1, view.pixelHeight) /
                (2 * math.tan(math.radians(math.clamp(view.verticalFovDegrees, 1f, 170f)) * 0.5) * distance);
            double curvature = spacing * spacing / (8 * source.radius);
            double macroSlope = math.min(1, source.ReliefBound / source.radius * 2);
            double detailError = source.solid ? source.detail.ResidualBound(spacing) * source.reliefScale : 0;
            // Error estimate plus a vertex footprint limit. Budgets cap refinement even at an extreme camera pose.
            return math.max(curvature + spacing * macroSlope + detailError, spacing / 12) * pixelsPerMetre;
        }

        // Unity 2022's managed profile has no PriorityQueue. A max heap avoids quadratic leaf scans.
        private sealed class CandidateHeap
        {
            private struct Entry { public TerrainNodeKey key; public double score; public int order; }
            private readonly List<Entry> items = new List<Entry>();
            private int order;
            private static bool Higher(Entry a, Entry b) => a.score > b.score || (a.score == b.score && a.order < b.order);
            public void Add(TerrainNodeKey key, double score)
            {
                var entry = new Entry { key = key, score = score, order = order++ };
                int i = items.Count; items.Add(entry);
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (!Higher(entry, items[parent])) break;
                    items[i] = items[parent]; i = parent;
                }
                items[i] = entry;
            }
            public bool TryPop(out TerrainNodeKey key)
            {
                key = default;
                if (items.Count == 0) return false;
                key = items[0].key;
                var last = items[items.Count - 1]; items.RemoveAt(items.Count - 1);
                if (items.Count == 0) return true;
                int i = 0;
                while (i * 2 + 1 < items.Count)
                {
                    int child = i * 2 + 1;
                    if (child + 1 < items.Count && Higher(items[child + 1], items[child])) child++;
                    if (!Higher(items[child], last)) break;
                    items[i] = items[child]; i = child;
                }
                items[i] = last;
                return true;
            }
        }

        private static double3 cameraDirectionFor(PlanetViewState view)
        {
            return math.normalizesafe(view.positionBfMetres, new double3(0, 1, 0));
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
