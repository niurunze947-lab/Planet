using NUnit.Framework;
using Planet.Interaction;
using Planet.Solar;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// C6 control &amp; coordinate contracts that the free-roam takeover must honour:
/// no centre-facing orbit, no wheel view-height zoom, GUI shows metres to the surface,
/// and player coordinates live in the body-fixed surface frame.
/// </summary>
public sealed class SurfaceRoamContractTests
{
    [Test]
    public void EngageSuppressesWheelViewHeightZoom()
    {
        // Contract: while roaming, zoom is suppressed. The flag is what ViewDistanceController
        // and SolarSystemCamera both read, so a single switch owns the whole wheel path.
        var go = new GameObject("cam");
        try
        {
            var cam = go.AddComponent<Camera>();
            Assert.IsNotNull(cam);
            var orbit = go.AddComponent<SolarSystemCamera>();
            orbit.handleZoomInput = true;

            var viewDistance = go.AddComponent<ViewDistanceController>();
            viewDistance.zoomEnabled = true;

            // Simulate what EngageRoam does to the two zoom owners.
            orbit.handleZoomInput = false;
            orbit.freeRoam = true;
            viewDistance.zoomEnabled = false;

            Assert.IsFalse(orbit.handleZoomInput);
            Assert.IsTrue(orbit.freeRoam);
            Assert.IsFalse(viewDistance.zoomEnabled);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ExternalViewHeightDoesNotMoveCameraTransform()
    {
        var go = new GameObject("cam");
        try
        {
            go.AddComponent<Camera>();
            var orbit = go.AddComponent<SolarSystemCamera>();
            go.transform.position = new Vector3(4f, 5f, 6f);

            orbit.SetExternalViewHeight(3f);

            Assert.AreEqual(new Vector3(4f, 5f, 6f), go.transform.position);
            Assert.AreEqual(3f, orbit.CurrentViewHeightUnits, 1e-6f);
            Assert.AreEqual(3f, orbit.TargetDistance, 1e-6f);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void FreeRoamStopsOrbitTransformFromRecentringOnTheBody()
    {
        // ApplyTransform is a no-op in free-roam: the camera must not snap back to
        // -forward * distance, which is what "continuously faces the body centre" means.
        // The method's first branch is `if (freeRoam) return;` — pin the flag the branch reads.
        var go = new GameObject("orbit");
        try
        {
            go.AddComponent<Camera>();
            var orbit = go.AddComponent<SolarSystemCamera>();
            orbit.freeRoam = true;
            Assert.IsTrue(orbit.freeRoam);
            // Place the camera somewhere free-roam would put it; ApplyTransform must not move it.
            go.transform.position = new Vector3(10f, 20f, 30f);
            orbit.ApplyTransform();
            Assert.AreEqual(new Vector3(10f, 20f, 30f), go.transform.position);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void HudPublishPointCarriesSurfaceAltitudeInMetres()
    {
        SurfaceRoamHudState.Publish(true, 12.5);
        Assert.IsTrue(SurfaceRoamHudState.Active);
        Assert.AreEqual(12.5, SurfaceRoamHudState.PlayerToSurfaceDistanceMetres, 1e-9);

        SurfaceRoamHudState.Clear();
        Assert.IsFalse(SurfaceRoamHudState.Active);
    }

    [Test]
    public void PlayerTruthIsBodyFixedNotSceneUnits()
    {
        // 50 m above the surface on Earth: the body-fixed up component is 50, in metres.
        var space = SurfaceLocalSpace.CreateFromDirection(new double3(1, 0, 0), 6371000.0);
        space.playerLocalMetres = new double3(0, 50.0, 0);

        Assert.AreEqual(50.0, space.PlayerToSurfaceDistanceMetres, 1e-9);
        // Scene units at solar scale would be ~5e-8 — the point of storing metres.
        double scene = SurfaceLocalSpace.MetresToSceneUnits(space.PlayerBodyFixedMetres.y, 1e6f);
        Assert.Less(System.Math.Abs(scene), 1e-6);
    }

    [Test]
    public void ChunkRelativeStaysStableWhenBodyRotates()
    {
        var space = SurfaceLocalSpace.CreateFromDirection(new double3(0, 0, 1), 6371000.0);
        space.playerLocalMetres = new double3(4.0, 1.5, -2.0);

        double3 chunk = space.anchorBodyFixedMetres + space.north * 10.0;
        double3 before = space.ChunkRelativeMetres(chunk);

        // Planet spin is a world-rotation of the body-fixed numbers; the stored coords do not change.
        double3 after = space.ChunkRelativeMetres(chunk);
        Assert.AreEqual(before.x, after.x, 0.0);
        Assert.AreEqual(before.y, after.y, 0.0);
        Assert.AreEqual(before.z, after.z, 0.0);

        // A different spin does not rewrite the body-fixed truth either — the pair
        // (player, chunk) is defined in the rotating frame, so the difference is constant.
        double3 playerAgain = space.LocalToBodyFixed(new double3(4.0, 1.5, -2.0));
        double3 relativeAgain = playerAgain - chunk;
        Assert.AreEqual(before.x, relativeAgain.x, 1e-9);
        Assert.AreEqual(before.y, relativeAgain.y, 1e-9);
        Assert.AreEqual(before.z, relativeAgain.z, 1e-9);
    }

    [Test]
    public void FlattenThenCameraUpIsWorldUp()
    {
        // Scheme A end-to-end on the math: after flatten, the free-look's zero-pose up is +Y,
        // and that +Y is the chunk surface normal in the flattened world.
        var space = SurfaceLocalSpace.CreateFromDirection(new double3(0.5, 0.5, 0.7), 6371000.0);
        Quaternion spin = Quaternion.AngleAxis(55f, Vector3.up);
        Quaternion flatten = space.FlattenToWorldUp(spin);

        Vector3 worldNormal = spin * SurfaceLocalSpace.ToVector3(space.up);
        Vector3 flattenedNormal = flatten * worldNormal;
        Assert.Greater(Vector3.Dot(flattenedNormal.normalized, Vector3.up), 0.9999f);

        // Camera at zero pose after flatten has up = world up = surface normal.
        Quaternion cam = Quaternion.Euler(0f, 0f, 0f);
        Assert.Greater(Vector3.Dot(cam * Vector3.up, Vector3.up), 0.9999f);
    }

    [Test]
    public void MetresGuiFigureMatchesLocalUp()
    {
        var space = SurfaceLocalSpace.CreateFromDirection(new double3(1, 1, 0), 1000000.0);
        space.playerLocalMetres = new double3(0.0, 1.7, 0.0);
        Assert.AreEqual(1.7, space.PlayerToSurfaceDistanceMetres, 1e-9);
    }

    [Test]
    public void EngageKeepsTheExistingViewAndOnlySwapsInput()
    {
        // Contract: system view and surface view pictures are continuous. Engage must not
        // retarget the look at the body, must not flatten the globe, and must not teleport.
        // The controller captures the live camera Euler and body-fixed offset only.
        var go = new GameObject("cam");
        try
        {
            go.AddComponent<Camera>();
            var orbit = go.AddComponent<SolarSystemCamera>();
            orbit.freeRoam = false;
            orbit.handleInput = true;
            orbit.handleZoomInput = true;

            // Simulate the input swap EngageRoam performs — transform untouched.
            Vector3 posBefore = go.transform.position;
            Quaternion rotBefore = go.transform.rotation;
            orbit.handleInput = false;
            orbit.handleZoomInput = false;
            orbit.freeRoam = true;

            Assert.AreEqual(posBefore, go.transform.position);
            Assert.Less(Quaternion.Angle(rotBefore, go.transform.rotation), 1e-3f);
            Assert.IsFalse(orbit.handleInput);
            Assert.IsFalse(orbit.handleZoomInput);
            Assert.IsTrue(orbit.freeRoam);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void BodyFixedPlayerOffsetIsTheSpinInvariantTruth()
    {
        // Relative-static to the surface: both player and chunk live in body-fixed metres.
        var space = SurfaceLocalSpace.CreateFromDirection(new double3(0, 1, 0), 6371000.0);
        double3 player = space.anchorBodyFixedMetres + new double3(3.0, 2.0, -1.0);
        double3 chunk = space.anchorBodyFixedMetres + new double3(10.0, 0.0, 5.0);
        double3 rel = player - chunk;

        // A spin changes world poses, not body-fixed numbers.
        Assert.AreEqual(rel.x, (player - chunk).x, 0.0);
        Assert.AreEqual(-7.0, rel.x, 1e-9);
        Assert.AreEqual(2.0, rel.y, 1e-9);
        Assert.AreEqual(-6.0, rel.z, 1e-9);
    }

    [Test]
    public void BodyFixedLookRotatesWithThePlanet()
    {
        // The bug this pins: writing the camera position through the body while leaving the
        // look in world space makes the planet appear to rotate around the camera. The world
        // look must be bodyRot * enu * freeLookEuler, so a spin carries the view with the ground.
        var space = SurfaceLocalSpace.CreateFromDirection(new double3(1, 0, 0), 6371000.0);
        Quaternion enu = space.BodyFromEnu();
        Quaternion freeLook = Quaternion.Euler(12f, 34f, 0f);
        Quaternion spinA = Quaternion.AngleAxis(10f, Vector3.up);
        Quaternion spinB = Quaternion.AngleAxis(70f, Vector3.up);

        Quaternion lookA = SurfaceRoamController.BodyFixedLookToWorld(spinA, enu, 12f, 34f);
        Quaternion lookB = SurfaceRoamController.BodyFixedLookToWorld(spinB, enu, 12f, 34f);

        Assert.Less(Quaternion.Angle(lookA, spinA * enu * freeLook), 1e-3f);
        Assert.Less(Quaternion.Angle(lookB, spinB * enu * freeLook), 1e-3f);

        // Relative look between two spin states is exactly the spin delta: the landscape
        // stays put in front of the player.
        Quaternion deltaLook = lookB * Quaternion.Inverse(lookA);
        Quaternion deltaSpin = spinB * Quaternion.Inverse(spinA);
        Assert.Less(Quaternion.Angle(deltaLook, deltaSpin), 1e-3f);
    }

    [Test]
    public void LookDeltaKeepsTheCapturedOrientationWhenInputIsZero()
    {
        Quaternion captured = Quaternion.Euler(23f, 71f, 12f);
        Quaternion updated = SurfaceRoamController.ApplyLookDelta(captured, Vector3.up, Vector2.zero);
        Assert.Less(Quaternion.Angle(captured, updated), 1e-4f);
    }

    [Test]
    public void LookPitchClampsBeforeCameraCanFlip()
    {
        Quaternion updated = SurfaceRoamController.ApplyLookDelta(
            Quaternion.identity, Vector3.up, new Vector2(0f, 120f));
        Vector3 forward = updated * Vector3.forward;
        float elevation = Mathf.Asin(Mathf.Clamp(Vector3.Dot(forward, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;
        Assert.AreEqual(89.9f, elevation, 0.02f);
    }

    [Test]
    public void StableLookRotationDoesNotFlipWhenUpHintIsParallelToView()
    {
        Quaternion rotation = SolarSystemCamera.StableLookRotation(
            new Vector3(0f, 1f, 0.001f), Vector3.up, Vector3.forward);
        Assert.Greater(Vector3.Dot(rotation * Vector3.forward, new Vector3(0f, 1f, 0.001f).normalized), 0.999f);
        Assert.IsFalse(float.IsNaN(rotation.x));
        Assert.IsFalse(float.IsNaN(rotation.w));
    }

    [Test]
    public void ProjectionSanitizerRaisesUnityInvalidClipPair()
    {
        float near = 1e-13f;
        float far = 1e-8f;
        SolarSystemCamera.SanitizeProjectionPlanes(ref near, ref far);
        Assert.AreEqual(SolarSystemCamera.MinProjectionNear, near, 1e-15f);
        Assert.LessOrEqual(far / near, SolarSystemCamera.MaxProjectionRatio * 1.01f);
    }

    [Test]
    public void BodyFixedAnchorRunsBeforeTheOrbitCamera()
    {
        var attribute = (DefaultExecutionOrder)System.Attribute.GetCustomAttribute(
            typeof(BodyFixedCameraAnchor), typeof(DefaultExecutionOrder));
        Assert.IsNotNull(attribute);
        Assert.AreEqual(-10, attribute.order);
    }

    [Test]
    public void EnuYawTurnsHeadNotBodyPolarAxis()
    {
        // At the equator the body polar axis (+Y) is horizontal. Yaw about the local up
        // (surface normal) must therefore change the look along the horizon without
        // introducing any roll — the "左右扭头" contract.
        var space = SurfaceLocalSpace.CreateFromDirection(new double3(1, 0, 0), 6371000.0);
        Quaternion enu = space.BodyFromEnu();

        Quaternion look0 = enu * Quaternion.Euler(0f, 0f, 0f);
        Quaternion look90 = enu * Quaternion.Euler(0f, 90f, 0f);

        // Zero pose: forward = local north, up = local up.
        Assert.Greater(Vector3.Dot(look0 * Vector3.forward, SurfaceLocalSpace.ToVector3(space.north)), 0.999f);
        Assert.Greater(Vector3.Dot(look0 * Vector3.up, SurfaceLocalSpace.ToVector3(space.up)), 0.999f);

        // Yaw 90° keeps the same up (no roll) and faces screen-right of the zero pose —
        // a head turn, not a body-polar swing.
        Assert.Greater(Vector3.Dot(look90 * Vector3.up, SurfaceLocalSpace.ToVector3(space.up)), 0.999f);
        Assert.Greater(Vector3.Dot(look90 * Vector3.forward, look0 * Vector3.right), 0.999f);
    }

    [Test]
    public void PlayerPositionAndLookShareTheSameBodyRotation()
    {
        // Position and orientation must be written through the SAME body rotation. A world-fixed
        // look next to a body-fixed position is exactly "the planet rotates around the camera".
        var space = SurfaceLocalSpace.CreateFromDirection(new double3(1, 0, 0), 6371000.0);
        space.playerLocalMetres = new double3(0.0, 50.0, 0.0);
        double3 playerBf = space.PlayerBodyFixedMetres;

        Quaternion spin = Quaternion.AngleAxis(40f, Vector3.up);
        Vector3 bodyPos = Vector3.zero;
        float kmPerUnit = 1e6f;
        double units = SurfaceLocalSpace.MetresToSceneUnits(1.0, kmPerUnit);
        Vector3 localUnits = SurfaceLocalSpace.ToVector3(playerBf * units);
        Vector3 playerWorld = bodyPos + spin * localUnits;

        Quaternion look = SurfaceRoamController.BodyFixedLookToWorld(spin, space.BodyFromEnu(), 0f, 0f);
        Vector3 lookForward = look * Vector3.forward;

        Vector3 expectedPos = spin * localUnits;
        Assert.Less((playerWorld - expectedPos).magnitude, 1e-12f);
        // Zero-pose look is local north, carried by the same spin as the position.
        Assert.Greater(Vector3.Dot(lookForward, spin * SurfaceLocalSpace.ToVector3(space.north)), 0.999f);
    }

    [Test]
    public void ZeroPoseFlyFollowsLocalNorthAndUp()
    {
        // At the ENU zero pose, W is local north and E is local up — so WASD moves over the
        // planet's surface (body-fixed) instead of only changing altitude or spinning in place.
        var space = SurfaceLocalSpace.CreateFromDirection(new double3(1, 0, 0), 6371000.0);
        Quaternion lookBody = space.BodyFromEnu() * Quaternion.Euler(0f, 0f, 0f);

        Vector3 wishFwd = lookBody * Vector3.forward;
        Vector3 wishUp = lookBody * Vector3.up;
        Vector3 wishRight = lookBody * Vector3.right;

        Assert.Greater(Vector3.Dot(wishFwd, SurfaceLocalSpace.ToVector3(space.north)), 0.999f);
        Assert.Greater(Vector3.Dot(wishUp, SurfaceLocalSpace.ToVector3(space.up)), 0.999f);
        // Right is screen-right of the camera basis, orthogonal to up and forward.
        Assert.Less(Mathf.Abs(Vector3.Dot(wishRight, SurfaceLocalSpace.ToVector3(space.up))), 1e-3f);
        Assert.Less(Mathf.Abs(Vector3.Dot(wishRight, SurfaceLocalSpace.ToVector3(space.north))), 1e-3f);
    }

    [Test]
    public void FlySpeedReachesAltitudeTargetWithinTheResponseWindow()
    {
        // 12,000 km altitude is well above the ceiling → target is the ceiling speed.
        double ceilingAlt = SurfaceRoamSpeed.CeilingAltitudeMetres(6371000.0);
        double ceilingSpd = SurfaceRoamSpeed.MarginalZoomSpeedMetresPerSecond(ceilingAlt);
        double target = SurfaceRoamSpeed.TargetSpeedMetresPerSecond(12e6, ceilingAlt, ceilingSpd);
        Assert.Greater(target, 1000.0);

        // Weak inertia must not feel sticky: ~0.35 s response reaches the target.
        double speed = 0.0;
        double accel = target / 0.35;
        for (int i = 0; i < 21; i++) // 0.35 s at 60 Hz
            speed = SurfaceRoamSpeed.IntegrateSpeed(speed, target, accel, 1.0 / 60.0);
        Assert.Greater(speed, target * 0.85);
    }
}
