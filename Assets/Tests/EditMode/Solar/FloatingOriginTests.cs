using System;
using NUnit.Framework;
using Planet.Solar;

/// <summary>
/// The floating origin, and the property that matters: <b>no steady-state error at any time scale</b>.
///
/// The failure this guards against is specific and was observed in this project. If the origin eases
/// towards its anchor by moving a fraction of the remaining distance each frame, then at high time
/// rates the target outruns the ease and the origin settles at a constant offset - 30,000 scene units
/// at 36,500 simulated days per second. The origin must therefore rigidly equal its anchor, and only
/// the residual from a deliberate anchor change may decay.
/// </summary>
public sealed class FloatingOriginTests
{
    private static SpaceFrame Frame()
    {
        var tree = new SpaceFrameTree();
        tree.CreateSolSystem(new Vec3d(0, 0, 0));
        tree.EnsureBodyFrame("Earth", new Vec3d(1.495978707e11, 0, 0));
        return tree.Stellar;
    }

    [Test]
    public void RigidFollowHasNoSteadyStateErrorAtAnyTimeRate()
    {
        // The anchor moves at a constant speed, which is exactly the case that exposes a proportional
        // controller. Run for long enough that any lag would have settled, and assert the origin is
        // on the anchor to the last bit.
        double[] speeds = { 1.0, 1e2, 1e4, 1e6, 1e8, 1e10 };
        const double frameSeconds = 1.0 / 60.0;

        foreach (double speed in speeds)
        {
            SpaceFrame frame = Frame();
            var origin = new FloatingOrigin { mode = OriginRebaseMode.RigidFollow, catchUpSeconds = 0.35 };
            origin.Reset(frame, new Vec3d(0, 0, 0));

            double anchorX = 0.0;
            for (int step = 0; step < 600; step++)
            {
                anchorX += speed * frameSeconds;
                origin.Update(frame, new Vec3d(anchorX, 0, 0), frameSeconds, false);
            }

            double error = origin.Current.positionInFrameMetres.x - anchorX;
            Assert.AreEqual(0.0, error, 0.0,
                            "the origin must sit exactly on its anchor at " + speed + " m/s");
        }
    }

    [Test]
    public void AnAnchorChangeIsTheOnlyThingThatLeavesAResidual()
    {
        SpaceFrame frame = Frame();
        var origin = new FloatingOrigin { mode = OriginRebaseMode.RigidFollow, catchUpSeconds = 0.35 };
        origin.Reset(frame, new Vec3d(0, 0, 0));

        // Once warmed up, a stationary anchor means the origin does not move at all.
        origin.Update(frame, new Vec3d(100, 0, 0), 1.0 / 60.0, false);
        int settles = 0;
        for (int i = 0; i < 200; i++)
        {
            origin.Update(frame, new Vec3d(100, 0, 0), 1.0 / 60.0, false);
            if (origin.Current.positionInFrameMetres.x == 100.0) settles++;
        }
        Assert.AreEqual(200, settles, "a still anchor must produce a bit-exact still origin");

        // A snap moves the origin immediately and leaves nothing behind.
        Assert.IsTrue(origin.Update(frame, new Vec3d(5000, 0, 0), 1.0 / 60.0, true));
        Assert.AreEqual(5000.0, origin.Current.positionInFrameMetres.x, 0.0);
        Assert.AreEqual(5000.0 - 100.0, origin.LastRebaseDeltaMetres, 1e-9);
        Assert.AreEqual(0.0, origin.PeakDriftMetres, 0.0);
    }

    [Test]
    public void ResidualDecaysGeometricallyAndNeverGrows()
    {
        SpaceFrame frame = Frame();
        var origin = new FloatingOrigin { mode = OriginRebaseMode.RigidFollow, catchUpSeconds = 0.35 };
        origin.Reset(frame, new Vec3d(0, 0, 0));

        // A deliberate jump of the anchor leaves a residual; it must shrink monotonically, and reach
        // exactly zero rather than merely getting small - otherwise the origin never truly converges.
        origin.Reanchor(frame, new Vec3d(1000, 0, 0));
        Assert.AreEqual(0.0, origin.Current.positionInFrameMetres.x, 0.0);

        var target = new Vec3d(1000, 0, 0);
        double previous = 1000.0;
        int frames = 0;
        while (previous > 0.0 && frames < 5000)
        {
            origin.Update(frame, target, 1.0 / 60.0, false);
            previous = (origin.Current.positionInFrameMetres - target).Length;
            Assert.IsTrue(previous <= 1000.0 * Math.Exp(-(frames + 1) * (1.0 / 60.0) / 0.35) + 1e-9,
                          "the residual must decay at least as fast as e^(-t/tau)");
            frames++;
        }

        Assert.AreEqual(0.0, previous, 0.0, "the residual must reach exactly zero");
        Assert.IsTrue(frames > 10 && frames < 5000, "convergence took " + frames + " frames");
    }

    [Test]
    public void ReanchorKeepsTheOriginStillAndEasesOntoTheNewTarget()
    {
        // The focus change: the anchor jumps from Earth to Neptune. The origin must NOT jump with it -
        // that would cut the view - and must NOT end up offset from it either. Seeding the residual
        // with the new anchor's offset achieves both: the origin is momentarily stationary, then
        // glides onto the new target and settles on it exactly.
        SpaceFrame frame = Frame();
        var origin = new FloatingOrigin { mode = OriginRebaseMode.RigidFollow, catchUpSeconds = 0.35 };
        origin.Reset(frame, new Vec3d(0, 0, 0));

        var earth = new Vec3d(0, 0, 0);
        var neptune = new Vec3d(4.5e12, 0, 0);

        for (int i = 0; i < 60; i++) origin.Update(frame, earth, 1.0 / 60.0, false);
        Assert.AreEqual(0.0, origin.Current.positionInFrameMetres.x, 0.0);

        origin.Reanchor(frame, neptune);
        Assert.AreEqual(0.0, origin.Current.positionInFrameMetres.x, 0.0,
                        "re-anchoring must not move the origin on the frame it happens");

        double previousError = (origin.Current.positionInFrameMetres - neptune).Length;
        Assert.AreEqual(neptune.x, previousError, 1.0);

        // Glide until the residual rounds away entirely - a few hundred frames for a jump this size,
        // because the residual only vanishes when it drops below half an ulp of the anchor.
        int frames = 0;
        double error1 = previousError;
        while (error1 > 0.0 && frames < 5000)
        {
            origin.Update(frame, neptune, 1.0 / 60.0, false);
            error1 = (origin.Current.positionInFrameMetres - neptune).Length;
            Assert.IsTrue(error1 <= previousError, "the glide must be monotone");
            previousError = error1;
            frames++;
        }

        Assert.AreEqual(0.0, error1, 0.0, "the origin must land exactly on the new focus");
        Assert.IsTrue(frames > 1, "it must take more than one frame - a cut is not a glide");
    }

    [Test]
    public void ThresholdModeHoldsTheOriginUntilDriftIsWorthAPrematureRebase()
    {
        SpaceFrame frame = Frame();
        var origin = new FloatingOrigin { mode = OriginRebaseMode.Threshold, rebaseThresholdMetres = 64.0 };
        origin.Reset(frame, new Vec3d(0, 0, 0));

        // Inside the threshold: the world does not move, and nothing is charged for it.
        for (int i = 1; i <= 64; i++)
            Assert.IsFalse(origin.Update(frame, new Vec3d(i, 0, 0), 1.0 / 60.0, false),
                           "no rebase should happen at " + i + " m of drift");
        Assert.AreEqual(0, origin.RebaseCount, "Reset initialises, it is not a rebase");
        Assert.AreEqual(64.0, origin.PeakDriftMetres, 1e-9);

        // Past it: one rebase, and the peak resets.
        Assert.IsTrue(origin.Update(frame, new Vec3d(65.0, 0, 0), 1.0 / 60.0, false));
        Assert.AreEqual(1, origin.RebaseCount);
        Assert.AreEqual(65.0, origin.LastRebaseDeltaMetres, 1e-9);
        Assert.AreEqual(0.0, origin.PeakDriftMetres, 1e-12);
        Assert.AreEqual(65.0, origin.Current.positionInFrameMetres.x, 0.0);
    }

    [Test]
    public void DriftPenaltyQuantifiesWhatTheOriginIsCosting()
    {
        // The penalty is the ratio of the float32 step at the observer's drift to the step at 1 m.
        // 1.0 means the origin is perfect; 2^20 means the world is being quantised a million times
        // coarser than it needs to be. Threshold mode is used because it is what holds a drift open
        // for inspection - the penalty is a property of the drift, not of the mode.
        SpaceFrame frame = Frame();
        var origin = new FloatingOrigin { mode = OriginRebaseMode.Threshold, rebaseThresholdMetres = 1e12 };
        origin.Reset(frame, Vec3d.Zero);
        Assert.AreEqual(1.0, origin.DriftPenalty(NumericFormat.Float32), 1e-9);

        // 1 m of drift is still free: the grid does not coarsen until the next power of two.
        origin.Update(frame, new Vec3d(1.0, 0, 0), 1.0 / 60.0, false);
        Assert.AreEqual(1.0, origin.DriftPenalty(NumericFormat.Float32), 1e-9);

        // 64 m = 2^6, so the step has doubled exactly six times.
        origin.Update(frame, new Vec3d(64.0, 0, 0), 1.0 / 60.0, false);
        Assert.AreEqual(64.0, origin.DriftPenalty(NumericFormat.Float32), 1e-6,
                        "64 m = 2^6 buys exactly 64x the quantisation");

        // 2^20 m = 1,048.576 km of drift costs 2^20 times the quantisation, exactly.
        origin.Update(frame, new Vec3d(1048576.0, 0, 0), 1.0 / 60.0, false);
        Assert.AreEqual(1048576.0, origin.DriftPenalty(NumericFormat.Float32), 1e-6);

        // The penalty is the number of mantissa bits the drift burns, so it is the same in either
        // format - a double frame is finer, but a bit of drift costs it a bit at the same place.
        Assert.AreEqual(origin.DriftPenalty(NumericFormat.Float32),
                        origin.DriftPenalty(NumericFormat.Float64), 1e-6);
    }

    [Test]
    public void AStillObserverCostsNothing()
    {
        // The cost story of the origin is that a rebase is a subtraction, and a subtraction that
        // changes nothing is skipped. This pins the "returns false" contract callers rely on.
        SpaceFrame frame = Frame();
        var origin = new FloatingOrigin { mode = OriginRebaseMode.RigidFollow };
        origin.Reset(frame, new Vec3d(7, 8, 9));

        int moved = 0;
        for (int i = 0; i < 100; i++)
            if (origin.Update(frame, new Vec3d(7, 8, 9), 1.0 / 60.0, false)) moved++;

        Assert.AreEqual(0, moved, "a still observer must not report movement");
        Assert.AreEqual(0, origin.RebaseCount, "and must not count as a rebase - Reset initialises, it does not rebase");
        Assert.AreEqual(9.0, origin.Current.positionInFrameMetres.z, 0.0);
    }

    [Test]
    public void UpdateRejectsANullFrameRatherThanProducingGarbage()
    {
        var origin = new FloatingOrigin();
        Assert.Throws<ArgumentNullException>(delegate { origin.Update(null, Vec3d.Zero, 0.016, false); });
    }
}
