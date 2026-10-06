using System;
using Unity.Mathematics;

namespace Planet.Procedural.Terrain
{
    /// <summary>View-independent physical coverage bands; the quadtree still enforces a hard budget.</summary>
    [Serializable]
    public struct TerrainNeighbourhoodPolicy
    {
        public double overviewAltitudeMetres, overviewRadiusMetres, overviewSpacingMetres;
        public double detailAltitudeMetres, detailRadiusMetres, detailSpacingMetres;
        public double contactRadiusMetres, contactSpacingMetres;

        public static TerrainNeighbourhoodPolicy Default
        {
            get
            {
                return new TerrainNeighbourhoodPolicy
                {
                    overviewAltitudeMetres = 10000, overviewRadiusMetres = 5000, overviewSpacingMetres = 64,
                    detailAltitudeMetres = 1000, detailRadiusMetres = 1000, detailSpacingMetres = 8,
                    contactRadiusMetres = 100, contactSpacingMetres = 1
                };
            }
        }

        public int RequiredLevel(double radius, double clearance, double nearestSurfaceDistance, bool retain = false)
        {
            double spacing = double.PositiveInfinity;
            if (nearestSurfaceDistance <= overviewRadiusMetres)
                spacing = math.min(spacing, BandSpacing(clearance, overviewAltitudeMetres, overviewSpacingMetres, retain));
            if (nearestSurfaceDistance <= detailRadiusMetres)
                spacing = math.min(spacing, BandSpacing(clearance, detailAltitudeMetres, detailSpacingMetres, retain));
            if (nearestSurfaceDistance <= contactRadiusMetres)
                spacing = math.min(spacing, BandSpacing(clearance, detailAltitudeMetres, contactSpacingMetres, retain));
            if (!math.isfinite(spacing)) return 0;
            return math.clamp((int)math.ceil(math.log2(2 * radius / (TerrainLattice.N * spacing))),
                0, TerrainLattice.MaxLevel);
        }

        private static double BandSpacing(double height, double altitude, double spacing, bool retain)
        {
            if (!(altitude > 0)) return double.PositiveInfinity;
            // Three levels enter progressively between 2x and 1x altitude, with release hysteresis.
            double ratio = height / (altitude * (retain ? 1.15 : 1));
            if (ratio >= 2) return double.PositiveInfinity;
            return math.max(0.01, spacing) * math.exp2(3 * math.saturate(ratio - 1));
        }

        public static double PredictedClearance(PlanetViewState view, double clearance, double seconds)
        {
            double3 up = math.normalizesafe(view.positionBfMetres, new double3(0, 1, 0));
            return math.max(0, clearance + math.min(0, math.dot(view.velocityBfMetresPerSecond, up)) * seconds);
        }

        public static double3 LookAhead(PlanetViewState view, double seconds)
        {
            // Limit speculative demand; current contact coverage retains higher priority.
            double3 offset = view.velocityBfMetresPerSecond * seconds;
            double length = math.length(offset);
            return view.positionBfMetres + offset * math.min(1, 5000 / math.max(1, length));
        }

        /// <summary>UV rectangle distance on the cube, avoiding relief/radius bounds that cover the whole planet.</summary>
        public static double SurfaceDistanceToNode(double3 direction, TerrainNodeKey key, double radius)
        {
            if (TerrainLattice.NodeAt(direction, key.level).Equals(key)) return 0;
            TerrainLattice.NodeUv(key, out double2 min, out double2 max);
            double2 uv = TerrainLattice.FaceUv(key.face, direction);
            if (!math.all(math.isfinite(uv))) return double.PositiveInfinity;
            double3 nearest = TerrainLattice.Direction(key.face, math.clamp(uv, min, max));
            return math.distance(direction, nearest) * radius;
        }
    }
}
