using System.Collections;
using NUnit.Framework;
using Planet.Interaction;
using Planet.Procedural.Terrain;
using Planet.Solar;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class UnifiedFlightPlayTests
{
    [UnityTest]
    public IEnumerator DelayedCollisionCookingDoesNotFreezeLowAltitudeFlight()
    {
        yield return SceneManager.LoadSceneAsync("SolarSystem");
        var sim = SolarSystemSimulation.Instance;
        sim.paused = true;
        var flight = sim.ViewCamera.GetComponent<UnifiedFlightController>();
        if (flight == null) flight = sim.ViewCamera.gameObject.AddComponent<UnifiedFlightController>();
        flight.handleInput = false;
        yield return null;
        var terrain = sim.FocusView.GetComponent<RuntimeTerrainRenderer>();
        float oldBudget = terrain.uploadBudgetMilliseconds;
        try
        {
            terrain.SetStreamingRequested(false);
            terrain.uploadBudgetMilliseconds = 0; // Deliberately delay cooking; height fallback remains available.
            double3 d = math.normalize(new double3(1, 0.15, 0.2));
            double3 p = terrain.Source.Position(d, 0.005) + d * 700;
            Vec3d rotated = SolarSystemSimulation.RotateMetres(sim.FocusView.transform.rotation, new Vec3d(p.x, p.y, p.z));
            Vec3d center = sim.PositionWorldMetresAt(sim.FocusIndex);
            double3 outward = math.normalize(new double3(rotated.x, rotated.y, rotated.z));
            Quaternion look = Quaternion.LookRotation((Vector3)(float3)(-outward),
                Vector3.Cross((Vector3)(float3)outward, Vector3.right).normalized);
            flight.SetPose(new double3(center.x + rotated.x, center.y + rotated.y, center.z + rotated.z), look, double3.zero);
            yield return null;
            double3 start = flight.PositionBodyFixedMetres;
            for (int i = 0; i < 20; i++)
            {
                flight.ApplyNavigationScroll(-120);
                yield return new WaitForSeconds(0.1f);
            }
            Assert.IsFalse(terrain.CollisionCoverageReady, "The test must actually exercise missing colliders.");
            Assert.Greater(terrain.FallbackSweepCount, 0);
            Assert.Greater(math.length(start) - math.length(flight.PositionBodyFixedMetres), 20,
                "Missing collision data must not fabricate a wall hundreds of metres above ground.");
            Assert.GreaterOrEqual(SurfaceRoamHudState.PlayerToSurfaceDistanceMetres, 0.49);
        }
        finally
        {
            terrain.uploadBudgetMilliseconds = oldBudget;
            flight.SetPose(new double3(1e13, 2e13, 3e13), Quaternion.identity, double3.zero);
        }
    }

    [UnityTest]
    public IEnumerator OceanIsSeparateAndObserverCanExploreBelowSeaLevel()
    {
        yield return SceneManager.LoadSceneAsync("SolarSystem");
        var sim = SolarSystemSimulation.Instance;
        sim.paused = true;
        var flight = sim.ViewCamera.GetComponent<UnifiedFlightController>();
        if (flight == null) flight = sim.ViewCamera.gameObject.AddComponent<UnifiedFlightController>();
        flight.handleInput = false;
        yield return null;
        var earth = sim.FocusView;
        var surface = earth.GetComponent<Planet.Procedural.PlanetSurfaceView>();
        Assert.IsNotNull(surface.OceanTransform);
        Assert.AreNotSame(surface.SurfaceTransform, surface.OceanTransform);
        Assert.IsNull(surface.OceanTransform.GetComponent<Collider>());
        var terrain = surface.TerrainRenderer;
        var source = terrain.Source;
        double3 direction = new double3(1, 0, 0);
        bool wet = false;
        for (int i = 0; i < 128; i++)
        {
            direction = math.normalize(new double3(math.cos(i), i / 64.0 - 1, math.sin(i)));
            if (source.Height(direction, 1) < source.macro.seaLevelMetres - 1000) { wet = true; break; }
        }
        Assert.IsTrue(wet);
        double3 below = source.Position(direction, 0.005) + direction * 500;
        Vec3d rotated = SolarSystemSimulation.RotateMetres(earth.transform.rotation, new Vec3d(below.x, below.y, below.z));
        Vec3d center = sim.PositionWorldMetresAt(sim.FocusIndex);
        flight.SetPose(new double3(center.x + rotated.x, center.y + rotated.y, center.z + rotated.z), Quaternion.identity, double3.zero);
        yield return null;
        Assert.IsTrue(flight.InNearSurface);
        Assert.Less(math.length(flight.PositionBodyFixedMetres), math.length(source.OceanPosition(direction)) - 400);
        float deadline = Time.realtimeSinceStartup + 120;
        bool renderedWater = false;
        while (!renderedWater && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            foreach (var renderer in surface.SurfaceTransform.GetComponentsInChildren<MeshRenderer>())
                if (renderer.enabled && renderer.sharedMaterial == surface.OceanMaterial) renderedWater = true;
        }
        Assert.IsTrue(renderedWater, "The active quadtree must stream a separate water surface.");
        Assert.IsFalse(surface.OceanTransform.GetComponent<MeshRenderer>().enabled, "Far ocean hands over with terrain coverage.");
        Assert.Greater(Shader.GetGlobalVector("_PlanetAtmosphereParams").x, 0);
        flight.SetPose(new double3(1e13, 2e13, 3e13), Quaternion.identity, double3.zero);
        yield return null;
        Assert.AreEqual(Vector4.zero, Shader.GetGlobalVector("_PlanetAtmosphereParams"));
    }

    [UnityTest]
    public IEnumerator NumpadControlsMultiplierWhileWheelAndTimeKeysStayIndependent()
    {
        yield return SceneManager.LoadSceneAsync("SolarSystem");
        var sim = SolarSystemSimulation.Instance;
        sim.paused = true;
        var flight = sim.ViewCamera.GetComponent<UnifiedFlightController>();
        if (flight == null) flight = sim.ViewCamera.gameObject.AddComponent<UnifiedFlightController>();
        yield return null;
        var keyboard = InputSystem.AddDevice<Keyboard>();
        var mouse = InputSystem.AddDevice<Mouse>();
        float timeRate = sim.daysPerSecond;
        try
        {
            flight.handleInput = true;
            flight.SetPose(new double3(1e13, 2e13, 3e13), Quaternion.identity, double3.zero);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.NumpadPlus));
            yield return null;
            Assert.AreEqual(2, flight.SpeedMultiplier);
            Assert.AreEqual(timeRate, sim.daysPerSecond);
            yield return null;
            Assert.AreEqual(2, flight.SpeedMultiplier, "Holding + must not double every frame.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.NumpadMinus));
            yield return null;
            Assert.AreEqual(1, flight.SpeedMultiplier);
            Assert.AreEqual(timeRate, sim.daysPerSecond);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            sim.daysPerSecond = 2;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Minus));
            yield return null;
            Assert.AreEqual(1, sim.daysPerSecond);
            Assert.AreEqual(1, flight.SpeedMultiplier);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Equals));
            yield return null;
            Assert.AreEqual(2, sim.daysPerSecond);
            Assert.AreEqual(1, flight.SpeedMultiplier);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());

            // Wheel still moves without a navigation target, including while right-look is held.
            double3 start = flight.PositionWorldMetres;
            InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, -120) }.WithButton(MouseButton.Right));
            yield return null;
            Assert.AreEqual(1, flight.SpeedMultiplier);
            Assert.AreEqual(-1, flight.NavigationTargetIndex);
            Assert.Greater(flight.PositionWorldMetres.z, start.z);
            double3 velocity = flight.VelocityWorldMetresPerSecond;
            flight.AdjustSpeedMultiplier(1);
            Assert.AreEqual(velocity, flight.VelocityWorldMetresPerSecond, "Multiplier changes preserve inertia.");
            flight.AdjustSpeedMultiplier(100);
            Assert.AreEqual(UnifiedFlightController.MaxSpeedMultiplier, flight.SpeedMultiplier);
            flight.AdjustSpeedMultiplier(-100);
            Assert.AreEqual(UnifiedFlightController.MinSpeedMultiplier, flight.SpeedMultiplier);
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return null;
            Assert.AreEqual(flight.SpeedMultiplier, SurfaceRoamHudState.SpeedMultiplier);
        }
        finally
        {
            flight.handleInput = false;
            flight.SetSpeedMultiplier(1);
            sim.daysPerSecond = timeRate;
            InputSystem.RemoveDevice(mouse);
            InputSystem.RemoveDevice(keyboard);
        }
    }

    [UnityTest]
    public IEnumerator FocusApproachOrbitAndWheelUseContinuousDistancesAndSpeeds()
    {
        yield return SceneManager.LoadSceneAsync("SolarSystem");
        var sim = SolarSystemSimulation.Instance;
        sim.paused = true;
        var flight = sim.ViewCamera.GetComponent<UnifiedFlightController>();
        if (flight == null) flight = sim.ViewCamera.gameObject.AddComponent<UnifiedFlightController>();
        flight.handleInput = false;
        yield return null;
        int earthIndex = sim.FocusIndex;
        double radius = sim.FocusView.Definition.RadiusKm * 1000;
        Vec3d center = sim.PositionWorldMetresAt(earthIndex);
        double3 centerWorld = new double3(center.x, center.y, center.z);
        // A trip across several orders of magnitude must expose the last approach over many frames.
        flight.SetPose(centerWorld + new double3(0, 0, -1.5e11), Quaternion.identity, double3.zero);
        flight.BeginFocus(earthIndex);
        double previousRange = math.distance(flight.PositionWorldMetres, centerWorld);
        int visibleApproachFrames = 0;
        float deadline = Time.realtimeSinceStartup + 20;
        while (flight.IsAutoFocusing && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            double range = math.distance(flight.PositionWorldMetres, centerWorld);
            double dt = Mathf.Min(Time.deltaTime, 0.1f);
            Assert.LessOrEqual(System.Math.Abs(System.Math.Log(range / previousRange)),
                flight.focusLogRate * dt + 1e-5, "No last-frame distance jump.");
            if (range < radius * 30 && flight.IsAutoFocusing) visibleApproachFrames++;
            previousRange = range;
        }
        Assert.IsFalse(flight.IsAutoFocusing);
        Assert.Greater(visibleApproachFrames, 8);

        double3 beforeOrbit = flight.PositionWorldMetres - centerWorld;
        flight.OrbitAroundTarget(new Vector2(90, 25), 0.25f);
        double3 afterOrbit = flight.PositionWorldMetres - centerWorld;
        Assert.AreEqual(math.length(beforeOrbit), math.length(afterOrbit), 0.01);
        Assert.Greater(math.distance(beforeOrbit, afterOrbit), 1000);
        yield return null;
        double3 inward = math.normalize(centerWorld - flight.PositionWorldMetres);
        Assert.Greater(Vector3.Dot(flight.transform.forward, (Vector3)(float3)inward), 0.9999f);
        flight.CancelNavigation();
        double3 noTargetPosition = flight.PositionWorldMetres;
        flight.OrbitAroundTarget(new Vector2(90, 25), 0.25f);
        Assert.AreEqual(noTargetPosition, flight.PositionWorldMetres);

        // Orbit can interrupt a new focus without snapping to its endpoint.
        flight.BeginFocus(earthIndex);
        flight.OrbitAroundTarget(new Vector2(5, 0), 0.02f);
        Assert.IsFalse(flight.IsAutoFocusing);
        Assert.AreEqual(earthIndex, flight.NavigationTargetIndex);

        // Repeated large wheel input at the entry band must already obey the surface envelope.
        double threshold = radius / (1 - ViewDistanceBoundary.DefaultSurfaceModeEnterRatio);
        flight.SetPose(centerWorld + new double3(0, 0, -threshold - 1500), Quaternion.identity, double3.zero);
        flight.BeginFocus(earthIndex);
        flight.OrbitAroundTarget(new Vector2(0.01f, 0), 0.02f);
        double speedBeforeEntry = 0;
        double previousSpeed = 0;
        deadline = Time.realtimeSinceStartup + 15;
        while (!flight.InNearSurface && Time.realtimeSinceStartup < deadline)
        {
            double cap = flight.MovementSpeedLimitMetresPerSecond;
            flight.ApplyNavigationScroll(-12000);
            yield return null;
            double speed = math.length(flight.VelocityWorldMetresPerSecond);
            Assert.LessOrEqual(speed, cap * 1.03 + 1);
            if (flight.InNearSurface) speedBeforeEntry = previousSpeed;
            previousSpeed = speed;
        }
        Assert.IsTrue(flight.InNearSurface);
        double entrySpeed = math.length(flight.VelocityBodyFixedMetresPerSecond);
        Assert.Greater(entrySpeed, 0);
        Assert.Less(System.Math.Abs(entrySpeed - speedBeforeEntry), entrySpeed * 0.5 + 1);
        yield return null;
        Assert.Greater(math.length(flight.VelocityBodyFixedMetresPerSecond), 0);
        Assert.Less(math.length(flight.VelocityBodyFixedMetresPerSecond),
            flight.MovementSpeedLimitMetresPerSecond * 1.1 + entrySpeed);
        flight.SetPose(centerWorld + new double3(0, 0, -1.5e11), Quaternion.identity, double3.zero);
        yield return null;
    }

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
        Assert.LessOrEqual(surface.ResidentCount, surface.ResidentBudget);
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

    [UnityTest]
    public IEnumerator GravityFallsWithoutInputAndCanBeDisabledWithoutVelocityReset()
    {
        yield return SceneManager.LoadSceneAsync("SolarSystem");
        var sim = SolarSystemSimulation.Instance;
        sim.paused = true;
        var flight = sim.ViewCamera.GetComponent<UnifiedFlightController>();
        if (flight == null) flight = sim.ViewCamera.gameObject.AddComponent<UnifiedFlightController>();
        flight.handleInput = false;
        yield return null;
        Vec3d c = sim.PositionWorldMetresAt(sim.FocusIndex);
        double3 center = new double3(c.x, c.y, c.z);
        double radius = sim.FocusView.Definition.RadiusKm * 1000;
        flight.SetPose(center + new double3(0, 0, -(radius + 100000)), Quaternion.identity, double3.zero);
        double startRange = math.distance(flight.PositionWorldMetres, center);
        double elapsed = 0;
        flight.SetSpeedMultiplier(4);
        flight.SetGravityEnabled(true);
        while (elapsed < 3)
        {
            yield return null;
            elapsed += Mathf.Min(Time.deltaTime, 0.1f);
        }
        double range = math.distance(flight.PositionWorldMetres, center);
        Assert.AreEqual(9.52 * elapsed, math.length(flight.VelocityBodyFixedMetresPerSecond), 0.6);
        Assert.AreEqual(4.76 * elapsed * elapsed, startRange - range, 2);
        double3 before = flight.VelocityBodyFixedMetresPerSecond;
        flight.SetGravityEnabled(false);
        Assert.AreEqual(before, flight.VelocityBodyFixedMetresPerSecond);
        yield return null;
        Assert.Less(math.length(flight.VelocityBodyFixedMetresPerSecond), math.length(before));
        flight.SetSpeedMultiplier(1);
        flight.SetPose(center + new double3(0, 0, -1.5e11), Quaternion.identity, double3.zero);
    }

    [UnityTest]
    public IEnumerator NearWheelStillAdvancesAndGravityReachesCollisionSurface()
    {
        yield return SceneManager.LoadSceneAsync("SolarSystem");
        var sim = SolarSystemSimulation.Instance;
        sim.paused = true;
        var flight = sim.ViewCamera.GetComponent<UnifiedFlightController>();
        if (flight == null) flight = sim.ViewCamera.gameObject.AddComponent<UnifiedFlightController>();
        flight.handleInput = false;
        yield return null;
        var terrain = sim.FocusView.GetComponent<RuntimeTerrainRenderer>();
        Vec3d c = sim.PositionWorldMetresAt(sim.FocusIndex);
        double3 center = new double3(c.x, c.y, c.z);
        double3 d = math.normalize(new double3(1, 0.15, 0.2));
        double ground = terrain.DisplayRadiusMetres(d.x, d.y, d.z);
        Vec3d offset = SolarSystemSimulation.RotateMetres(sim.FocusView.transform.rotation,
            new Vec3d(d.x * (ground + 1000), d.y * (ground + 1000), d.z * (ground + 1000)));
        flight.SetPose(center + new double3(offset.x, offset.y, offset.z), Quaternion.identity, double3.zero);
        flight.BeginFocus(sim.FocusIndex);
        flight.OrbitAroundTarget(new Vector2(0.001f, 0), 0.02f);
        yield return null;
        float deadline = Time.realtimeSinceStartup + 240;
        while ((!terrain.CollisionCoverageReady || terrain.FinestVisibleLevel < 16) && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.IsTrue(terrain.CollisionCoverageReady);
        double startHeight = SurfaceRoamHudState.PlayerToSurfaceDistanceMetres;
        double3 wheelStart = flight.PositionBodyFixedMetres;
        for (int i = 0; i < 20; i++)
        {
            flight.ApplyNavigationScroll(-120);
            yield return new WaitForSeconds(0.15f);
        }
        Assert.Less(SurfaceRoamHudState.PlayerToSurfaceDistanceMetres, startHeight - 10,
            "Near scroll must advance. Displacement=" + math.distance(wheelStart, flight.PositionBodyFixedMetres) +
            " near=" + flight.InNearSurface + " target=" + flight.NavigationTargetIndex +
            " cap=" + flight.MovementSpeedLimitMetresPerSecond + " ready=" + terrain.CollisionCoverageReady);

        // Gravity must reach the displayed mesh without being cancelled by input damping.
        flight.CancelNavigation();
        flight.SetGravityEnabled(true);
        deadline = Time.realtimeSinceStartup + 240;
        while ((SurfaceRoamHudState.PlayerToSurfaceDistanceMetres > 2 || !terrain.CollisionCoverageReady) &&
            Time.realtimeSinceStartup < deadline) yield return null;
        Assert.IsTrue(terrain.CollisionCoverageReady);
        Assert.GreaterOrEqual(SurfaceRoamHudState.PlayerToSurfaceDistanceMetres, 0.49);
        Assert.Less(SurfaceRoamHudState.PlayerToSurfaceDistanceMetres, 2);
        double contactSeconds = 0;
        while (contactSeconds < 1)
        {
            yield return null;
            contactSeconds += Mathf.Min(Time.deltaTime, 0.1f);
            Assert.GreaterOrEqual(SurfaceRoamHudState.PlayerToSurfaceDistanceMetres, 0.49,
                "Gravity may slide along the slope but must not enter the solid surface.");
        }
        Assert.IsTrue(math.all(math.isfinite(flight.VelocityBodyFixedMetresPerSecond)),
            "Contact must leave a finite velocity while allowing tangent sliding.");
        Assert.LessOrEqual(terrain.ResidentCount, terrain.ResidentBudget);
        ScreenCapture.CaptureScreenshot("Logs/gravity-neighbourhood-contact.png");
        yield return null;
        flight.SetGravityEnabled(false);
        flight.SetPose(center + new double3(0, 0, -1.5e11), Quaternion.identity, double3.zero);
    }
}
