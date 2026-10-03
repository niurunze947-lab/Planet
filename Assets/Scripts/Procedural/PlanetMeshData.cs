using UnityEngine;
using UnityEngine.Rendering;

namespace Planet.Procedural
{
    public sealed class PlanetMeshData
    {
        public readonly Vector3[] positions;
        public readonly Vector3[] normals;
        public readonly Vector3[] directions;
        public readonly Vector2[] faceUV;
        public readonly Vector4[] samples;
        public readonly int[] triangles;

        public PlanetMeshData(int vertexCount, int indexCount)
        {
            positions = new Vector3[vertexCount]; normals = new Vector3[vertexCount];
            directions = new Vector3[vertexCount]; faceUV = new Vector2[vertexCount];
            samples = new Vector4[vertexCount]; triangles = new int[indexCount];
        }

        public Mesh CreateMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.indexFormat = positions.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.uv = faceUV;
            mesh.SetUVs(1, directions);
            mesh.SetUVs(2, samples);
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
