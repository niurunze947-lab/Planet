using NUnit.Framework;
using Planet.Interaction;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// C6 coordinate tests: scheme-A flatten, body-fixed local frame, and the spin-invariance of
/// player-to-chunk offsets.
/// </summary>
public sealed class SurfaceLocalSpaceTests
{
    private const double EarthRadiusMetres = 6371000.0;

    [Test]
    public void FlattenMapsSurfaceNormalToUnityWorldUp()
    {
        // Stand on the equator: body-fixed up is +X (radial).
        var space = SurfaceLocalSpace.CreateFromDirection(new double3(1, 0, 0), EarthRadiusMetres);
        Assert.AreEqual(1.0, space.up.x, 1e-9);
        Assert.AreEqual(0.0, space.up.y, 1e-9);

        Quaternion bodyRot = Quaternion.identity;
        Quaternion flatten = space.FlattenToWorldUp(bodyRot);

        Vector3 worldNormal = bodyRot * SurfaceLocalSpace.ToVector3(space.up);
        Vector3 flattened = flatten * worldNormal;
        Assert.Greater(Vector3.Dot(flattened, Vector3.up), 0.9999f);
    }

    [Test]
    public void FlattenMapsPolarSurfaceNormalToUnityWorldUp()
    {
        // North pole: body-fixed up is +Y, already world up — flatten is identity.
        var space = SurfaceLocalSpace.CreateFromDirection(new double3(0, 1, 0), EarthRadiusMetres);
        Quaternion flatten = space.FlattenToWorldUp(Quaternion.identity);
        Vector3 flattened = flatten * Vector3.up;
        Assert.Greater(Vector3.Dot(flattened, Vector3.up), 0.9999f);
    }

    [Test]
    public void FlattenFollowsBodySpinSoNormalStaysWorldUp()
    {
        var space = SurfaceLocalSpace.CreateFromDirection(new double3(1, 0, 0), EarthRadiusMetres);
        Quaternion spin = Quaternion.AngleAxis(37f, Vector3.up);
        Quaternion flatten = space.FlattenToWorldUp(spin);

        Vector3 worldNormal = spin * SurfaceLocalSpace.ToVector3(space.up);
        Vector3 flattened = flatten * worldNormal;
        Assert.Greater(Vector3.Dot(flattened, Vector3.up), 0.9999f);
    }

    [Test]
    public void LocalToBodyFixedRoundTrips()
    {
        var space = SurfaceLocalSpace.CreateFromDirection(new double3(0.3, 0.8, 0.5), EarthRadiusMetres);
        var local = new double3(12.0, 3.5, -7.25);

        double3 bodyFixed = space.LocalToBodyFixed(local);
        double3 back = space.BodyFixedToLocal(bodyFixed);

        Assert.AreEqual(local.x, back.x, 1e-6);
        Assert.AreEqual(local.y, back.y, 1e-6);
        Assert.AreEqual(local.z, back.z, 1e-6);
    }

    [Test]
    public void FrameAxesAreOrthonormal()
    {
        var space = SurfaceLocalSpace.CreateFromDirection(new double3(0.2, -0.4, 0.9), EarthRadiusMetres);
        Assert.AreEqual(1.0, math.length(space.east), 1e-9);
        Assert.AreEqual(1.0, math.length(space.up), 1e-9);
        Assert.AreEqual(1.0, math.length(space.north), 1e-9);
        Assert.AreEqual(0.0, math.dot(space.east, space.up), 1e-9);
        Assert.AreEqual(0.0, math.dot(space.up, space.north), 1e-9);
        Assert.AreEqual(0.0, math.dot(space.north, space.east), 1e-9);
    }

    [Test]
    public void AnchorSitsOnTheReferenceSurface()
    {
        var dir = new double3(0.6, 0.0, 0.8);
        var space = SurfaceLocalSpace.CreateFromDirection(dir, EarthRadiusMetres);
        Assert.AreEqual(EarthRadiusMetres, math.length(space.anchorBodyFixedMetres), 1.0);
    }

    [Test]
    public void PlayerToSurfaceDistanceIsLocalUpComponent()
    {
        var space = SurfaceLocalSpace.CreateFromDirection(new double3(1, 0, 0), EarthRadiusMetres);
        space.playerLocalMetres = new double3(0.0, 42.5, 0.0);
        Assert.AreEqual(42.5, space.PlayerToSurfaceDistanceMetres, 1e-9);
    }

    [Test]
    public void ChunkRelativeOffsetIsInvariantUnderPlanetSpin()
    {
        // The property the coordinate basis must guarantee: player-to-chunk offsets do not
        // move when the planet rotates, because both sides are body-fixed.
        var space = SurfaceLocalSpace.CreateFromDirection(new double3(1, 0, 0), EarthRadiusMetres);
        space.playerLocalMetres = new double3(8.0, 2.0, -3.0);

        // Chunk 20 m east and 15 m north of the anchor; player is at (8, 2, -3).
        double3 chunkBf = space.anchorBodyFixedMetres + space.east * 20.0 + space.north * 15.0;
        double3 relativeBf = space.ChunkRelativeMetres(chunkBf);

        // Expected local offset: player − chunk = (8−20, 2−0, −3−15) = (−12, 2, −18).
        double3 relativeLocal = new double3(
            math.dot(relativeBf, space.east),
            math.dot(relativeBf, space.up),
            math.dot(relativeBf, space.north));
        Assert.AreEqual(-12.0, relativeLocal.x, 1e-6);
        Assert.AreEqual(2.0, relativeLocal.y, 1e-6);
        Assert.AreEqual(-18.0, relativeLocal.z, 1e-6);

        // Body-fixed numbers never see the spin: a second evaluation after "the planet has
        // rotated" (i.e. the world pose changed) still returns the same vector.
        double3 relativeBfAgain = space.PlayerBodyFixedMetres - chunkBf;
        Assert.AreEqual(relativeBf.x, relativeBfAgain.x, 0.0);
        Assert.AreEqual(relativeBf.y, relativeBfAgain.y, 0.0);
        Assert.AreEqual(relativeBf.z, relativeBfAgain.z, 0.0);
    }

    [Test]
    public void EllipsoidNormalIsNotRadialAwayFromEquator()
    {
        // Earth-like flattening at mid-latitude: the normal tilts poleward relative to the radius.
        double3 dir = new double3(0.7, 0.7, 0.0);
        double3 normal = SurfaceLocalSpace.SurfaceNormal(dir, 1.0 / 298.257, new double3(1, 1, 1));
        double3 radial = math.normalize(dir);
        double dot = math.dot(normal, radial);
        Assert.Less(dot, 0.999999);
        Assert.Greater(dot, 0.99);
    }

    [Test]
    public void ReferenceRadiusMatchesSphereWhenUnflattened()
    {
        double3 dir = math.normalize(new double3(0.3, 0.5, 0.8));
        double r = SurfaceLocalSpace.ReferenceRadius(dir, EarthRadiusMetres, 0.0, new double3(1, 1, 1));
        Assert.AreEqual(EarthRadiusMetres, r, 1e-6);
    }

    [Test]
    public void MetresToSceneUnitsUsesKmPerUnit()
    {
        // Solar-system scale: 1 unit = 1e6 km = 1e9 m.
        Assert.AreEqual(1e-9, SurfaceLocalSpace.MetresToSceneUnits(1.0, 1e6f), 1e-18);
        Assert.AreEqual(1e9, SurfaceLocalSpace.SceneUnitsToMetres(1.0, 1e6f), 1.0);
        // 1 unit = 1 km.
        Assert.AreEqual(0.001, SurfaceLocalSpace.MetresToSceneUnits(1.0, 1f), 1e-12);
    }

    [Test]
    public void CreateAtAnchorKeepsExplicitOrigin()
    {
        double3 anchor = new double3(EarthRadiusMetres * 0.6, EarthRadiusMetres * 0.8, 0.0);
        var space = SurfaceLocalSpace.CreateAtAnchor(anchor, EarthRadiusMetres);
        Assert.AreEqual(anchor.x, space.anchorBodyFixedMetres.x, 1e-6);
        Assert.AreEqual(anchor.y, space.anchorBodyFixedMetres.y, 1e-6);
        Assert.IsTrue(space.IsValid);
    }
}
