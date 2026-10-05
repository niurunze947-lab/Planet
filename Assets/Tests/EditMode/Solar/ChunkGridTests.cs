using System;
using System.Collections.Generic;
using NUnit.Framework;
using Planet.Procedural;
using Planet.Solar;

/// <summary>
/// The cube-sphere chunk grid: the invariants that make it watertight and addressable.
///
/// These are the properties that are expensive to discover visually - a crack between two chunks is a
/// hairline in a capture and obvious in an assertion.
/// </summary>
public sealed class ChunkGridTests
{
    private const double EarthRadiusMetres = 6371000.0;
    private const double EarthFlattening = 0.0033528;

    private static SurfaceChunkGrid Grid(int maxLevel, int resolution)
    {
        return new SurfaceChunkGrid(EarthRadiusMetres, EarthFlattening, maxLevel, resolution);
    }

    private static readonly PlanetCubeFace[] AllFaces =
        (PlanetCubeFace[])Enum.GetValues(typeof(PlanetCubeFace));

    private static readonly ChunkNeighbourDirection[] AllDirections =
        (ChunkNeighbourDirection[])Enum.GetValues(typeof(ChunkNeighbourDirection));

    private static Vec3d[] EdgeDirections(SurfaceChunkGrid grid, SurfaceChunkKey key, int edge)
    {
        var result = new Vec3d[grid.resolution + 1];
        for (int i = 0; i <= grid.resolution; i++)
        {
            switch (edge)
            {
                case 0: result[i] = grid.SampleDirection(key, i, 0); break;
                case 1: result[i] = grid.SampleDirection(key, grid.resolution, i); break;
                case 2: result[i] = grid.SampleDirection(key, i, grid.resolution); break;
                default: result[i] = grid.SampleDirection(key, 0, i); break;
            }
        }
        return result;
    }

    private static bool SameDirectionSet(Vec3d[] a, Vec3d[] b)
    {
        if (a.Length != b.Length) return false;
        var remaining = new List<int>(b.Length);
        for (int i = 0; i < b.Length; i++) remaining.Add(i);

        for (int i = 0; i < a.Length; i++)
        {
            int hit = -1;
            for (int j = 0; j < remaining.Count; j++)
            {
                if (a[i].x == b[remaining[j]].x && a[i].y == b[remaining[j]].y && a[i].z == b[remaining[j]].z)
                {
                    hit = j;
                    break;
                }
            }
            if (hit < 0) return false;
            remaining.RemoveAt(hit);
        }
        return true;
    }

    [Test]
    public void EveryChunkEdgeHasANeighbourIncludingAcrossCubeFaces()
    {
        // Four chunks per face means every chunk is on a face boundary, so this exercises the
        // cross-face path on every single edge rather than only on the perimeter.
        SurfaceChunkGrid grid = Grid(2, 4);

        int edges = 0, missing = 0, crossFace = 0;
        for (int level = 1; level <= 3; level++)
        {
            int cells = grid.CellsPerFace(level);
            for (int y = 0; y < cells; y++)
            {
                for (int x = 0; x < cells; x++)
                {
                    foreach (PlanetCubeFace face in AllFaces)
                    {
                        var key = new SurfaceChunkKey(face, level, x, y);
                        foreach (ChunkNeighbourDirection direction in AllDirections)
                        {
                            edges++;
                            SurfaceChunkKey neighbour;
                            if (!grid.TryNeighbour(key, direction, out neighbour)) { missing++; continue; }
                            if (neighbour.face != key.face) crossFace++;

                            // The neighbour must be inside its own face's grid.
                            Assert.IsTrue(neighbour.x >= 0 && neighbour.x < cells && neighbour.y >= 0 && neighbour.y < cells,
                                          "neighbour " + neighbour + " of " + key + " is out of range");
                        }
                    }
                }
            }
        }

        Assert.AreEqual(0, missing, "every chunk edge must resolve to a neighbour");
        Assert.IsTrue(crossFace > 0, "the sweep must actually cross face boundaries, or it tests nothing");
    }

    [Test]
    public void NeighbouringChunksSampleTheirSharedEdgeAtIdenticalPoints()
    {
        // Set equality rather than index equality: across a cube-face boundary the shared edge can be
        // traversed the other way round, and pairing by index would report a crack where the geometry
        // is in fact watertight. Set equality is the property the mesher needs.
        SurfaceChunkGrid grid = Grid(2, 4);
        int cells = grid.CellsPerFace(2);
        int matched = 0, unmatched = 0;

        foreach (PlanetCubeFace face in AllFaces)
        {
            for (int y = 0; y < cells; y++)
            {
                for (int x = 0; x < cells; x++)
                {
                    var key = new SurfaceChunkKey(face, 2, x, y);
                    foreach (ChunkNeighbourDirection direction in AllDirections)
                    {
                        SurfaceChunkKey neighbour;
                        Assert.IsTrue(grid.TryNeighbour(key, direction, out neighbour));

                        bool found = false;
                        for (int ke = 0; ke < 4 && !found; ke++)
                        {
                            Vec3d[] self = EdgeDirections(grid, key, ke);
                            for (int ne = 0; ne < 4 && !found; ne++)
                                if (SameDirectionSet(self, EdgeDirections(grid, neighbour, ne))) found = true;
                        }

                        if (found) matched++;
                        else
                        {
                            unmatched++;
                            Assert.Fail(key + " and its " + direction + " neighbour " + neighbour +
                                        " share no edge exactly");
                        }
                    }
                }
            }
        }

        Assert.AreEqual(matched, matched + unmatched);
        Assert.IsTrue(matched >= 96, "expected at least the 96 L2 edges across six faces");
    }

    [Test]
    public void SameFaceNeighboursAgreeBitForBitBecauseTheGridIndexIsGlobal()
    {
        // The index of a chunk vertex is 2 * (chunk * resolution + local) - gridSize, an integer
        // function of the global position. Two chunks therefore compute the SAME index for the vertex
        // they share, with no epsilon and no welding pass. This test walks a whole row of chunks at
        // several levels to confirm the directions match rather than merely being close.
        SurfaceChunkGrid grid = Grid(6, 4);

        for (int level = 1; level <= 6; level++)
        {
            int cells = grid.CellsPerFace(level);
            for (int y = 0; y < cells; y++)
            {
                for (int x = 0; x + 1 < cells; x++)
                {
                    var left = new SurfaceChunkKey(PlanetCubeFace.PositiveZ, level, x, y);
                    var right = new SurfaceChunkKey(PlanetCubeFace.PositiveZ, level, x + 1, y);

                    for (int iy = 0; iy <= grid.resolution; iy++)
                    {
                        Vec3d a = grid.SampleDirection(left, grid.resolution, iy);
                        Vec3d b = grid.SampleDirection(right, 0, iy);
                        Assert.AreEqual(a.x, b.x, 0.0, "L" + level + " x");
                        Assert.AreEqual(a.y, b.y, 0.0, "L" + level + " y");
                        Assert.AreEqual(a.z, b.z, 0.0, "L" + level + " z");
                    }
                }
            }
        }
    }

    [Test]
    public void EqualLevelNeighboursNeverNeedAStitch()
    {
        SurfaceChunkGrid grid = Grid(4, 4);
        for (int level = 1; level <= 4; level++)
        {
            int cells = grid.CellsPerFace(level);
            for (int y = 0; y < cells; y++)
            {
                for (int x = 0; x + 1 < cells; x++)
                {
                    Assert.IsFalse(SurfaceChunkGrid.NeedsStitch(
                        new SurfaceChunkKey(PlanetCubeFace.PositiveY, level, x, y),
                        new SurfaceChunkKey(PlanetCubeFace.PositiveY, level, x + 1, y)));
                }
            }
        }
    }

    [Test]
    public void NeedsStitchFlagsOnlyThePathologicalLevelGap()
    {
        // NeedsStitch is not "are these two different levels": one level of difference is the normal
        // case, and the standard edge fan handles it from the vertex counts alone. What has to be
        // flagged is a gap of two or more, where the LOD selector has let a chunk sit beside a
        // neighbour it cannot stitch against at all. That is an upstream bug, and this is where it
        // gets caught rather than showing up as a torn seam in a capture.
        var two = new SurfaceChunkKey(PlanetCubeFace.PositiveX, 2, 1, 1);
        var three = new SurfaceChunkKey(PlanetCubeFace.PositiveX, 3, 2, 2);
        var four = new SurfaceChunkKey(PlanetCubeFace.PositiveX, 4, 4, 4);

        Assert.IsFalse(SurfaceChunkGrid.NeedsStitch(two, two), "equal levels share vertices exactly");
        Assert.IsFalse(SurfaceChunkGrid.NeedsStitch(two, three), "one level apart is the ordinary case");
        Assert.IsTrue(SurfaceChunkGrid.NeedsStitch(two, four), "two levels apart cannot be stitched");
        Assert.IsTrue(SurfaceChunkGrid.NeedsStitch(four, two), "and the test is symmetric");
    }

    [Test]
    public void ParentChildRoundTripsExactly()
    {
        SurfaceChunkGrid grid = Grid(8, 4);
        var key = new SurfaceChunkKey(PlanetCubeFace.NegativeY, 5, 13, 27);

        SurfaceChunkKey parent;
        Assert.IsTrue(grid.TryParent(key, out parent));
        Assert.AreEqual(4, parent.level);
        Assert.AreEqual(13 / 2, parent.x);
        Assert.AreEqual(27 / 2, parent.y);

        var children = new SurfaceChunkKey[4];
        Assert.IsTrue(grid.TryChildren(parent, children));
        bool containsKey = false;
        foreach (SurfaceChunkKey child in children)
        {
            Assert.AreEqual(5, child.level);
            Assert.AreEqual(parent.face, child.face);
            if (child.Equals(key)) containsKey = true;
        }
        Assert.IsTrue(containsKey, "the parent's four children must include the original chunk");

        Assert.IsTrue(grid.TryParent(children[0], out parent), "a level-5 chunk has a parent");
        SurfaceChunkKey root = children[0];
        while (grid.TryParent(root, out parent)) root = parent;
        Assert.AreEqual(0, root.level, "walking parents must reach level 0");
    }

    [Test]
    public void ChunkSizeAndStretchMatchTheCubeGeometry()
    {
        SurfaceChunkGrid grid = Grid(20, 16);

        // Central angle per chunk: a quarter turn divided by the number of chunks per face edge.
        Assert.AreEqual(EarthRadiusMetres * Math.PI * 0.5 / 1024.0, grid.ChunkSizeMetres(10), 1e-6);

        // A cube face's corners are sqrt(3) further from the centre than its middle, so the worst
        // chunk on a face is that much larger than the nominal size. The constant is the design's, and
        // it is what the local extent budget is derived from.
        Assert.AreEqual(Math.Sqrt(3.0), grid.chunkStretchFactor, 1e-15);
        Assert.AreEqual(grid.ChunkSizeMetres(10) * Math.Sqrt(3.0), grid.ChunkWorstSizeMetres(10), 1e-9);

        // Half the diagonal of the worst chunk: the radius of the ball that contains it.
        Assert.AreEqual(grid.ChunkWorstSizeMetres(10) * Math.Sqrt(2.0) * 0.5, grid.ChunkLocalExtentMetres(10), 1e-9);
    }

    [Test]
    public void ChunkLocalFloat32StepBeatsTheGlobeByFiveOrdersOfMagnitudeAtLevel20()
    {
        SurfaceChunkGrid grid = Grid(20, 16);

        double globeStep = ChunkLocalGeometry.GlobeVertexStepMetres(EarthRadiusMetres);
        double chunkStep = ChunkLocalGeometry.VertexStepMetres(grid, 20);

        Assert.AreEqual(0.5, globeStep, 0.001, "the unit-sphere globe quantises Earth to 50 cm");
        Assert.IsTrue(chunkStep < 1e-6, "a 9.5 m chunk stores vertices to under a micron");
        Assert.IsTrue(globeStep / chunkStep > 500000.0,
                      "chunk-local coordinates must beat the globe by more than 500,000x, measured " +
                      (globeStep / chunkStep));
    }

    [Test]
    public void LodLevelGrowsAsTheCameraApproaches()
    {
        SurfaceChunkGrid grid = Grid(20, 16);
        const double fov = 50.0;
        const int height = 1080;
        const double tolerance = 3.0;
        const double relief = 0.5;

        int previous = -1;
        foreach (double altitude in new[] { 1e7, 1e6, 1e5, 1e4, 1e3, 100.0, 10.0 })
        {
            int level = ChunkLodSelector.SelectLevel(grid, altitude, fov, height, tolerance, relief);
            Assert.IsTrue(level >= previous, "level must not fall as the camera descends (at " + altitude + " m)");
            Assert.IsTrue(level >= 0 && level <= grid.maxLevel, "level out of range at " + altitude + " m");
            previous = level;
        }

        // 10 m above the ground must reach deeper than 10,000 km up.
        Assert.IsTrue(ChunkLodSelector.SelectLevel(grid, 10.0, fov, height, tolerance, relief) >
                      ChunkLodSelector.SelectLevel(grid, 1e7, fov, height, tolerance, relief));
    }

    [Test]
    public void LodLevelSaturatesAtMaxLevelRatherThanRunningAway()
    {
        SurfaceChunkGrid grid = Grid(6, 16);
        Assert.AreEqual(6, ChunkLodSelector.SelectLevel(grid, 1e-6, 50.0, 1080, 0.5, 0.5));
        Assert.AreEqual(0, ChunkLodSelector.SelectLevel(grid, 1e30, 50.0, 1080, 0.5, 0.5));
    }

    [Test]
    public void GeometricErrorHalvesWithEveryLevel()
    {
        SurfaceChunkGrid grid = Grid(20, 16);
        double previous = ChunkLodSelector.GeometricErrorMetres(grid, 0, 0.5);
        Assert.IsTrue(previous > 0.0);

        for (int level = 1; level <= 12; level++)
        {
            double error = ChunkLodSelector.GeometricErrorMetres(grid, level, 0.5);
            Assert.AreEqual(previous / 2.0, error, Math.Abs(error) * 1e-9, "error at L" + level);
            previous = error;
        }

        // Level 0 is a whole face: a quarter of the great circle, stretched at the corners, times the
        // relief ratio. 8,667 km of permissible deviation is why level 0 is only ever used from far
        // enough away that the whole planet is a few pixels across.
        double faceEdge = EarthRadiusMetres * Math.PI * 0.5 * Math.Sqrt(3.0);
        Assert.AreEqual(faceEdge * 0.5, ChunkLodSelector.GeometricErrorMetres(grid, 0, 0.5), 1.0);
        Assert.AreEqual(8666786.8, ChunkLodSelector.GeometricErrorMetres(grid, 0, 0.5), 1.0);
    }

    [Test]
    public void LodDistanceIsTheInverseOfGeometricError()
    {
        SurfaceChunkGrid grid = Grid(20, 16);
        Func<SurfaceChunkKey, double> distanceForChunk = delegate (SurfaceChunkKey key) { return 1e5; };
        Func<SurfaceChunkKey, int> desired = ChunkLodSelector.BuildLevelFunction(
            grid, distanceForChunk, 50.0, 1080, 3.0, 0.5);

        // At the LOD distance for a level, the selector must return that level; anywhere inside it,
        // something finer. It must never return something coarser, or the terrain would visibly pop
        // backwards as the camera moves in.
        for (int level = 4; level <= 12; level++)
        {
            double distance = ChunkLodSelector.LodDistanceMetres(grid, level, 50.0, 1080, 3.0, 0.5);
            Assert.AreEqual(level, ChunkLodSelector.SelectLevel(grid, distance, 50.0, 1080, 3.0, 0.5),
                            "exactly at the L" + level + " LOD distance (" + distance + " m)");
            Assert.IsTrue(ChunkLodSelector.SelectLevel(grid, distance * 0.5, 50.0, 1080, 3.0, 0.5) > level,
                          "half the L" + level + " LOD distance must ask for more detail");
        }

        Assert.IsNotNull(desired);
        Assert.IsTrue(desired(default(SurfaceChunkKey)) >= 0);
    }

    [Test]
    public void ReferenceRadiusHonoursFlatteningAndShapeScale()
    {
        // The polar axis is Y, not Z: the body-fixed frame here is the sampler's, where the ecliptic
        // pole was already swapped into Unity's up axis. Getting this wrong puts every chunk on the
        // wrong spheroid and the error is largest exactly at the poles.
        var polar = new Vec3d(0, 1, 0);
        var equator = new Vec3d(1, 0, 0);
        var unit = new Vec3d(1, 1, 1);

        // A sphere: every direction gives back the radius.
        Assert.AreEqual(EarthRadiusMetres, CubeFaceMath.ReferenceRadiusMetres(equator, EarthRadiusMetres, 0.0, unit), 1e-6);
        Assert.AreEqual(EarthRadiusMetres, CubeFaceMath.ReferenceRadiusMetres(polar, EarthRadiusMetres, 0.0, unit), 1e-6);

        // A flattened spheroid: the pole is closer to the centre by f * R, the equator is untouched.
        double atPole = CubeFaceMath.ReferenceRadiusMetres(polar, EarthRadiusMetres, EarthFlattening, unit);
        Assert.AreEqual(EarthRadiusMetres * (1.0 - EarthFlattening), atPole, 1.0);
        Assert.AreEqual(EarthRadiusMetres,
                        CubeFaceMath.ReferenceRadiusMetres(equator, EarthRadiusMetres, EarthFlattening, unit), 1e-6);

        // A triaxial shape: a scale on one axis moves the radius along that axis and nowhere else.
        var squashed = new Vec3d(1, 1, 0.5);
        Assert.AreEqual(EarthRadiusMetres * 0.5,
                        CubeFaceMath.ReferenceRadiusMetres(new Vec3d(0, 0, 1), EarthRadiusMetres, 0.0, squashed), 1e-6);
        Assert.AreEqual(EarthRadiusMetres,
                        CubeFaceMath.ReferenceRadiusMetres(equator, EarthRadiusMetres, 0.0, squashed), 1e-6);

        // Flattening and the axis scale compose multiplicatively, as they must.
        double both = CubeFaceMath.ReferenceRadiusMetres(polar, EarthRadiusMetres, EarthFlattening, new Vec3d(1, 0.5, 1));
        Assert.AreEqual(EarthRadiusMetres * (1.0 - EarthFlattening) * 0.5, both, 1.0);
    }

    [Test]
    public void DirectionToFaceInvertsTheFaceProjection()
    {
        SurfaceChunkGrid grid = Grid(20, 2);
        var random = new Random(20261003);

        for (int i = 0; i < 2000; i++)
        {
            // A uniformly distributed direction, so the sweep covers face centres and all the corners.
            double z = 2.0 * random.NextDouble() - 1.0;
            double phi = 2.0 * Math.PI * random.NextDouble();
            double r = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
            var direction = new Vec3d(r * Math.Cos(phi), z, r * Math.Sin(phi));

            PlanetCubeFace face;
            double u, v;
            CubeFaceMath.DirectionToFace(direction, out face, out u, out v);

            Vec3d roundTrip = CubeFaceMath.Direction(face, u, v);
            Assert.AreEqual(direction.x, roundTrip.x, 1e-12, "x at " + direction);
            Assert.AreEqual(direction.y, roundTrip.y, 1e-12, "y at " + direction);
            Assert.AreEqual(direction.z, roundTrip.z, 1e-12, "z at " + direction);
        }
    }

    [Test]
    public void CollectLeavesProducesAWatertightCoverWithNoOverlaps()
    {
        // The quadtree's job: every chunk is either a leaf or fully covered by its four children.
        // A gap here is a hole in the planet; an overlap is a z-fighting seam.
        SurfaceChunkGrid grid = Grid(6, 2);
        Func<SurfaceChunkKey, double> distanceForChunk = delegate (SurfaceChunkKey key) { return 1e4; };
        var levelFunction = ChunkLodSelector.BuildLevelFunction(
            grid, distanceForChunk, 50.0, 1080, 40.0, 0.5);

        var leaves = new List<SurfaceChunkKey>();
        grid.CollectLeaves(levelFunction, leaves);

        Assert.IsTrue(leaves.Count > 0, "the cover must not be empty");

        var byCell = new HashSet<string>();
        foreach (SurfaceChunkKey leaf in leaves)
        {
            Assert.IsTrue(leaf.level >= 0 && leaf.level <= grid.maxLevel);

            // Walk this leaf's footprint at the finest level and assert no other leaf claims the same cell.
            int deepest = grid.CellsPerFace(grid.maxLevel);
            int scale = deepest >> leaf.level;
            for (int dy = 0; dy < scale; dy++)
            {
                for (int dx = 0; dx < scale; dx++)
                {
                    string cell = leaf.face + ":" + (leaf.x * scale + dx) + ":" + (leaf.y * scale + dy);
                    Assert.IsTrue(byCell.Add(cell), "two leaves both claim cell " + cell);
                }
            }
        }

        // And the union must be the whole sphere: six faces, each fully covered.
        int expected = AllFaces.Length * grid.CellsPerFace(grid.maxLevel) * grid.CellsPerFace(grid.maxLevel);
        Assert.AreEqual(expected, byCell.Count, "the leaves must tile every face completely");
    }
}
