using System;
using System.Collections.Generic;
using UnityEngine;

namespace Planet.Solar
{
    /// <summary>One chunk the LOD selection wants resident, with the priority it was ranked by.</summary>
    public struct ChunkRequest
    {
        public SurfaceChunkKey key;

        /// <summary>Camera distance in metres. Lower is more urgent.</summary>
        public double distanceMetres;

        /// <summary>Hysteresis band: a resident chunk is kept until the camera is this multiple of its LOD distance away.</summary>
        public double keepDistanceMetres;

        public ChunkRequest(SurfaceChunkKey key, double distanceMetres, double keepDistanceMetres)
        {
            this.key = key;
            this.distanceMetres = distanceMetres;
            this.keepDistanceMetres = keepDistanceMetres;
        }
    }

    /// <summary>
    /// What the streamer asks of whoever can actually produce a chunk.
    ///
    /// The interface is deliberately synchronous and cost-reporting rather than <c>async</c>: the
    /// streamer already time-slices, and a caller that wants to build on a worker thread can do so
    /// inside <see cref="TryAcquire"/> and return false until the job lands. That keeps the
    /// budget logic in one place instead of splitting it between here and a Task scheduler.
    /// </summary>
    public interface IChunkContentProvider
    {
        /// <summary>
        /// Produces content for a chunk, or returns false if it is not ready yet.
        /// </summary>
        /// <param name="key">Chunk to build.</param>
        /// <param name="content">Opaque handle the streamer stores and hands back to <see cref="Release"/>.</param>
        /// <param name="costMilliseconds">Measured cost of the work just done, for the frame budget.</param>
        bool TryAcquire(SurfaceChunkKey key, out object content, out double costMilliseconds);

        /// <summary>Frees a chunk's content. Called for evictions and for everything still resident on dispose.</summary>
        void Release(SurfaceChunkKey key, object content);
    }

    /// <summary>What the streamer did last frame. Surfaced by the HUD and the audit tool.</summary>
    public struct ChunkStreamStats
    {
        public int desired;
        public int resident;
        public int built;
        public int evicted;
        public int pending;
        public double buildMilliseconds;
        public bool budgetExhausted;
        public bool residentCapReached;

        public override string ToString()
        {
            return string.Format(
                "chunks resident {0}/{1} desired, built {2}, evicted {3}, pending {4}, {5:F2} ms{6}{7}",
                resident, desired, built, evicted, pending, buildMilliseconds,
                budgetExhausted ? " [budget]" : "", residentCapReached ? " [cap]" : "");
        }
    }

    /// <summary>
    /// What the streamer remembers about one resident chunk.
    ///
    /// The distance is remembered rather than recomputed because the point of the eviction band is
    /// to judge a chunk <i>after</i> it has left the selection, and once it has left the selection
    /// nobody is evaluating its distance any more.
    /// </summary>
    internal struct ResidentChunk
    {
        public object content;

        /// <summary>Camera distance when this chunk was last selected, in metres.</summary>
        public double distanceMetres;

        /// <summary>Distance inside which it survives leaving the selection, in metres.</summary>
        public double keepDistanceMetres;
    }

    /// <summary>
    /// Multi-level LOD streaming: decides what surface detail should exist, and builds it without
    /// hitching.
    ///
    /// <para>Three rules make this stream rather than pop:</para>
    /// <list type="number">
    /// <item><b>A hard per-frame budget.</b> At most <see cref="maxBuildsPerFrame"/> chunks and
    /// <see cref="budgetMilliseconds"/> of work. A camera dropped onto a planet wants thousands of
    /// chunks in one frame; building them all is a multi-second freeze. The budget turns that into
    /// a coarse planet that sharpens over the next second.</item>
    /// <item><b>Nearest first.</b> Requests are serviced in distance order, so the ground under the
    /// camera is sharp before the horizon is. A queue that builds in request order produces the
    /// worst possible artefact: a detailed patchwork with a blurry hole exactly where the camera
    /// is looking.</item>
    /// <item><b>Hysteresis on eviction.</b> A chunk that leaves the selection is kept while the
    /// camera has not yet moved far enough to prove it is outside the band. Without it, a camera
    /// hovering at a level boundary thrashes: build, evict, build, evict, every frame, and the mesh
    /// visibly flickers.</item>
    /// </list>
    /// </summary>
    public sealed class ChunkStreamer
    {
        /// <summary>Chunks built per frame. 2 keeps a hitch under ~10 ms for a 4 ms chunk.</summary>
        public int maxBuildsPerFrame = 2;

        /// <summary>Wall-clock ceiling for chunk work in one frame, in milliseconds.</summary>
        public double budgetMilliseconds = 4.0;

        /// <summary>Resident chunks are kept until the camera is this far past their LOD distance.</summary>
        public double keepFactor = 1.35;

        /// <summary>Hard cap on resident chunks. Guards against a level spike allocating without bound.</summary>
        public int maxResident = 1024;

        private readonly Dictionary<SurfaceChunkKey, ResidentChunk> _resident =
            new Dictionary<SurfaceChunkKey, ResidentChunk>();
        private readonly List<SurfaceChunkKey> _evictionScratch = new List<SurfaceChunkKey>();
        private readonly List<ChunkRequest> _pending = new List<ChunkRequest>();
        private readonly HashSet<SurfaceChunkKey> _wanted = new HashSet<SurfaceChunkKey>();
        private ChunkStreamStats _stats;
        private double _clock;
        private Vec3d _lastCameraPositionBodyMetres;
        private bool _hasLastCameraPosition;
        private double _cameraTravelSinceEvictionCheckMetres;

        public ChunkStreamStats Stats { get { return _stats; } }
        public int ResidentCount { get { return _resident.Count; } }

        public bool IsResident(SurfaceChunkKey key) { return _resident.ContainsKey(key); }

        public object Get(SurfaceChunkKey key)
        {
            ResidentChunk entry;
            return _resident.TryGetValue(key, out entry) ? entry.content : null;
        }

        /// <summary>Drops everything. Called when the focus body changes - the chunk set is body-specific.</summary>
        public void Clear(IChunkContentProvider provider)
        {
            if (provider != null)
                foreach (KeyValuePair<SurfaceChunkKey, ResidentChunk> pair in _resident)
                    provider.Release(pair.Key, pair.Value.content);

            _resident.Clear();
            _pending.Clear();
            _stats = default(ChunkStreamStats);
            _hasLastCameraPosition = false;
            _cameraTravelSinceEvictionCheckMetres = 0.0;
        }

        /// <summary>
        /// One streaming step. <paramref name="desired"/> is this frame's LOD selection, in any
        /// order; <paramref name="cameraPositionBodyMetres"/> is used for the eviction band and for
        /// the resident cap's eviction order. Returns what this step did.
        /// </summary>
        public ChunkStreamStats Update(IList<ChunkRequest> desired, IChunkContentProvider provider,
                                       Vec3d cameraPositionBodyMetres, double frameSeconds)
        {
            if (provider == null) throw new ArgumentNullException("provider");
            if (desired == null) throw new ArgumentNullException("desired");

            _clock += frameSeconds;
            var stats = new ChunkStreamStats();
            stats.desired = desired.Count;

            // How far the camera has moved since the last eviction check. This is the only sound
            // basis for judging a chunk that is no longer in the selection: its exact new distance is
            // unknown, but it cannot have grown by more than the camera's own travel.
            double cameraTravel = 0.0;
            if (_hasLastCameraPosition)
                cameraTravel = (cameraPositionBodyMetres - _lastCameraPositionBodyMetres).Length;
            _lastCameraPositionBodyMetres = cameraPositionBodyMetres;
            _hasLastCameraPosition = true;
            _cameraTravelSinceEvictionCheckMetres += cameraTravel;

            _wanted.Clear();
            for (int i = 0; i < desired.Count; i++) _wanted.Add(desired[i].key);

            // ---- 1. evict: resident chunks that are no longer wanted AND have provably left their
            // keep band. The band is what stops a camera parked on a level boundary from rebuilding
            // the same two chunks forever.
            //
            // A chunk that has dropped out of the selection keeps its last measured distance. Walking
            // straight away from it is the fastest it can recede, so it is only safe to evict once
            // even that worst case puts it outside the band. Until then it stays: the camera is
            // hovering on a threshold, which is exactly what the band is for.
            _evictionScratch.Clear();
            foreach (KeyValuePair<SurfaceChunkKey, ResidentChunk> pair in _resident)
            {
                if (_wanted.Contains(pair.Key)) continue;
                ResidentChunk entry = pair.Value;
                double worstCaseDistance = entry.distanceMetres + _cameraTravelSinceEvictionCheckMetres;
                if (worstCaseDistance <= entry.keepDistanceMetres) continue;
                _evictionScratch.Add(pair.Key);
            }
            for (int i = 0; i < _evictionScratch.Count; i++)
            {
                ResidentChunk entry = _resident[_evictionScratch[i]];
                provider.Release(_evictionScratch[i], entry.content);
                _resident.Remove(_evictionScratch[i]);
                stats.evicted++;
            }
            if (stats.evicted > 0) _cameraTravelSinceEvictionCheckMetres = 0.0;

            // ---- 1b. refresh the remembered distances for everything still selected, which also
            // arms the band for the frame it drops out on.
            for (int i = 0; i < desired.Count; i++)
            {
                ChunkRequest r = desired[i];
                ResidentChunk entry;
                if (!_resident.TryGetValue(r.key, out entry)) continue;
                entry.distanceMetres = r.distanceMetres;
                entry.keepDistanceMetres = r.keepDistanceMetres;
                _resident[r.key] = entry;
            }

            // ---- 2. collect what is missing, then service it nearest-first.
            _pending.Clear();
            for (int i = 0; i < desired.Count; i++)
            {
                ChunkRequest r = desired[i];
                if (_resident.ContainsKey(r.key)) continue;
                _pending.Add(r);
            }
            _pending.Sort(CompareByDistance);
            stats.pending = _pending.Count;

            // ---- 3. build under the budget. nearest-first is the whole reason the ground under the
            // camera sharpens before the horizon.
            double spent = 0.0;
            int built = 0;
            int cursor = 0;
            while (cursor < _pending.Count && built < maxBuildsPerFrame && spent < budgetMilliseconds)
            {
                ChunkRequest r = _pending[cursor];
                object content;
                double cost;
                if (!provider.TryAcquire(r.key, out content, out cost))
                {
                    // Not ready (a worker thread still owns it). Leave it queued and stop: building
                    // out of order would hand the frame to a far chunk while the near one waits.
                    // The cursor deliberately does not advance, so this request is still counted as
                    // pending - it is the most urgent one outstanding.
                    break;
                }

                // Only a serviced request advances the cursor, which makes `cursor` exactly "how many
                // of the queue were dealt with" and `pending` the rest.
                cursor++;
                _resident[r.key] = new ResidentChunk
                {
                    content = content,
                    distanceMetres = r.distanceMetres,
                    keepDistanceMetres = r.keepDistanceMetres
                };
                spent += cost;
                built++;
            }

            stats.built = built;
            stats.buildMilliseconds = spent;
            stats.budgetExhausted = spent >= budgetMilliseconds;
            stats.pending = Math.Max(0, _pending.Count - cursor);

            // ---- 4. enforce the resident cap, evicting the furthest chunk first.
            if (maxResident > 0 && _resident.Count > maxResident)
            {
                stats.residentCapReached = true;
                var ranked = new List<KeyValuePair<SurfaceChunkKey, double>>();
                foreach (KeyValuePair<SurfaceChunkKey, ResidentChunk> pair in _resident)
                    ranked.Add(new KeyValuePair<SurfaceChunkKey, double>(pair.Key, pair.Value.distanceMetres));

                ranked.Sort(delegate (KeyValuePair<SurfaceChunkKey, double> a, KeyValuePair<SurfaceChunkKey, double> b)
                {
                    return b.Value.CompareTo(a.Value);
                });
                for (int i = 0; i < ranked.Count && _resident.Count > maxResident; i++)
                {
                    ResidentChunk entry = _resident[ranked[i].Key];
                    provider.Release(ranked[i].Key, entry.content);
                    _resident.Remove(ranked[i].Key);
                    stats.evicted++;
                }
            }

            stats.resident = _resident.Count;
            _stats = stats;
            return stats;
        }

        private static int CompareByDistance(ChunkRequest a, ChunkRequest b)
        {
            return a.distanceMetres.CompareTo(b.distanceMetres);
        }

        /// <summary>
        /// Turns the LOD level function into the request list the streamer consumes, applying the
        /// hysteresis band.
        ///
        /// Separated from <see cref="Update"/> so a caller can feed the same list to a culling pass
        /// or to the audit tool without re-running the selection.
        /// </summary>
        public static void BuildRequests(SurfaceChunkGrid grid, List<SurfaceChunkKey> leaves,
                                         Func<SurfaceChunkKey, double> distanceForChunk,
                                         double verticalFovDegrees, int screenHeightPixels,
                                         double pixelTolerance, double reliefRatio, double keepFactor,
                                         List<ChunkRequest> requests)
        {
            if (grid == null) throw new ArgumentNullException("grid");
            if (leaves == null) throw new ArgumentNullException("leaves");
            if (requests == null) throw new ArgumentNullException("requests");

            requests.Clear();
            for (int i = 0; i < leaves.Count; i++)
            {
                SurfaceChunkKey key = leaves[i];
                double distance = distanceForChunk(key);
                double lod = ChunkLodSelector.LodDistanceMetres(grid, key.level, verticalFovDegrees,
                                                               screenHeightPixels, pixelTolerance, reliefRatio);
                requests.Add(new ChunkRequest(key, distance, lod * Math.Max(1.0, keepFactor)));
            }
        }
    }
}
