using System;
using System.Collections.Generic;
using NUnit.Framework;
using Planet.Procedural;
using Unity.Mathematics;
using UnityEngine;

public sealed class PlanetAppearanceTests
{
    private PlanetDefinition definition;

    [SetUp]
    public void SetUp()
    {
        definition = ScriptableObject.CreateInstance<PlanetDefinition>();
        definition.features.hasEcology = false;
        definition.features.hasOcean = false;
        definition.features.hasClouds = false;
    }

    [TearDown]
    public void TearDown() { UnityEngine.Object.DestroyImmediate(definition); }

    [TestCase(PlanetSurfaceKind.Terrestrial)]
    [TestCase(PlanetSurfaceKind.CrateredRock)]
    [TestCase(PlanetSurfaceKind.DustyRock)]
    [TestCase(PlanetSurfaceKind.SulfurVolcanic)]
    [TestCase(PlanetSurfaceKind.FracturedIce)]
    [TestCase(PlanetSurfaceKind.GasGiant)]
    [TestCase(PlanetSurfaceKind.IceGiant)]
    [TestCase(PlanetSurfaceKind.OpaqueHaze)]
    [TestCase(PlanetSurfaceKind.StellarPhotosphere)]
    public void EverySurfaceKindProducesFiniteDeterministicSamplesIncludingPoles(PlanetSurfaceKind kind)
    {
        definition.appearance.surfaceKind = kind;
        definition.appearance.fractureStrength = 0.7f;
        PlanetSurfaceSampler sampler = definition.CreateSampler();
        for (int i = 0; i < 64; i++)
        {
            float3 direction = i < 2 ? new float3(0, i == 0 ? 1 : -1, 0) :
                math.normalize(new float3(math.cos(i * 2.3999f), i / 32f - 1f, math.sin(i * 2.3999f)));
            PlanetSurfaceSample a = sampler.Evaluate(direction);
            PlanetSurfaceSample b = sampler.Evaluate(direction);
            Assert.IsTrue(math.all(math.isfinite(a.normal)));
            Assert.IsTrue(math.all(math.isfinite(a.albedoLinear)));
            Assert.IsTrue(math.isfinite(a.heightMetres));
            Assert.AreEqual(a.heightMetres, b.heightMetres);
            Assert.AreEqual(a.albedoLinear, b.albedoLinear);
            Assert.That(math.length(a.normal), Is.EqualTo(1f).Within(0.00001f));
            if (kind != PlanetSurfaceKind.Terrestrial)
                Assert.AreEqual(a.albedoLinear, sampler.EvaluateAlbedoOnly(direction));
        }
    }

    [TestCase(PlanetSurfaceKind.CrateredRock)]
    [TestCase(PlanetSurfaceKind.FracturedIce)]
    [TestCase(PlanetSurfaceKind.GasGiant)]
    [TestCase(PlanetSurfaceKind.StellarPhotosphere)]
    public void CubeFaceEdgesShareIdenticalPositionsNormalsAndColors(PlanetSurfaceKind kind)
    {
        definition.appearance.surfaceKind = kind;
        definition.appearance.fractureStrength = 0.85f;
        PlanetSurfaceSampler sampler = definition.CreateSampler();
        PlanetMeshData mesh = CubeSphere.Generate(sampler, 12);
        var firstByDirection = new Dictionary<float3, int>();
        int repeated = 0;
        for (int i = 0; i < mesh.positions.Length; i++)
        {
            float3 direction = mesh.directions[i];
            int previous;
            if (!firstByDirection.TryGetValue(direction, out previous)) { firstByDirection.Add(direction, i); continue; }
            repeated++;
            Assert.AreEqual(mesh.positions[previous], mesh.positions[i]);
            Assert.AreEqual(mesh.normals[previous], mesh.normals[i]);
            Assert.AreEqual(sampler.EvaluateAlbedoOnly(mesh.directions[previous]), sampler.EvaluateAlbedoOnly(direction));
        }
        Assert.That(repeated, Is.GreaterThan(100));
    }

    [TestCase(PlanetSurfaceKind.GasGiant)]
    [TestCase(PlanetSurfaceKind.IceGiant)]
    [TestCase(PlanetSurfaceKind.OpaqueHaze)]
    [TestCase(PlanetSurfaceKind.StellarPhotosphere)]
    public void OpticalLayersHaveNoInventedGroundRelief(PlanetSurfaceKind kind)
    {
        definition.appearance.surfaceKind = kind;
        definition.appearance.reliefMetres = 90000;
        definition.appearance.craterStrength = 1;
        definition.appearance.opticalSurfaceHeightMetres = 70000;
        definition.physical.flattening = 0;
        PlanetSurfaceSampler sampler = definition.CreateSampler();
        float3 direction = math.normalize(new float3(1, 2, 3));
        Assert.AreEqual(0, sampler.EvaluateHeight(direction));
        Assert.That(math.length(sampler.SurfacePosition(direction, 0)),
            Is.EqualTo(1f + 70000f / (float)definition.physical.radiusMetres).Within(0.000001f));
    }

    [Test]
    public void TriaxialShapeRetainsAllThreeAxesAndItsEllipsoidNormal()
    {
        definition.appearance.surfaceKind = PlanetSurfaceKind.OpaqueHaze;
        definition.physical.shapeScale = new Vector3(1.346f, 0.688f, 1.077f);
        definition.physical.flattening = 0;
        PlanetSurfaceSampler sampler = definition.CreateSampler();
        Assert.That(sampler.SurfacePosition(new float3(1, 0, 0), 0).x, Is.EqualTo(1.346f).Within(0.000001f));
        Assert.That(sampler.SurfacePosition(new float3(0, 1, 0), 0).y, Is.EqualTo(0.688f).Within(0.000001f));
        Assert.That(sampler.SurfacePosition(new float3(0, 0, 1), 0).z, Is.EqualTo(1.077f).Within(0.000001f));
        float3 d = math.normalize(new float3(1, 1, 1));
        float3 expected = math.normalize(d / new float3(1.346f * 1.346f, 0.688f * 0.688f, 1.077f * 1.077f));
        Assert.That(math.distance(sampler.Evaluate(d).normal, expected), Is.LessThan(0.000001f));
    }

    [Test]
    public void AppearanceAndShapeMutationsInvalidateDerivedCaches()
    {
        string original = definition.ContentFingerprint();
        definition.appearance.bandCount += 1;
        string appearance = definition.ContentFingerprint();
        Assert.AreNotEqual(original, appearance);
        definition.physical.shapeScale = new Vector3(1.2f, 1f, 1f);
        Assert.AreNotEqual(appearance, definition.ContentFingerprint());
    }

    [Test]
    public void NorthernPolarDepositDoesNotCreateASouthernDeposit()
    {
        definition.appearance.surfaceKind = PlanetSurfaceKind.FracturedIce;
        definition.appearance.featureLongitudeLatitude = new Vector2(0f, 90f);
        definition.appearance.featureAngularSize = new Vector2(30f, 30f);
        definition.appearance.featureColor = Color.red;
        definition.appearance.featureStrength = 1;
        float3 north = definition.CreateSampler().EvaluateAlbedoOnly(new float3(0, 1, 0));
        float3 south = definition.CreateSampler().EvaluateAlbedoOnly(new float3(0, -1, 0));
        Assert.AreEqual(new float3(1, 0, 0), north);
        Assert.That(south.y, Is.GreaterThan(0.01f));
    }

    [Test]
    public void LightingOnlyChangesDoNotMoveTheCraterField()
    {
        definition.appearance.surfaceKind = PlanetSurfaceKind.CrateredRock;
        float3 direction = math.normalize(new float3(3, 4, 5));
        float before = definition.CreateSampler().EvaluateHeight(direction);
        definition.appearance.baseColor = Color.cyan;
        definition.appearance.emissionStrength = 2;
        definition.appearance.atmosphereDensity = 0.8f;
        Assert.AreEqual(before, definition.CreateSampler().EvaluateHeight(direction));
    }

    [TestCase(PlanetSurfaceKind.GasGiant, PlanetBiome.CloudTop)]
    [TestCase(PlanetSurfaceKind.IceGiant, PlanetBiome.CloudTop)]
    [TestCase(PlanetSurfaceKind.OpaqueHaze, PlanetBiome.Haze)]
    [TestCase(PlanetSurfaceKind.StellarPhotosphere, PlanetBiome.Photosphere)]
    [TestCase(PlanetSurfaceKind.FracturedIce, PlanetBiome.Ice)]
    [TestCase(PlanetSurfaceKind.SulfurVolcanic, PlanetBiome.Volcanic)]
    public void NonTerrestrialSamplesDescribeTheCorrectVisibleLayer(PlanetSurfaceKind kind, PlanetBiome biome)
    {
        definition.appearance.surfaceKind = kind;
        Assert.AreEqual(biome, definition.CreateSampler().Evaluate(new float3(1, 0, 0)).biome);
    }
}
