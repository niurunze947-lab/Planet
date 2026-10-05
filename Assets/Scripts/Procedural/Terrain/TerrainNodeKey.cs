using System;
using Unity.Mathematics;

namespace Planet.Procedural.Terrain
{
    /// <summary>Face-local node edge. U edges run along v; V edges run along u.</summary>
    public enum TerrainEdge { NegativeU = 0, PositiveU = 1, NegativeV = 2, PositiveV = 3 }

    /// <summary>Quadtree address: face, level and node coordinates in [0, 2^level).</summary>
    public readonly struct TerrainNodeKey : IEquatable<TerrainNodeKey>
    {
        public readonly PlanetCubeFace face;
        public readonly int level;
        public readonly int x;
        public readonly int y;

        public TerrainNodeKey(PlanetCubeFace face, int level, int x, int y)
        {
            this.face = face;
            this.level = level;
            this.x = x;
            this.y = y;
        }

        public static TerrainNodeKey Root(PlanetCubeFace face) => new TerrainNodeKey(face, 0, 0, 0);

        public int NodesPerAxis => 1 << level;

        public bool IsValid => level >= 0 && level <= TerrainLattice.MaxLevel &&
            x >= 0 && y >= 0 && x < (1 << level) && y < (1 << level) &&
            face >= PlanetCubeFace.PositiveX && face <= PlanetCubeFace.NegativeZ;

        public TerrainNodeKey Parent => new TerrainNodeKey(face, level - 1, x >> 1, y >> 1);

        /// <summary>Child index bit 0 = +u half, bit 1 = +v half.</summary>
        public TerrainNodeKey Child(int index) =>
            new TerrainNodeKey(face, level + 1, (x << 1) | (index & 1), (y << 1) | ((index >> 1) & 1));

        public int ChildIndexInParent => (x & 1) | ((y & 1) << 1);

        public TerrainNodeKey Ancestor(int ancestorLevel)
        {
            int shift = level - ancestorLevel;
            return new TerrainNodeKey(face, ancestorLevel, x >> shift, y >> shift);
        }

        public bool IsAncestorOf(TerrainNodeKey other) =>
            other.face == face && other.level > level && other.Ancestor(level) == this;

        public bool Equals(TerrainNodeKey other) =>
            face == other.face && level == other.level && x == other.x && y == other.y;

        public override bool Equals(object obj) => obj is TerrainNodeKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)face * 73856093 ^ level * 19349663;
                h = h * 486187739 ^ x;
                return h * 486187739 ^ y;
            }
        }

        public static bool operator ==(TerrainNodeKey a, TerrainNodeKey b) => a.Equals(b);
        public static bool operator !=(TerrainNodeKey a, TerrainNodeKey b) => !a.Equals(b);

        public override string ToString() => face + "/L" + level + "/" + x + "," + y;
    }
}
