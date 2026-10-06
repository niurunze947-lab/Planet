using NUnit.Framework;
using Planet.Interaction;
using Planet.Solar;
using Unity.Mathematics;
using UnityEngine;

public sealed class UnifiedFlightTests
{
    [Test]
    public void RotationRoundTripRetainsMillimetresAtPlanetaryRadius()
    {
        Quaternion spin = Quaternion.Euler(31, 123, 56);
        var position = new Vec3d(6371000.001, 15321.025, 400.55);
        Vec3d rotated = SolarSystemSimulation.RotateMetres(spin, position);
        Quaternion conjugate = new Quaternion(-spin.x, -spin.y, -spin.z, spin.w);
        Vec3d result = SolarSystemSimulation.RotateMetres(conjugate, rotated);
        Assert.Less((result - position).Length, 1e-6);
        Assert.AreEqual(position.Length, rotated.Length, 1e-6);
    }
    [Test]
    public void GateUsesActualPositionAndPreservesHysteresis()
    {
        const double radius = 6371000;
        Assert.IsFalse(FlightMotion.NearSurface(radius * 1.1, radius, false));
        Assert.IsTrue(FlightMotion.NearSurface(radius * 1.03, radius, false));
        Assert.IsTrue(FlightMotion.NearSurface(radius * 1.07, radius, true));
        Assert.IsFalse(FlightMotion.NearSurface(radius * 1.1, radius, true));
        Assert.IsTrue(FlightMotion.NearSurface(radius * 0.999, radius, true));
        Assert.IsFalse(FlightMotion.NearSurface(radius * 0.5, radius, false));
    }

    [Test]
    public void HandoffVelocityCanBrakeWithoutBeingReset()
    {
        double3 incoming = new double3(0, -6000, 0);
        double3 after = FlightMotion.Accelerate(incoming, double3.zero, 12000, 1.0 / 60);
        Assert.AreEqual(-5800, after.y, 1e-9);
        Assert.Greater(math.length(after), 0);
        Assert.AreEqual(incoming, FlightMotion.Accelerate(incoming, double3.zero, 12000, 0));
    }

    [Test]
    public void ContinuousSweepFindsContactEvenWhenBothEndpointsAreOutside()
    {
        Assert.IsTrue(FlightMotion.SphereEntry(new double3(-20, 0, 0), new double3(20, 0, 0), 10, out double t));
        Assert.AreEqual(0.25, t, 1e-12);
        Assert.IsFalse(FlightMotion.SphereEntry(new double3(-20, 11, 0), new double3(20, 11, 0), 10, out t));
        Assert.IsFalse(FlightMotion.SphereEntry(new double3(10, 0, 0), new double3(20, 0, 0), 10, out t));
    }

    [Test]
    public void SafeApproachSpeedMatchesStoppingDistance()
    {
        double speed = FlightMotion.SafeApproachSpeed(1000, 60);
        Assert.AreEqual(1000, speed * speed / 120, 1e-8);
        Assert.AreEqual(0, FlightMotion.SafeApproachSpeed(-1, 60));
    }

    [Test]
    public void PublishedObserverDistanceDoesNotApplyReferenceSphereFloorOrMovePose()
    {
        var go = new GameObject("observer test");
        try
        {
            var rig = go.AddComponent<SolarSystemCamera>();
            rig.externalObserver = true;
            rig.minDistance = 100;
            go.transform.SetPositionAndRotation(new Vector3(1, 2, 3), Quaternion.Euler(20, 30, 40));
            Quaternion look = go.transform.rotation;
            rig.SetExternalViewHeight(0.01f);
            rig.ApplyTransform();
            Assert.AreEqual(0.01f, rig.CurrentViewHeightUnits);
            Assert.AreEqual(new Vector3(1, 2, 3), go.transform.position);
            Assert.Less(Quaternion.Angle(look, go.transform.rotation), 0.001);
        }
        finally { Object.DestroyImmediate(go); }
    }
}
