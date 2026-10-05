using NUnit.Framework;
using Planet.Interaction;
using UnityEngine;

public sealed class SurfaceModeTests
{
    // Earth calibration from the spec: center height 6562 km, surface height ~100 km.
    private const float EarthRadiusKm = 6371f;
    private const float CenterHeightKm = 6562f;
    private const float SurfaceHeightKm = 100f;

    [Test]
    public void EarthCalibrationProducesTheSpecEnterRatio()
    {
        // Widened band: 5% of centre distance (Earth ≈ 335 km). The original 100/6562 ≈ 1.524%
        // was too tight for play and is recorded in ViewDistanceBoundary's comment.
        Assert.AreEqual(0.05f, ViewDistanceBoundary.DefaultSurfaceModeEnterRatio, 1e-6f);
        Assert.AreEqual(0.08f, ViewDistanceBoundary.DefaultSurfaceModeExitRatio, 1e-6f);
        Assert.Greater(ViewDistanceBoundary.DefaultSurfaceModeExitRatio,
            ViewDistanceBoundary.DefaultSurfaceModeEnterRatio);
    }

    [Test]
    public void TwoHeightRatioIsSurfaceOverCenter()
    {
        // center 6562, radius 6371 → surface 191; ratio = 191/6562
        float view = CenterHeightKm;
        float radius = EarthRadiusKm;
        float ratio = ViewDistanceBoundary.SurfaceModeRatio(view, radius);
        Assert.AreEqual(191f / 6562f, ratio, 1e-5f);
    }

    [Test]
    public void SurfaceModeEntersAtSpecPointAndHoldsThroughHysteresis()
    {
        const float enter = ViewDistanceBoundary.DefaultSurfaceModeEnterRatio;
        const float exit = ViewDistanceBoundary.DefaultSurfaceModeExitRatio;

        // Ratio above the enter band → stay in system view.
        Assert.IsFalse(ViewDistanceBoundary.EvaluateSurfaceMode(1f, 1f - enter * 1.1f, false));

        // Clearly inside the band (half the enter ratio) → enter.
        // (Exactly on the float threshold is not a reliable assertion.)
        float viewInBand = 1f;
        float radiusInBand = 1f - enter * 0.5f;
        Assert.IsTrue(ViewDistanceBoundary.EvaluateSurfaceMode(viewInBand, radiusInBand, false));

        // Dead band: still in surface mode even though we are above enter.
        float midRatio = (enter + exit) * 0.5f;
        float viewMid = 1f;
        float radiusMid = 1f - midRatio;
        Assert.IsTrue(ViewDistanceBoundary.EvaluateSurfaceMode(viewMid, radiusMid, true));

        // Above exit ratio → leave.
        float viewHigh = 1f;
        float radiusHigh = 1f - (exit + 0.01f);
        Assert.IsFalse(ViewDistanceBoundary.EvaluateSurfaceMode(viewHigh, radiusHigh, true));
    }

    [Test]
    public void SurfaceModeNeverEntersFromInteriorOrNearCentre()
    {
        const float enter = ViewDistanceBoundary.DefaultSurfaceModeEnterRatio;
        const float radius = 6371f;

        // Deep interior / near centre: ratio clamps to 0, which must NOT open the gate.
        Assert.IsFalse(ViewDistanceBoundary.EvaluateSurfaceMode(radius * 0.1f, radius, false, enter, 0.03f));
        Assert.IsFalse(ViewDistanceBoundary.EvaluateSurfaceMode(radius * 0.99f, radius, false, enter, 0.03f));
        Assert.IsFalse(ViewDistanceBoundary.EvaluateSurfaceMode(1e-6f, radius, false, enter, 0.03f));
        Assert.IsFalse(ViewDistanceBoundary.IsOutsideSurface(radius * 0.5f, radius));

        // Approach from outside at the Earth calibration band does enter.
        // Use a point clearly inside the band so float rounding cannot sit on the threshold.
        float viewInBand = radius / (1f - enter * 0.5f);
        Assert.IsTrue(ViewDistanceBoundary.IsOutsideSurface(viewInBand, radius));
        Assert.Greater(viewInBand - radius, 0f);
        Assert.IsTrue(ViewDistanceBoundary.EvaluateSurfaceMode(viewInBand, radius, false, enter, 0.03f));

        // Exact calibration altitude (≈98.6 km on Earth R=6371) is still outside and in band.
        float viewAtSpec = radius / (1f - enter);
        Assert.Greater(viewAtSpec, radius);
        Assert.AreEqual(enter, ViewDistanceBoundary.SurfaceModeRatio(viewAtSpec, radius), 1e-5f);
    }

    [Test]
    public void FakeUnitRadiusWouldLockOutTheNearSurfaceGate()
    {
        // Regression for the "can never enter surface mode" report: if the focused body's
        // scale resolves to a fake 1-unit fallback, a true-scale camera (Earth ≈ 0.006371)
        // sits far *inside* that radius and EvaluateSurfaceMode keeps the gate shut forever.
        const float fakeRadius = 1f;
        const float earthCamera = 0.006371f;

        Assert.IsFalse(ViewDistanceBoundary.IsOutsideSurface(earthCamera, fakeRadius));
        Assert.IsFalse(ViewDistanceBoundary.EvaluateSurfaceMode(
            earthCamera, fakeRadius, false,
            ViewDistanceBoundary.DefaultSurfaceModeEnterRatio,
            ViewDistanceBoundary.DefaultSurfaceModeExitRatio));

        // With the real baked radius the same camera is outside and deep in the enter band.
        const float realRadius = 0.006371f;
        Assert.IsTrue(ViewDistanceBoundary.IsOutsideSurface(earthCamera, realRadius));
        Assert.IsTrue(ViewDistanceBoundary.EvaluateSurfaceMode(
            earthCamera, realRadius, false,
            ViewDistanceBoundary.DefaultSurfaceModeEnterRatio,
            ViewDistanceBoundary.DefaultSurfaceModeExitRatio));
    }

    [Test]
    public void ZeroRadiusDoesNotOpenTheGate()
    {
        // SurfaceModeController treats radius<=0 as "unknown scale" and refuses to gate.
        // Pin the underlying evaluation too: a zero radius makes every finite distance look
        // far away (ratio → 1) and must not enter.
        Assert.IsFalse(ViewDistanceBoundary.EvaluateSurfaceMode(
            1f, 0f, false,
            ViewDistanceBoundary.DefaultSurfaceModeEnterRatio,
            ViewDistanceBoundary.DefaultSurfaceModeExitRatio));
    }

    [Test]
    public void HysteresisDoesNotChatterAtTheEnterThreshold()
    {
        const float enter = ViewDistanceBoundary.DefaultSurfaceModeEnterRatio;
        float view = 1f;
        // Sit inside the band, not exactly on the float threshold.
        float radius = 1f - enter * 0.5f;

        bool state = ViewDistanceBoundary.EvaluateSurfaceMode(view, radius, false);
        Assert.IsTrue(state);
        // Oscillate slightly around this point while already inside.
        for (int i = 0; i < 8; i++)
        {
            float wobble = (i % 2 == 0) ? 1.001f : 0.999f;
            state = ViewDistanceBoundary.EvaluateSurfaceMode(view * wobble, radius, state);
            Assert.IsTrue(state, "surface mode must not chatter near the enter threshold");
        }
    }

    [Test]
    public void CeilingAltitudeMatchesEnterRatioOnEarth()
    {
        // altitude / (radius + altitude) = enter  ⇒  altitude = enter/(1-enter) * radius
        float enter = ViewDistanceBoundary.DefaultSurfaceModeEnterRatio;
        float expected = EarthRadiusKm * (enter / (1f - enter));
        // 5% of centre on Earth R=6371 → altitude ≈ 335 km.
        Assert.AreEqual(335.3f, expected, 1.0f);

        var go = new GameObject("mode");
        try
        {
            var ctrl = go.AddComponent<SurfaceModeController>();
            Assert.AreEqual(expected, ctrl.ComputeCeilingAltitude(EarthRadiusKm), 1e-2f);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void NearSurfaceZoomRateIsDampedInward()
    {
        // Inside the surface-mode band (ratio ≤ 1.524%), not the approach to it.
        // 50 km over Earth: 50 / 6421 ≈ 0.78% < 1.524%.
        float view = 6371f + 50f;
        float radius = 6371f;
        float rate = ViewDistanceBoundary.ZoomRateMultiplier(view, radius, 0.08f, 1f);
        Assert.Less(rate, 0.5f);
        Assert.GreaterOrEqual(rate, 0.08f);

        // On the ground the rate sits at the near floor.
        Assert.AreEqual(0.08f, ViewDistanceBoundary.ZoomRateMultiplier(radius, radius, 0.08f, 1f), 0.01f);

        // Just above the enter band the approach keeps the far rate so the gate is reachable.
        float justOutside = radius / (1f - ViewDistanceBoundary.DefaultSurfaceModeEnterRatio * 1.1f);
        Assert.AreEqual(1f, ViewDistanceBoundary.ZoomRateMultiplier(justOutside, radius, 0.08f, 1f), 0.01f);
    }
}
