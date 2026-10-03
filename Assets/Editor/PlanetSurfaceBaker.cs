using Planet.Procedural;
using UnityEditor;
using UnityEngine;

namespace Planet.EditorTools
{
    public static class PlanetSurfaceBaker
    {
        public static Cubemap BakeAlbedo(PlanetDefinition definition, int size)
        {
            size = Mathf.Clamp(size, 16, 512);
            PlanetSurfaceSampler sampler = definition.CreateSampler();
            Cubemap cube = new Cubemap(size, TextureFormat.RGBA32, true, true)
            {
                name = definition.displayName + "_ProceduralAlbedo",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 1
            };

            for (int face = 0; face < 6; face++)
            {
                Color[] pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    float v = ((y + 0.5f) / size) * 2f - 1f;
                    for (int x = 0; x < size; x++)
                    {
                        float u = ((x + 0.5f) / size) * 2f - 1f;
                        PlanetSurfaceSample sample = sampler.Evaluate(CubeSphere.Direction((PlanetCubeFace)face, new Vector2(u, v)));
                        float3Color(sample.albedoLinear, out Color color);
                        pixels[y * size + x] = color;
                    }
                }
                cube.SetPixels(pixels, (CubemapFace)face);
            }
            cube.Apply(true, false);
            return cube;
        }

        private static void float3Color(Unity.Mathematics.float3 value, out Color color)
        {
            color = new Color(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y), Mathf.Clamp01(value.z), 1f);
        }
    }
}
