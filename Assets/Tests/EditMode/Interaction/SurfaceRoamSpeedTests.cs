using NUnit.Framework;
using Planet.Interaction;

/// <summary>
/// C6 altitude-bound free-roam speed: continuous linear curve, ground cap 100 m/s,
/// ceiling = near-surface marginal zoom speed, weak linear acceleration.
/// </summary>
public sealed class SurfaceRoamSpeedTests
{
    private const double EarthRadiusMetres = 6371000.0;

    [Test]
    public void GroundSpeedIsCappedAt100MetresPerSecond()
    {
        double hCeiling = SurfaceRoamSpeed.CeilingAltitudeMetres(EarthRadiusMetres);
        double vCeiling = SurfaceRoamSpeed.MarginalZoomSpeedMetresPerSecond(hCeiling);
        double vGround = SurfaceRoamSpeed.TargetSpeedMetresPerSecond(0.0, hCeiling, vCeiling);
        Assert.AreEqual(100.0, vGround, 1e-6);
        Assert.LessOrEqual(vGround, 100.0);
    }

    [Test]
    public void CeilingSpeedEqualsMarginalZoomSpeedAtEnterPercentage()
    {
        double enter = ViewDistanceBoundary.DefaultSurfaceModeEnterRatio;
        double hCeiling = SurfaceRoamSpeed.CeilingAltitudeMetres(EarthRadiusMetres, enter);
        double expected = SurfaceRoamSpeed.MarginalZoomSpeedMetresPerSecond(
            hCeiling, ViewDistanceBoundary.DefaultNearRate);

        Assert.AreEqual(expected, SurfaceRoamSpeed.CeilingSpeedForRadius(EarthRadiusMetres), 1e-6);
        Assert.AreEqual(expected,
            SurfaceRoamSpeed.TargetSpeedMetresPerSecond(hCeiling, hCeiling, expected), 1e-6);
    }

    [Test]
    public void SpeedIsLinearInAltitudeWithNoHardBands()
    {
        double hCeiling = SurfaceRoamSpeed.CeilingAltitudeMetres(EarthRadiusMetres);
        double vCeiling = SurfaceRoamSpeed.MarginalZoomSpeedMetresPerSecond(hCeiling);

        double v0 = SurfaceRoamSpeed.TargetSpeedMetresPerSecond(0.0, hCeiling, vCeiling);
        double vMid = SurfaceRoamSpeed.TargetSpeedMetresPerSecond(hCeiling * 0.5, hCeiling, vCeiling);
        double vTop = SurfaceRoamSpeed.TargetSpeedMetresPerSecond(hCeiling, hCeiling, vCeiling);

        // Exact midpoint of a linear curve (continuous linear interpolation).
        Assert.AreEqual((v0 + vTop) * 0.5, vMid, 1e-6);
        Assert.Greater(vMid, v0);
        Assert.Less(vMid, vTop);

        // Monotone and dense — no jump larger than a single linear step of the same span.
        double prev = SurfaceRoamSpeed.TargetSpeedMetresPerSecond(0.0, hCeiling, vCeiling);
        for (int i = 1; i <= 20; i++)
        {
            double h = hCeiling * (i / 20.0);
            double v = SurfaceRoamSpeed.TargetSpeedMetresPerSecond(h, hCeiling, vCeiling);
            Assert.GreaterOrEqual(v, prev - 1e-9);
            prev = v;
        }
    }

    [Test]
    public void SpeedClampsAboveTheCeilingAndBelowTheGround()
    {
        double hCeiling = 1000.0;
        double vCeiling = 500.0;
        Assert.AreEqual(500.0, SurfaceRoamSpeed.TargetSpeedMetresPerSecond(2000.0, hCeiling, vCeiling), 1e-6);
        Assert.AreEqual(100.0, SurfaceRoamSpeed.TargetSpeedMetresPerSecond(-5.0, hCeiling, vCeiling), 1e-6);
    }

    [Test]
    public void AccelerationIsLinearAndNeverSnaps()
    {
        // From rest to 100 m/s at 60 m/s² needs ~1.67 s; one 0.1 s step moves 6 m/s.
        double speed = 0.0;
        speed = SurfaceRoamSpeed.IntegrateSpeed(speed, 100.0, 60.0, 0.1);
        Assert.AreEqual(6.0, speed, 1e-6);

        // Saturates at the target, never overshoots.
        for (int i = 0; i < 50; i++)
            speed = SurfaceRoamSpeed.IntegrateSpeed(speed, 100.0, 60.0, 0.1);
        Assert.AreEqual(100.0, speed, 1e-6);

        // Deceleration is the same linear ramp.
        speed = SurfaceRoamSpeed.IntegrateSpeed(100.0, 0.0, 60.0, 0.1);
        Assert.AreEqual(94.0, speed, 1e-6);
    }

    [Test]
    public void VelocityIntegrationUsesLinearAccelCap()
    {
        double vx = 0, vy = 0, vz = 0;
        SurfaceRoamSpeed.IntegrateVelocity(ref vx, ref vy, ref vz, 10, 0, 0, 60, 0.1);
        Assert.AreEqual(6.0, vx, 1e-6);
        Assert.AreEqual(0.0, vy, 1e-6);

        // Direction change stays continuous: cap applies to the whole delta vector.
        vx = 10; vy = 0; vz = 0;
        SurfaceRoamSpeed.IntegrateVelocity(ref vx, ref vy, ref vz, 0, 0, 10, 60, 0.1);
        double mag = System.Math.Sqrt(vx * vx + vy * vy + vz * vz);
        Assert.Greater(mag, 0.0);
        Assert.Less(System.Math.Abs(vx - 10.0), 6.0 + 1e-6); // moved toward target, not snapped
    }

    [Test]
    public void CeilingAltitudeMatchesEnterBandFormula()
    {
        double enter = 0.05;
        double h = SurfaceRoamSpeed.CeilingAltitudeMetres(EarthRadiusMetres, enter);
        Assert.AreEqual(EarthRadiusMetres * (enter / (1.0 - enter)), h, 1.0);
        Assert.AreEqual(335315.8, h, 1.0);
    }

    [Test]
    public void MarginalZoomSpeedIsProportionalToAltitude()
    {
        double a = SurfaceRoamSpeed.MarginalZoomSpeedMetresPerSecond(1000.0, 0.08);
        double b = SurfaceRoamSpeed.MarginalZoomSpeedMetresPerSecond(2000.0, 0.08);
        Assert.AreEqual(2.0 * a, b, 1e-6);
        Assert.AreEqual(0.0, SurfaceRoamSpeed.MarginalZoomSpeedMetresPerSecond(0.0), 0.0);
    }
}
