using System;
using UnityEngine;

namespace Planet.Procedural
{
    [Serializable]
    public sealed class PlanetTextureTier
    {
        public string label;
        public int albedoSize;
        public int cloudSize;
        public int normalSize;
        [Tooltip("Resources paths, deliberately avoiding serialized high-resolution texture references.")]
        public string albedoResource, cloudResource, normalResource;
    }

    [CreateAssetMenu(menuName = "Planet/Texture LOD Set")]
    public sealed class PlanetTextureLodSet : ScriptableObject
    {
        public PlanetDefinition definition;
        public PlanetTextureTier[] tiers = new PlanetTextureTier[0];
        // Thresholds use the public centre-distance contract, not frustum dimensions.
        public float orbitEnter = 4f, orbitExit = 4.8f;
        public float nearEnter = 1f, nearExit = 1.2f;
        public float surfaceEnter = 0.25f, surfaceExit = 0.35f;

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
            return Mathf.Min(tier, Mathf.Max(tiers.Length - 1, 0));
        }
    }
}
