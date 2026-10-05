using System;
using System.Collections.Generic;
using UnityEngine;

namespace Planet.Solar
{
    /// <summary>
    /// The four rungs of the space hierarchy. Each rung exists because the rung below it cannot
    /// carry the precision on its own.
    /// </summary>
    public enum SpaceFrameLevel : byte
    {
        /// <summary>Origin at the galactic centre. Holds star positions. Double metres.</summary>
        Galactic = 0,

        /// <summary>Origin at a star. Holds planetary orbits. Double metres.</summary>
        Stellar = 1,

        /// <summary>Origin at a body's centre. Holds the body-fixed terrain surface. Double metres.</summary>
        Planetary = 2,

        /// <summary>Origin at a surface chunk's corner. Holds render vertices as float metres.</summary>
        Surface = 3
    }

    /// <summary>
    /// One origin in the hierarchy: a position, expressed in its parent, plus the format that
    /// position is stored in.
    ///
    /// <para><b>The rule this type exists to enforce.</b> A coordinate is only ever narrowed to
    /// float <i>after</i> the subtraction, and the subtraction always happens in the frame that
    /// owns the point. Converting first and subtracting second is what makes a true-scale solar
    /// system jitter, and it is the single mistake that the whole two-coordinate-system split is
    /// built to make impossible: there is exactly one place in the runtime that calls
    /// <see cref="RenderOrigin.ToRender"/>, and it subtracts in double.</para>
    ///
    /// <para><b>Scaling is never used to move between frames.</b> A frame change is an origin
    /// change (a subtraction), not a unit change (a multiplication). Units only enter at the very
    /// last step, when metres become render units for a camera that has already been re-centred.</para>
    /// </summary>
    public sealed class SpaceFrame
    {
        /// <summary>Human-readable name, shown by the audit tool.</summary>
        public string name;

        public SpaceFrameLevel level;

        /// <summary>Parent frame, or null for the root.</summary>
        public SpaceFrame parent;

        /// <summary>
        /// This frame's origin, expressed in the parent frame. Double metres, and the only stored
        /// position in the whole type - there is deliberately no cached absolute position, because a
        /// stale cache is a silent precision bug.
        /// </summary>
        public Vec3d originInParentMetres;

        /// <summary>
        /// Render units per metre for geometry expressed in this frame. 1 for a surface frame
        /// (1 unit = 1 m); <c>1e-9</c> for the solar-system frame (1 unit = 1e6 km). Only the
        /// outermost conversion multiplies by this.
        /// </summary>
        public double unitsPerMetre = 1.0;

        public SpaceFrame(string name, SpaceFrameLevel level, SpaceFrame parent, Vec3d originInParentMetres)
        {
            this.name = name;
            this.level = level;
            this.parent = parent;
            this.originInParentMetres = originInParentMetres;
            this.unitsPerMetre = parent != null ? parent.unitsPerMetre : 1.0;
        }

        public bool IsRoot { get { return parent == null; } }

        /// <summary>Number of rungs from the root down to and including this frame.</summary>
        public int Depth
        {
            get
            {
                int d = 0;
                for (SpaceFrame f = this; f != null; f = f.parent) d++;
                return d;
            }
        }

        /// <summary>
        /// This frame's origin measured from the root, accumulated in double. Recomputed on every
        /// call rather than cached: the chain is at most four links and a wrong cache would show up
        /// as a body sliding away over hours.
        /// </summary>
        public Vec3d OriginInRootMetres
        {
            get
            {
                Vec3d sum = Vec3d.Zero;
                for (SpaceFrame f = this; f != null; f = f.parent) sum = sum + f.originInParentMetres;
                return sum;
            }
        }

        /// <summary>Lifts a point from this frame up to the root frame. Pure double, no narrowing.</summary>
        public Vec3d ToRoot(Vec3d localMetres) { return localMetres + OriginInRootMetres; }

        /// <summary>Brings a root-frame point down into this frame. Pure double, no narrowing.</summary>
        public Vec3d FromRoot(Vec3d rootMetres) { return rootMetres - OriginInRootMetres; }

        /// <summary>
        /// Moves a point between two frames of the same tree. Always goes through the root rather
        /// than subtracting the two origins directly, so a point can never be expressed relative to
        /// an origin that is not an ancestor of it.
        /// </summary>
        public Vec3d ConvertTo(SpaceFrame target, Vec3d localMetres)
        {
            if (target == null) throw new ArgumentNullException("target");
            return target.FromRoot(ToRoot(localMetres));
        }

        /// <summary>
        /// The precision actually available for a point at <paramref name="localMetres"/> in this
        /// frame, in the format given. This is the query that decides "is this a frame problem or a
        /// chunk problem".
        /// </summary>
        public PrecisionBudget BudgetAt(Vec3d localMetres, NumericFormat format)
        {
            return SpacePrecision.BudgetAt(localMetres.Length, format);
        }

        public override string ToString()
        {
            return string.Format("{0} [{1}] origin {2} m in parent", name, level, originInParentMetres);
        }
    }

    /// <summary>
    /// The renderer's current origin: which frame the camera is standing in, and where in that
    /// frame the camera-centred origin sits.
    ///
    /// This is the "floating origin" half of the design. The origin is a <i>point in a frame</i>,
    /// not a world position: the renderer never learns where the frame is in the galaxy, so it can
    /// never accidentally mix a cosmic distance into a float.
    /// </summary>
    public struct RenderOrigin
    {
        /// <summary>Frame the origin belongs to. Geometry outside it is converted down through the tree.</summary>
        public SpaceFrame frame;

        /// <summary>The origin's own position, in <see cref="frame"/>. Double metres.</summary>
        public Vec3d positionInFrameMetres;

        /// <summary>
        /// The one and only place a cosmic coordinate becomes a Unity <c>Vector3</c>.
        ///
        /// The subtraction happens in double and in the frame that owns the point; the multiply by
        /// <c>unitsPerMetre</c> happens last, on the small residual. The ecliptic-to-Unity axis swap
        /// is applied here too, so no caller has to remember it.
        /// </summary>
        public Vector3 ToRender(Vec3d pointInFrameMetres)
        {
            Vec3d residual = pointInFrameMetres - positionInFrameMetres;
            return OrbitalMechanics.ToUnity(residual, frame != null ? frame.unitsPerMetre : 1.0);
        }

        /// <summary>
        /// Same conversion, but the point is given in another frame and is brought into this one
        /// first - still in double, still before any narrowing. This is the path used for a body that
        /// lives in a different frame from the camera.
        ///
        /// Named for what it does rather than for the common case: any frame works, not only the root,
        /// and the conversion composes the origin chain either way.
        /// </summary>
        public Vector3 ToRenderFrom(SpaceFrame pointFrame, Vec3d pointInItsFrameMetres)
        {
            return ToRender(pointFrame.ConvertTo(frame, pointInItsFrameMetres));
        }

        /// <summary>Distance from the origin to a point, in metres. Double throughout.</summary>
        public double DistanceMetres(Vec3d pointInFrameMetres)
        {
            return (pointInFrameMetres - positionInFrameMetres).Length;
        }

        public bool IsValid { get { return frame != null; } }
    }

    /// <summary>
    /// The frame tree for one scene, plus the current render origin. Owns the rungs; the simulation
    /// owns the numbers that move the leaf origins around.
    /// </summary>
    public sealed class SpaceFrameTree
    {
        /// <summary>Root of the tree: the galactic frame. Null until <see cref="CreateSolSystem"/>.</summary>
        public SpaceFrame Galactic { get; private set; }

        /// <summary>The star frame (Sol, i.e. the simulation's own origin).</summary>
        public SpaceFrame Stellar { get; private set; }

        /// <summary>Frame of the currently focused body, rebuilt whenever the focus changes.</summary>
        public SpaceFrame FocusBody { get; private set; }

        /// <summary>Double-metre offset from the galactic centre to Sol. Cosmetic by default.</summary>
        public Vec3d solInGalaxyMetres;

        private readonly List<SpaceFrame> _all = new List<SpaceFrame>();

        /// <summary>Every frame ever created by this tree, in creation order. For tooling and audits.</summary>
        public IList<SpaceFrame> AllFrames { get { return _all; } }

        /// <summary>
        /// Builds the galactic -> stellar rungs.
        ///
        /// <paramref name="solInGalaxyMetres"/> is where the Sun sits inside the galaxy. The scene
        /// never renders anything at that distance, so the default places Sol at the galactic
        /// centre and the rung costs nothing but makes the hierarchy explicit: when a galactic
        /// layer is added later, only this number changes.
        /// </summary>
        public void CreateSolSystem(Vec3d solInGalaxyMetres)
        {
            _all.Clear();
            this.solInGalaxyMetres = solInGalaxyMetres;

            Galactic = new SpaceFrame("Milky Way", SpaceFrameLevel.Galactic, null, Vec3d.Zero);
            Galactic.unitsPerMetre = 1.0;
            _all.Add(Galactic);

            // 1 render unit = 1e6 km = 1e9 m at the solar-system rung. Matches
            // SolarSystemSimulation.kmPerUnit so orbit geometry keeps its existing size.
            Stellar = new SpaceFrame("Sol", SpaceFrameLevel.Stellar, Galactic, solInGalaxyMetres);
            Stellar.unitsPerMetre = 1e-9;
            _all.Add(Stellar);

            FocusBody = null;
        }

        /// <summary>
        /// Creates or re-uses the planetary rung for a body. The frame is re-used across focus
        /// changes so its identity is stable for anything holding a reference.
        /// </summary>
        public SpaceFrame EnsureBodyFrame(string bodyName, Vec3d bodyInStellarMetres)
        {
            if (Stellar == null) throw new InvalidOperationException("CreateSolSystem must run first.");

            if (FocusBody != null && FocusBody.name == bodyName)
            {
                FocusBody.originInParentMetres = bodyInStellarMetres;
                return FocusBody;
            }

            // Retire the old rung by detaching it, then create the new one. Keeping a pool would
            // let a stale frame keep being used as a render origin after its body moved.
            SpaceFrame frame = new SpaceFrame(bodyName, SpaceFrameLevel.Planetary, Stellar, bodyInStellarMetres);
            frame.unitsPerMetre = Stellar.unitsPerMetre;
            FocusBody = frame;
            _all.Add(frame);
            return frame;
        }

        /// <summary>Creates the surface rung that hangs off the focused body's planetary frame.</summary>
        public SpaceFrame CreateSurfaceFrame(SpaceFrame bodyFrame, string chunkName, Vec3d chunkOriginInBodyMetres)
        {
            if (bodyFrame == null) throw new ArgumentNullException("bodyFrame");

            // 1 render unit = 1 metre at the surface rung. The camera sits centimetres from the
            // ground here, so the cosmic scale would put every vertex on the same float.
            SpaceFrame frame = new SpaceFrame(
                bodyFrame.name + "/" + chunkName, SpaceFrameLevel.Surface, bodyFrame, chunkOriginInBodyMetres);
            frame.unitsPerMetre = 1.0;
            _all.Add(frame);
            return frame;
        }
    }
}
