using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace Planet.Procedural.Terrain
{
    /// <summary>Managed buffers produced on a worker. Unity mesh creation happens on the main thread.</summary>
    public sealed class RuntimeTerrainChunk
    {
        public TerrainNodeKey key;
        public double3 origin;
        public Vector3[] vertices, normals, directions;
        public Vector4[] samples;
        public Color[] colors;
        public int[] triangles;
        public double spacing;

        public Mesh Upload()
        {
            var mesh = new Mesh { name = "Terrain " + key, hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices; mesh.normals = normals; mesh.colors = colors;
            mesh.SetUVs(1, directions); mesh.SetUVs(2, samples); mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        public static RuntimeTerrainChunk Build(RuntimeTerrainSource source, TerrainNodeKey key, CancellationToken cancel)
        {
            if (!key.IsValid) throw new ArgumentOutOfRangeException(nameof(key));
            var macroCache = new Dictionary<float3, float>();
            const int n = TerrainLattice.N, side = n + 1, gridCount = side * side;
            int skirtCount = 4 * side;
            var result = new RuntimeTerrainChunk
            {
                key = key, spacing = TerrainLattice.Spacing(source.radius, key.level),
                vertices = new Vector3[gridCount + skirtCount], normals = new Vector3[gridCount + skirtCount],
                directions = new Vector3[gridCount + skirtCount], samples = new Vector4[gridCount + skirtCount],
                colors = new Color[gridCount + skirtCount], triangles = new int[n * n * 6 + n * 4 * 6]
            };
            long startX = TerrainLattice.VertexLattice(key.x, key.level, 0);
            long startY = TerrainLattice.VertexLattice(key.y, key.level, 0);
            long step = TerrainLattice.VertexStep(key.level);
            double3 centerDirection = TerrainLattice.Direction(key.face, startX + n / 2 * step, startY + n / 2 * step);
            result.origin = source.Position(centerDirection, result.spacing, macroCache);
            // One halo supplies central differences. This samples height once per grid point,
            // rather than five full terrain evaluations per vertex for independently derived normals.
            const int haloSide = n + 3;
            var positions = new double3[haloSide * haloSide];
            var heights = new double[positions.Length];
            for (int y = -1; y <= n + 1; y++)
            {
                cancel.ThrowIfCancellationRequested();
                for (int x = -1; x <= n + 1; x++)
                {
                    int i = (y + 1) * haloSide + x + 1;
                    double3 d = TerrainLattice.Direction(key.face, startX + x * step, startY + y * step);
                    heights[i] = source.Height(d, result.spacing, macroCache);
                    positions[i] = d * (source.ReferenceRadius(d) + source.DisplayDisplacement(heights[i]));
                }
            }
            for (int y = 0; y <= n; y++)
            {
                cancel.ThrowIfCancellationRequested();
                for (int x = 0; x <= n; x++)
                {
                    int i = y * side + x, hIndex = (y + 1) * haloSide + x + 1;
                    double3 p = positions[hIndex], d = math.normalize(p);
                    double h = heights[hIndex];
                    result.vertices[i] = (float3)(p - result.origin);
                    result.directions[i] = (float3)d;
                    float water = source.macro.hasOcean ? math.saturate((source.macro.seaLevelMetres - (float)h) / 120f) : 0;
                    double3 alongU = positions[hIndex + 1] - positions[hIndex - 1];
                    double3 alongV = positions[hIndex + haloSide] - positions[hIndex - haloSide];
                    double3 shapeNormal = math.normalize(d / (source.axes * source.axes));
                    double3 normal = math.normalizesafe(math.cross(alongV, alongU), shapeNormal);
                    if (math.dot(normal, d) < 0) normal = -normal;
                    result.normals[i] = (float3)math.normalize(math.lerp(normal, shapeNormal, water));
                    double dhU = (heights[hIndex + 1] - heights[hIndex - 1]) / (2 * result.spacing);
                    double dhV = (heights[hIndex + haloSide] - heights[hIndex - haloSide]) / (2 * result.spacing);
                    result.samples[i] = new Vector4((float)h, (float)math.degrees(math.atan(math.sqrt(dhU * dhU + dhV * dhV))), water, 0);
                    float3 albedo = source.macro.EvaluateAlbedoOnly((float3)d);
                    result.colors[i] = new Color(albedo.x, albedo.y, albedo.z, 1);
                }
            }
            int index = 0;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int a = y * side + x, b = a + 1, c = a + side, d = c + 1;
                result.triangles[index++] = a; result.triangles[index++] = c; result.triangles[index++] = b;
                result.triangles[index++] = b; result.triangles[index++] = c; result.triangles[index++] = d;
            }
            // Conservative radial skirts cover coarse/fine boundaries while the parent remains resident.
            double depth = math.min(source.radius * 0.25, math.max(0.02,
                result.spacing * 2 + source.detail.ResidualBound(result.spacing * 2) * source.reliefScale * 2));
            for (int edge = 0; edge < 4; edge++)
            for (int v = 0; v <= n; v++)
            {
                int a = EdgeVertex(edge, v, n), b = gridCount + edge * side + v;
                result.vertices[b] = result.vertices[a] - result.directions[a] * (float)depth;
                result.normals[b] = result.normals[a]; result.directions[b] = result.directions[a];
                result.colors[b] = result.colors[a]; result.samples[b] = result.samples[a];
                if (v == n) continue;
                int next = EdgeVertex(edge, v + 1, n);
                result.triangles[index++] = a; result.triangles[index++] = b; result.triangles[index++] = next;
                result.triangles[index++] = next; result.triangles[index++] = b; result.triangles[index++] = b + 1;
            }
            return result;
        }

        private static int EdgeVertex(int edge, int v, int n)
        {
            int side = n + 1;
            switch (edge)
            {
                case 0: return v;
                case 1: return v * side + n;
                case 2: return n * side + n - v;
                default: return (n - v) * side;
            }
        }
    }
}
