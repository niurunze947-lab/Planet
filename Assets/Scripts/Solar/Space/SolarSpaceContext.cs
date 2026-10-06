using System;
using UnityEngine;

namespace Planet.Solar
{
    /// <summary>How much of a body's rendering is worth paying for at its current screen size.</summary>
    public enum BodyRenderTier
    {
        /// <summary>Large on screen: full procedural surface, shells, rings.</summary>
        Full = 0,

        /// <summary>A few pixels: the surface is kept but the shells and rings are not worth a pass.</summary>
        Proxy = 1,

        /// <summary>Sub-pixel: only the body's presence matters, which the orbit line already shows.</summary>
        Marker = 2,

        /// <summary>Projected size is below the displayable threshold; submitting it costs a draw call for nothing.</summary>
        Skipped = 3
    }

    /// <summary>
    /// Binds <see cref="SolarSystemSimulation"/> to the space frame tree, and is the single place
    /// that answers "which coordinate system is this point in".
    ///
    /// <para>It is created automatically by the simulation, so an existing scene needs no rewiring;
    /// the scene builder also adds it explicitly so it is visible in the Inspector.</para>
    ///
    /// <para><b>Two coordinate systems, one conversion point.</b> Everything cosmic is kept in
    /// double metres in <see cref="SpaceFrameTree"/>; everything the renderer sees goes through
    /// <see cref="RenderOrigin"/>. There is deliberately no property that hands out an absolute
    /// position in render units - asking for one is the bug this class exists to prevent.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SolarSpaceContext : MonoBehaviour
    {
        [Header("Hierarchy")]
        [Tooltip("Where the Sun sits inside the galaxy, in metres. Zero keeps the stellar frame at " +
                 "the galactic origin; the rung exists so a galactic layer only has to change this number.")]
        public Vector3 solInGalaxyMetres = Vector3.zero;

        [Header("Surface chunk grid")]
        [Tooltip("Finest quadtree level. 2^level chunks per cube face edge. 20 gives 9.5 m chunks and " +
                 "60 cm grid spacing on Earth, which is the finest the integer grid index can address.")]
        [Range(0, 20)] public int maxChunkLevel = 20;
        [Tooltip("Segments per chunk edge. Chunk vertices are (resolution+1)^2.")]
        [Range(1, 64)] public int chunkResolution = 16;
        [Tooltip("Chunks beyond this are evicted even if the LOD still wants them.")]
        public int maxResidentChunks = 1024;

        [Header("LOD")]
        [Tooltip("Screen-space error budget in pixels. The level is split when its geometric error would exceed this.")]
        public double lodPixelTolerance = 3.0;
        [Tooltip("Geometric error as a fraction of chunk size. 0.5 is conservative for procedural terrain.")]
        [Range(0.02f, 2f)] public float reliefRatio = 0.5f;

        [Header("Origin policy")]
        [Tooltip("Threshold mode drift allowance for the surface rung, in metres.")]
        public double surfaceRebaseThresholdMetres = 64.0;

        [Header("Focus policy")]
        [Tooltip("Turn off to stop culling body renderers by projected size. Orbit lines and debris " +
                 "belts keep their own policies.")]
        public bool cullSubPixelBodies = true;
        [Tooltip("Projected size below which a body renderer is disabled, in pixels.")]
        public float subPixelThreshold = 0.15f;
        [Tooltip("Projected size below which a body counts as a marker, in pixels.")]
        public float markerThreshold = 3f;

        private SolarSystemSimulation _sim;
        private SpaceFrameTree _tree = new SpaceFrameTree();
        private readonly FloatingOrigin _surfaceOrigin = new FloatingOrigin();
        private SurfaceChunkGrid _surfaceGrid;
        private int _surfaceGridBodyIndex = -1;
        private bool _initialized;

        /// <summary>The frame tree. Cosmic coordinates only ever live in here.</summary>
        public SpaceFrameTree Tree { get { return _tree; } }

        /// <summary>Where the renderer's origin currently sits.</summary>
        public RenderOrigin Origin { get { return _origin; } }

        /// <summary>Frame of the focused body, or null before the first update.</summary>
        public SpaceFrame FocusBodyFrame { get { return _tree.FocusBody; } }

        /// <summary>Chunk grid of the focused body. Rebuilt when the focus changes.</summary>
        public SurfaceChunkGrid SurfaceGrid { get { return _surfaceGrid; } }

        /// <summary>The surface rung's own floating origin, for chunk-anchored rendering.</summary>
        public FloatingOrigin SurfaceOrigin { get { return _surfaceOrigin; } }

        /// <summary>The observer (camera) in the focused body's frame, double metres.</summary>
        public Vec3d ObserverInBodyMetres { get; private set; }

        /// <summary>The observer in the root (galactic) frame, double metres. The only absolute position there is.</summary>
        public Vec3d ObserverInRootMetres { get; private set; }

        /// <summary>Focused body's centre in the stellar frame, double metres.</summary>
        public Vec3d FocusBodyInStellarMetres { get; private set; }

        /// <summary>
        /// Float32 step at the observer's own coordinates, in metres. This is the number that says
        /// whether the current origin is good enough: the renderer's whole world is quantised to it.
        ///
        /// Returns 0 when the observer sits on the origin (the step there is a subnormal, which is a
        /// true statement about the format and a useless one about the scene). Use
        /// <see cref="WorstRenderStepMetres"/> for the number that is never degenerate.
        /// </summary>
        public double ObserverStepMetres
        {
            get
            {
                if (_tree.FocusBody == null || _origin.frame == null) return 0.0;
                Vec3d inOriginFrame = _tree.FocusBody.ConvertTo(_origin.frame, ObserverInBodyMetres);
                double distance = (inOriginFrame - _origin.positionInFrameMetres).Length;

                // Below a nanometre the answer is the format's subnormal floor, not the scene's.
                if (!(distance > 1e-9)) return 0.0;
                return SpacePrecision.ExactUlp(distance, NumericFormat.Float32);
            }
        }

        private RenderOrigin _origin;

        /// <summary>
        /// The float32 step at the largest render-space coordinate currently in the scene, in metres.
        ///
        /// <para>This is the number the floating-origin policy exists to control. World positions are
        /// handed to Unity as float32, so every visible object is quantised by the ulp at its own
        /// render-space magnitude - and the farthest object sets the worst case. Unlike
        /// <see cref="ObserverStepMetres"/>, which is zero whenever the camera sits exactly on the
        /// origin, this is never degenerate.</para>
        /// </summary>
        public double WorstRenderStepMetres
        {
            get
            {
                if (_sim == null || _sim.BodyCount == 0 || !(_stellarUnitsPerMetre > 0.0)) return 0.0;

                double worstUnits = 0.0;
                for (int i = 0; i < _sim.BodyCount; i++)
                {
                    Vec3d p = _sim.PositionKmAt(i);
                    double dx = p.x * 1000.0 - _origin.positionInFrameMetres.x;
                    double dy = p.y * 1000.0 - _origin.positionInFrameMetres.y;
                    double dz = p.z * 1000.0 - _origin.positionInFrameMetres.z;
                    double units = Math.Sqrt(dx * dx + dy * dy + dz * dz) * _stellarUnitsPerMetre;
                    if (units > worstUnits) worstUnits = units;
                }

                return SpacePrecision.ExactUlp(worstUnits, NumericFormat.Float32) / _stellarUnitsPerMetre;
            }
        }

        /// <summary>Metres -> scene units at the stellar rung, derived from the simulation's kmPerUnit.</summary>
        private double _stellarUnitsPerMetre = 1e-9;

        /// <summary>
        /// Builds the frame tree for a simulation. Public because the scene builder and the editor
        /// audit tool live in other assemblies and both need to bring the tree up outside play mode.
        /// </summary>
        public void Initialize(SolarSystemSimulation simulation)
        {
            _sim = simulation;
            _tree.CreateSolSystem(new Vec3d(solInGalaxyMetres.x, solInGalaxyMetres.y, solInGalaxyMetres.z));
            _stellarUnitsPerMetre = simulation.UnitsPerKm * 0.001;
            _tree.Stellar.unitsPerMetre = _stellarUnitsPerMetre;
            _tree.Galactic.unitsPerMetre = _stellarUnitsPerMetre;
            _surfaceOrigin.mode = OriginRebaseMode.Threshold;
            _surfaceOrigin.rebaseThresholdMetres = surfaceRebaseThresholdMetres;
            _initialized = true;
            RefreshFrames(0.0);
        }

        /// <summary>
        /// Recomputes the frame tree for this frame: where the focused body is, where the surface
        /// rung's origin is, and where the observer sits in cosmic coordinates.
        ///
        /// Called by the simulation after it has advanced the orbits and before it lays the bodies
        /// out, so everything downstream in the same frame sees one consistent set of origins.
        /// </summary>
        public void RefreshFrames(double frameSeconds)
        {
            SyncFrames();
            UpdateSurfaceOrigin(frameSeconds);
        }

        /// <summary>
        /// Recomputes the frame tree for this frame. Called by the simulation after it has advanced
        /// the orbits and before it lays the bodies out.
        /// </summary>
        internal void SyncFrames()
        {
            // BodyCount is zero until the simulation has resolved its scene, which is the normal
            // state in edit mode before anything has called Build(). Staying inert is better than
            // throwing from a tool that only wanted to read the frame tree.
            if (!_initialized || _sim == null || _sim.BodyCount == 0 || _sim.FocusView == null) return;

            FocusBodyInStellarMetres = new Vec3d(
                _sim.PositionKmAt(_sim.FocusIndex).x * 1000.0,
                _sim.PositionKmAt(_sim.FocusIndex).y * 1000.0,
                _sim.PositionKmAt(_sim.FocusIndex).z * 1000.0);

            _tree.EnsureBodyFrame(_sim.FocusView != null ? _sim.FocusView.bodyName : "Body",
                                  FocusBodyInStellarMetres);

            EnsureSurfaceGrid();

            // The render origin mirrors the simulation's own floating origin, expressed in metres.
            // The simulation stays the owner of that policy - this class only re-expresses it in the
            // frame tree so the surface rung has something consistent to hang off.
            Vec3d originKm = _sim.OriginKm;
            _origin = new RenderOrigin
            {
                frame = _tree.Stellar,
                positionInFrameMetres = new Vec3d(originKm.x * 1000.0, originKm.y * 1000.0, originKm.z * 1000.0)
            };

            // Observer = the camera's offset from the focused body's centre, in the body frame.
            Vec3d observerInStellar = _origin.positionInFrameMetres;
            if (_sim.HasObserver)
            {
                Vec3d observer = _sim.ObserverWorldMetres;
                observerInStellar = new Vec3d(observer.x, -observer.z, observer.y);
            }
            else if (_sim.ViewCamera != null)
            {
                Vector3 cam = _sim.ViewCamera.transform.position;
                observerInStellar = observerInStellar + new Vec3d(
                    cam.x / _stellarUnitsPerMetre, cam.y / _stellarUnitsPerMetre, cam.z / _stellarUnitsPerMetre);
            }

            ObserverInBodyMetres = _tree.FocusBody.FromRoot(_tree.Stellar.ToRoot(observerInStellar));
            ObserverInRootMetres = _tree.FocusBody.ToRoot(ObserverInBodyMetres);
        }

        /// <summary>Advances the surface rung's own origin. Returns true when it rebased.</summary>
        internal bool UpdateSurfaceOrigin(double frameSeconds)
        {
            if (_tree.FocusBody == null) return false;
            return _surfaceOrigin.Update(_tree.FocusBody, ObserverInBodyMetres, frameSeconds, false);
        }

        private void EnsureSurfaceGrid()
        {
            Body definition = _sim.FocusDefinition;
            if (definition == null) { _surfaceGrid = null; _surfaceGridBodyIndex = -1; return; }

            int index = _sim.FocusIndex;
            if (_surfaceGrid != null && _surfaceGridBodyIndex == index &&
                _surfaceGrid.maxLevel == maxChunkLevel && _surfaceGrid.resolution == chunkResolution)
                return;

            _surfaceGrid = new SurfaceChunkGrid(
                definition.RadiusKm * 1000.0, definition.Flattening, maxChunkLevel, chunkResolution);
            Vector3 axes = definition.ShapeScale;
            _surfaceGrid.referenceShapeScale = new Vec3d(axes.x, axes.y, axes.z);
            _surfaceGridBodyIndex = index;
        }

        // ======================================================================================
        // Focus policy: what happens to everything that is not the focused body
        // ======================================================================================

        /// <summary>
        /// Classifies a body by how much screen it covers.
        ///
        /// At true scale every planet except the focused one is a fraction of a pixel, so the tier
        /// is what decides whether the 21 procedural surfaces are worth submitting at all. The 'G'
        /// exaggeration multiplies the radius, so the tiers reclassify themselves the moment the
        /// user asks for a visible solar system - no separate code path.
        /// </summary>
        public BodyRenderTier ClassifyBody(int index, Camera camera, float verticalFovDegrees, int screenHeightPixels)
        {
            if (_sim == null || index < 0 || index >= _sim.BodyCount) return BodyRenderTier.Skipped;

            CelestialBody view = _sim.ViewAt(index);
            if (view == null) return BodyRenderTier.Skipped;

            double radiusMetres = view.RadiusUnits / _stellarUnitsPerMetre;
            if (!(radiusMetres > 0.0)) return BodyRenderTier.Skipped;

            Vec3d bodyInStellar = new Vec3d(
                _sim.PositionKmAt(index).x * 1000.0,
                _sim.PositionKmAt(index).y * 1000.0,
                _sim.PositionKmAt(index).z * 1000.0);
            Vec3d toBody = bodyInStellar - _origin.positionInFrameMetres;
            double distanceMetres = Math.Max(toBody.Length, 1e-6);
            if (_sim.HasObserver)
            {
                Vec3d observer = _sim.ObserverWorldMetres;
                Vec3d observerStellar = new Vec3d(observer.x, -observer.z, observer.y);
                distanceMetres = Math.Max((bodyInStellar - observerStellar).Length, 1e-6);
            }
            else if (camera != null)
            {
                Vector3 camWorld = camera.transform.position;
                Vec3d camInStellar = _origin.positionInFrameMetres + new Vec3d(
                    camWorld.x / _stellarUnitsPerMetre, camWorld.y / _stellarUnitsPerMetre,
                    camWorld.z / _stellarUnitsPerMetre);
                distanceMetres = Math.Max((bodyInStellar - camInStellar).Length, 1e-6);
            }

            double diameterPixels = 2.0 * radiusMetres / distanceMetres /
                                    (2.0 * Math.Tan(verticalFovDegrees * Math.PI / 360.0)) * screenHeightPixels;

            if (diameterPixels < subPixelThreshold) return BodyRenderTier.Skipped;
            if (diameterPixels < markerThreshold) return BodyRenderTier.Marker;
            if (diameterPixels < 64.0) return BodyRenderTier.Proxy;
            return BodyRenderTier.Full;
        }

        /// <summary>Convenience overload using the simulation's own camera.</summary>
        public BodyRenderTier ClassifyBody(int index)
        {
            Camera cam = _sim != null ? _sim.ViewCamera : Camera.main;
            float fov = cam != null ? cam.fieldOfView : 50f;
            int height = cam != null ? Math.Max(cam.pixelHeight, 1) : Screen.height;
            return ClassifyBody(index, cam, fov, height);
        }

        /// <summary>Counts bodies in each tier. Used by the audit tool and the HUD.</summary>
        public void CountTiers(int[] counts)
        {
            if (counts == null || counts.Length < 4) throw new ArgumentException("needs 4 slots", "counts");
            for (int i = 0; i < counts.Length; i++) counts[i] = 0;
            if (_sim == null) return;
            for (int i = 0; i < _sim.BodyCount; i++) counts[(int)ClassifyBody(i)]++;
        }
    }
}
