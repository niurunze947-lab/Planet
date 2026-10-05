using System;
using UnityEngine;

namespace Planet.Procedural
{
    [Serializable]
    public sealed class PlanetMeshTier
    {
        public string label;
        public int resolution;
        public Mesh mesh;
    }

    /// <summary>Offline-derived Cube Sphere meshes for one planet definition.</summary>
    [CreateAssetMenu(menuName = "Planet/Planet Mesh LOD Set", fileName = "PlanetMeshLods")]
    public sealed class PlanetMeshLodSet : ScriptableObject
    {
        public PlanetDefinition definition;
        public PlanetMeshTier[] tiers = new PlanetMeshTier[0];
        public float orbitEnter = 4f;
        public float orbitExit = 4.8f;
        public float nearEnter = 1f;
        public float nearExit = 1.2f;
        public float surfaceEnter = 0.25f;
        public float surfaceExit = 0.35f;

        public int SelectTier(float clearanceRatio, bool focused, int previous)
        {
            if (!focused || float.IsNaN(clearanceRatio) || float.IsInfinity(clearanceRatio)) return 0;
            clearanceRatio = Mathf.Max(0f, clearanceRatio);
            int tier = Mathf.Clamp(previous, 0, 3);
            if (tier == 0 && clearanceRatio <= orbitEnter) tier = 1;
            if (tier <= 1 && clearanceRatio <= nearEnter) tier = 2;
            if (tier <= 2 && clearanceRatio <= surfaceEnter) tier = 3;
            if (tier == 3 && clearanceRatio >= surfaceExit) tier = 2;
            if (tier >= 2 && clearanceRatio >= nearExit) tier = 1;
            if (tier >= 1 && clearanceRatio >= orbitExit) tier = 0;
            return Mathf.Clamp(tier, 0, Mathf.Max(tiers.Length - 1, 0));
        }

        public Mesh MeshAt(int tier)
        {
            if (tiers == null || tiers.Length == 0) return null;
            tier = Mathf.Clamp(tier, 0, tiers.Length - 1);
            return tiers[tier] != null ? tiers[tier].mesh : null;
        }
    }
}
