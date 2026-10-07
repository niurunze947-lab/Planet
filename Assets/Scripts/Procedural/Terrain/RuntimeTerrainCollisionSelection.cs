using System.Collections.Generic;
using Unity.Mathematics;

namespace Planet.Procedural.Terrain
{
    public static class RuntimeTerrainCollisionSelection
    {
        /// <summary>Finds the finest resident chunk up to the target collision level.</summary>
        public static bool TryFindResidentAncestor(ICollection<TerrainNodeKey> residentKeys,
            double3 direction, int targetLevel, out TerrainNodeKey key)
        {
            key = default;
            if (residentKeys == null || residentKeys.Count == 0) return false;

            int level = math.clamp(targetLevel, 0, TerrainLattice.MaxLevel);
            TerrainNodeKey target = TerrainLattice.NodeAt(
                math.normalizesafe(direction, new double3(1, 0, 0)), level);
            for (int candidateLevel = level; candidateLevel >= 0; candidateLevel--)
            {
                TerrainNodeKey candidate = target.Ancestor(candidateLevel);
                if (!residentKeys.Contains(candidate)) continue;
                key = candidate;
                return true;
            }
            return false;
        }
    }
}
