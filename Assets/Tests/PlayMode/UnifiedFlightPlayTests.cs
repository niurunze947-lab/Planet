using System.Collections;
using NUnit.Framework;
using Planet.Interaction;
using Planet.Procedural.Terrain;
using Planet.Solar;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class UnifiedFlightPlayTests
{
    [UnityTest]
    public IEnumerator NavigationFramesStreamingAndCollisionRemainIndependent()
    {
        yield return SceneManager.LoadSceneAsync("SolarSystem");
        SolarSystemSimulation sim = SolarSystemSimulation.Instance;
        Assert.IsNotNull(sim);
        sim.paused = true;
        var flight = sim.ViewCamera.GetComponent<UnifiedFlightController>();
        if (flight == null) flight = sim.ViewCamera.gameObject.AddComponent<UnifiedFlightController>();
        flight.handleInput = false;
        yield return null;
        yield return null;
        Assert.AreEqual(1, Object.FindObjectsOfType<UnifiedFlightController>().Length);
        Assert.IsTrue(sim.HasObserver);

        // Selected target survives free flight; no camera is attached to that selection.
        double3 deepSpace = new double3(1e13, 2e13, 3e13);
        Quaternion initialLook = Quaternion.Euler(13, 57, 21);
        flight.SetPose(deepSpace, initialLook, new double3(1000, 0, 0));
        int selected = sim.FocusIndex;
        yield return null;
        Assert.IsFalse(flight.InNearSurface);
        Assert.AreEqual(-1, flight.NavigationTargetIndex);
        Assert.AreEqual(selected, sim.FocusIndex);
        Assert.Greater(flight.PositionWorldMetres.x, deepSpace.x);
        Assert.Less(Quaternion.Angle(initialLook, flight.transform.rotation), 0.001);
        foreach (var terrain in Object.FindObjectsOfType<RuntimeTerrainRenderer>())
            Assert.AreEqual(0, terrain.ResidentCount);

        flight.BeginFocus(selected);
        Assert.IsTrue(flight.IsAutoFocusing);
        flight.CancelNavigation();
        Assert.IsFalse(flight.IsAutoFocusing);
        flight.BeginFocus(selected);
        float deadline = Time.realtimeSinceStartup + 10;
        while (flight.IsAutoFocusing && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.IsFalse(flight.IsAutoFocusing);
        Assert.IsFalse(flight.InNearSurface);
        Assert.AreEqual(0, math.length(flight.VelocityWorldMetresPerSecond), 0.001);

        CelestialBody body = sim.FocusView;
        var surface = body.GetComponent<RuntimeTerrainRenderer>();
        Assert.IsNotNull(surface);
        double radius = body.Definition.RadiusKm * 1000;
        double3 direction = math.normalize(new double3(1, 0.15, 0.2));
        Vec3d center = sim.PositionWorldMetresAt(selected);
        Vec3d offset = SolarSystemSimulation.RotateMetres(body.transform.rotation,
            new Vec3d(direction.x * (radius + 100000), direction.y * (radius + 100000), direction.z * (radius + 100000)));
        flight.SetPose(new double3(center.x + offset.x, center.y + offset.y, center.z + offset.z),
            initialLook, new double3(0, 0, 1000));
        Assert.IsTrue(flight.InNearSurface);
        Assert.AreEqual(1000, math.length(flight.VelocityBodyFixedMetresPerSecond), 0.001);
        yield return null;
        Assert.Less(Quaternion.Angle(initialLook, flight.transform.rotation), 0.001);
        Assert.Greater(math.length(flight.VelocityBodyFixedMetresPerSecond), 0);
        Assert.Less(math.length(flight.VelocityBodyFixedMetresPerSecond), 1000);

        // Teleport is an explicit test spawn. Collision then acts through the normal frame loop.
        direction = math.normalize(flight.PositionBodyFixedMetres);
        double ground = surface.DisplayRadiusMetres(direction.x, direction.y, direction.z);
        offset = SolarSystemSimulation.RotateMetres(body.transform.rotation,
            new Vec3d(direction.x * (ground + 100), direction.y * (ground + 100), direction.z * (ground + 100)));
        flight.SetPose(new double3(center.x + offset.x, center.y + offset.y, center.z + offset.z),
            initialLook, double3.zero);
        deadline = Time.realtimeSinceStartup + 180;
        while ((!surface.CollisionCoverageReady || surface.PendingWork > 0 || surface.FinestVisibleLevel < 12) &&
            Time.realtimeSinceStartup < deadline) yield return null;
        Assert.IsTrue(surface.CollisionCoverageReady);
        Assert.Greater(surface.CollisionChunkCount, 0);
        Assert.GreaterOrEqual(surface.FinestVisibleLevel, 12);
        Assert.IsNull(surface.LastError);
        direction = math.normalize(flight.PositionBodyFixedMetres);
        ground = surface.SampleCollisionRadiusMetres(direction);
        Assert.IsTrue(surface.SweepSphere(direction * (ground + 10), direction * (ground - 10), 0.5,
            out double3 contact, out double3 normal));
        Assert.GreaterOrEqual(math.length(contact) - ground, 0.49);
        Assert.Less(math.length(contact) - ground, 2.0);

        offset = SolarSystemSimulation.RotateMetres(body.transform.rotation,
            new Vec3d(direction.x * (ground + 2), direction.y * (ground + 2), direction.z * (ground + 2)));
        Vec3d inward = SolarSystemSimulation.RotateMetres(body.transform.rotation,
            new Vec3d(-direction.x * 100, -direction.y * 100, -direction.z * 100));
        flight.SetPose(new double3(center.x + offset.x, center.y + offset.y, center.z + offset.z),
            initialLook, new double3(inward.x, inward.y, inward.z));
        deadline = Time.realtimeSinceStartup + 180;
        while ((surface.FinestVisibleLevel < 24 || surface.PendingWork > 0 ||
            math.length(flight.VelocityBodyFixedMetresPerSecond) > 0.01) &&
            Time.realtimeSinceStartup < deadline) yield return null;
        Assert.GreaterOrEqual(surface.FinestVisibleLevel, 24);
        Assert.IsTrue(surface.CollisionCoverageReady);
        Assert.GreaterOrEqual(SurfaceRoamHudState.PlayerToSurfaceDistanceMetres, 0.49);
        Assert.Less(SurfaceRoamHudState.PlayerToSurfaceDistanceMetres, 2);
        Assert.LessOrEqual(surface.ResidentCount, surface.maxResidentChunks);
        Vec3d outwardWorld = SolarSystemSimulation.RotateMetres(body.transform.rotation,
            new Vec3d(direction.x, direction.y, direction.z));
        Vector3 upWorld = new Vector3((float)outwardWorld.x, (float)outwardWorld.y, (float)outwardWorld.z);
        Vector3 tangentWorld = Vector3.Cross(upWorld, Vector3.up).normalized;
        flight.SetPose(flight.PositionWorldMetres, Quaternion.LookRotation(tangentWorld - upWorld * 0.6f, upWorld),
            double3.zero);
        yield return null;
        ScreenCapture.CaptureScreenshot("Logs/unified-flight-verified-contact.png");
        yield return null;

        // Rotation and observer rebasing must not move a stationary body-fixed explorer.
        double3 beforeSpin = flight.PositionBodyFixedMetres;
        sim.julianDate += 0.1;
        yield return null;
        Assert.Less(math.distance(beforeSpin, flight.PositionBodyFixedMetres), 0.001);
        Assert.Less(flight.transform.position.magnitude, 1e-6f);
        Assert.IsNotNull(sim.ViewCamera.GetComponent<ObserverRenderRig>().LocalCamera);

        flight.SetPose(deepSpace, initialLook, double3.zero);
        yield return null;
        yield return null;
        Assert.IsFalse(flight.InNearSurface);
        Assert.AreEqual(0, surface.CollisionChunkCount);
        Assert.AreEqual(0, surface.ResidentCount);
        Assert.Less(Quaternion.Angle(initialLook, flight.transform.rotation), 0.001);
    }
}
