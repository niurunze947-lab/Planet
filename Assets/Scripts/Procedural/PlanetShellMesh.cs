using UnityEngine;

namespace Planet.Procedural
{
    /// <summary>Smooth shared shell, with the same half-unit radius as a Unity sphere primitive.</summary>
    public static class PlanetShellMesh
    {
        private static Mesh _sphere;

        public static Mesh Sphere
        {
            get
            {
                if (_sphere != null) return _sphere;
                const int longitude = 192, latitude = 96;
                int columns = longitude + 1;
                var positions = new Vector3[columns * (latitude + 1)];
                var normals = new Vector3[positions.Length];
                var triangles = new int[longitude * latitude * 6];
                for (int y = 0; y <= latitude; y++)
                {
                    float phi = (float)y / latitude * Mathf.PI;
                    for (int x = 0; x <= longitude; x++)
                    {
                        float theta = (float)x / longitude * 2f * Mathf.PI;
                        int i = y * columns + x;
                        Vector3 d = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi),
                            Mathf.Sin(phi) * Mathf.Sin(theta));
                        normals[i] = d;
                        positions[i] = d * 0.5f;
                    }
                }
                int t = 0;
                for (int y = 0; y < latitude; y++)
                for (int x = 0; x < longitude; x++)
                {
                    int a = y * columns + x, b = a + 1, c = a + columns, d = c + 1;
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                    triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
                }
                _sphere = new Mesh { name = "PlanetShell_192x96", hideFlags = HideFlags.DontSave };
                _sphere.vertices = positions;
                _sphere.normals = normals;
                _sphere.triangles = triangles;
                _sphere.RecalculateBounds();
                return _sphere;
            }
        }
    }
}
