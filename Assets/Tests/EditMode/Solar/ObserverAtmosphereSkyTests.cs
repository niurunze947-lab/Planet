using NUnit.Framework;
using Planet.Procedural;
using Planet.Procedural.Terrain;
using Planet.Solar;
using UnityEditor;
using Unity.Mathematics;
using UnityEngine;

public sealed class ObserverAtmosphereSkyTests
{
    [Test]
    public void AirColumnFadesAtAtmosphereTopAndDaylightTracksSolarElevation()
    {
        Assert.AreEqual(0, ObserverAtmosphereSky.AirColumn(80000, 80000));
        Assert.AreEqual(0, ObserverAtmosphereSky.AirColumn(81000, 80000));
        Assert.AreEqual(0, ObserverAtmosphereSky.AirColumn(0, 0));
        Assert.AreEqual(1, ObserverAtmosphereSky.AirColumn(0, 80000), 1e-12);
        double middle = ObserverAtmosphereSky.AirColumn(40000, 80000);
        Assert.That(middle, Is.InRange(0.01, 0.5));
        Assert.Less(ObserverAtmosphereSky.AirColumn(79000, 80000), middle);
        Assert.AreEqual(0, ObserverAtmosphereSky.Daylight(-0.2f));
        Assert.AreEqual(1, ObserverAtmosphereSky.Daylight(0.2f));
        Assert.That(ObserverAtmosphereSky.Daylight(0f), Is.InRange(0.1f, 0.9f));
    }

    [Test]
    public void EarthShowsPhysicalReliefAtOneToOneScale()
    {
        var asset = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(
            "Assets/Resources/Solar/ProceduralTerra.asset");
        Assert.IsNotNull(asset);
        Assert.AreEqual(1f, asset.generation.visualReliefScale);
        var source = new RuntimeTerrainSource(asset);
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        var sampler = asset.CreateSampler();
        const int resolution = 128;
        for (int face = 0; face < 6; face++)
        for (int y = 0; y <= resolution; y++)
        for (int x = 0; x <= resolution; x++)
        {
            var d = CubeSphere.Direction((PlanetCubeFace)face, new float2(2f * x / resolution - 1, 2f * y / resolution - 1));
            double h = sampler.EvaluateHeight(d);
            min = System.Math.Min(min, h); max = System.Math.Max(max, h);
        }
        Assert.That(max - min, Is.InRange(19500, 20500), "Earth's physical trough-to-peak range must be about 20 km.");
        double3 direction = math.normalize(new double3(1, 0.2, 0.3));
        double physicalHeight = source.Height(direction, 1);
        Assert.AreEqual(physicalHeight + asset.appearance.opticalSurfaceHeightMetres,
            source.DisplayDisplacement(physicalHeight), 1e-6);

        PlanetDefinition generated = SolarSystemAppearanceCatalog.CreateDefault(SolarSystemCatalog.Find("Earth"));
        try { Assert.AreEqual(1f, generated.generation.visualReliefScale); }
        finally { Object.DestroyImmediate(generated); }
    }
}
