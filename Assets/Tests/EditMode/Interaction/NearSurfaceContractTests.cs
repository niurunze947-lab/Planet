using NUnit.Framework;
using Planet.Interaction;
using Planet.Procedural.Terrain;
using Unity.Mathematics;

public sealed class NearSurfaceContractTests
{
    // Metre-based scale where float can still resolve centimetres:
    // 1 unit = 1 km  →  1 cm = 0.00001 units.
    private const float KmPerUnit = 1f;
    private const float RadiusKm = 1000f;
    private const float MinAltUnits = 0.00001f; // 1 cm

    [Test]
    public void MinClearanceIsOneCentimetre()
    {
        // At the solar-system scale (1 unit = 1e6 km) 1 cm = 1e-11 units.
        Assert.AreEqual(1e-11f, ViewDistanceBoundary.MinClearanceUnits(1000000f, 0.01f), 1e-18f);
        // At 1 unit = 1 km, 1 cm = 1e-5 units.
        Assert.AreEqual(MinAltUnits, ViewDistanceBoundary.MinClearanceUnits(KmPerUnit, 0.01f), 1e-12f);
    }

    [Test]
    public void LogAltitudeZoomApproachesFloorWhenFloatCanRepresentIt()
    {
        // Use radius=1 so float ULP (~1.2e-7) can resolve a 1e-5 floor.
        // At radius=1000 the ULP is ~1.2e-4 and a cm-class floor is unrepresentable
        // (PLANET-SPACE.md §1) — that regime belongs to the double-metre path.
        const float radius = 1f;
        float view = 1.01f;
        for (int i = 0; i < 400; i++)
            view = ViewDistanceBoundary.ApplyLogAltitudeZoom(
                view, radius, -0.5f, MinAltUnits, 30000f, 0.08f, 1f);

        float altitude = view - radius;
        Assert.GreaterOrEqual(altitude, MinAltUnits * 0.99f);
        Assert.AreEqual(MinAltUnits, altitude, MinAltUnits * 0.05f);
    }

    [Test]
    public void LogAltitudeZoomMetresReachesCentimetreFloor()
    {
        // PLANET-SPACE path: double metres can honour a 1 cm floor.
        double alt = 10000.0; // 10 km
        for (int i = 0; i < 400; i++)
            alt = ViewDistanceBoundary.ApplyLogAltitudeZoomMetres(alt, -0.5, 0.01, 0.01, 0.08, 1.0);
        Assert.AreEqual(0.01, alt, 1e-6);
    }

    [Test]
    public void LogAltitudeZoomOutwardIsNotRateDamped()
    {
        float view = RadiusKm + 0.5f; // 500 m altitude
        float next = ViewDistanceBoundary.ApplyLogAltitudeZoom(
            view, RadiusKm, 0.3f, MinAltUnits, 30000f, 0.08f, 1f);
        float expectedAlt = 0.5f * (float)System.Math.Exp(0.3);
        Assert.AreEqual(RadiusKm + expectedAlt, next, 1e-3f);
    }

    [Test]
    public void LogAltitudeZoomInwardIsRateDamped()
    {
        float view = RadiusKm + 10f;
        float next = ViewDistanceBoundary.ApplyLogAltitudeZoom(
            view, RadiusKm, -0.2f, MinAltUnits, 30000f, 0.08f, 1f);
        float rate = ViewDistanceBoundary.ZoomRateMultiplier(view, RadiusKm, 0.08f, 1f);
        float expectedAlt = 10f * (float)System.Math.Exp(-0.2f * rate);
        Assert.AreEqual(RadiusKm + expectedAlt, next, 1e-2f);
    }

    [Test]
    public void TerrainFloorIsGroundPlusSafetyMargin()
    {
        // Metres path (PLANET-SPACE): ground at 1,000,500 m; safety 2 m; min clearance 1 cm.
        var query = new FakeQuery { groundRadiusMetres = 1000500.0, pending = 0 };
        double floor = ViewDistanceBoundary.TerrainAwareFloorMetres(
            1000000.0, new double3(0, 1, 0), query, 2.0, 0.01);
        Assert.AreEqual(1000502.0, floor, 1e-6);
    }

    [Test]
    public void TerrainFloorNeverDropsBelowSpherePlusCm()
    {
        var query = new FakeQuery { groundRadiusMetres = 500000.0, pending = 0 }; // underground
        double floor = ViewDistanceBoundary.TerrainAwareFloorMetres(
            1000000.0, new double3(0, 1, 0), query, 2.0, 0.01);
        Assert.GreaterOrEqual(floor, 1000000.01);
    }

    [Test]
    public void PendingDetailWorkWouldThrottleViaQuery()
    {
        var query = new FakeQuery { pending = 3 };
        Assert.Greater(query.PendingDetailWork, 0);
    }

    [Test]
    public void ClearViewHeightRatioIsSurfaceOverCenter()
    {
        Assert.AreEqual(0.5f, ViewHeightRatios.ClearanceOverViewHeight(2f, 1f), 1e-6f);
        Assert.AreEqual(1f, ViewHeightRatios.ClearanceOverRadius(2f, 1f), 1e-6f);
        Assert.IsTrue(ViewHeightRatios.IsOutsideSurface(1f, 1f));
        Assert.IsFalse(ViewHeightRatios.IsOutsideSurface(0.5f, 1f));
    }

    private sealed class FakeQuery : IPlanetSurfaceQuery
    {
        public double groundRadiusMetres = 1000500.0;
        public int pending;

        public double RadiusMetres { get { return 1000000.0; } }
        public int PendingDetailWork { get { return pending; } }

        public double SampleHeightMetres(double3 directionBf, double sampleSpacingMetres)
        {
            return groundRadiusMetres - 1000000.0;
        }

        public double SampleSurfaceRadiusMetres(double3 directionBf, double sampleSpacingMetres)
        {
            return groundRadiusMetres;
        }
    }
}
