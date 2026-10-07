using Unity.Mathematics;

namespace Planet.Procedural.Terrain
{
    /// <summary>
    /// Exact integer addressing for the terrain quadtree. Every vertex of every level lies on one
    /// 2^30 lattice per cube face, so shared edges and parent/child vertices are the same integer
    /// point and therefore the same double uv. uv = (2i - 2^30) / 2^30 is exact in double.
    /// </summary>
    public static class TerrainLattice
    {
        /// <summary>Quads per chunk side.</summary>
        public const int N = 32;
        public const int NShift = 5;
        /// <summary>Deepest level. 2R / (N * 2^25) is about 1.19 cm on an Earth-sized body.</summary>
        public const int MaxLevel = 25;
        public const int LatticeBits = 30;
        public const long LatticeSize = 1L << LatticeBits;

        /// <summary>Lattice units between adjacent vertices of a level-L chunk.</summary>
        public static long VertexStep(int level) { return 1L << (MaxLevel - level); }

        /// <summary>Lattice coordinate of chunk vertex v (may be outside 0..N for halo rows).</summary>
        public static long VertexLattice(int nodeCoord, int level, int v)
        {
            return ((long)nodeCoord * N + v) * VertexStep(level);
        }

        public static double ToUv(long lattice)
        {
            return (2.0 * lattice - LatticeSize) / LatticeSize;
        }

        public static double ToLattice(double uv)
        {
            return (uv + 1.0) * 0.5 * LatticeSize;
        }

        /// <summary>Double mirror of <see cref="CubeSphere.CubePoint"/>; sign flips only, so it is exact.</summary>
        public static double3 CubePoint(PlanetCubeFace face, double2 uv)
        {
            switch (face)
            {
                case PlanetCubeFace.PositiveX: return new double3(1.0, -uv.y, -uv.x);
                case PlanetCubeFace.NegativeX: return new double3(-1.0, -uv.y, uv.x);
                case PlanetCubeFace.PositiveY: return new double3(uv.x, 1.0, uv.y);
                case PlanetCubeFace.NegativeY: return new double3(uv.x, -1.0, -uv.y);
                case PlanetCubeFace.PositiveZ: return new double3(uv.x, -uv.y, 1.0);
                default: return new double3(-uv.x, -uv.y, -1.0);
            }
        }

        public static double3 Direction(PlanetCubeFace face, double2 uv)
        {
            return math.normalize(CubePoint(face, uv));
        }

        public static double3 Direction(PlanetCubeFace face, long x, long y)
        {
            return math.normalize(CubePoint(face, new double2(ToUv(x), ToUv(y))));
        }

        /// <summary>Face plane coordinates of any direction, relative to a given face (may exceed [-1, 1]).</summary>
        public static double2 FaceUv(PlanetCubeFace face, double3 p)
        {
            switch (face)
            {
                case PlanetCubeFace.PositiveX: return new double2(-p.z, -p.y) / p.x;
                case PlanetCubeFace.NegativeX: return new double2(p.z, -p.y) / -p.x;
                case PlanetCubeFace.PositiveY: return new double2(p.x, p.z) / p.y;
                case PlanetCubeFace.NegativeY: return new double2(p.x, -p.z) / -p.y;
                case PlanetCubeFace.PositiveZ: return new double2(p.x, -p.y) / p.z;
                default: return new double2(-p.x, -p.y) / -p.z;
            }
        }

        /// <summary>Same tie rule as <see cref="CubeSphere.DirectionToFace"/>: x wins, then y.</summary>
        public static PlanetCubeFace DominantFace(double3 d)
        {
            double3 a = math.abs(d);
            if (a.x >= a.y && a.x >= a.z) return d.x >= 0.0 ? PlanetCubeFace.PositiveX : PlanetCubeFace.NegativeX;
            if (a.y >= a.z) return d.y >= 0.0 ? PlanetCubeFace.PositiveY : PlanetCubeFace.NegativeY;
            return d.z >= 0.0 ? PlanetCubeFace.PositiveZ : PlanetCubeFace.NegativeZ;
        }

        /// <summary>The node at a level whose face area contains the direction.</summary>
        public static TerrainNodeKey NodeAt(double3 direction, int level)
        {
            PlanetCubeFace face = DominantFace(direction);
            double2 uv = math.clamp(FaceUv(face, direction), -1.0, 1.0);
            int n = 1 << level;
            int x = math.clamp((int)math.floor((uv.x + 1.0) * 0.5 * n), 0, n - 1);
            int y = math.clamp((int)math.floor((uv.y + 1.0) * 0.5 * n), 0, n - 1);
            return new TerrainNodeKey(face, level, x, y);
        }

        /// <summary>Nominal node edge length in metres (exact at the face centre, smaller towards corners).</summary>
        public static double NodeSize(double radiusMetres, int level)
        {
            return 2.0 * radiusMetres / (1L << level);
        }

        /// <summary>Nominal vertex spacing of a level in metres. Depends on the level only.</summary>
        public static double Spacing(double radiusMetres, int level)
        {
            return NodeSize(radiusMetres, level) / N;
        }

        /// <summary>Smallest quadtree level whose nominal vertex spacing reaches a target.</summary>
        public static int LevelForSpacing(double radiusMetres, double spacingMetres)
        {
            if (!(radiusMetres > 0) || !(spacingMetres > 0) || !math.isfinite(radiusMetres) ||
                !math.isfinite(spacingMetres)) return 0;
            double ratio = 2.0 * radiusMetres / (N * spacingMetres);
            if (ratio <= 1.0) return 0;
            return math.clamp((int)math.ceil(math.log2(ratio)), 0, MaxLevel);
        }

        /// <summary>Uv rectangle of a node (min corner, max corner).</summary>
        public static void NodeUv(TerrainNodeKey key, out double2 min, out double2 max)
        {
            long step = (long)N * VertexStep(key.level);
            min = new double2(ToUv(key.x * step), ToUv(key.y * step));
            max = new double2(ToUv((key.x + 1) * step), ToUv((key.y + 1) * step));
        }
    }
}
