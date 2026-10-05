using NUnit.Framework;
using Planet.Interaction;

/// <summary>
/// Surface free-roam clip planes at true solar-system scale. Regression: a fixed 0.05-unit
/// near plane is 50,000 km when kmPerUnit=1e6 and hid the whole Earth.
/// </summary>
public sealed class SurfaceCameraClipTests
{
    private const float KmPerUnit = 1000000f; // 1 unit = 1e6 km = 1e9 m
    private const double EarthRadiusMetres = 6371000.0;

    [Test]
    public void NearPlaneNeverSwallowsTheEarth()
    {
        // The bug: near = 0.05 units = 50,000 km > Earth radius.
        float fixedNearUnits = 0.05f;
        double fixedNearMetres = SurfaceLocalSpace.SceneUnitsToMetres(fixedNearUnits, KmPerUnit);
        Assert.Greater(fixedNearMetres, EarthRadiusMetres);

        // From 335 km (surface-mode ceiling on Earth) the near plane is 1% of altitude.
        float near = SurfaceCameraClip.NearMetres(335000.0);
        Assert.AreEqual(3350.0, near, 1.0);
        Assert.Less(near, 335000.0);

        // Convert to scene units: must be far smaller than the planet.
        double nearUnits = SurfaceLocalSpace.MetresToSceneUnits(near, KmPerUnit);
        double earthUnits = SurfaceLocalSpace.MetresToSceneUnits(EarthRadiusMetres, KmPerUnit);
        Assert.Less(nearUnits, earthUnits * 0.1);
    }

    [Test]
    public void NearPlaneFallsBackToCentimetresOnTheGround()
    {
        Assert.AreEqual(0.05f, SurfaceCameraClip.NearMetres(0.0), 1e-5);
        Assert.AreEqual(0.05f, SurfaceCameraClip.NearMetres(1.7), 1e-5);
        Assert.AreEqual(50f, SurfaceCameraClip.NearMetres(5000.0), 1f);
    }

    [Test]
    public void FarPlaneAlwaysFitsTheGlobe()
    {
        float far = SurfaceCameraClip.FarMetres(335000.0, EarthRadiusMetres);
        Assert.GreaterOrEqual(far, EarthRadiusMetres * SurfaceCameraClip.FarRadiusFraction);
        // From the surface the horizon is ~0.05 R, so R*20 is plenty of sky.
        float groundFar = SurfaceCameraClip.FarMetres(0.0, EarthRadiusMetres);
        Assert.GreaterOrEqual(groundFar, EarthRadiusMetres * 2.0);
    }

    [Test]
    public void NearIsAlwaysBelowFarInSceneUnits()
    {
        foreach (double altitude in new[] { 0.0, 1.7, 1000.0, 335000.0, 2e6 })
        {
            float nearM = SurfaceCameraClip.NearMetres(altitude);
            float farM = SurfaceCameraClip.FarMetres(altitude, EarthRadiusMetres);
            double nearU = SurfaceLocalSpace.MetresToSceneUnits(nearM, KmPerUnit);
            double farU = SurfaceLocalSpace.MetresToSceneUnits(farM, KmPerUnit);
            Assert.Greater(farU, nearU * 100.0, "altitude=" + altitude);
        }
    }
}
