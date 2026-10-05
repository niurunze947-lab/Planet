using System;
using Unity.Mathematics;

namespace Planet.Procedural.Terrain
{
    /// <summary>The face edge on the other side of a cube edge.</summary>
    public readonly struct TerrainEdgeLink
    {
        public readonly PlanetCubeFace face;
        public readonly TerrainEdge edge;
        /// <summary>True when the edge parameter runs in opposite directions on the two faces.</summary>
        public readonly bool reversed;

        public TerrainEdgeLink(PlanetCubeFace face, TerrainEdge edge, bool reversed)
        {
            this.face = face;
            this.edge = edge;
            this.reversed = reversed;
        }
    }

    /// <summary>
    /// Cube face adjacency derived from the <see cref="TerrainLattice.CubePoint"/> corner geometry,
    /// so it cannot drift from the face mapping. The edge parameter is v on U edges and u on V edges,
    /// increasing with that face's own uv.
    /// </summary>
    public static class CubeFaceAdjacency
    {
        private static readonly TerrainEdgeLink[] links = Build();

        public static TerrainEdgeLink Across(PlanetCubeFace face, TerrainEdge edge)
        {
            return links[(int)face * 4 + (int)edge];
        }

        public static bool IsUEdge(TerrainEdge edge)
        {
            return edge == TerrainEdge.NegativeU || edge == TerrainEdge.PositiveU;
        }

        /// <summary>Same-level neighbour across an edge, crossing to the adjacent face when needed.</summary>
        public static TerrainNodeKey Neighbour(TerrainNodeKey key, TerrainEdge edge)
        {
            int last = (1 << key.level) - 1;
            switch (edge)
            {
                case TerrainEdge.NegativeU:
                    if (key.x > 0) return new TerrainNodeKey(key.face, key.level, key.x - 1, key.y);
                    break;
                case TerrainEdge.PositiveU:
                    if (key.x < last) return new TerrainNodeKey(key.face, key.level, key.x + 1, key.y);
                    break;
                case TerrainEdge.NegativeV:
                    if (key.y > 0) return new TerrainNodeKey(key.face, key.level, key.x, key.y - 1);
                    break;
                default:
                    if (key.y < last) return new TerrainNodeKey(key.face, key.level, key.x, key.y + 1);
                    break;
            }

            TerrainEdgeLink link = Across(key.face, edge);
            int along = IsUEdge(edge) ? key.y : key.x;
            if (link.reversed) along = last - along;
            switch (link.edge)
            {
                case TerrainEdge.NegativeU: return new TerrainNodeKey(link.face, key.level, 0, along);
                case TerrainEdge.PositiveU: return new TerrainNodeKey(link.face, key.level, last, along);
                case TerrainEdge.NegativeV: return new TerrainNodeKey(link.face, key.level, along, 0);
                default: return new TerrainNodeKey(link.face, key.level, along, last);
            }
        }

        /// <summary>Maps a lattice point on a face edge to the identical point in the neighbour face lattice.</summary>
        public static void MapEdgeLattice(PlanetCubeFace face, TerrainEdge edge, long along,
            out PlanetCubeFace neighbourFace, out long x, out long y)
        {
            TerrainEdgeLink link = Across(face, edge);
            long a = link.reversed ? TerrainLattice.LatticeSize - along : along;
            neighbourFace = link.face;
            switch (link.edge)
            {
                case TerrainEdge.NegativeU: x = 0; y = a; break;
                case TerrainEdge.PositiveU: x = TerrainLattice.LatticeSize; y = a; break;
                case TerrainEdge.NegativeV: x = a; y = 0; break;
                default: x = a; y = TerrainLattice.LatticeSize; break;
            }
        }

        private static TerrainEdgeLink[] Build()
        {
            var result = new TerrainEdgeLink[24];
            for (int f = 0; f < 6; f++)
            for (int e = 0; e < 4; e++)
            {
                EdgeEnds((TerrainEdge)e, out double2 a, out double2 b);
                double3 pa = TerrainLattice.CubePoint((PlanetCubeFace)f, a);
                double3 pb = TerrainLattice.CubePoint((PlanetCubeFace)f, b);
                bool found = false;
                for (int g = 0; g < 6 && !found; g++)
                {
                    if (g == f) continue;
                    var gf = (PlanetCubeFace)g;
                    if (!OnFace(gf, pa, out double2 ua) || !OnFace(gf, pb, out double2 ub)) continue;
                    TerrainEdge ge;
                    bool reversed;
                    if (ua.x == ub.x)
                    {
                        ge = ua.x < 0.0 ? TerrainEdge.NegativeU : TerrainEdge.PositiveU;
                        reversed = ua.y > ub.y;
                    }
                    else
                    {
                        ge = ua.y < 0.0 ? TerrainEdge.NegativeV : TerrainEdge.PositiveV;
                        reversed = ua.x > ub.x;
                    }
                    result[f * 4 + e] = new TerrainEdgeLink(gf, ge, reversed);
                    found = true;
                }
                if (!found) throw new InvalidOperationException("Cube face adjacency is inconsistent.");
            }
            return result;
        }

        /// <summary>Edge end points in uv, ordered by increasing edge parameter.</summary>
        private static void EdgeEnds(TerrainEdge edge, out double2 a, out double2 b)
        {
            switch (edge)
            {
                case TerrainEdge.NegativeU: a = new double2(-1, -1); b = new double2(-1, 1); break;
                case TerrainEdge.PositiveU: a = new double2(1, -1); b = new double2(1, 1); break;
                case TerrainEdge.NegativeV: a = new double2(-1, -1); b = new double2(1, -1); break;
                default: a = new double2(-1, 1); b = new double2(1, 1); break;
            }
        }

        private static bool OnFace(PlanetCubeFace face, double3 p, out double2 uv)
        {
            uv = TerrainLattice.FaceUv(face, p);
            return math.all(math.abs(uv) <= 1.0) && math.all(TerrainLattice.CubePoint(face, uv) == p);
        }
    }
}
