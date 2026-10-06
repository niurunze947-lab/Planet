using NUnit.Framework;
using Planet.Procedural;
using Planet.Procedural.Terrain;
using Unity.Mathematics;
using UnityEngine;

public sealed class PlanetTerrainClimateTests
{
    private PlanetDefinition definition;
    [SetUp] public void SetUp() { definition = ScriptableObject.CreateInstance<PlanetDefinition>(); }
    [TearDown] public void TearDown() { Object.DestroyImmediate(definition); }

    private static float3 Direction(int i)
    {
        float y = 1f - (i + 0.5f) / 64f;
        float r = math.sqrt(1f - y * y);
        return new float3(r * math.cos(i * 2.39996f), y, r * math.sin(i * 2.39996f));
    }

    [Test]
    public void CloudFieldRespondsToOceanAndMoistureWithoutMovingTerrain()
    {
        definition.generation.cloudCoverage = 0.65f;
        definition.physical.seaLevelMetres = 100000f; // Same terrain, wholly covered by water.
        var wet = definition.CreateSampler();
        definition.features.hasOcean = false;
        var dry = definition.CreateSampler();
        double wetTotal = 0, dryTotal = 0;
        for (int i = 0; i < 128; i++)
        {
            var d = Direction(i);
            Assert.AreEqual(wet.EvaluateHeight(d), dry.EvaluateHeight(d));
            wetTotal += wet.EvaluateCloudDensity(d); dryTotal += dry.EvaluateCloudDensity(d);
        }
        Assert.That(wetTotal, Is.GreaterThan(dryTotal * 1.15));
        definition.generation.moistureOffset = -0.3f;
        var arid = definition.CreateSampler();
        definition.generation.moistureOffset = 0.3f;
        var humid = definition.CreateSampler();
        double aridTotal = 0, humidTotal = 0;
        for (int i = 0; i < 128; i++)
        { aridTotal += arid.EvaluateCloudDensity(Direction(i)); humidTotal += humid.EvaluateCloudDensity(Direction(i)); }
        Assert.That(humidTotal, Is.GreaterThan(aridTotal * 1.1));
    }

    [Test]
    public void HighTerrainBiasAndWindwardEffectChangeClouds()
    {
        definition.physical.seaLevelMetres = -10000;
        definition.generation.cloudCoverage = 0.65f;
        definition.generation.cloudAltitudeBias = -1;
        var suppressed = definition.CreateSampler();
        definition.generation.cloudAltitudeBias = 0;
        var neutral = definition.CreateSampler();
        double suppressedTotal = 0, neutralTotal = 0;
        for (int i = 0; i < 128; i++)
        { suppressedTotal += suppressed.EvaluateCloudDensity(Direction(i)); neutralTotal += neutral.EvaluateCloudDensity(Direction(i)); }
        Assert.That(neutralTotal, Is.GreaterThan(suppressedTotal));
        definition.generation.cloudOrographicInfluence = 0;
        var calm = definition.CreateSampler();
        definition.generation.cloudOrographicInfluence = 1;
        var wind = definition.CreateSampler();
        double difference = 0;
        for (int i = 0; i < 128; i++)
            difference += math.abs(wind.EvaluateCloudDensity(Direction(i)) - calm.EvaluateCloudDensity(Direction(i)));
        Assert.That(difference, Is.GreaterThan(0.01));
    }

    [TestCase(PlanetSurfaceKind.Terrestrial)]
    [TestCase(PlanetSurfaceKind.GasGiant)]
    public void CloudsRemainFiniteDeterministicAndRespectOptOut(PlanetSurfaceKind kind)
    {
        definition.appearance.surfaceKind = kind;
        var sampler = definition.CreateSampler();
        for (int i = 0; i < 128; i++)
        {
            var d = i < 2 ? new float3(0, i == 0 ? 1 : -1, 0) : Direction(i);
            float a = sampler.EvaluateCloudDensity(d);
            Assert.That(a, Is.InRange(0f, 1f));
            Assert.AreEqual(a, sampler.EvaluateCloudDensity(d));
        }
        definition.generation.cloudTerrainInfluence = 0;
        var baseline = definition.CreateSampler();
        definition.generation.mountainHeightMetres *= 10;
        Assert.AreEqual(baseline.EvaluateCloudDensity(Direction(16)), definition.CreateSampler().EvaluateCloudDensity(Direction(16)));
        definition.features.hasClouds = false;
        Assert.AreEqual(0, definition.CreateSampler().EvaluateCloudDensity(Direction(16)));
    }

    [Test]
    public void ExaggerationMovesSolidTerrainIncludingSeabedButNotOceanOrPhysicalQueries()
    {
        definition.physical.seaLevelMetres = -500;
        definition.generation.visualReliefScale = 1;
        var physical = definition.CreateSampler();
        definition.generation.visualReliefScale = 40;
        var exaggerated = definition.CreateSampler();
        var d = new float3(1, 0, 0);
        Assert.Less(exaggerated.SurfacePosition(d, -2000).x, physical.SurfacePosition(d, -2000).x);
        Assert.AreEqual(physical.OceanPosition(d), exaggerated.OceanPosition(d));
        Assert.That(exaggerated.SurfacePosition(d, 3000).x, Is.GreaterThan(physical.SurfacePosition(d, 3000).x + 0.02f));
        for (int i = 0; i < 128; i++)
        {
            var a = physical.Evaluate(Direction(i)); var b = exaggerated.Evaluate(Direction(i));
            Assert.AreEqual(a.heightMetres, b.heightMetres);
            Assert.AreEqual(a.waterDepthMetres, b.waterDepthMetres);
            Assert.AreEqual(a.ocean, b.ocean);
            Assert.AreEqual(physical.EvaluateCloudDensity(Direction(i)), exaggerated.EvaluateCloudDensity(Direction(i)));
        }
        var mesh = CubeSphere.Generate(exaggerated, 12);
        var baseMesh = CubeSphere.Generate(physical, 12);
        float maxMotion = 0;
        for (int i = 0; i < mesh.positions.Length; i++)
            maxMotion = Mathf.Max(maxMotion, Vector3.Distance(mesh.positions[i], baseMesh.positions[i]));
        Assert.That(maxMotion, Is.GreaterThan(0.005f));
    }

    [Test]
    public void FineDetailInvalidatesOnlyTerrainCache()
    {
        string globe = definition.ContentFingerprint(), terrain = definition.TerrainContentFingerprint();
        definition.detail.amplitudeMetres *= 2;
        Assert.AreEqual(globe, definition.ContentFingerprint());
        Assert.AreNotEqual(terrain, definition.TerrainContentFingerprint());
        definition.generation.cloudOceanDensity += 0.1f;
        Assert.AreNotEqual(globe, definition.ContentFingerprint());
    }

    [Test]
    public void DetailCutoffAndParentBandsRespectPhysicalSpacing()
    {
        var noise = new PlanetDetailSampler(42, 40, 0.55, 0.02);
        double shortest = PlanetDetailSampler.BaseWavelengthMetres / math.pow(2, noise.octaveCount - 1);
        Assert.That(shortest, Is.GreaterThanOrEqualTo(0.02));
        Assert.That(shortest * 0.5, Is.LessThan(0.02));
        var p = new double3(6371000.125, 23.46, -711.18);
        Assert.AreEqual(0, noise.Evaluate(p, 10000));
        noise.Evaluate2(p, 10, 20, out double own, out double parent);
        Assert.AreEqual(noise.Evaluate(p, 10), own);
        Assert.AreEqual(noise.Evaluate(p, 20), parent);
        noise.Evaluate2(p, 20, 10, out own, out parent);
        Assert.AreEqual(noise.Evaluate(p, 20), own);
        Assert.AreEqual(noise.Evaluate(p, 10), parent);
        Assert.That(noise.Evaluate(p, 0), Is.EqualTo(noise.Evaluate(p, -1)));
        Assert.AreEqual(0, noise.Evaluate(p, double.NaN));
        Assert.AreEqual(0, noise.Evaluate(p, double.PositiveInfinity));
        // A centimetre shift at Earth-sized coordinates is not quantised away by float3.
        Assert.AreNotEqual(noise.Evaluate(p, 0.005), noise.Evaluate(p + new double3(0.01, 0, 0), 0.005));
        Assert.That(math.abs(noise.Evaluate(p, 0.005)), Is.LessThanOrEqualTo(noise.AmplitudeBound));
    }
}
