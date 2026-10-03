using System.Collections.Generic;
using UnityEngine;

namespace Planet.Solar
{
    /// <summary>
    /// Procedural meshes for the white-model solar system. Everything is generated at runtime
    /// (like <see cref="PlanetMeshGenerator"/> does for Earth and Moon) so the repository carries
    /// no binary mesh assets, and the shapes stay reproducible from the code alone.
    /// </summary>
    public static class SolarMeshFactory
    {
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        /// <summary>
        /// A smooth-shaded UV sphere of unit radius. <paramref name="flattening"/> squashes the
        /// polar axis (Jupiter is 6.5% oblate, Saturn 9.8%), and <paramref name="shape"/> applies
        /// an extra triaxial stretch so Haumea can be the 1050x840x537 km ellipsoid it really is.
        /// </summary>
        public static Mesh Sphere(int longitudes, int latitudes, float flattening, Vector3 shape)
        {
            longitudes = Mathf.Clamp(longitudes, 8, 512);
            latitudes = Mathf.Clamp(latitudes, 4, 256);

            string key = "sph_" + longitudes + "x" + latitudes + "_" + flattening.ToString("F5") + "_" + shape.ToString("F4");
            Mesh cached;
            if (Cache.TryGetValue(key, out cached) && cached != null) return cached;

            int cols = longitudes + 1;          // duplicate the seam column so UVs are continuous
            int rows = latitudes + 1;
            var verts = new Vector3[cols * rows];
            var uvs = new Vector2[cols * rows];
            var norms = new Vector3[cols * rows];

            for (int y = 0; y < rows; y++)
            {
                float v = (float)y / latitudes;
                float phi = v * Mathf.PI;                       // 0 at the north pole
                float sinPhi = Mathf.Sin(phi);
                float cosPhi = Mathf.Cos(phi);

                for (int x = 0; x < cols; x++)
                {
                    float u = (float)x / longitudes;
                    float theta = u * Mathf.PI * 2f;

                    float px = sinPhi * Mathf.Cos(theta);
                    float py = cosPhi;
                    float pz = sinPhi * Mathf.Sin(theta);

                    int idx = y * cols + x;
                    verts[idx] = new Vector3(px * shape.x, py * shape.y * (1f - flattening), pz * shape.z);
                    uvs[idx] = new Vector2(u, 1f - v);
                    norms[idx] = new Vector3(px, py, pz);
                }
            }

            var tris = new int[longitudes * latitudes * 6];
            int t = 0;
            for (int y = 0; y < latitudes; y++)
            {
                for (int x = 0; x < longitudes; x++)
                {
                    int a = y * cols + x;
                    int b = a + 1;
                    int c = a + cols;
                    int d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            }

            var mesh = new Mesh();
            mesh.name = key;
            mesh.hideFlags = HideFlags.DontSave;
            if (verts.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.normals = norms;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);

            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// One or more flat annuli in the XZ plane, both faces present so they need no double-sided
        /// material. Radii are fractions of the outer radius, letting one mesh serve any planet.
        ///
        /// Passing several bands is what produces Saturn's Cassini and Roche divisions: a single
        /// solid annulus reads as a dinner plate rather than as a ring system.
        /// </summary>
        public static Mesh Ring(double innerOverOuter, int segments)
        {
            return Rings(new[] { (float)innerOverOuter, 1f }, segments);
        }

        /// <summary>Builds a ring system from pairs of radii, each a fraction of the outer radius.</summary>
        public static Mesh Rings(float[] innerOuterPairs, int segments)
        {
            segments = Mathf.Clamp(segments, 16, 512);
            int bandCount = Mathf.Max(1, innerOuterPairs.Length / 2);

            var keyBuilder = new System.Text.StringBuilder("rings");
            keyBuilder.Append('_').Append(segments);
            for (int i = 0; i < innerOuterPairs.Length; i++)
                keyBuilder.Append('_').Append(innerOuterPairs[i].ToString("F4"));
            string key = keyBuilder.ToString();

            Mesh cached;
            if (Cache.TryGetValue(key, out cached) && cached != null) return cached;

            int ringVerts = 2 * (segments + 1);
            var verts = new Vector3[ringVerts * 2 * bandCount];
            var norms = new Vector3[ringVerts * 2 * bandCount];
            var uvs = new Vector2[ringVerts * 2 * bandCount];
            var tris = new int[segments * 6 * 2 * bandCount];

            int t = 0;
            for (int band = 0; band < bandCount; band++)
            {
                float inner = innerOuterPairs[band * 2];
                float outer = innerOuterPairs[band * 2 + 1];
                int bandBase = band * ringVerts * 2;

                for (int face = 0; face < 2; face++)
                {
                    int baseIdx = bandBase + face * ringVerts;
                    float sign = face == 0 ? 1f : -1f;
                    for (int k = 0; k <= segments; k++)
                    {
                        float a = (float)k / segments * Mathf.PI * 2f;
                        float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                        verts[baseIdx + k * 2 + 0] = new Vector3(ca * inner, 0f, sa * inner);
                        verts[baseIdx + k * 2 + 1] = new Vector3(ca * outer, 0f, sa * outer);
                        norms[baseIdx + k * 2 + 0] = new Vector3(0f, sign, 0f);
                        norms[baseIdx + k * 2 + 1] = new Vector3(0f, sign, 0f);
                        uvs[baseIdx + k * 2 + 0] = new Vector2((float)k / segments, 0f);
                        uvs[baseIdx + k * 2 + 1] = new Vector2((float)k / segments, 1f);
                    }
                }

                for (int face = 0; face < 2; face++)
                {
                    int baseIdx = bandBase + face * ringVerts;
                    for (int k = 0; k < segments; k++)
                    {
                        int i0 = baseIdx + k * 2;
                        int i1 = i0 + 1;
                        int i2 = i0 + 2;
                        int i3 = i0 + 3;
                        if (face == 0)
                        {
                            tris[t++] = i0; tris[t++] = i1; tris[t++] = i2;
                            tris[t++] = i1; tris[t++] = i3; tris[t++] = i2;
                        }
                        else
                        {
                            tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
                            tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
                        }
                    }
                }
            }

            var mesh = new Mesh();
            mesh.name = key;
            mesh.hideFlags = HideFlags.DontSave;
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);

            Cache[key] = mesh;
            return mesh;
        }
    }
}
