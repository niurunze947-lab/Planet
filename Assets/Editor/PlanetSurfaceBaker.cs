using System;
using System.Threading.Tasks;
using Planet.Procedural;
using UnityEditor;
using UnityEngine;

namespace Planet.EditorTools
{
    public static class PlanetSurfaceBaker
    {
        public enum MapKind { Albedo, CloudDensity, Normal }

        /// <summary>Worker buffers only. Texture creation and asset upload stay on the editor thread.</summary>
        public static System.Threading.Tasks.Task<Color[][]> BakeFacesAsync(PlanetDefinition definition, int size, MapKind kind)
        {
            PlanetSurfaceSampler sampler = definition.CreateSampler();
            return System.Threading.Tasks.Task.Run(() =>
            {
                Color[][] faces = new Color[6][];
                Parallel.For(0, 6, ParallelOptions(), face =>
                {
                    if (kind == MapKind.Albedo) faces[face] = BakeAlbedoFace(sampler, (PlanetCubeFace)face, size);
                    else if (kind == MapKind.CloudDensity) faces[face] = BakeCloudFace(sampler, (PlanetCubeFace)face, size);
                    else
                    {
                        Color[] pixels = new Color[size * size];
                        for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            Unity.Mathematics.float3 d = CubeSphere.Direction((PlanetCubeFace)face,
                                new Unity.Mathematics.float2((x + 0.5f) * 2f / size - 1f, (y + 0.5f) * 2f / size - 1f));
                            Unity.Mathematics.float3 normal = sampler.Evaluate(d).normal * 0.5f + 0.5f;
                            pixels[y * size + x] = new Color(normal.x, normal.y, normal.z, 1f);
                        }
                        faces[face] = pixels;
                    }
                });
                return faces;
            });
        }

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
