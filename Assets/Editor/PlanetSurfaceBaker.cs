using System;
using System.Threading.Tasks;
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

            Color[][] facePixels = new Color[6][];
            Parallel.For(0, 6, ParallelOptions(), face =>
            {
                facePixels[face] = BakeAlbedoFace(sampler, (PlanetCubeFace)face, size);
            });
            for (int face = 0; face < 6; face++) cube.SetPixels(facePixels[face], (CubemapFace)face);
            cube.Apply(true, false);
            return cube;
        }

        public static Cubemap BakeCloudDensity(PlanetDefinition definition, int size)
        {
            size = Mathf.Clamp(size, 16, 512);
            PlanetSurfaceSampler sampler = definition.CreateSampler();
            // RGBA32 keeps SetPixels compatible across the Unity 2022.3 texture backends. The
            // cloud map is only 128² per face, so the extra channels are negligible here.
            Cubemap cube = new Cubemap(size, TextureFormat.RGBA32, true, true)
            {
                name = definition.displayName + "_ProceduralCloudDensity",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 1
            };

            Color[][] facePixels = new Color[6][];
            Parallel.For(0, 6, ParallelOptions(), face =>
            {
                facePixels[face] = BakeCloudFace(sampler, (PlanetCubeFace)face, size);
            });
            for (int face = 0; face < 6; face++) cube.SetPixels(facePixels[face], (CubemapFace)face);
            cube.Apply(true, false);
            return cube;
        }

        private static Color[] BakeAlbedoFace(PlanetSurfaceSampler sampler, PlanetCubeFace face, int size)
        {
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = ((y + 0.5f) / size) * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float u = ((x + 0.5f) / size) * 2f - 1f;
                    Unity.Mathematics.float3 albedo = sampler.EvaluateAlbedoOnly(
                        CubeSphere.Direction(face, new Unity.Mathematics.float2(u, v)));
                    float3Color(albedo, out Color color);
                    pixels[y * size + x] = color;
                }
            }
            return pixels;
        }

        private static Color[] BakeCloudFace(PlanetSurfaceSampler sampler, PlanetCubeFace face, int size)
        {
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = ((y + 0.5f) / size) * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float u = ((x + 0.5f) / size) * 2f - 1f;
                    float density = sampler.EvaluateCloudDensity(
                        CubeSphere.Direction(face, new Unity.Mathematics.float2(u, v)));
                    pixels[y * size + x] = new Color(density, density, density, 1f);
                }
            }
            return pixels;
        }

        private static ParallelOptions ParallelOptions()
        {
            return new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) };
        }

        private static void float3Color(Unity.Mathematics.float3 value, out Color color)
        {
            color = new Color(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y), Mathf.Clamp01(value.z), 1f);
        }
    }
}
