using NUnit.Framework;
using Planet.Interaction;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// C6 control tests: Unity-editor style free-roam input mapping and the contract that
/// wheel zoom is replaced by fly-speed control while roaming.
/// </summary>
public sealed class SurfaceRoamInputTests
{
    [Test]
    public void LookOnlyAppliesWhileRightMouseIsHeld()
    {
        Vector2 delta = new Vector2(10f, 4f);

        Vector2 held = SurfaceRoamInput.LookDeltaDegrees(delta, true, 0.1f);
        Assert.AreEqual(1f, held.x, 1e-5f);
        Assert.AreEqual(0.4f, held.y, 1e-5f);

        Vector2 released = SurfaceRoamInput.LookDeltaDegrees(delta, false, 0.1f);
        Assert.AreEqual(0f, released.x);
        Assert.AreEqual(0f, released.y);
    }

    [Test]
    public void LookPitchMatchesOrbitDragConvention()
    {
        // Same convention as SolarSystemCamera left-drag: mouse up looks down (scene is dragged),
        // so a positive mouse Y lowers the pitch.
        Vector2 look = SurfaceRoamInput.LookDeltaDegrees(new Vector2(0f, 5f), true, 0.2f);
        float yaw = 0f, pitch = 0f;
        SurfaceRoamInput.ApplyLook(ref yaw, ref pitch, look);
        Assert.Less(pitch, 0f);
    }

    [Test]
    public void PitchIsClampedToFreeLookBand()
    {
        float yaw = 0f, pitch = 80f;
        SurfaceRoamInput.ApplyLook(ref yaw, ref pitch, new Vector2(0f, -100f));
        Assert.LessOrEqual(pitch, 89.9f);

        pitch = -80f;
        SurfaceRoamInput.ApplyLook(ref yaw, ref pitch, new Vector2(0f, 100f));
        Assert.GreaterOrEqual(pitch, -89.9f);
    }

    [Test]
    public void FlyAxesRequireRightMouse()
    {
        // Unity-editor gesture: bare QWEASD must not drift the player. Movement is gated on
        // the same button as look so the free-look direction and the fly direction cannot
        // disagree (C6: 右键 + QWEASD).
        AssertVec(SurfaceRoamInput.FlyLocalAxes(true, false, false, false, false, false, false), 0, 0, 0);
        AssertVec(SurfaceRoamInput.FlyLocalAxes(false, false, false, false, true, false, false), 0, 0, 0);
    }

    [Test]
    public void FlyAxesMatchUnityEditorLayout()
    {
        // W = forward (+z / north in the local tangent frame).
        AssertVec(SurfaceRoamInput.FlyLocalAxes(true, false, false, false, false, false, true), 0, 0, 1);
        // S = back.
        AssertVec(SurfaceRoamInput.FlyLocalAxes(false, false, true, false, false, false, true), 0, 0, -1);
        // D = right (+x / east); A = left.
        AssertVec(SurfaceRoamInput.FlyLocalAxes(false, false, false, true, false, false, true), 1, 0, 0);
        AssertVec(SurfaceRoamInput.FlyLocalAxes(false, true, false, false, false, false, true), -1, 0, 0);
        // Q = down, E = up.
        AssertVec(SurfaceRoamInput.FlyLocalAxes(false, false, false, false, true, false, true), 0, -1, 0);
        AssertVec(SurfaceRoamInput.FlyLocalAxes(false, false, false, false, false, true, true), 0, 1, 0);
    }

    private static void AssertVec(double3 actual, double x, double y, double z)
    {
        Assert.AreEqual(x, actual.x, 1e-12);
        Assert.AreEqual(y, actual.y, 1e-12);
        Assert.AreEqual(z, actual.z, 1e-12);
    }

    [Test]
    public void FlyIsCameraRelativeAroundYaw()
    {
        // Facing east (yaw = 90°): W should move along +x in the local frame.
        double3 axes = new double3(0, 0, 1); // W
        double3 moved = SurfaceRoamInput.IntegrateMove(
            double3.zero, axes, 90f, 1f, 1f);

        Assert.AreEqual(1.0, moved.x, 1e-6);
        Assert.AreEqual(0.0, moved.y, 1e-6);
        Assert.AreEqual(0.0, moved.z, 1e-6);
    }

    [Test]
    public void FlyDoesNotMoveWithoutInput()
    {
        double3 start = new double3(3.0, 1.0, -2.0);
        double3 end = SurfaceRoamInput.IntegrateMove(start, double3.zero, 45f, 10f, 0.1f);
        Assert.AreEqual(start.x, end.x, 1e-12);
        Assert.AreEqual(start.y, end.y, 1e-12);
        Assert.AreEqual(start.z, end.z, 1e-12);
    }

    [Test]
    public void HorizontalFlyKeepsUpComponentUnchanged()
    {
        // WASD is tangent-plane motion: the up coordinate must not drift toward the centre.
        var start = new double3(0.0, 10.0, 0.0);
        double3 axes = SurfaceRoamInput.FlyLocalAxes(true, false, false, true, false, false, true);
        double3 end = SurfaceRoamInput.IntegrateMove(start, axes, 0f, 5f, 0.2f);
        Assert.AreEqual(10.0, end.y, 1e-12);
    }

    [Test]
    public void VerticalFlyChangesUpComponent()
    {
        var start = new double3(0.0, 10.0, 0.0);
        double3 up = SurfaceRoamInput.FlyLocalAxes(false, false, false, false, false, true, true);
        // 0.25 s at 4 m/s is exactly 1 m — avoid float time-step dust in the assertion.
        double3 end = SurfaceRoamInput.IntegrateMove(start, up, 0f, 4f, 0.25f);
        Assert.AreEqual(11.0, end.y, 1e-9);
    }

    [Test]
    public void WheelAdjustsSpeedLevelNotViewHeight()
    {
        // One notch up, one notch down — clamped at the ends. This is the C6 contract that
        // replaces orbit zoom: the wheel is rebound to fly speed.
        Assert.AreEqual(1, SurfaceRoamInput.AdjustSpeedLevel(0, 1f));
        Assert.AreEqual(-1, SurfaceRoamInput.AdjustSpeedLevel(0, -1f));
        Assert.AreEqual(SurfaceRoamInput.MaxSpeedLevel,
            SurfaceRoamInput.AdjustSpeedLevel(SurfaceRoamInput.MaxSpeedLevel, 10f));
        Assert.AreEqual(SurfaceRoamInput.MinSpeedLevel,
            SurfaceRoamInput.AdjustSpeedLevel(SurfaceRoamInput.MinSpeedLevel, -10f));
    }

    [Test]
    public void SpeedLevelScalesByPowersOfTwo()
    {
        Assert.AreEqual(SurfaceRoamInput.DefaultMoveSpeedMetres,
            SurfaceRoamInput.SpeedForLevel(0), 1e-5f);
        Assert.AreEqual(SurfaceRoamInput.DefaultMoveSpeedMetres * 2f,
            SurfaceRoamInput.SpeedForLevel(1), 1e-5f);
        Assert.AreEqual(SurfaceRoamInput.DefaultMoveSpeedMetres * 0.5f,
            SurfaceRoamInput.SpeedForLevel(-1), 1e-5f);
    }

    [Test]
    public void NegativeDeltaTimeIsIgnored()
    {
        var start = new double3(1.0, 2.0, 3.0);
        double3 end = SurfaceRoamInput.IntegrateMove(start, new double3(1, 0, 0), 0f, 10f, 0f);
        Assert.AreEqual(start.x, end.x, 1e-12);
    }
}
