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

        public int RequiredLevel(double radius, double clearance, double nearestSurfaceDistance)
        {
            double spacing = double.PositiveInfinity;
            if (clearance <= overviewAltitudeMetres && nearestSurfaceDistance <= overviewRadiusMetres)
                spacing = math.min(spacing, math.max(0.01, overviewSpacingMetres));
            if (clearance <= detailAltitudeMetres && nearestSurfaceDistance <= detailRadiusMetres)
                spacing = math.min(spacing, math.max(0.01, detailSpacingMetres));
            if (clearance <= detailAltitudeMetres && nearestSurfaceDistance <= contactRadiusMetres)
                spacing = math.min(spacing, math.max(0.01, contactSpacingMetres));
            if (!math.isfinite(spacing)) return 0;
            return math.clamp((int)math.ceil(math.log2(2 * radius / (TerrainLattice.N * spacing))),
                0, TerrainLattice.MaxLevel);
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
