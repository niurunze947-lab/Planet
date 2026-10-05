using NUnit.Framework;
using Planet.Interaction;

public sealed class ViewDistanceBoundaryTests
{
    private const float Radius = 1f;
    private const float Min = 1e-5f;
    private const float Max = 30000f;

    [Test]
    public void SurfaceClearanceIsZeroOnSurfaceAndPositiveOutside()
    {
        Assert.AreEqual(0f, ViewDistanceBoundary.SurfaceClearance(Radius, Radius), 1e-6f);
        Assert.AreEqual(0f, ViewDistanceBoundary.SurfaceClearance(Radius * 0.5f, Radius), 1e-6f);
        Assert.AreEqual(1f, ViewDistanceBoundary.SurfaceClearance(Radius * 2f, Radius), 1e-6f);
    }

    [Test]
    public void ClearanceOverViewHeightIsClearanceDividedByCenterDistance()
    {
        // view = 2R → clearance = R → 50% of view height
        Assert.AreEqual(0.5f, ViewDistanceBoundary.ClearanceOverViewHeight(Radius * 2f, Radius), 1e-5f);
        // on surface → 0%
        Assert.AreEqual(0f, ViewDistanceBoundary.ClearanceOverViewHeight(Radius, Radius), 1e-6f);
        // far away → approaches 1
        Assert.Greater(ViewDistanceBoundary.ClearanceOverViewHeight(Radius * 100f, Radius), 0.98f);
    }

    [Test]
    public void ZoomRateIsNearOnSurfaceAndFarAway()
    {
        float near = ViewDistanceBoundary.ZoomRateMultiplier(Radius, Radius, 0.08f, 1f);
        float far = ViewDistanceBoundary.ZoomRateMultiplier(Radius * 1000f, Radius, 0.08f, 1f);
        Assert.AreEqual(0.08f, near, 1e-4f);
        Assert.Greater(far, 0.99f);
    }

    [Test]
    public void ZoomRateIsContinuousAcrossTheTransition()
    {
        float previous = ViewDistanceBoundary.ZoomRateMultiplier(Radius, Radius, 0.08f, 1f);
        for (int i = 1; i <= 64; i++)
        {
            float view = Radius * (1f + i * 0.1f);
            float rate = ViewDistanceBoundary.ZoomRateMultiplier(view, Radius, 0.08f, 1f);
            Assert.GreaterOrEqual(rate, previous - 1e-5f);
            previous = rate;
        }
    }

    [Test]
    public void ClampToSurfaceAllowsRadiusButRejectsInterior()
    {
        Assert.AreEqual(Radius, ViewDistanceBoundary.ClampToSurface(Radius * 0.5f, Radius, Min, Max), 1e-6f);
        Assert.AreEqual(Radius, ViewDistanceBoundary.ClampToSurface(Radius, Radius, Min, Max), 1e-6f);
        Assert.AreEqual(Radius * 3f, ViewDistanceBoundary.ClampToSurface(Radius * 3f, Radius, Min, Max), 1e-6f);
    }

    [Test]
    public void ClampToSurfaceNeverDropsBelowMinDistance()
    {
        float clamped = ViewDistanceBoundary.ClampToSurface(0f, 0f, Min, Max);
        Assert.AreEqual(Min, clamped, 1e-8f);
    }

    [Test]
    public void ApplyLogZoomClampsToOneScrollDiveIntoPlanet()
    {
        // A huge zoom-in from just outside the surface must stop at the radius, not pierce it.
        float next = ViewDistanceBoundary.ApplyLogZoom(
            Radius * 1.01f, -20f, Radius, Min, Max, 0.08f, 1f);
        Assert.GreaterOrEqual(next, Radius - 1e-5f);
    }

    [Test]
    public void ApplyLogZoomDampsInwardButNotOutward()
    {
        float view = Radius * 1.2f;
        float inward = ViewDistanceBoundary.ApplyLogZoom(view, -0.5f, Radius, Min, Max, 0.08f, 1f);
        float outward = ViewDistanceBoundary.ApplyLogZoom(view, 0.5f, Radius, Min, Max, 0.08f, 1f);

        float undampedIn = view * UnityEngine.Mathf.Exp(-0.5f);
        Assert.Greater(inward, undampedIn + 1e-4f);
        Assert.AreEqual(view * UnityEngine.Mathf.Exp(0.5f), outward, 1e-3f);
    }

    [Test]
    public void ApplyLogZoomNearSurfaceMovesLessThanFarZoom()
    {
        const float logDelta = -0.2f;
        float nearStart = Radius * 1.05f;
        float farStart = Radius * 20f;

        float nearNext = ViewDistanceBoundary.ApplyLogZoom(nearStart, logDelta, Radius, Min, Max, 0.08f, 1f);
        float farNext = ViewDistanceBoundary.ApplyLogZoom(farStart, logDelta, Radius, Min, Max, 0.08f, 1f);

        float nearChange = nearStart - nearNext;
        float farChange = farStart - farNext;
        // Same log step: near the surface the absolute move is smaller (marginal effect).
        Assert.Less(nearChange, farChange);
    }
}
