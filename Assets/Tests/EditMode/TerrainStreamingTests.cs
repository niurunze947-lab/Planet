using NUnit.Framework;
using Planet.Procedural;
using Planet.Procedural.Terrain;
using Unity.Mathematics;
using UnityEngine;

public sealed class TerrainStreamingTests
{
    [Test]
    public void MissingCollidersDoNotBlockClearAirInsideTheReliefEnvelope()
    {
        // The planet's highest mountain is elsewhere. Flying at 500m above this valley is legal.
        Assert.IsFalse(TerrainSurfaceSweep.Sweep(new double3(0, 0, 6500), new double3(25, 0, 6490),
            0.5, 8000, d => 6000, out var position, out _));
        Assert.AreEqual(new double3(25, 0, 6490), position);
    }

    [Test]
    public void MissingCollidersStillStopFastCrossPlanetTransits()
    {
        Assert.IsTrue(TerrainSurfaceSweep.Sweep(new double3(-20000, 0, 0), new double3(20000, 0, 0),
            0.5, 8000, d => 6000, out var position, out var normal));
        Assert.That(position.x, Is.InRange(-6000.53, -6000.49));
        Assert.Less(normal.x, -0.999);
    }

    [Test]
    public void HeightFallbackAllowsEscapeFromContactAndReturnsTheTerrainNormal()
    {
        Assert.IsFalse(TerrainSurfaceSweep.Sweep(new double3(0, 6000.5, 0), new double3(0, 6010, 0),
            0.5, 8000, d => 6000, out _, out _));
        Assert.IsTrue(TerrainSurfaceSweep.Sweep(new double3(0, 6020, 0), new double3(2, 5980, 0),
            0.5, 8000, d => 6000, out var position, out var normal));
        Assert.Greater(math.length(position), 6000.49);
        Assert.Greater(normal.y, 0.999);
        Assert.Less(math.abs(normal.x), 0.001, "Surface normal must not be the reverse movement vector.");
    }

    [Test]
    public void DetailPreloadsProgressivelyBeforeOneKilometreAndRetainsOnExit()
    {
        var policy = TerrainNeighbourhoodPolicy.Default;
        int high = policy.RequiredLevel(6371000, 2100, 500);
        int approaching = policy.RequiredLevel(6371000, 1500, 500);
        int near = policy.RequiredLevel(6371000, 999, 500);
        Assert.Less(high, approaching);
        Assert.Less(approaching, near);
        Assert.AreEqual(near, policy.RequiredLevel(6371000, 1001, 500));
        Assert.AreEqual(near, policy.RequiredLevel(6371000, 1100, 500, true));
        var view = new PlanetViewState { positionBfMetres = new double3(6373000, 0, 0),
            velocityBfMetresPerSecond = new double3(-400, 200, 0) };
        Assert.AreEqual(1200, TerrainNeighbourhoodPolicy.PredictedClearance(view, 2000, 2));
        Assert.Greater(TerrainNeighbourhoodPolicy.LookAhead(view, 2).y, 0);
    }

    [Test]
    public void StableSelectionProducesTheSamePartitionAndHonoursTheBudget()
    {
        var definition = ScriptableObject.CreateInstance<PlanetDefinition>();
        try
        {
            var source = new RuntimeTerrainSource(definition);
            var view = new PlanetViewState { positionBfMetres = new double3(6373500, 0, 0),
                rotationBf = quaternion.identity, localCameraActive = true, pixelHeight = 1080,
                aspect = 1.6f, verticalFovDegrees = 60, velocityBfMetresPerSecond = new double3(-400, 0, 100) };
            var a = RuntimeTerrainSelection.Select(source, view, 384, 25, 3, true);
            var b = RuntimeTerrainSelection.Select(source, view, 384, 25, 3, true);
            Assert.IsTrue(a.SetEquals(b));
            Assert.LessOrEqual(a.Count, 384);
            Assert.LessOrEqual(RuntimeTerrainSelection.WithAncestors(a).Count, 512);
        }
        finally { Object.DestroyImmediate(definition); }
    }
}
