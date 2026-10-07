using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Planet.Procedural;
using Planet.Procedural.Terrain;
using Unity.Mathematics;
using UnityEngine;

public sealed class RuntimeTerrainTests
{
    private PlanetDefinition definition;
    [SetUp] public void SetUp() { definition = ScriptableObject.CreateInstance<PlanetDefinition>(); }
    [TearDown] public void TearDown() { Object.DestroyImmediate(definition); }

    private PlanetViewState View(double altitude)
    {
        return new PlanetViewState
        {
            positionBfMetres = new double3(definition.physical.radiusMetres + altitude, 0, 0),
            rotationBf = quaternion.LookRotationSafe(new float3(-1, 0, 0), new float3(0, 1, 0)),
            verticalFovDegrees = 60, aspect = 16f / 9f, pixelHeight = 1080
        };
    }

    [Test]
    public void SelectionRefinesNearbyTerrainWithinABoundedCompletePartition()
    {
        var source = new RuntimeTerrainSource(definition);
        var far = RuntimeTerrainSelection.Select(source, View(100000000), 96, 25, 3, true);
        var near = RuntimeTerrainSelection.Select(source, View(10), 96, 25, 3, true);
        int farLevel = 0, nearLevel = 0;
        foreach (var key in far) farLevel = math.max(farLevel, key.level);
        foreach (var key in near) { nearLevel = math.max(nearLevel, key.level); Assert.IsTrue(key.IsValid); }
        Assert.That(nearLevel, Is.GreaterThan(farLevel));
        Assert.That(near.Count, Is.LessThanOrEqualTo(96));
        for (int f = 0; f < 6; f++)
        for (int i = 0; i < 30; i++)
        {
            double3 direction = TerrainLattice.Direction((PlanetCubeFace)f, new double2(-0.97 + i * 0.061, 0.123));
            var finest = TerrainLattice.NodeAt(direction, TerrainLattice.MaxLevel);
            int covering = 0;
            foreach (var leaf in near) if (leaf == finest || leaf.IsAncestorOf(finest)) covering++;
            Assert.AreEqual(1, covering, "Each point has exactly one selected leaf.");
        }
        Assert.AreEqual(6, RuntimeTerrainSelection.Select(source, View(10), 96, 25, 3, false).Count);
    }

    [TestCase(6371000.0, 1.0, 19)]
    [TestCase(1000000.0, 1.0, 16)]
    [TestCase(6371000.0, 0.5, 20)]
    public void CollisionSpacingSelectsTheSmallestSufficientLevel(double radius, double spacing, int expected)
    {
        int level = TerrainLattice.LevelForSpacing(radius, spacing);
        Assert.AreEqual(expected, level);
        Assert.LessOrEqual(TerrainLattice.Spacing(radius, level), spacing);
        if (level > 0) Assert.Greater(TerrainLattice.Spacing(radius, level - 1), spacing);
    }

    [Test]
    public void VisualSelectionHonoursIndependentLevelCap()
    {
        var source = new RuntimeTerrainSource(definition);
        var leaves = RuntimeTerrainSelection.Select(source, View(0.5), 512, 20, 3, true);
        foreach (TerrainNodeKey leaf in leaves) Assert.LessOrEqual(leaf.level, 20);
        Assert.Greater(TerrainLattice.LevelForSpacing(source.radius, 1.0), 0,
            "Collision target is a separate spacing-derived level.");
    }

    [Test]
    public void AncestorRequestsKeepParentsUntilAllChildrenCanReplaceThem()
    {
        var source = new RuntimeTerrainSource(definition);
        var leaves = RuntimeTerrainSelection.Select(source, View(1000), 96, 25, 3, true);
        var all = RuntimeTerrainSelection.WithAncestors(leaves);
        Assert.That(all.Count, Is.LessThanOrEqualTo(128));
        foreach (var key in all)
        {
            if (key.level > 0) Assert.IsTrue(all.Contains(key.Parent));
            if (!leaves.Contains(key)) for (int i = 0; i < 4; i++) Assert.IsTrue(all.Contains(key.Child(i)));
        }
    }

    [Test]
    public void AdjacentCentimetreChunksSharePositionsWithoutGlobalFloatCoordinates()
    {
        definition.features.hasOcean = false;
        var source = new RuntimeTerrainSource(definition);
        var key = TerrainLattice.NodeAt(new double3(1, 0.123, -0.243), 25);
        var next = new TerrainNodeKey(key.face, key.level, key.x + 1, key.y);
        var a = RuntimeTerrainChunk.Build(source, key, CancellationToken.None);
        var b = RuntimeTerrainChunk.Build(source, next, CancellationToken.None);
        int side = TerrainLattice.N + 1;
        Assert.That(a.spacing, Is.InRange(0.01, 0.02));
        for (int y = 0; y < side; y++)
        {
            double3 pa = a.origin + (double3)(float3)a.vertices[y * side + side - 1];
            double3 pb = b.origin + (double3)(float3)b.vertices[y * side];
            Assert.That(math.distance(pa, pb), Is.LessThan(0.00001));
            Assert.AreEqual(a.colors[y * side + side - 1], b.colors[y * side],
                "Macro color interpolation must agree at shared chunk vertices.");
        }
        Assert.That(Vector3.Distance(a.vertices[0], a.vertices[1]), Is.InRange(0.001f, 0.1f));
        foreach (var vertex in a.vertices) Assert.IsTrue(math.all(math.isfinite((float3)vertex)));
        foreach (int index in a.triangles) Assert.That(index, Is.InRange(0, a.vertices.Length - 1));
    }

    [Test]
    public void ChildBoundaryMorphMatchesParentFrequencyAtEverySharedVertex()
    {
        definition.features.hasOcean = false;
        var source = new RuntimeTerrainSource(definition);
        TerrainNodeKey parentKey = TerrainLattice.NodeAt(new double3(1, 0.2, 0.3), 14);
        RuntimeTerrainChunk parent = RuntimeTerrainChunk.Build(source, parentKey, CancellationToken.None);
        RuntimeTerrainChunk child = RuntimeTerrainChunk.Build(source, parentKey.Child(0), CancellationToken.None);
        int side = TerrainLattice.N + 1;

        for (int v = 0; v <= TerrainLattice.N; v += 2)
        {
            AssertShared(child, 0, v, parent, 0, v / 2, side);
            AssertShared(child, TerrainLattice.N, v, parent, TerrainLattice.N / 2, v / 2, side);
            AssertShared(child, v, 0, parent, v / 2, 0, side);
            AssertShared(child, v, TerrainLattice.N, parent, v / 2, TerrainLattice.N / 2, side);
        }
    }

    private static void AssertShared(RuntimeTerrainChunk a, int ax, int ay,
        RuntimeTerrainChunk b, int bx, int by, int side)
    {
        double3 pa = a.origin + (double3)(float3)a.vertices[ay * side + ax];
        double3 pb = b.origin + (double3)(float3)b.vertices[by * side + bx];
        Assert.Less(math.distance(pa, pb), 0.0001,
            "Fine child boundary must use the parent-resolved relief band.");
    }

    [Test]
    public void MacroInterpolationIsContinuousAcrossCubeFaceEdges()
    {
        var source = new RuntimeTerrainSource(definition);
        for (int face = 0; face < 6; face++)
        for (int edge = 0; edge < 4; edge++)
        {
            long a = TerrainLattice.LatticeSize / 3;
            CubeFaceAdjacency.MapEdgeLattice((PlanetCubeFace)face, (TerrainEdge)edge, a, out var other, out long x, out long y);
            long ox = edge == 0 ? 0 : edge == 1 ? TerrainLattice.LatticeSize : a;
            long oy = edge == 2 ? 0 : edge == 3 ? TerrainLattice.LatticeSize : a;
            var da = TerrainLattice.Direction((PlanetCubeFace)face, ox, oy);
            var db = TerrainLattice.Direction(other, x, y);
            Assert.AreEqual(source.MacroHeight(da), source.MacroHeight(db));
        }
    }

    [Test]
    public void FineBandsDoNotCreateWaterOnDryCoastsOrTerrainOnOpticalLayers()
    {
        var source = new RuntimeTerrainSource(definition);
        for (int i = 0; i < 128; i++)
        {
            double3 d = math.normalize(new double3(math.cos(i), i / 64.0 - 1, math.sin(i)));
            bool wet = source.MacroHeight(d) < source.macro.seaLevelMetres;
            Assert.AreEqual(wet, source.Height(d, 0.01) < source.macro.seaLevelMetres);
        }
        definition.appearance.surfaceKind = PlanetSurfaceKind.GasGiant;
        var gas = new RuntimeTerrainSource(definition);
        Assert.AreEqual(gas.MacroHeight(new double3(1, 0, 0)), gas.Height(new double3(1, 0, 0), 0.001));
    }

    [Test]
    public void ObsoleteChunkWorkCanBeCancelledBeforeAllocatingUnityObjects()
    {
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            Assert.Throws<System.OperationCanceledException>(() => RuntimeTerrainChunk.Build(new RuntimeTerrainSource(definition),
                TerrainNodeKey.Root(PlanetCubeFace.PositiveX), cancel.Token));
        }
    }
}
