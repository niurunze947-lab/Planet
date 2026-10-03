using System;
using Unity.Mathematics;
using UnityEngine;

namespace Planet.Procedural
{
    // Values deliberately match UnityEngine.CubemapFace.
    public enum PlanetCubeFace { PositiveX, NegativeX, PositiveY, NegativeY, PositiveZ, NegativeZ }

    public struct PlanetFaceCoordinate
    {
        public PlanetCubeFace face;
        public float2 uv; // [-1, 1], before projection
    }

    /// <summary>Six equally subdividable faces; direction is the seam-independent surface identifier.</summary>
    public static class CubeSphere
    {
        public static float3 CubePoint(PlanetCubeFace face, float2 uv)
        {
            switch (face)
            {
                case PlanetCubeFace.PositiveX: return new float3(1f, -uv.y, -uv.x);
                case PlanetCubeFace.NegativeX: return new float3(-1f, -uv.y, uv.x);
                case PlanetCubeFace.PositiveY: return new float3(uv.x, 1f, uv.y);
                case PlanetCubeFace.NegativeY: return new float3(uv.x, -1f, -uv.y);
                case PlanetCubeFace.PositiveZ: return new float3(uv.x, -uv.y, 1f);
                case PlanetCubeFace.NegativeZ: return new float3(-uv.x, -uv.y, -1f);
                default: throw new ArgumentOutOfRangeException(nameof(face));
            }
        }

        public static float3 Direction(PlanetCubeFace face, float2 uv)
        {
            return math.normalize(CubePoint(face, uv));
        }

        public static PlanetFaceCoordinate DirectionToFace(float3 direction)
        {
            if (!(math.lengthsq(direction) > 1e-20f) || !math.all(math.isfinite(direction)))
                throw new ArgumentException("Direction must be finite and nonzero.", nameof(direction));
            float3 a = math.abs(direction);
            PlanetFaceCoordinate result;
            if (a.x >= a.y && a.x >= a.z)
            {
                result.face = direction.x >= 0f ? PlanetCubeFace.PositiveX : PlanetCubeFace.NegativeX;
                result.uv = new float2(direction.x >= 0f ? -direction.z : direction.z, -direction.y) / a.x;
            }
            else if (a.y >= a.z)
            {
                result.face = direction.y >= 0f ? PlanetCubeFace.PositiveY : PlanetCubeFace.NegativeY;
                result.uv = new float2(direction.x, direction.y >= 0f ? direction.z : -direction.z) / a.y;
            }
            else
            {
                result.face = direction.z >= 0f ? PlanetCubeFace.PositiveZ : PlanetCubeFace.NegativeZ;
                result.uv = new float2(direction.z >= 0f ? direction.x : -direction.x, -direction.y) / a.z;
            }
            return result;
        }

        /// <summary>Managed mesh buffers, also suitable for future patch generation before Unity upload.</summary>
        public static PlanetMeshData Generate(PlanetSurfaceSampler sampler, int resolution)
        {
            resolution = math.clamp(resolution, 2, 512);
            int side = resolution + 1;
            int perFace = side * side;
            var data = new PlanetMeshData(perFace * 6, resolution * resolution * 36);
            int t = 0;
            for (int face = 0; face < 6; face++)
            {
                for (int y = 0; y <= resolution; y++)
                for (int x = 0; x <= resolution; x++)
                {
                    int index = face * perFace + y * side + x;
                    float2 uv = new float2(x, y) * (2f / resolution) - 1f;
                    float3 d = Direction((PlanetCubeFace)face, uv);
                    PlanetSurfaceSample sample = sampler.Evaluate(d);
                    data.positions[index] = sampler.SurfacePosition(d, sample.heightMetres);
                    // Water normals ignore the seabed gradient. Same rule is used by the surface shader.
                    float polar = 1f - sampler.flattening;
                    float3 radialNormal = math.normalize(new float3(d.x, d.y / (polar * polar), d.z));
                    data.normals[index] = math.normalize(math.lerp(sample.normal, radialNormal, sample.ocean));
                    data.directions[index] = d;
                    data.faceUV[index] = (uv + 1f) * 0.5f;
                    data.samples[index] = new Vector4(sample.heightMetres, sample.slopeDegrees, sample.temperature, sample.moisture);
                }
                for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    int a = face * perFace + y * side + x;
                    int b = a + 1, c = a + side, d = c + 1;
                    data.triangles[t++] = a; data.triangles[t++] = c; data.triangles[t++] = b;
                    data.triangles[t++] = b; data.triangles[t++] = c; data.triangles[t++] = d;
                }
            }
            return data;
        }
    }
}
