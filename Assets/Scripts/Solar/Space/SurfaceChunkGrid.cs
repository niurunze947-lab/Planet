using System;
using System.Collections.Generic;
using Planet.Procedural;

namespace Planet.Solar
{
    /// <summary>
    /// The six cube-face mappings, in <b>double</b> precision.
    ///
    /// <para>These are the same six formulas as <see cref="CubeSphere.CubePoint"/>, deliberately
    /// re-expressed for doubles instead of float2/float3. The chunk grid cannot use the float
    /// version: a level-20 chunk on Earth is 6 m across and its vertices are stored as float metres
    /// relative to the chunk origin, so the <i>direction</i> a vertex is sampled at still has to
    /// carry far more than 7 digits or two adjacent chunks would sample two different terrain
    /// points and open a crack. The float path stays where it belongs - the single-resolution globe
    /// mesh - and the ChunkGridParity test asserts the two agree where they overlap.</para>
    /// </summary>
    public static class CubeFaceMath
    {
        /// <summary>Unit-cube point for a face and a uv in [-1, 1]. Signs match <see cref="CubeSphere"/>.</summary>
        public static Vec3d CubePoint(PlanetCubeFace face, double u, double v)
        {
            switch (face)
            {
                case PlanetCubeFace.PositiveX: return new Vec3d(1.0, -v, -u);
                case PlanetCubeFace.NegativeX: return new Vec3d(-1.0, -v, u);
                case PlanetCubeFace.PositiveY: return new Vec3d(u, 1.0, v);
                case PlanetCubeFace.NegativeY: return new Vec3d(u, -1.0, -v);
                case PlanetCubeFace.PositiveZ: return new Vec3d(u, -v, 1.0);
                case PlanetCubeFace.NegativeZ: return new Vec3d(-u, -v, -1.0);
                default: throw new ArgumentOutOfRangeException("face");
            }
        }

        /// <summary>Unit direction for a face and a uv in [-1, 1].</summary>
        public static Vec3d Direction(PlanetCubeFace face, double u, double v)
        {
            Vec3d p = CubePoint(face, u, v);
            double length = p.Length;
            return length > 0.0 ? p / length : new Vec3d(0.0, 1.0, 0.0);
        }

        /// <summary>
        /// Inverse of <see cref="CubePoint"/>: which face a direction lands on and where in that
        /// face's uv square. Same tie-breaking (largest absolute component, <c>&gt;=</c> wins) as the
        /// float version, so both agree on face boundaries.
        /// </summary>
        /// <summary>
        /// Distance from the body's centre to the reference spheroid along a direction, in metres,
        /// evaluated in double so a terrain vertex 6 km from its chunk origin is not rounded by the
        /// radius itself.
        ///
        /// <para>Same formula and the same axis order as the sampler's reference axes
        /// (<c>shapeScale</c> with the polar axis already flattened), so a chunk vertex lands on the
        /// globe's surface and not a few metres off it. Deliberately not
        /// <c>PlanetCoordinates.ReferenceRadius</c>: that one takes a <c>double3</c>, which would
        /// drag <c>Unity.Mathematics</c> into this assembly's reference list, and it ignores
        /// <c>shapeScale</c>, which would put Haumea's chunks in the wrong place.</para>
        /// </summary>
        public static double ReferenceRadiusMetres(Vec3d direction, double radiusMetres,
                                                   double flattening, Vec3d shapeScale)
        {
            double polar = Math.Max(1.0 - Math.Max(0.0, Math.Min(0.2, flattening)), 0.8);
            double px = Math.Abs(shapeScale.x) > 1e-9 ? shapeScale.x : 1.0;
            double py = Math.Abs(shapeScale.y) > 1e-9 ? shapeScale.y * polar : polar;
            double pz = Math.Abs(shapeScale.z) > 1e-9 ? shapeScale.z : 1.0;

            double s = direction.x * direction.x / (px * px) +
                       direction.z * direction.z / (pz * pz) +
                       direction.y * direction.y / (py * py);
            return s > 0.0 ? radiusMetres / Math.Sqrt(s) : radiusMetres;
        }

        public static void DirectionToFace(Vec3d direction, out PlanetCubeFace face, out double u, out double v)
        {
            double ax = Math.Abs(direction.x), ay = Math.Abs(direction.y), az = Math.Abs(direction.z);

            if (ax >= ay && ax >= az)
            {
                face = direction.x >= 0.0 ? PlanetCubeFace.PositiveX : PlanetCubeFace.NegativeX;
                u = (direction.x >= 0.0 ? -direction.z : direction.z) / ax;
                v = -direction.y / ax;
            }
            else if (ay >= az)
            {
                face = direction.y >= 0.0 ? PlanetCubeFace.PositiveY : PlanetCubeFace.NegativeY;
                u = direction.x / ay;
                v = (direction.y >= 0.0 ? direction.z : -direction.z) / ay;
            }
            else
            {
                face = direction.z >= 0.0 ? PlanetCubeFace.PositiveZ : PlanetCubeFace.NegativeZ;
                u = (direction.z >= 0.0 ? direction.x : -direction.x) / az;
                v = -direction.y / az;
            }
        }
    }

    /// <summary>
    /// Address of one surface chunk: which cube face, which quadtree level, which cell in that
    /// face's 2^level x 2^level grid.
    ///
    /// The address is stable across frames and across machines, so it is usable as a cache key, a
    /// streaming request and a save-game record - unlike a mesh reference or a world position,
    /// neither of which survives a floating-origin rebase.
    /// </summary>
    public struct SurfaceChunkKey : IEquatable<SurfaceChunkKey>
    {
        public PlanetCubeFace face;
        public byte level;
        public int x;
        public int y;

        public SurfaceChunkKey(PlanetCubeFace face, int level, int x, int y)
        {
            this.face = face;
            this.level = (byte)Math.Max(0, Math.Min(31, level));
            this.x = x;
            this.y = y;
        }

        public bool IsValid
        {
            get
            {
                int n = 1 << level;
                return x >= 0 && y >= 0 && x < n && y < n;
            }
        }

        public bool Equals(SurfaceChunkKey other)
        {
            return face == other.face && level == other.level && x == other.x && y == other.y;
        }

        public override bool Equals(object obj) { return obj is SurfaceChunkKey && Equals((SurfaceChunkKey)obj); }

        public override int GetHashCode()
        {
            unchecked { return ((int)face * 397 ^ level) * 397 ^ (x * 73856093 ^ y * 19349663); }
        }

        public override string ToString()
        {
            return string.Format("{0}/L{1}/{2},{3}", face, level, x, y);
        }
    }

    /// <summary>Which of a chunk's four neighbours, in its own face-local uv axes.</summary>
    public enum ChunkNeighbourDirection { NegativeU, PositiveU, NegativeV, PositiveV }

    /// <summary>
    /// A cube-sphere chunk grid with a quadtree over it.
    ///
    /// <para><b>Why chunked local coordinates.</b> The existing globe stores every vertex on a
    /// unit-radius sphere and scales the whole mesh by the body radius. That representation
    /// quantises height at <c>radius x 1.19e-7</c>: on Earth, 0.76 m. No amount of better terrain
    /// maths gets past it, because the vertex lives at magnitude 1.0 where a float's step is
    /// 1.19e-7 <i>radii</i>.</para>
    ///
    /// <para>A chunk instead stores its vertices as float <b>metres relative to the chunk's own
    /// origin</b>. A level-10 chunk on Earth is 6.2 km across, so its worst-case float step is
    /// <c>6222 x 1.19e-7</c> = <b>0.74 mm</b> - a factor of a thousand better, from the same
    /// format, purely by choosing where the origin is. That is the entire trick: precision is a
    /// property of distance-from-origin, not of the number format.</para>
    ///
    /// <para><b>Seams.</b> A chunk's sample directions come from a <i>global</i> signed integer grid
    /// index <c>(2*gx - N)/N</c>, exactly as <see cref="CubeSphere.Generate"/> does, with
    /// <c>gx = chunk.x * resolution + ix</c>. Two chunks that share an edge compute the same
    /// integer <c>gx</c> for the shared vertices, so they sample bit-identical directions and the
    /// seam closes without any welding.</para>
    /// </summary>
    public sealed class SurfaceChunkGrid
    {
        /// <summary>Equatorial radius of the body, in metres. Diagnostics only; directions are unit.</summary>
        public double bodyRadiusMetres;

        /// <summary>Flattening, so chunk origins sit on the reference spheroid rather than a sphere.</summary>
        public double flattening;

        /// <summary>
        /// Triaxial shape multiplier, matching <c>PlanetPhysicalParameters.shapeScale</c>. Only
        /// Haumea uses anything other than (1,1,1), but a chunk origin that ignores it lands
        /// hundreds of kilometres off that body's surface.
        /// </summary>
        public Vec3d referenceShapeScale = new Vec3d(1.0, 1.0, 1.0);

        /// <summary>
        /// Finest quadtree level. 2^level chunks per face edge. 20 gives 9.5 m chunks and 60 cm grid
        /// spacing on Earth, which is the finest the integer grid index can address (a level-20 face
        /// already has 2^20 chunks per edge). The distance LOD decides how deep to actually go, so a
        /// high ceiling costs nothing while the camera is in orbit.
        /// </summary>
        public int maxLevel = 20;

        /// <summary>Segments per chunk edge. Chunk vertices are (resolution+1)^2.</summary>
        public int resolution = 16;

        /// <summary>
        /// Worst-case cube-sphere stretch, used so the LOD and precision budgets are conservative
        /// rather than optimistic. The normalised cube mapping is 1.0 at a face centre and sqrt(3)
        /// at a face corner, where chunks cover the most arc for the same grid index span.
        /// </summary>
        public double chunkStretchFactor = 1.7320508075688772;

        public SurfaceChunkGrid() { }

        public SurfaceChunkGrid(double bodyRadiusMetres, double flattening, int maxLevel, int resolution)
        {
            this.bodyRadiusMetres = bodyRadiusMetres;
            this.flattening = flattening;
            this.maxLevel = Math.Max(0, Math.Min(20, maxLevel));
            this.resolution = Math.Max(1, Math.Min(64, resolution));
        }

        // ---------------------------------------------------------------------------------------
        // Grid geometry
        // ---------------------------------------------------------------------------------------

        /// <summary>Chunks per face edge at a level.</summary>
        public int CellsPerFace(int level) { return 1 << Math.Max(0, Math.Min(20, level)); }

        /// <summary>Vertices per face edge at a level; the N in <c>(2*gx - N)/N</c>.</summary>
        public int GridSize(int level) { return CellsPerFace(level) * resolution; }

        /// <summary>Angular span of one chunk edge, in radians, at the face centre.</summary>
        public double ChunkSpanRadians(int level) { return (Math.PI * 0.5) / CellsPerFace(level); }

        /// <summary>Arc length of one chunk edge on the reference sphere, in metres.</summary>
        public double ChunkSizeMetres(int level) { return bodyRadiusMetres * ChunkSpanRadians(level); }

        /// <summary>
        /// Worst-case chunk edge after the cube-sphere stretch. The precision budget uses this, not
        /// <see cref="ChunkSizeMetres"/>, so a chunk in a face corner is budgeted for what it
        /// actually is rather than for what a face-centre chunk would be.
        /// </summary>
        public double ChunkWorstSizeMetres(int level) { return ChunkSizeMetres(level) * chunkStretchFactor; }

        /// <summary>
        /// Largest chunk-local coordinate any vertex of a chunk can have: half the patch diagonal.
        ///
        /// This is the magnitude that sets the float32 step for the whole chunk, so it is the number
        /// every precision claim in the audit tool and the tests is derived from. Taking the edge
        /// instead of the diagonal would understate the step by 41%.
        /// </summary>
        public double ChunkLocalExtentMetres(int level)
        {
            return ChunkWorstSizeMetres(level) * 0.7071067811865476;
        }

        // ---------------------------------------------------------------------------------------
        // Sampling - the seam rule
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Direction of sample <c>(ix, iy)</c> inside a chunk, in <c>[0, resolution]</c>.
        ///
        /// The signed integer grid coordinate is formed <b>before</b> the divide. That is what makes
        /// two neighbouring chunks produce bit-identical directions on their shared edge; forming
        /// the local uv first and offsetting it by the chunk's uv would give the two sides slightly
        /// different roundings, which the finite-difference terrain normals then amplify into a
        /// visible line.
        /// </summary>
        public Vec3d SampleDirection(SurfaceChunkKey key, int ix, int iy)
        {
            int n = GridSize(key.level);
            if (n <= 0) n = resolution;
            double gx = key.x * (double)resolution + ix;
            double gy = key.y * (double)resolution + iy;
            double u = (2.0 * gx - n) / n;
            double v = (2.0 * gy - n) / n;
            return CubeFaceMath.Direction(key.face, u, v);
        }

        /// <summary>Direction at the centre of a chunk, used as that chunk's origin direction.</summary>
        public Vec3d ChunkCenterDirection(SurfaceChunkKey key)
        {
            double half = 1.0 / CellsPerFace(key.level);
            double u = -1.0 + (2.0 * key.x + 1.0) * half;
            double v = -1.0 + (2.0 * key.y + 1.0) * half;
            return CubeFaceMath.Direction(key.face, u, v);
        }

        /// <summary>
        /// The chunk's origin, in the body-fixed frame, double metres.
        ///
        /// The origin is the chunk's <i>centre</i>, not a corner: centring halves the largest
        /// coordinate any vertex of the chunk can have, and therefore halves the float step the
        /// whole chunk pays. It is the cheapest precision win available and it costs one direction
        /// evaluation per chunk.
        /// </summary>
        public Vec3d ChunkOriginInBodyMetres(SurfaceChunkKey key)
        {
            Vec3d direction = ChunkCenterDirection(key);
            return direction * CubeFaceMath.ReferenceRadiusMetres(
                direction, bodyRadiusMetres, flattening, referenceShapeScale);
        }

        // ---------------------------------------------------------------------------------------
        // Quadtree
        // ---------------------------------------------------------------------------------------

        public bool TryParent(SurfaceChunkKey key, out SurfaceChunkKey parent)
        {
            if (key.level == 0) { parent = default(SurfaceChunkKey); return false; }
            parent = new SurfaceChunkKey(key.face, key.level - 1, key.x >> 1, key.y >> 1);
            return true;
        }

        /// <summary>Fills <paramref name="children"/> with the four sub-chunks. Returns false at max level.</summary>
        public bool TryChildren(SurfaceChunkKey key, SurfaceChunkKey[] children)
        {
            if (children == null || children.Length < 4) throw new ArgumentException("needs 4 slots", "children");
            if (key.level >= maxLevel) return false;

            int level = key.level + 1;
            int cx = key.x << 1, cy = key.y << 1;
            children[0] = new SurfaceChunkKey(key.face, level, cx, cy);
            children[1] = new SurfaceChunkKey(key.face, level, cx + 1, cy);
            children[2] = new SurfaceChunkKey(key.face, level, cx, cy + 1);
            children[3] = new SurfaceChunkKey(key.face, level, cx + 1, cy + 1);
            return true;
        }

        /// <summary>
        /// The neighbouring chunk in the given direction, crossing onto another cube face when the
        /// edge is a cube edge.
        ///
        /// Cross-face adjacency is resolved by stepping one chunk <i>past</i> the edge in the parent
        /// face's uv square and asking <see cref="CubeFaceMath.DirectionToFace"/> where that lands.
        /// Writing out the 24 edge-pair cases by hand is the classic way to get one of them mirrored;
        /// this way there is nothing to get wrong, and it works identically at every level.
        /// </summary>
        public bool TryNeighbour(SurfaceChunkKey key, ChunkNeighbourDirection direction, out SurfaceChunkKey neighbour)
        {
            int n = CellsPerFace(key.level);
            double half = 1.0 / n;
            double u = -1.0 + (2.0 * key.x + 1.0) * half;
            double v = -1.0 + (2.0 * key.y + 1.0) * half;

            switch (direction)
            {
                case ChunkNeighbourDirection.NegativeU: u -= 2.0 * half; break;
                case ChunkNeighbourDirection.PositiveU: u += 2.0 * half; break;
                case ChunkNeighbourDirection.NegativeV: v -= 2.0 * half; break;
                default: v += 2.0 * half; break;
            }

            // Nudge off the boundary so DirectionToFace cannot tie-break back onto the same face.
            const double epsilon = 1e-9;
            if (u > 1.0) u = 1.0 + epsilon;
            if (u < -1.0) u = -1.0 - epsilon;
            if (v > 1.0) v = 1.0 + epsilon;
            if (v < -1.0) v = -1.0 - epsilon;

            PlanetCubeFace face;
            double nu, nv;
            CubeFaceMath.DirectionToFace(CubeFaceMath.CubePoint(key.face, u, v), out face, out nu, out nv);

            int nx = (int)Math.Floor((nu + 1.0) * 0.5 * n);
            int ny = (int)Math.Floor((nv + 1.0) * 0.5 * n);
            nx = Math.Max(0, Math.Min(n - 1, nx));
            ny = Math.Max(0, Math.Min(n - 1, ny));

            neighbour = new SurfaceChunkKey(face, key.level, nx, ny);
            return true;
        }

        /// <summary>
        /// Enumerates the leaf chunks of an adaptive quadtree, given a desired level per chunk.
        ///
        /// The quadtree is descended from level 0 and a chunk is split when any of its four children
        /// wants to be finer than it is. That is what keeps the resident set a <i>tree</i> rather
        /// than an arbitrary soup of chunks, which in turn is what makes the level-step rule below
        /// enough to bound cracks without per-edge stitching on every chunk.
        /// </summary>
        public void CollectLeaves(Func<SurfaceChunkKey, int> desiredLevel, List<SurfaceChunkKey> leaves)
        {
            if (desiredLevel == null) throw new ArgumentNullException("desiredLevel");
            if (leaves == null) throw new ArgumentNullException("leaves");
            leaves.Clear();

            for (int f = 0; f < 6; f++)
            {
                var root = new SurfaceChunkKey((PlanetCubeFace)f, 0, 0, 0);
                Descend(root, desiredLevel, leaves);
            }
        }

        private void Descend(SurfaceChunkKey key, Func<SurfaceChunkKey, int> desiredLevel, List<SurfaceChunkKey> leaves)
        {
            int wanted = desiredLevel(key);
            if (key.level >= maxLevel || key.level >= wanted)
            {
                leaves.Add(key);
                return;
            }

            var children = new SurfaceChunkKey[4];
            if (!TryChildren(key, children)) { leaves.Add(key); return; }
            for (int i = 0; i < 4; i++) Descend(children[i], desiredLevel, leaves);
        }

        /// <summary>
        /// True when <paramref name="a"/> and <paramref name="b"/> differ by more than one level and
        /// therefore need an edge stitch. The leaf collection keeps steps at 1 or less by
        /// construction; this exists so the invariant can be asserted in tests and reported by the
        /// audit tool rather than merely assumed.
        /// </summary>
        public static bool NeedsStitch(SurfaceChunkKey a, SurfaceChunkKey b)
        {
            return Math.Abs(a.level - b.level) > 1;
        }
    }

    /// <summary>
    /// Distance-dependent level selection: the standard screen-space-error rule.
    ///
    /// A chunk at level L is drawn while its geometric error projects to fewer than
    /// <see cref="pixelTolerance"/> pixels; the moment it would project larger, the four children
    /// take over. Expressing the rule in pixels rather than in metres is what makes one setting
    /// work for a 2,440 km Mercury and a 696,340 km Sun.
    /// </summary>
    public static class ChunkLodSelector
    {
        /// <summary>
        /// Geometric error of a chunk: how far its flat/sampled surface can be from the true
        /// surface. Taken as the chunk edge times <paramref name="reliefRatio"/>, the fraction of
        /// the chunk size that terrain can deviate by within the chunk. 0.5 is a safe default for
        /// procedural terrain whose detail amplitude is below the grid spacing.
        /// </summary>
        public static double GeometricErrorMetres(SurfaceChunkGrid grid, int level, double reliefRatio)
        {
            return grid.ChunkWorstSizeMetres(level) * reliefRatio;
        }

        /// <summary>Distance beyond which a level's error is under the pixel tolerance.</summary>
        public static double LodDistanceMetres(SurfaceChunkGrid grid, int level, double verticalFovDegrees,
                                              int screenHeightPixels, double pixelTolerance, double reliefRatio)
        {
            double error = GeometricErrorMetres(grid, level, reliefRatio);
            double halfFov = verticalFovDegrees * Math.PI / 360.0;
            double tanHalf = Math.Tan(halfFov);
            if (!(tanHalf > 1e-9)) tanHalf = 1e-9;
            if (!(pixelTolerance > 0.0)) pixelTolerance = 1.0;
            return error * screenHeightPixels / (2.0 * tanHalf * pixelTolerance);
        }

        /// <summary>
        /// The coarsest level that is still sharp enough at <paramref name="distanceMetres"/>.
        ///
        /// <para>The rule is "draw level L while its geometric error projects to fewer than
        /// <paramref name="pixelTolerance"/> pixels", which rearranges to
        /// <c>distance >= LodDistanceMetres(L)</c>. Geometric error <i>shrinks</i> as levels get
        /// finer, so <c>LodDistanceMetres</c> shrinks too, and the set of acceptable levels is always
        /// a suffix: <c>{L : LodDistance(L) &lt;= d}</c>. The coarsest member of that suffix is the
        /// cheapest level that meets the tolerance, so the scan returns the first level that
        /// qualifies - and only clamps to <see cref="SurfaceChunkGrid.maxLevel"/> when even that is
        /// too coarse to satisfy the tolerance.</para>
        ///
        /// <para>Getting this backwards is silent and expensive: selecting the <i>finest</i>
        /// qualifying level instead makes the terrain cost grow with distance from the camera.</para>
        /// </summary>
        public static int SelectLevel(SurfaceChunkGrid grid, double distanceMetres, double verticalFovDegrees,
                                      int screenHeightPixels, double pixelTolerance, double reliefRatio)
        {
            for (int level = 0; level <= grid.maxLevel; level++)
            {
                if (distanceMetres >= LodDistanceMetres(grid, level, verticalFovDegrees,
                                                        screenHeightPixels, pixelTolerance, reliefRatio))
                    return level;
            }

            // Every level's error is still over tolerance: max out the detail and let the caller's
            // chunk budget decide whether it can afford it.
            return grid.maxLevel;
        }

        /// <summary>
        /// Per-chunk level for the whole grid: the level its own distance asks for, plus the split
        /// pressure its neighbours exert.
        ///
        /// Without the neighbour term a chunk whose centre is just past a threshold would sit one
        /// level coarser than the chunk beside it, and the leaf rule would split it anyway - the
        /// point of pre-splitting is to keep the <i>resulting</i> tree within a one-level step
        /// everywhere, so no chunk ever has to stitch against a neighbour two levels finer.
        /// </summary>
        public static Func<SurfaceChunkKey, int> BuildLevelFunction(
            SurfaceChunkGrid grid, Func<SurfaceChunkKey, double> distanceForChunk,
            double verticalFovDegrees, int screenHeightPixels, double pixelTolerance, double reliefRatio)
        {
            if (grid == null) throw new ArgumentNullException("grid");
            if (distanceForChunk == null) throw new ArgumentNullException("distanceForChunk");

            return delegate (SurfaceChunkKey key)
            {
                double nearest = distanceForChunk(key);

                // A chunk is also split when a neighbour closer to the camera would be drawn at a
                // finer level: take the finest level any of the 8 surrounding chunks wants.
                int wanted = ChunkLodSelector.SelectLevel(grid, nearest, verticalFovDegrees,
                                                          screenHeightPixels, pixelTolerance, reliefRatio);
                int step = Math.Max(1, grid.resolution);
                for (int dx = -1; dx <= 1; dx += 2)
                for (int dy = -1; dy <= 1; dy += 2)
                {
                    // Sample the *edge midpoint* of the half-size neighbour rather than the chunk
                    // itself, which is a cheap way to bias splitting towards the camera side.
                    double probeU = key.x + (dx > 0 ? 1.0 : 0.0);
                    double probeV = key.y + (dy > 0 ? 1.0 : 0.0);
                    var probe = new SurfaceChunkKey(key.face, key.level,
                        (int)Math.Floor(probeU), (int)Math.Floor(probeV));
                    if (!probe.IsValid) continue;
                    double d = distanceForChunk(probe);
                    if (d < nearest) nearest = d;
                }
                int neighbourWanted = ChunkLodSelector.SelectLevel(grid, nearest, verticalFovDegrees,
                                                                   screenHeightPixels, pixelTolerance, reliefRatio);
                if (neighbourWanted > wanted) wanted = neighbourWanted;
                return wanted;
            };
        }
    }
}
