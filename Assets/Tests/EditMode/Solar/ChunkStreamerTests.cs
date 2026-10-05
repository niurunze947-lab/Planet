using System;
using System.Collections.Generic;
using NUnit.Framework;
using Planet.Solar;

/// <summary>
/// The streaming policy: nearest-first under a hard per-frame budget, with hysteresis on eviction.
///
/// The streamer is the piece that decides whether a level-20 chunked planet is affordable, so the
/// properties worth pinning are the ones a naive implementation gets wrong: building far chunks
/// before near ones, thrashing a chunk that sits exactly on a level boundary, and letting the
/// resident set grow without bound.
/// </summary>
public sealed class ChunkStreamerTests
{
    private const double EarthRadiusMetres = 6371000.0;

    /// <summary>A provider that hands out content instantly and records what it was asked for.</summary>
    private sealed class RecordingProvider : IChunkContentProvider
    {
        public readonly List<SurfaceChunkKey> Acquired = new List<SurfaceChunkKey>();
        public readonly List<SurfaceChunkKey> Released = new List<SurfaceChunkKey>();
        public double costPerChunkMilliseconds = 1.0;

        public bool TryAcquire(SurfaceChunkKey key, out object content, out double costMilliseconds)
        {
            Acquired.Add(key);
            content = key;
            costMilliseconds = costPerChunkMilliseconds;
            return true;
        }

        public void Release(SurfaceChunkKey key, object content) { Released.Add(key); }
    }

    /// <summary>A provider whose first N requests are still in flight, like a worker thread.</summary>
    private sealed class PendingProvider : IChunkContentProvider
    {
        public int pendingCount;

        public bool TryAcquire(SurfaceChunkKey key, out object content, out double costMilliseconds)
        {
            costMilliseconds = 0.5;
            if (pendingCount > 0) { pendingCount--; content = null; return false; }
            content = key;
            return true;
        }

        public void Release(SurfaceChunkKey key, object content) { }
    }

    private static SurfaceChunkGrid Grid()
    {
        return new SurfaceChunkGrid(EarthRadiusMetres, 0.00335, 12, 8);
    }

    private static List<ChunkRequest> Requests(double distanceMetres, int count)
    {
        var list = new List<ChunkRequest>(count);
        for (int i = 0; i < count; i++)
        {
            var key = new SurfaceChunkKey(Planet.Procedural.PlanetCubeFace.PositiveZ, 4, i % 16, i / 16);
            list.Add(new ChunkRequest(key, distanceMetres * (i + 1), distanceMetres * (i + 1) * 2.0));
        }
        return list;
    }

    private static List<ChunkRequest> Single(SurfaceChunkKey key, double distance, double keepDistance)
    {
        return new List<ChunkRequest> { new ChunkRequest(key, distance, keepDistance) };
    }

    private static readonly List<ChunkRequest> None = new List<ChunkRequest>();

    [Test]
    public void NearestChunksAreBuiltFirst()
    {
        // The whole point of the ordering: the ground under the camera sharpens before the horizon.
        // Requests are deliberately supplied farthest-first so a streamer that trusted the input
        // order would fail.
        var streamer = new ChunkStreamer { maxBuildsPerFrame = 4, budgetMilliseconds = 1000.0 };
        var provider = new RecordingProvider { costPerChunkMilliseconds = 1.0 };

        var requests = new List<ChunkRequest>();
        for (int i = 0; i < 8; i++)
        {
            var key = new SurfaceChunkKey(Planet.Procedural.PlanetCubeFace.PositiveZ, 3, i, 0);
            double distance = 8000.0 - i * 1000.0;    // i = 0 is the farthest
            requests.Add(new ChunkRequest(key, distance, distance * 2.0));
        }

        streamer.Update(requests, provider, Vec3d.Zero, 1.0 / 60.0);

        Assert.AreEqual(4, provider.Acquired.Count, "the per-frame build cap must be honoured");
        for (int i = 0; i < provider.Acquired.Count; i++)
            Assert.AreEqual(requests[requests.Count - 1 - i].key, provider.Acquired[i],
                            "build " + i + " should have taken the " + (i + 1) + "th nearest chunk");
    }

    [Test]
    public void TheTimeBudgetStopsTheFrameEvenWhenTheCountCapHasRoom()
    {
        // Both limits have to bite, or a deep level on a slow frame stalls the whole game.
        var streamer = new ChunkStreamer { maxBuildsPerFrame = 100, budgetMilliseconds = 3.5 };
        var provider = new RecordingProvider { costPerChunkMilliseconds = 1.0 };

        ChunkStreamStats stats = streamer.Update(Requests(100.0, 40), provider, Vec3d.Zero, 1.0 / 60.0);

        Assert.AreEqual(4, stats.built, "3.5 ms at 1 ms each is four chunks, not forty");
        Assert.AreEqual(4.0, stats.buildMilliseconds, 1e-9);
        Assert.IsTrue(stats.budgetExhausted);
        Assert.AreEqual(36, stats.pending, "the rest stay queued for next frame");
    }

    [Test]
    public void AnInFlightChunkDoesNotBlockTheOnesBehindItOutOfOrder()
    {
        // A worker thread that is not finished must not be skipped over: building out of order would
        // hand the frame to a far chunk while the near one waits. The queue stops instead.
        var streamer = new ChunkStreamer { maxBuildsPerFrame = 16, budgetMilliseconds = 1000.0 };
        var provider = new PendingProvider { pendingCount = 2 };

        // Two frames of "not ready yet". Each one stops the queue at the nearest chunk rather than
        // skipping past it to build a farther one, and the stalled request stays counted as pending -
        // it is the most urgent thing outstanding.
        for (int frame = 0; frame < 2; frame++)
        {
            ChunkStreamStats stalled = streamer.Update(Requests(100.0, 10), provider, Vec3d.Zero, 1.0 / 60.0);
            Assert.AreEqual(0, stalled.built, "frame " + frame + " built a chunk out of order");
            Assert.AreEqual(10, stalled.pending, "frame " + frame + " lost a pending request");
            Assert.AreEqual(0, streamer.ResidentCount);
        }

        ChunkStreamStats drained = streamer.Update(Requests(100.0, 10), provider, Vec3d.Zero, 1.0 / 60.0);
        Assert.AreEqual(10, drained.built, "once they land the whole queue drains");
        Assert.AreEqual(0, drained.pending);
        Assert.AreEqual(10, streamer.ResidentCount);
    }

    [Test]
    public void AChunkInsideTheKeepBandSurvivesLeavingTheSelection()
    {
        // Hysteresis: when the LOD flips and a chunk drops out of the selection, it is kept until the
        // camera has provably moved past its band. Without this, a camera parked on a level boundary
        // rebuilds the same chunk every frame.
        var streamer = new ChunkStreamer { maxBuildsPerFrame = 64, budgetMilliseconds = 1000.0 };
        var provider = new RecordingProvider();
        var key = new SurfaceChunkKey(Planet.Procedural.PlanetCubeFace.PositiveY, 5, 3, 4);

        // Selected at 100 m, with a band out to 200 m.
        streamer.Update(Single(key, 100.0, 200.0), provider, Vec3d.Zero, 1.0 / 60.0);
        Assert.AreEqual(1, streamer.ResidentCount);

        // It drops out of the selection while the camera is still: it stays.
        ChunkStreamStats stats = streamer.Update(None, provider, Vec3d.Zero, 1.0 / 60.0);
        Assert.AreEqual(0, stats.evicted, "a still camera must not evict a chunk it just had");
        Assert.IsTrue(streamer.IsResident(key));

        // Camera walks 50 m away: the worst case is 150 m, still inside the band.
        stats = streamer.Update(None, provider, new Vec3d(50, 0, 0), 1.0 / 60.0);
        Assert.AreEqual(0, stats.evicted, "150 m is still inside a 200 m band");

        // Another 60 m: the worst case is 210 m and the chunk is provably outside. Now it goes.
        stats = streamer.Update(None, provider, new Vec3d(110, 0, 0), 1.0 / 60.0);
        Assert.AreEqual(1, stats.evicted);
        Assert.AreEqual(0, streamer.ResidentCount);
        Assert.AreEqual(key, provider.Released[0]);
    }

    [Test]
    public void TheResidentCapEvictsTheFarthestChunkFirst()
    {
        var streamer = new ChunkStreamer { maxBuildsPerFrame = 64, budgetMilliseconds = 1000.0, maxResident = 4 };

        // Sixteen requested at once, against a cap of four. The four that survive must be the nearest.
        var requests = new List<ChunkRequest>();
        for (int i = 0; i < 16; i++)
        {
            var key = new SurfaceChunkKey(Planet.Procedural.PlanetCubeFace.NegativeX, 4, i % 4, i / 4);
            double distance = 100.0 * (i + 1);
            requests.Add(new ChunkRequest(key, distance, distance * 100.0));
        }

        ChunkStreamStats stats = streamer.Update(requests, new RecordingProvider(), Vec3d.Zero, 1.0 / 60.0);

        Assert.AreEqual(4, streamer.ResidentCount, "the cap must hold");
        Assert.IsTrue(stats.residentCapReached);
        Assert.AreEqual(12, stats.evicted);
        for (int i = 0; i < 4; i++) Assert.IsTrue(streamer.IsResident(requests[i].key),
                                                  "the nearest four must be the survivors");
    }

    [Test]
    public void ASteadySelectionDoesNoWorkAfterItConverges()
    {
        // Build once, then hold: a converged streamer must acquire nothing and evict nothing, which
        // is what makes a still camera cost nothing.
        var streamer = new ChunkStreamer { maxBuildsPerFrame = 64, budgetMilliseconds = 1000.0 };
        var provider = new RecordingProvider();
        List<ChunkRequest> requests = Requests(500.0, 20);

        streamer.Update(requests, provider, Vec3d.Zero, 1.0 / 60.0);
        int afterFirst = provider.Acquired.Count;

        for (int frame = 0; frame < 60; frame++)
        {
            ChunkStreamStats stats = streamer.Update(requests, provider, Vec3d.Zero, 1.0 / 60.0);
            Assert.AreEqual(0, stats.built, "frame " + frame + " built something");
            Assert.AreEqual(0, stats.evicted, "frame " + frame + " evicted something");
            Assert.IsFalse(stats.budgetExhausted);
        }

        Assert.AreEqual(afterFirst, provider.Acquired.Count);
        Assert.AreEqual(20, streamer.ResidentCount);
    }

    [Test]
    public void BuildRequestsAppliesTheKeepFactorAndTheDistance()
    {
        SurfaceChunkGrid grid = Grid();
        var leaves = new List<SurfaceChunkKey>
        {
            new SurfaceChunkKey(Planet.Procedural.PlanetCubeFace.PositiveZ, 4, 1, 1),
            new SurfaceChunkKey(Planet.Procedural.PlanetCubeFace.PositiveZ, 4, 2, 1),
        };

        var distances = new Dictionary<SurfaceChunkKey, double>();
        distances[leaves[0]] = 1000.0;
        distances[leaves[1]] = 9000.0;

        var requests = new List<ChunkRequest>();
        ChunkStreamer.BuildRequests(grid, leaves,
            delegate (SurfaceChunkKey key) { return distances[key]; },
            50.0, 1080, 3.0, 0.5, 1.35, requests);

        Assert.AreEqual(2, requests.Count);
        Assert.AreEqual(1000.0, requests[0].distanceMetres, 0.0);
        Assert.AreEqual(9000.0, requests[1].distanceMetres, 0.0);

        // The keep distance is the level's own LOD distance widened by keepFactor, so a chunk stays
        // resident a little past the point that would otherwise drop it.
        double lod = ChunkLodSelector.LodDistanceMetres(grid, 4, 50.0, 1080, 3.0, 0.5);
        Assert.AreEqual(lod * 1.35, requests[0].keepDistanceMetres, 1e-9);
        Assert.AreEqual(lod * 1.35, requests[1].keepDistanceMetres, 1e-9);
        Assert.IsTrue(requests[0].keepDistanceMetres > lod, "the keep band must be wider than the selection");
    }

    [Test]
    public void ClearingReleasesEverything()
    {
        var streamer = new ChunkStreamer { maxBuildsPerFrame = 64, budgetMilliseconds = 1000.0 };
        var provider = new RecordingProvider();
        streamer.Update(Requests(100.0, 6), provider, Vec3d.Zero, 1.0 / 60.0);
        Assert.AreEqual(6, streamer.ResidentCount);

        streamer.Clear(provider);

        Assert.AreEqual(0, streamer.ResidentCount);
        Assert.AreEqual(6, provider.Released.Count, "every resident chunk must be handed back");
    }
}
