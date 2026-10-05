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
        float ratio = SurfaceHeightKm / CenterHeightKm;
        Assert.AreEqual(ViewDistanceBoundary.DefaultSurfaceModeEnterRatio, ratio, 1e-6f);
        Assert.AreEqual(0.01524f, ratio, 1e-4f);
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

        // Spec point (100/6562) → enter.
        float viewAtSpec = 1f;
        float radiusAtSpec = 1f - enter; // surface/center = enter
        Assert.IsTrue(ViewDistanceBoundary.EvaluateSurfaceMode(viewAtSpec, radiusAtSpec, false));

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
    public void HysteresisDoesNotChatterAtTheEnterThreshold()
    {
        const float enter = ViewDistanceBoundary.DefaultSurfaceModeEnterRatio;
        float view = 1f;
        float radius = 1f - enter;

        bool state = ViewDistanceBoundary.EvaluateSurfaceMode(view, radius, false);
        Assert.IsTrue(state);
        // Oscillate slightly around the enter ratio while already inside.
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
        // On a true sphere radius 6371 the same ratio gives altitude ≈ 98.6 km.
        Assert.AreEqual(98.65f, expected, 0.2f);

        var go = new GameObject("mode");
        try
        {
            var ctrl = go.AddComponent<SurfaceModeController>();
            Assert.AreEqual(expected, ctrl.ComputeCeilingAltitude(EarthRadiusKm), 1e-3f);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void NearSurfaceZoomRateIsDampedInward()
    {
        float view = 6562f;
        float radius = 6371f;
        float rate = ViewDistanceBoundary.ZoomRateMultiplier(view, radius, 0.08f, 1f);
        // In the Earth calibration band the rate sits just above the near floor.
        Assert.AreEqual(0.08f, rate, 0.01f);
    }
}
