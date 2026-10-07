using Unity.Mathematics;

namespace Planet.Procedural
{
    public enum PlanetBiome : byte
    {
        Ocean, Coast, Desert, Grassland, Forest, Rainforest, Tundra, Alpine, Ice,
        Rock, Volcanic, CloudTop, Haze, Photosphere
    }

    /// <summary>All values describe the physical planet, independently of the rendering scale.</summary>
    public struct PlanetSurfaceSample
    {
        public float heightMetres;
        public float slopeDegrees;
        public float temperature;
        public float moisture;
        public float ocean;
        public float waterDepthMetres;
        public float waterContent;
        public PlanetBiome biome;
        public float3 normal;
        public float3 albedoLinear;
        public PlanetTerrainWeights terrainWeights;
    }
}
