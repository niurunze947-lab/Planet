using System.Threading;
using NUnit.Framework;
using Planet.Procedural;
using Planet.Procedural.Terrain;
using Unity.Mathematics;
using UnityEngine;

public sealed class TerrainCollisionTests
{
    [Test]
    public void ColliderReusesDisplayedVerticesAndExcludesAllSkirts()
    {
        var definition = ScriptableObject.CreateInstance<PlanetDefinition>();
        Mesh mesh = null;
        try
        {
            definition.features.hasOcean = false;
            var source = new RuntimeTerrainSource(definition);
            var key = TerrainLattice.NodeAt(new double3(1, 0.2, 0.3), 18);
            var chunk = RuntimeTerrainChunk.Build(source, key, CancellationToken.None);
            mesh = chunk.UploadCollisionMesh();
            Assert.AreEqual(33 * 33, mesh.vertexCount);
            Assert.AreEqual(32 * 32 * 6, mesh.triangles.Length);
            for (int i = 0; i < mesh.vertexCount; i++) Assert.AreEqual(chunk.vertices[i], mesh.vertices[i]);
            foreach (int index in mesh.triangles) Assert.Less(index, 33 * 33);
        }
        finally
        {
            if (mesh != null) Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(definition);
        }
    }

    [Test]
    public void SurfaceRadiusMatchesDisplayedTriangleEvenWithExaggeratedRelief()
    {
        var definition = ScriptableObject.CreateInstance<PlanetDefinition>();
        try
        {
            definition.features.hasOcean = false;
            definition.generation.visualReliefScale = 4;
            var source = new RuntimeTerrainSource(definition);
            var key = TerrainLattice.NodeAt(new double3(1, 0.2, 0.3), 14);
            var chunk = RuntimeTerrainChunk.Build(source, key, CancellationToken.None);
            int tri = (7 * 32 + 11) * 6;
            double3 a = chunk.origin + (double3)(float3)chunk.vertices[chunk.triangles[tri]];
            double3 b = chunk.origin + (double3)(float3)chunk.vertices[chunk.triangles[tri + 1]];
            double3 c = chunk.origin + (double3)(float3)chunk.vertices[chunk.triangles[tri + 2]];
            double3 point = a * 0.2 + b * 0.3 + c * 0.5;
            Assert.IsTrue(chunk.TrySurfaceRadius(math.normalize(point), out double radius));
            Assert.AreEqual(math.length(point), radius, 0.001);
        }
        finally { Object.DestroyImmediate(definition); }
    }

    [Test]
    public void LookingAwayDoesNotCullTerrainUnderThePlayer()
    {
        var definition = ScriptableObject.CreateInstance<PlanetDefinition>();
        try
        {
            var source = new RuntimeTerrainSource(definition);
            double3 direction = math.normalize(new double3(1, 0.2, 0.3));
            var view = new PlanetViewState
            {
                positionBfMetres = source.Position(direction, 0.01) + direction * 0.5,
                rotationBf = quaternion.LookRotationSafe((float3)direction, new float3(0, 1, 0)),
                verticalFovDegrees = 60, aspect = 1.6f, pixelHeight = 1080,
                localCameraActive = true
            };
            var leaves = RuntimeTerrainSelection.Select(source, view, 192, 25, 3, true);
            int coveringLevel = 0;
            foreach (var key in leaves)
                if (TerrainLattice.NodeAt(direction, key.level).Equals(key)) coveringLevel = key.level;
            Assert.GreaterOrEqual(coveringLevel, 24);
        }
        finally { Object.DestroyImmediate(definition); }
    }
}
