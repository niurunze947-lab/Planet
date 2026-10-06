using NUnit.Framework;
using Planet.Interaction;
using Planet.Solar;
using Unity.Mathematics;
using UnityEngine;

public sealed class UnifiedFlightTests
{
    [TestCase(1.5e11, 2.9e7)]
    [TestCase(7.5e12, 2.9e7)]
    [TestCase(7e6, 2.9e7)]
    public void FocusBoundsApparentGrowthAndStopsSmoothly(double start, double end)
    {
        double duration = FlightMotion.FocusDuration(start, end);
        double previous = FlightMotion.FocusRange(start, end, 0, duration, out double initialSpeed);
        Assert.AreEqual(start, previous, start * 1e-12);
        Assert.AreEqual(0, initialSpeed);
        const double dt = 1.0 / 60;
        for (double elapsed = dt; elapsed < duration; elapsed += dt)
        {
            double distance = FlightMotion.FocusRange(start, end, elapsed / duration, duration, out double speed);
            Assert.LessOrEqual(System.Math.Abs(System.Math.Log(distance / previous)), 4 * dt + 1e-6);
            Assert.That(distance, Is.InRange(System.Math.Min(start, end), System.Math.Max(start, end)));
            Assert.LessOrEqual(System.Math.Abs(speed / distance), 4 + 1e-10);
            previous = distance;
        }
        Assert.AreEqual(end, FlightMotion.FocusRange(start, end, 1, duration, out double finalSpeed), end * 1e-12);
        Assert.AreEqual(0, finalSpeed);
        Assert.AreEqual(System.Math.Sqrt(start * end),
            FlightMotion.FocusRange(start, end, 0.5, duration, out _), System.Math.Sqrt(start * end) * 1e-12);
    }

    [Test]
    public void ApproachSpeedEnvelopeIsContinuousAcrossBothNearModeThresholds()
    {
        const double radius = 6371000;
        Assert.AreEqual(100, SurfaceRoamSpeed.FlightSpeedLimit(0, radius));
        foreach (double ratio in new[] { 0.05, 0.08 })
        {
            double height = radius * ratio / (1 - ratio);
            double below = SurfaceRoamSpeed.FlightSpeedLimit(height - 0.01, radius);
            double above = SurfaceRoamSpeed.FlightSpeedLimit(height + 0.01, radius);
            Assert.Less(above - below, 0.02);
            Assert.Greater(above, below);
        }
        Assert.Less(SurfaceRoamSpeed.FlightSpeedLimit(1000, radius),
            SurfaceRoamSpeed.FlightSpeedLimit(100000, radius));
    }

    [Test]
    public void GravityUsesRealMassInverseSquareAndPreservesAccelerationOverTime()
    {
        const double radius = 6371000, mass = 5.9722e24;
        double3 gravity = FlightMotion.GravityAcceleration(new double3(radius, 0, 0), mass);
        Assert.AreEqual(-9.8203, gravity.x, 0.001);
        Assert.AreEqual(gravity.x / 4, FlightMotion.GravityAcceleration(new double3(radius * 2, 0, 0), mass).x, 1e-10);
        Assert.AreEqual(double3.zero, FlightMotion.GravityAcceleration(new double3(radius, 0, 0), 0));
        double3 velocity = double3.zero, position = new double3(radius + 1000, 0, 0);
        const double dt = 0.01;
        for (int i = 0; i < 100; i++)
        {
            velocity += FlightMotion.GravityAcceleration(position, mass) * dt;
            position += velocity * dt;
        }
        Assert.AreEqual(-9.817, velocity.x, 0.01);
        Assert.AreEqual(4.91, radius + 1000 - position.x, 0.06);
    }

    [Test]
    public void WheelSizedImpulseIsLimitedBeforeIntegrationWithoutChangingItsDirection()
    {
        double3 impulse = math.normalize(new double3(-1, 2, -3)) * 4e6;
        double cap = SurfaceRoamSpeed.FlightSpeedLimit(350000, 6371000);
        double3 target = FlightMotion.LimitMagnitude(impulse, cap);
        Assert.AreEqual(cap, math.length(target), 1e-8);
        Assert.Less(math.distance(math.normalize(impulse), math.normalize(target)), 1e-12);
        double3 velocity = double3.zero;
        for (int i = 0; i < 300; i++)
        {
            velocity = FlightMotion.Accelerate(velocity, target, cap / 0.35, 1.0 / 60);
            Assert.LessOrEqual(math.length(velocity), cap + 1e-8);
        }
    }
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
