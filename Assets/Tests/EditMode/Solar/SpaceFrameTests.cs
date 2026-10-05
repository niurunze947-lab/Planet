using System;
using NUnit.Framework;
using Planet.Solar;
using UnityEngine;

/// <summary>
/// The frame tree and the single narrowing point into render units.
///
/// The design's central rule is "subtract in double, narrow to float last". These tests hold that
/// rule in place: a conversion that goes through float anywhere in the middle loses exactly the
/// precision the tree exists to protect, and the error is invisible at solar-system scale until the
/// camera is close enough to a planet for it to matter.
/// </summary>
public sealed class SpaceFrameTests
{
    private const double AuMetres = 1.495978707e11;
    private const double EarthRadiusMetres = 6371000.0;

    /// <summary>Sol at the galactic origin, Earth 1 AU out. The same tree the scene builds.</summary>
    private static SpaceFrameTree SolTree()
    {
        var tree = new SpaceFrameTree();
        tree.CreateSolSystem(new Vec3d(0, 0, 0));
        tree.EnsureBodyFrame("Earth", new Vec3d(AuMetres, 0, 0));
        return tree;
    }

    [Test]
    public void CreateSolSystemBuildsTheThreeCosmicRungsInOrder()
    {
        SpaceFrameTree tree = SolTree();

        Assert.AreEqual(SpaceFrameLevel.Galactic, tree.Galactic.level);
        Assert.AreEqual(SpaceFrameLevel.Stellar, tree.Stellar.level);
        Assert.AreEqual(SpaceFrameLevel.Planetary, tree.FocusBody.level);

        Assert.AreEqual(1, tree.Galactic.Depth);
        Assert.AreEqual(2, tree.Stellar.Depth);
        Assert.AreEqual(3, tree.FocusBody.Depth);

        // The chain must be a single path, or the origin arithmetic composes the wrong offsets.
        Assert.AreSame(tree.Galactic, tree.Stellar.parent);
        Assert.AreSame(tree.Stellar, tree.FocusBody.parent);
        Assert.IsNull(tree.Galactic.parent);
    }

    [Test]
    public void SolInGalaxyOnlyMovesTheOriginItIsGiven()
    {
        var tree = new SpaceFrameTree();
        tree.CreateSolSystem(new Vec3d(2.4e20, 0, 0));
        tree.EnsureBodyFrame("Earth", new Vec3d(AuMetres, 0, 0));

        // The galactic offset is carried by the stellar rung's own origin, so a galactic layer only
        // ever has to write one number - nothing downstream recomputes.
        Assert.AreEqual(2.4e20, tree.Stellar.OriginInRootMetres.x, 1e6);
        Assert.AreEqual(2.4e20 + AuMetres, tree.FocusBody.OriginInRootMetres.x, 1e6);

        // And the Sun's own local coordinates are unaffected by where the galaxy is.
        Assert.AreEqual(0.0, tree.Stellar.FromRoot(tree.Stellar.OriginInRootMetres).Length, 1e-6);
    }

    [Test]
    public void ToRootAndFromRootAreExactInverses()
    {
        SpaceFrameTree tree = SolTree();
        var point = new Vec3d(1234.5, -6789.25, 0.125);

        // Between two frames the round trip is exact: it is two subtractions and an addition of the
        // same quantities, and every one of them is representable.
        Vec3d inStellar = tree.FocusBody.ConvertTo(tree.Stellar, point);
        Vec3d back = tree.Stellar.ConvertTo(tree.FocusBody, inStellar);

        Assert.AreEqual(point.x, back.x, 0.0);
        Assert.AreEqual(point.y, back.y, 0.0);
        Assert.AreEqual(point.z, back.z, 0.0);
    }

    [Test]
    public void ConvertingBetweenFramesOfDifferentDepthComposesTheOrigins()
    {
        var tree = new SpaceFrameTree();
        tree.CreateSolSystem(new Vec3d(1e18, 2e18, -3e18));
        tree.EnsureBodyFrame("Mars", new Vec3d(-2.0 * AuMetres, 0.5 * AuMetres, 0));

        var local = new Vec3d(10.0, 20.0, 30.0);
        Vec3d inGalactic = tree.FocusBody.ConvertTo(tree.Galactic, local);
        Vec3d expected = new Vec3d(1e18 + -2.0 * AuMetres + 10.0,
                                   2e18 + 0.5 * AuMetres + 20.0,
                                   -3e18 + 30.0);

        Assert.AreEqual(expected.x, inGalactic.x, 1e-6);
        Assert.AreEqual(expected.y, inGalactic.y, 1e-6);
        Assert.AreEqual(expected.z, inGalactic.z, 1e-6);
    }

    [Test]
    public void RenderOriginNarrowsToFloatOnlyAtTheEnd()
    {
        // The rule the whole design turns on, and the one that is easy to violate by accident: the
        // origin is subtracted in double and the residual is narrowed last. A point 1 m from the
        // origin must therefore come out as 1e-9 scene units no matter how far the frame itself is
        // from the Sun - because the subtraction never sees a solar-system-sized number.
        SpaceFrameTree tree = SolTree();
        Vec3d bodyOrigin = new Vec3d(50.0 * AuMetres, 0, 0);
        tree.EnsureBodyFrame("Pluto", bodyOrigin);

        double unitsPerMetre = 1e-9;   // kmPerUnit = 1e6
        tree.Stellar.unitsPerMetre = unitsPerMetre;
        tree.Galactic.unitsPerMetre = unitsPerMetre;
        tree.FocusBody.unitsPerMetre = unitsPerMetre;

        double bodyOriginUnits = 50.0 * AuMetres * unitsPerMetre;
        Vec3d a = new Vec3d(50.0 * AuMetres, 0, 0);
        Vec3d b = new Vec3d(50.0 * AuMetres + 1.0, 0, 0);

        var render = new RenderOrigin { frame = tree.Stellar, positionInFrameMetres = a };
        Vector3 ra = render.ToRenderFrom(tree.Stellar, a);
        Vector3 rb = render.ToRenderFrom(tree.Stellar, b);

        Assert.AreEqual(0f, ra.x, 0f, "the origin renders at exactly zero");
        Assert.AreEqual(1e-9f, rb.x, 1e-14f, "1 m from the origin is 1e-9 scene units");

        // The trap: doing the same thing float-first. Narrowing the absolute position and then
        // subtracting loses the metre entirely, because a float32 at 7.48e12 m has a step of 524 km.
        float narrowedFirst = (float)(b.x * unitsPerMetre) - (float)(a.x * unitsPerMetre);
        Assert.AreEqual(0f, narrowedFirst, 0f,
                        "a float-first conversion collapses a metre at 50 AU to nothing");
        Assert.AreNotEqual(narrowedFirst, rb.x, "which is exactly what subtracting in double avoids");

        // And on the body rung this is not academic: the planet's own centre is 7,480 body-radii
        // away, so a float32 there cannot hold a metre either.
        PrecisionBudget atBodyOrigin = tree.FocusBody.BudgetAt(bodyOrigin, NumericFormat.Float32);
        Assert.IsFalse(atBodyOrigin.CanPlace(1.0), "the body rung cannot resolve a metre at 50 AU");
        Assert.AreEqual(524288.0, atBodyOrigin.ulpMetres, 1.0, "its step there is 2^19 m = 524 km");
    }

    [Test]
    public void RenderOriginReproducesTheExistingFloatingOriginTransformExactly()
    {
        // The framework must not change a single rendered position. The simulation's own path is
        // OrbitalMechanics.ToUnity(positionKm - originKm, unitsPerKm); the framework's is
        // RenderOrigin.ToRender. They have to agree bit for bit or this is a rewrite, not an addition.
        SpaceFrameTree tree = SolTree();
        double unitsPerKm = 1e-6;
        double unitsPerMetre = unitsPerKm * 0.001;
        tree.Stellar.unitsPerMetre = unitsPerMetre;

        var originKm = new Vec3d(-2.6504442e7, 1.4469323e8, -38.663464);
        var positionKm = new Vec3d(-2.6500000e7, 1.4469000e8, -30.0);

        var render = new RenderOrigin
        {
            frame = tree.Stellar,
            positionInFrameMetres = new Vec3d(originKm.x * 1000.0, originKm.y * 1000.0, originKm.z * 1000.0)
        };

        Vector3 legacy = OrbitalMechanics.ToUnity(positionKm - originKm, unitsPerKm);
        Vector3 framework = render.ToRenderFrom(tree.Stellar,
            new Vec3d(positionKm.x * 1000.0, positionKm.y * 1000.0, positionKm.z * 1000.0));

        Assert.AreEqual(legacy.x, framework.x, 0f);
        Assert.AreEqual(legacy.y, framework.y, 0f);
        Assert.AreEqual(legacy.z, framework.z, 0f);
    }

    [Test]
    public void EnsureBodyFrameReusesTheRungForTheSameBodyAndReplacesItForAnother()
    {
        // The focus can change every frame (the TAB key cycles bodies), so the rung is a single slot
        // rather than a per-body pool - and deliberately so. A pooled frame that outlives its body
        // keeps being handed out as a render origin after the body has moved, which is a stale
        // reference that only ever shows up as geometry in the wrong place.
        SpaceFrameTree tree = SolTree();
        SpaceFrame first = tree.FocusBody;

        // Same body: the same frame, moved. This is the focus-tracking path and it must not allocate.
        SpaceFrame same = tree.EnsureBodyFrame("Earth", new Vec3d(AuMetres + 5000.0, 0, 0));
        Assert.AreSame(first, same, "re-ensuring the same body must reuse its rung, not allocate");
        Assert.AreEqual(AuMetres + 5000.0, same.originInParentMetres.x, 1e-6);

        // Different body: a fresh rung under the same parent, and FocusBody follows it.
        SpaceFrame jupiter = tree.EnsureBodyFrame("Jupiter", new Vec3d(5.2 * AuMetres, 0, 0));
        Assert.AreNotSame(first, jupiter);
        Assert.AreSame(tree.Stellar, jupiter.parent);
        Assert.AreSame(jupiter, tree.FocusBody, "FocusBody must be the rung that was just built");
        Assert.AreEqual(5.2 * AuMetres, jupiter.originInParentMetres.x, 1e-6);

        // Both rungs are still tracked for tooling; the tree never quietly drops a frame's history.
        Assert.AreEqual(4, tree.AllFrames.Count, "galactic, stellar, Earth and Jupiter");
    }

    [Test]
    public void CreateSurfaceFrameHangsTheChunkRungUnderTheBody()
    {
        SpaceFrameTree tree = SolTree();
        var chunkOriginInBody = new Vec3d(1000.0, 2000.0, 3000.0);
        SpaceFrame surface = tree.CreateSurfaceFrame(tree.FocusBody, "Earth L12/13,27", chunkOriginInBody);

        Assert.AreEqual(SpaceFrameLevel.Surface, surface.level);
        Assert.AreSame(tree.FocusBody, surface.parent);
        Assert.AreEqual(4, surface.Depth);

        // One unit per metre is the natural surface scale: the camera is only ever metres from here.
        surface.unitsPerMetre = 1.0;

        // A point 5 m from the chunk origin, given in the BODY frame, comes out as 5 units - because
        // the chunk rung's origin carries the kilometre-scale offset and the arithmetic happens in
        // double before anything narrows.
        var render = new RenderOrigin { frame = surface, positionInFrameMetres = Vec3d.Zero };
        Vec3d inBody = chunkOriginInBody + new Vec3d(5.0, 0, 0);

        Vector3 fromBody = render.ToRenderFrom(tree.FocusBody, inBody);
        Assert.AreEqual(5f, fromBody.x, 1e-4f, "the chunk origin must be subtracted before narrowing");
        Assert.AreEqual(0f, fromBody.y, 0f);
        Assert.AreEqual(0f, fromBody.z, 0f);

        // The same point 5 m out, expressed in the frame that owns it, agrees.
        Vector3 fromOwn = render.ToRender(new Vec3d(5.0, 0, 0));
        Assert.AreEqual(fromBody.x, fromOwn.x, 0f);
        Assert.AreEqual(fromBody.y, fromOwn.y, 0f);
        Assert.AreEqual(fromBody.z, fromOwn.z, 0f);
    }

    [Test]
    public void BudgetAtReportsTheStepThatActuallyBites()
    {
        SpaceFrameTree tree = SolTree();
        double unitsPerMetre = 1e-9;
        tree.FocusBody.unitsPerMetre = unitsPerMetre;

        // The body rung's local coordinates are metres, so the step at 6,371 km is ~0.49 m.
        PrecisionBudget atSurface = tree.FocusBody.BudgetAt(new Vec3d(EarthRadiusMetres, 0, 0), NumericFormat.Float32);
        Assert.AreEqual(0.5, atSurface.ulpMetres, 0.02);
        Assert.IsFalse(atSurface.CanPlace(0.01), "a 1 cm feature is not representable at the body rung");

        // A chunk-local frame at 9.5 m carries the same 1 cm with five orders of magnitude to spare.
        PrecisionBudget atChunk = SpacePrecision.BudgetAt(9.5, NumericFormat.Float32);
        Assert.IsTrue(atChunk.CanPlace(0.01));
        Assert.IsTrue(atChunk.CanPlace(0.000001), "and even a micron");
    }

    [Test]
    public void EveryFrameInTheTreeIsReachable()
    {
        SpaceFrameTree tree = SolTree();
        tree.CreateSurfaceFrame(tree.FocusBody, "chunk", new Vec3d(1, 2, 3));

        int count = 0;
        foreach (SpaceFrame frame in tree.AllFrames)
        {
            Assert.IsNotNull(frame);
            Assert.IsNotNull(frame.name);
            count++;
        }

        Assert.AreEqual(4, count, "galactic, stellar, planetary, surface");
    }
}
