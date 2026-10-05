using System;
using NUnit.Framework;
using Planet.Solar;

/// <summary>
/// The debris belts: how the solve cadence turns angular staleness into a CPU budget.
///
/// The belts are 7,200 particles that each need a Kepler solve. Solving all of them every frame is
/// the single largest per-frame cost in the scene, and the whole optimisation rests on one fact:
/// at any realistic time rate a particle's <i>direction</i> changes far more slowly than the frame
/// rate, so a throttled frame can legitimately reuse the previous positions bit for bit.
/// </summary>
public sealed class BeltCadenceTests
{
    private static BeltField NewBelt(int count)
    {
        var go = new UnityEngine.GameObject("belt-test");
        var belt = go.AddComponent<BeltField>();
        belt.count = count;
        belt.fieldName = "test";
        return belt;
    }

    private static void Destroy(BeltField belt)
    {
        if (belt != null) UnityEngine.Object.DestroyImmediate(belt.gameObject);
    }

    [Test]
    public void SolveIntervalShrinksAsTheTimeRateGrows()
    {
        BeltField belt = NewBelt(64);
        try
        {
            // A real belt's max mean motion is around 0.3 deg/day for a main-belt particle, so the
            // interval is the time it takes the fastest particle to sweep the staleness budget.
            double slow = belt.SolveIntervalSeconds(0.001);
            double fast = belt.SolveIntervalSeconds(10.0);

            Assert.IsTrue(slow > 0.0, "the interval must be finite at a slow rate");
            Assert.IsTrue(fast > 0.0);
            Assert.IsTrue(slow >= fast, "a slower clock must allow a longer interval between solves");

            // The interval is exactly staleness / (mean motion x days per second), so halving the
            // time rate must double it.
            Assert.AreEqual(2.0 * belt.SolveIntervalSeconds(1.0), belt.SolveIntervalSeconds(0.5), 1e-9);
        }
        finally { Destroy(belt); }
    }

    [Test]
    public void APausedClockNeverNeedsAnotherSolve()
    {
        // Paused means the positions are constant, so the interval is infinite and every frame after
        // the first is a pure cache hit. This is the case that takes 648,000 matrix builds per 120
        // frames down to 1,476.
        BeltField belt = NewBelt(64);
        try
        {
            Assert.IsTrue(double.IsPositiveInfinity(belt.SolveIntervalSeconds(0.0)),
                          "a paused clock has no staleness, so it never needs to solve again");

            // And the jump threshold is then a day, not a fraction of a second: a resume after any
            // realistic pause must not be treated as a discontinuity.
            Assert.AreEqual(1.0, BeltField.JumpThresholdDays(double.PositiveInfinity, 0.0), 1e-12);
        }
        finally { Destroy(belt); }
    }

    [Test]
    public void JumpThresholdIsFourCadenceIntervalsFlooredAtADay()
    {
        // A forward jump of more than four cadence intervals means the cache is stale for a reason
        // the interval cannot know about - a time-rate change, or a seek - so the next solve must not
        // be throttled. The one-day floor stops that test from firing constantly when the cadence is
        // long, or infinite because the clock is paused.
        Assert.AreEqual(1.0, BeltField.JumpThresholdDays(0.01, 5.0), 1e-12,
                        "a short interval is dominated by the one-day floor");
        Assert.AreEqual(0.25 * 5.0 * 4.0, BeltField.JumpThresholdDays(0.25, 5.0), 1e-12);
        Assert.AreEqual(1.0, BeltField.JumpThresholdDays(double.PositiveInfinity, 0.0), 1e-12);
        Assert.AreEqual(1.0, BeltField.JumpThresholdDays(double.NaN, 5.0), 1e-12);
    }

    [Test]
    public void TheBeltSolvesOnceAndThenServesFromCache()
    {
        // The whole contract in one test: first Render solves, the following ones at the same instant
        // do not. SolveCount is monotonic since Build, so it counts performed work rather than
        // attempts - which is what makes it usable as a benchmark.
        BeltField belt = NewBelt(128);
        try
        {
            belt.angularStalenessDegrees = 0.01;
            belt.adaptSolveCadence = true;
            belt.cullBehindCamera = false;
            belt.cullBeyondFarPlane = false;

            // No camera and no GPU work: Render's solve path is exercised without a draw.
            var state = new BeltTimeState { daysPerSecond = 0.0f, paused = true, nowSeconds = 1000.0 };

            belt.Render(null, Vec3d.Zero, 1e-6, 2451545.0, state);
            int afterFirst = belt.SolveCount;
            Assert.AreEqual(1, afterFirst, "one orbital refresh, however many particles it covered");
            Assert.AreEqual(128, belt.LastSolvedCount, "and that refresh covered every particle");

            // The date is held fixed as well as the clock: a paused belt is not being asked for a
            // different epoch, so there is nothing to re-solve.
            for (int frame = 0; frame < 60; frame++)
            {
                state.nowSeconds += 1.0 / 60.0;
                belt.Render(null, Vec3d.Zero, 1e-6, 2451545.0, state);
            }

            Assert.AreEqual(afterFirst, belt.SolveCount,
                            "a paused belt must not solve again, however many frames pass");
            Assert.IsTrue(belt.LastRenderWasIdle || belt.LastRenderUsedCache,
                          "and it must report that it re-used the cache rather than re-solving");
        }
        finally { Destroy(belt); }
    }

    [Test]
    public void ASlowClockStretchesTheCadenceByOrdersOfMagnitude()
    {
        // The regime the optimisation exists for. At 0.002 days/s - a real-time-ish clock for the
        // outer system - the fastest particle sweeps the staleness budget once every ~15 s, so 30 s
        // of wall clock needs two or three refreshes instead of 1,800.
        BeltField belt = NewBelt(64);
        try
        {
            belt.angularStalenessDegrees = 0.01;
            belt.adaptSolveCadence = true;
            belt.cullBehindCamera = false;
            belt.cullBeyondFarPlane = false;
            belt.Build();

            const double daysPerSecond = 0.002;
            const double frameSeconds = 1.0 / 60.0;
            double interval = belt.SolveIntervalSeconds(daysPerSecond);
            Assert.AreEqual(0.01 / (belt.MaxMeanMotionDegPerDay * daysPerSecond), interval, 1e-9);
            Assert.IsTrue(interval > 10.0, "at 0.002 d/s the interval is tens of seconds, measured " + interval);

            var state = new BeltTimeState { daysPerSecond = (float)daysPerSecond, paused = false, nowSeconds = 1000.0 };
            double jd = 2451545.0;
            belt.Render(null, Vec3d.Zero, 1e-6, jd, state);

            int frames = 1800;      // 30 s of wall clock
            for (int frame = 0; frame < frames; frame++)
            {
                state.nowSeconds += frameSeconds;
                jd += daysPerSecond * frameSeconds;
                belt.Render(null, Vec3d.Zero, 1e-6, jd, state);
            }

            // One refresh up front, then one per interval: 30 s / 15 s is two more.
            int expected = 1 + (int)((frames * frameSeconds) / interval);
            Assert.IsTrue(belt.SolveCount >= expected - 1 && belt.SolveCount <= expected + 1,
                          "expected about " + expected + " refreshes over 30 s, saw " + belt.SolveCount);
            Assert.IsTrue(belt.SolveCount < frames / 10,
                          "a slow clock must cost far less than a solve per frame, saw " + belt.SolveCount);
        }
        finally { Destroy(belt); }
    }

    [Test]
    public void AFastClockCollapsesTheCadenceToEveryFrame()
    {
        // The other end, and the reason the cadence is derived rather than tuned: at 5 days/s the
        // interval is under one frame, so the throttle correctly stops throttling. A fixed cadence
        // would either freeze the belt here or waste work at the slow end.
        BeltField belt = NewBelt(64);
        try
        {
            belt.angularStalenessDegrees = 0.01;
            belt.adaptSolveCadence = true;
            belt.cullBehindCamera = false;
            belt.cullBeyondFarPlane = false;
            belt.Build();

            const double daysPerSecond = 5.0;
            const double frameSeconds = 1.0 / 60.0;
            double interval = belt.SolveIntervalSeconds(daysPerSecond);
            Assert.IsTrue(interval < frameSeconds,
                          "at 5 d/s the interval (" + interval + " s) is shorter than a frame");

            var state = new BeltTimeState { daysPerSecond = (float)daysPerSecond, paused = false, nowSeconds = 1000.0 };
            double jd = 2451545.0;
            belt.Render(null, Vec3d.Zero, 1e-6, jd, state);

            int frames = 120;
            for (int frame = 0; frame < frames; frame++)
            {
                state.nowSeconds += frameSeconds;
                jd += daysPerSecond * frameSeconds;
                belt.Render(null, Vec3d.Zero, 1e-6, jd, state);
            }

            // The date advance per frame (0.083 d) is under the one-day jump floor, so this is the
            // cadence and not the jump path: it must have solved on essentially every frame.
            Assert.IsTrue(belt.SolveCount >= frames, "expected a solve per frame, saw " + belt.SolveCount);
            Assert.IsTrue(belt.SolveCount <= frames + 1, "and not more than one per frame, saw " + belt.SolveCount);
        }
        finally { Destroy(belt); }
    }

    [Test]
    public void EveryParticleEndsUpOnARealKeplerOrbit()
    {
        // Build is deterministic from seed, and every particle must be a physically plausible orbit:
        // this is what makes the field read as a belt rather than as a random cloud.
        BeltField belt = NewBelt(512);
        try
        {
            belt.innerAu = 2.06;
            belt.outerAu = 3.27;
            belt.Build();

            Assert.AreEqual(512, belt.ParticleCount);
            Assert.IsTrue(belt.MaxMeanMotionDegPerDay > 0.0, "the cadence needs a real mean motion");

            double inner = double.MaxValue, outer = 0.0, fastest = 0.0;
            for (int i = 0; i < belt.ParticleCount; i++)
            {
                BeltField.BeltParticle p = belt.ParticleAt(i);

                // Elliptic: e in [0, 1) and a positive semi-major axis.
                Assert.IsTrue(p.semiMajorAu > 0.0, "particle " + i + " has a non-positive semi-major axis");
                Assert.IsTrue(p.eccentricity >= 0.0 && p.eccentricity < 1.0, "particle " + i + " is not elliptic");
                Assert.IsTrue(p.eccentricity <= belt.maxEccentricity + 1e-12, "eccentricity exceeded the cap");
                Assert.IsTrue(p.meanMotionDegPerDay > 0.0, "particle " + i + " has no mean motion");

                // Perifocal basis: orthonormal, or positions come out skewed.
                Assert.AreEqual(1.0, p.periapsisBasis.Length, 1e-9, "px is not a unit vector");
                Assert.AreEqual(1.0, p.apoapsisBasis.Length, 1e-9, "py is not a unit vector");
                Assert.AreEqual(0.0, p.periapsisBasis.x * p.apoapsisBasis.x +
                                     p.periapsisBasis.y * p.apoapsisBasis.y +
                                     p.periapsisBasis.z * p.apoapsisBasis.z, 1e-9, "px and py are not perpendicular");

                // The periapsis and apoapsis must straddle the requested annulus, since a is drawn
                // from the annulus and e then pulls the ends of the orbit in and out of it.
                if (p.PeriapsisAu < inner) inner = p.PeriapsisAu;
                if (p.ApoapsisAu > outer) outer = p.ApoapsisAu;
                if (p.meanMotionDegPerDay > fastest) fastest = p.meanMotionDegPerDay;
            }

            Assert.AreEqual(fastest, belt.MaxMeanMotionDegPerDay, 0.0,
                            "the cached max mean motion must be the actual maximum");

            // The belt lies where it was asked to, within the eccentricity spread.
            Assert.IsTrue(inner > belt.innerAu * 0.5, "no particle should be near the Sun, innermost was " + inner);
            Assert.IsTrue(outer < belt.outerAu * 2.0, "no particle should be flung far past the belt, outermost was " + outer);
            Assert.IsTrue(fastest < 1.0, "a main-belt particle's mean motion is a fraction of a degree per day");
        }
        finally { Destroy(belt); }
    }

    [Test]
    public void BuildIsDeterministic()
    {
        // Same seed, same belt - which is what makes a capture reproducible and the cadence
        // measurements comparable between runs.
        BeltField a = NewBelt(256);
        BeltField b = NewBelt(256);
        try
        {
            a.Build();
            b.Build();

            Assert.AreEqual(a.ParticleCount, b.ParticleCount);
            for (int i = 0; i < a.ParticleCount; i++)
            {
                Assert.AreEqual(a.ParticleAt(i).semiMajorAu, b.ParticleAt(i).semiMajorAu, 0.0, "a at " + i);
                Assert.AreEqual(a.ParticleAt(i).eccentricity, b.ParticleAt(i).eccentricity, 0.0, "e at " + i);
                Assert.AreEqual(a.ParticleAt(i).meanAnomalyAtJ2000Deg, b.ParticleAt(i).meanAnomalyAtJ2000Deg, 0.0, "m0 at " + i);
            }
        }
        finally { Destroy(a); Destroy(b); }
    }
}
