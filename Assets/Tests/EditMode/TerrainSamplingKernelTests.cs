using NUnit.Framework;
using Planet.Procedural;
using Planet.Procedural.Terrain;
using Unity.Mathematics;
using UnityEngine;

public sealed class TerrainSamplingKernelTests
{
    // The macro field is already float and sampled on a fixed kilometre-scale lattice.
    // Burst and Mono math intrinsics need not be bit-identical; centimetre detail is tested separately.
    private const double MacroHeightToleranceMetres = 5;
    private const double MacroColorTolerance = 0.01;
    private const double MacroNormalTolerance = 0.002;
    [TestCase(PlanetSurfaceKind.Terrestrial)]
    [TestCase(PlanetSurfaceKind.CrateredRock)]
    [TestCase(PlanetSurfaceKind.DustyRock)]
    [TestCase(PlanetSurfaceKind.FracturedIce)]
    [TestCase(PlanetSurfaceKind.SulfurVolcanic)]
    [TestCase(PlanetSurfaceKind.GasGiant)]
    [TestCase(PlanetSurfaceKind.IceGiant)]
    [TestCase(PlanetSurfaceKind.OpaqueHaze)]
    public void CompiledMacroSamplingPreservesTheDefinitionAcrossSurfaceKinds(PlanetSurfaceKind kind)
    {
        var definition = ScriptableObject.CreateInstance<PlanetDefinition>();
        try
        {
            definition.appearance.surfaceKind = kind;
            PlanetSurfaceSampler sampler = definition.CreateSampler();
            for (int i = 0; i < 64; i++)
            {
                float3 direction = math.normalizesafe(new float3(math.cos(i * 2.399963f), i / 32f - 1,
                    math.sin(i * 2.399963f)));
                TerrainSamplingKernels.MacroHeight(sampler, direction, out float height);
                TerrainSamplingKernels.MacroAlbedo(sampler, direction, out float3 albedo);
                TerrainSamplingKernels.Surface(sampler, direction, out PlanetSurfaceSample sample);
                PlanetSurfaceSample expected = sampler.Evaluate(direction);
                Assert.AreEqual(sampler.EvaluateHeight(direction), height, MacroHeightToleranceMetres,
                    "Compiling the macro field must not invent different terrain.");
                Assert.Less(math.distance(sampler.EvaluateAlbedoOnly(direction), albedo), MacroColorTolerance);
                Assert.AreEqual(expected.heightMetres, sample.heightMetres, MacroHeightToleranceMetres);
                Assert.Less(math.distance(expected.normal, sample.normal), MacroNormalTolerance);
            }
        }
        finally { Object.DestroyImmediate(definition); }
    }

    [Test]
    public void MacroBackendErrorRemainsBoundedOverTheWholeTerrestrialSphere()
    {
        var definition = ScriptableObject.CreateInstance<PlanetDefinition>();
        try
        {
            PlanetSurfaceSampler sampler = definition.CreateSampler();
            for (int i = 0; i < 4096; i++)
            {
                float3 direction = math.normalizesafe(new float3(math.cos(i * 2.399963f), i / 2048f - 1,
                    math.sin(i * 2.399963f)));
                TerrainSamplingKernels.MacroHeight(sampler, direction, out float height);
                Assert.AreEqual(sampler.EvaluateHeight(direction), height, MacroHeightToleranceMetres);
            }
        }
        finally { Object.DestroyImmediate(definition); }
    }

    [TestCase(0.005)]
    [TestCase(1.0)]
    [TestCase(128.0)]
    [TestCase(8192.0)]
    public void CompiledDetailPreservesDoublePrecisionAndTheParentBand(double spacing)
    {
        var sampler = new PlanetDetailSampler(271828, 16, 0.55, 0.005);
        for (int i = 0; i < 128; i++)
        {
            double3 point = new double3(6371000 + i * 0.01, -5137291.125 + i * 0.02, 3314821.25);
            TerrainSamplingKernels.DetailBands(sampler, point, spacing, spacing * 2, out double own, out double parent);
            Assert.AreEqual(sampler.Evaluate(point, spacing), own, 1e-7);
            Assert.AreEqual(sampler.Evaluate(point, spacing * 2), parent, 1e-7);
        }
    }

    [TestCase(8)]
    [TestCase(14)]
    [TestCase(24)]
    public void EveryChildReconstructsItsParentsAlreadyMorphedBoundary(int level)
    {
        var definition = ScriptableObject.CreateInstance<PlanetDefinition>();
        try
        {
            definition.features.hasOcean = false;
            var source = new RuntimeTerrainSource(definition);
            for (int face = 0; face < 6; face++)
            {
                var parent = new TerrainNodeKey((PlanetCubeFace)face, level, (1 << level) / 3, (1 << level) / 5);
                for (int childIndex = 0; childIndex < 4; childIndex++)
                for (int edge = 0; edge < 4; edge++)
                for (int v = 0; v <= TerrainLattice.N; v += 2)
                {
                    TerrainNodeKey child = parent.Child(childIndex);
                    int x = edge < 2 ? edge * TerrainLattice.N : v;
                    int y = edge >= 2 ? (edge - 2) * TerrainLattice.N : v;
                    long gx = TerrainLattice.VertexLattice(child.x, child.level, x);
                    long gy = TerrainLattice.VertexLattice(child.y, child.level, y);
                    double3 direction = TerrainLattice.Direction(child.face, gx, gy);
                    double spacing = TerrainLattice.Spacing(source.radius, child.level);
                    double px = (x + (child.x & 1) * TerrainLattice.N) * 0.5;
                    double py = (y + (child.y & 1) * TerrainLattice.N) * 0.5;
                    Assert.AreEqual(source.NodeHeight(direction, parent, px, py, spacing * 2),
                        source.NodeHeight(direction, child, x, y, spacing), 1e-7);
                }
            }
        }
        finally { Object.DestroyImmediate(definition); }
    }
}
