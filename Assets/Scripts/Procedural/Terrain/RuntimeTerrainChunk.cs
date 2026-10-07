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
        public double3 oceanOrigin;
        public Vector3[] oceanVertices, oceanNormals;
        /// <summary>Uphill bed direction (body axes) and bed slope, for the nearshore wave train.</summary>
        public Vector4[] oceanShore;

        public Mesh UploadOceanMesh()
        {
            if (oceanVertices == null) return null;
            int count = oceanVertices.Length;
            var oceanSamples = new Vector4[count];
            var oceanDirections = new Vector3[count];
            Array.Copy(samples, oceanSamples, count);
            Array.Copy(directions, oceanDirections, count);
            var indices = new int[TerrainLattice.N * TerrainLattice.N * 6];
            Array.Copy(triangles, indices, indices.Length);
            var mesh = new Mesh { name = "Ocean " + key, hideFlags = HideFlags.DontSave };
            mesh.vertices = oceanVertices; mesh.normals = oceanNormals;
            mesh.SetUVs(1, oceanDirections); mesh.SetUVs(2, oceanSamples); mesh.triangles = indices;
            if (oceanShore != null)
            {
                var shore = new Vector4[count];
                Array.Copy(oceanShore, shore, count);
                mesh.SetUVs(3, shore);
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Ray intersection with the displayed triangles, not a different-frequency height sample.</summary>
        public bool TrySurfaceRadius(double3 direction, out double radius)
        {
            radius = 0;
            direction = math.normalizesafe(direction);
            if (!TerrainLattice.NodeAt(direction, key.level).Equals(key)) return false;
            TerrainLattice.NodeUv(key, out double2 min, out double2 max);
            double2 uv = TerrainLattice.FaceUv(key.face, direction);
            double2 grid = (uv - min) / (max - min) * TerrainLattice.N;
            int x = math.clamp((int)math.floor(grid.x), 0, TerrainLattice.N - 1);
            int y = math.clamp((int)math.floor(grid.y), 0, TerrainLattice.N - 1);
            int triangle = (y * TerrainLattice.N + x) * 6;
            for (int i = 0; i < 2; i++)
            {
                double3 a = origin + (double3)(float3)vertices[triangles[triangle + i * 3]];
                double3 b = origin + (double3)(float3)vertices[triangles[triangle + i * 3 + 1]];
                double3 c = origin + (double3)(float3)vertices[triangles[triangle + i * 3 + 2]];
                double3 normal = math.cross(b - a, c - a);
                double denominator = math.dot(normal, direction);
                if (math.abs(denominator) < 1e-18) continue;
                double t = math.dot(normal, a) / denominator;
                double3 point = direction * t;
                double3 ab = b - a, ac = c - a, ap = point - a;
                double aa = math.dot(ab, ab), cc = math.dot(ac, ac), cross = math.dot(ab, ac);
                double det = aa * cc - cross * cross;
                if (math.abs(det) < 1e-28) continue;
                double u = (math.dot(ap, ab) * cc - math.dot(ap, ac) * cross) / det;
                double v = (math.dot(ap, ac) * aa - math.dot(ap, ab) * cross) / det;
                if (t > 0 && u >= -1e-5 && v >= -1e-5 && u + v <= 1.00001)
                { radius = t; return true; }
            }
            return false;
        }

        public Mesh UploadCollisionMesh()
        {
            const int gridCount = (TerrainLattice.N + 1) * (TerrainLattice.N + 1);
            var surfaceVertices = new Vector3[gridCount];
            Array.Copy(vertices, surfaceVertices, gridCount);
            var surfaceTriangles = new int[TerrainLattice.N * TerrainLattice.N * 6];
            Array.Copy(triangles, surfaceTriangles, surfaceTriangles.Length);
            var mesh = new Mesh { name = "Collision " + key, hideFlags = HideFlags.DontSave };
            mesh.vertices = surfaceVertices;
            mesh.triangles = surfaceTriangles;
            mesh.RecalculateBounds();
            return mesh;
        }

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
            var colorCache = new Dictionary<float3, float3>();
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
            if (source.macro.hasOcean)
            {
                result.oceanOrigin = source.OceanPosition(centerDirection);
                result.oceanVertices = new Vector3[gridCount];
                result.oceanNormals = new Vector3[gridCount];
                result.oceanShore = new Vector4[gridCount];
            }
            bool anyWater = false;
            // One halo supplies central differences. This samples height once per grid point,
            // rather than five full terrain evaluations per vertex for independently derived normals.
            const int haloSide = n + 3;
            var positions = new double3[haloSide * haloSide];
            var heights = new double[positions.Length];
            double parentSpacing = result.spacing * 2.0;
            for (int y = -1; y <= n + 1; y++)
            {
                cancel.ThrowIfCancellationRequested();
                for (int x = -1; x <= n + 1; x++)
                {
                    int i = (y + 1) * haloSide + x + 1;
                    double3 d = TerrainLattice.Direction(key.face, startX + x * step, startY + y * step);
                    double height;
                    if (key.level > 0)
                    {
                        source.HeightBands(d, result.spacing, parentSpacing, out height, out double parentHeight, macroCache);
                        double edgeDistance = math.min(math.min(x, n - x), math.min(y, n - y));
                        double blend = math.saturate(edgeDistance * 0.5);
                        height = math.lerp(parentHeight, height, blend);
                    }
                    else height = source.Height(d, result.spacing, macroCache);
                    heights[i] = height;
                    positions[i] = d * (source.ReferenceRadius(d) + source.DisplayDisplacement(height));
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
                    result.normals[i] = (float3)normal;
                    if (result.oceanVertices != null)
                    {
                        result.oceanVertices[i] = (float3)(source.OceanPosition(d) - result.oceanOrigin);
                        result.oceanNormals[i] = (float3)shapeNormal;
                        anyWater |= h < source.macro.seaLevelMetres;
                        // Steepest ascent of the seabed, measured in the surface metric from the same
                        // halo as the normals. The shoreline is its contour line, so this is the
                        // direction the nearshore wave train travels and the slope its phase scale.
                        double hx = heights[hIndex + 1] - heights[hIndex - 1];
                        double hy = heights[hIndex + haloSide] - heights[hIndex - haloSide];
                        double e = math.dot(alongU, alongU), f = math.dot(alongU, alongV), g = math.dot(alongV, alongV);
                        double3 gradient = alongU * (g * hx - f * hy) + alongV * (e * hy - f * hx);
                        gradient -= d * math.dot(gradient, d);
                        double bedSlope = math.length(gradient);
                        result.oceanShore[i] = bedSlope > 1e-9
                            ? new Vector4((float)(gradient.x / bedSlope), (float)(gradient.y / bedSlope),
                                (float)(gradient.z / bedSlope), (float)bedSlope)
                            : new Vector4(0f, 0f, 0f, 0f);
                    }
                    double dhU = (heights[hIndex + 1] - heights[hIndex - 1]) / (2 * result.spacing);
                    double dhV = (heights[hIndex + haloSide] - heights[hIndex - haloSide]) / (2 * result.spacing);
                    result.samples[i] = new Vector4((float)h, (float)math.degrees(math.atan(math.sqrt(dhU * dhU + dhV * dhV))), water, 0);
                    float3 albedo = source.MacroAlbedo(d, result.spacing, colorCache);
                    result.colors[i] = new Color(albedo.x, albedo.y, albedo.z, 1);
                }
            }
            if (!anyWater) { result.oceanVertices = null; result.oceanNormals = null; result.oceanShore = null; }
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
