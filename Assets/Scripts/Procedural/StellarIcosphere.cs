using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Planet.Procedural
{
    /// <summary>Shared unit-radius, UV-free photospheric geometry. Convection relief is normal based.</summary>
    public static class StellarIcosphere
    {
        private static readonly Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();
        public static Mesh Get(int subdivisions)
        {
            subdivisions = Mathf.Clamp(subdivisions, 0, 6);
            Mesh cached;
            if (meshes.TryGetValue(subdivisions, out cached) && cached != null) return cached;
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3> { new Vector3(-1,t,0), new Vector3(1,t,0), new Vector3(-1,-t,0), new Vector3(1,-t,0),
                new Vector3(0,-1,t),new Vector3(0,1,t),new Vector3(0,-1,-t),new Vector3(0,1,-t),
                new Vector3(t,0,-1),new Vector3(t,0,1),new Vector3(-t,0,-1),new Vector3(-t,0,1) };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var faces = new List<int> {0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1};
            for (int level = 0; level < subdivisions; level++)
            {
                var edges = new Dictionary<long, int>(); var next = new List<int>(faces.Count * 4);
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i], b = faces[i+1], c = faces[i+2];
                    int ab = Midpoint(a,b,v,edges), bc = Midpoint(b,c,v,edges), ca = Midpoint(c,a,v,edges);
                    next.AddRange(new[] {a,ab,ca, b,bc,ab, c,ca,bc, ab,bc,ca});
                }
                faces = next;
            }
            Mesh mesh = new Mesh {name = "Stellar_Icosphere_" + subdivisions, hideFlags = HideFlags.DontSave,
                indexFormat = v.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16};
            mesh.SetVertices(v); mesh.SetNormals(v); mesh.SetTriangles(faces, 0); mesh.RecalculateBounds();
            meshes[subdivisions] = mesh; return mesh;
        }
        private static int Midpoint(int a, int b, List<Vector3> v, Dictionary<long, int> edges)
        {
            long key = ((long)Mathf.Min(a,b) << 32) | (uint)Mathf.Max(a,b);
            int index; if (edges.TryGetValue(key, out index)) return index;
            index = v.Count; v.Add((v[a] + v[b]).normalized); edges[key] = index; return index;
        }
    }
}
