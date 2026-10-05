using System;

namespace Planet.Solar
{
    /// <summary>How an origin decides when to move.</summary>
    public enum OriginRebaseMode
    {
        /// <summary>
        /// The origin tracks the anchor every frame, with a decaying residual so a change of anchor
        /// does not teleport the view.
        ///
        /// This is the mode the stellar frame uses. It is safe there because nothing is baked
        /// against the origin: a rebase is one subtraction, so doing it 120 times a second costs
        /// nothing and there is never any drift to accumulate.
        /// </summary>
        RigidFollow,

        /// <summary>
        /// The origin stays put until the observer is further than the threshold, then snaps to the
        /// anchor.
        ///
        /// This is the mode a surface frame has to use. There the origin is a <i>terrain feature</i>
        /// (a chunk corner), and every resident chunk transform is expressed against it; moving the
        /// origin means rewriting all of them. Rebasing on a threshold turns that from a per-frame
        /// cost into a once-every-few-hundred-metres cost, and - more importantly - keeps the
        /// floating-point grid stable, so a stationary camera sees a stationary world.
        /// </summary>
        Threshold
    }

    /// <summary>
    /// The floating origin, expressed as a policy rather than as a line of code inside whichever
    /// system happened to need it first.
    ///
    /// <para><b>The one thing this class refuses to do is smooth the origin towards its anchor.</b>
    /// Exponentially chasing the target leaves a steady-state error of <c>velocity x time
    /// constant</c>: at 5 days/second that was a harmless 4 scene units in the solar system, and at
    /// 36,500 days/second it became 30,000 units and the planet left the screen. The correct shape
    /// is "the origin <i>is</i> the anchor, plus a residual that decays to zero", which is exact at
    /// every time scale. <see cref="RigidFollow"/> implements exactly that, and
    /// <c>FloatingOriginTests</c> asserts there is no steady-state error at any speed.</para>
    /// </summary>
    public sealed class FloatingOrigin
    {
        public OriginRebaseMode mode = OriginRebaseMode.RigidFollow;

        /// <summary>Threshold mode only: how far the observer may drift before the origin snaps.</summary>
        public double rebaseThresholdMetres = 64.0;

        /// <summary>Rigid-follow only: approximate seconds the residual takes to decay after an anchor change.</summary>
        public double catchUpSeconds = 0.35;

        /// <summary>The current origin. Read-only to callers; only <see cref="Update"/> writes it.</summary>
        public RenderOrigin Current { get; private set; }

        /// <summary>How many times the origin has moved. A sky-high count in Threshold mode means the threshold is too small.</summary>
        public int RebaseCount { get; private set; }

        /// <summary>Distance the origin moved on the last rebase, in metres.</summary>
        public double LastRebaseDeltaMetres { get; private set; }

        /// <summary>Largest observer-to-origin distance seen since the last rebase. The audit tool prints this.</summary>
        public double PeakDriftMetres { get; private set; }

        /// <summary>Residual carried by <see cref="OriginRebaseMode.RigidFollow"/>, in metres.</summary>
        private Vec3d _lag;
        private bool _ready;

        /// <summary>Puts the origin at a position with no residual. Used on focus changes and scene build.</summary>
        public void Reset(SpaceFrame frame, Vec3d positionInFrameMetres)
        {
            Current = new RenderOrigin { frame = frame, positionInFrameMetres = positionInFrameMetres };
            _lag = Vec3d.Zero;
            _ready = true;
            PeakDriftMetres = 0.0;
        }

        /// <summary>
        /// Re-anchors onto a new target without teleporting the view: the origin stays where it is and
        /// eases onto the new anchor over <see cref="catchUpSeconds"/>.
        ///
        /// Call this when the anchor jumps <i>by design</i> - a focus change from Earth to Neptune -
        /// because a deliberate jump is the one case where following rigidly would cut. Contrast
        /// <see cref="Reset"/>, which is for a jump the user asked for and should not be eased.
        /// </summary>
        public void Reanchor(SpaceFrame frame, Vec3d anchorInFrameMetres)
        {
            if (frame == null) throw new ArgumentNullException("frame");

            // The residual is whatever offset the new anchor currently has from the old origin, so
            // the origin's immediate position is unchanged and only the reference frame it is
            // measured against moves. Seeding it here rather than letting Update discover it is what
            // keeps the track exact: the residual is a decaying constant, never a feedback term.
            _lag = _ready ? (Current.positionInFrameMetres - anchorInFrameMetres) : Vec3d.Zero;
            _ready = true;
            Current = new RenderOrigin { frame = frame, positionInFrameMetres = anchorInFrameMetres + _lag };
            PeakDriftMetres = 0.0;
        }

        /// <summary>
        /// Advances the origin towards <paramref name="anchorInFrameMetres"/>. Returns true when the
        /// origin actually moved, so callers can skip dependent work on a still frame.
        /// </summary>
        /// <param name="frame">Frame both the origin and the anchor are expressed in.</param>
        /// <param name="anchorInFrameMetres">The point the origin should track - the camera, or the focused body's centre.</param>
        /// <param name="frameSeconds">Frame time, for the decaying residual.</param>
        /// <param name="snap">When true, clears the residual: the view should not ease after a deliberate jump.</param>
        public bool Update(SpaceFrame frame, Vec3d anchorInFrameMetres, double frameSeconds, bool snap)
        {
            if (frame == null) throw new ArgumentNullException("frame");

            if (snap || !_ready)
            {
                // A deliberate teleport, or the very first update: cut, and leave nothing to decay.
                double delta = _ready ? (Current.positionInFrameMetres - anchorInFrameMetres).Length : 0.0;
                bool jumped = !_ready || delta > 0.0;
                Current = new RenderOrigin { frame = frame, positionInFrameMetres = anchorInFrameMetres };
                _lag = Vec3d.Zero;
                _ready = true;
                LastRebaseDeltaMetres = delta;
                if (jumped) RebaseCount++;
                PeakDriftMetres = 0.0;
                return jumped;
            }

            if (mode == OriginRebaseMode.RigidFollow)
            {
                // The origin *is* the anchor; only the residual differs, and it only ever decays. It
                // is seeded by Reanchor when the anchor jumps by design, so there is deliberately no
                // "move part of the way towards the anchor" term - that is the term whose steady-state
                // error is velocity x tau, which at 36,500 days/s reaches 30,000 scene units.
                double tau = Math.Max(catchUpSeconds, 1e-4);
                _lag = _lag * Math.Exp(-Math.Max(frameSeconds, 0.0) / tau);

                Vec3d next = anchorInFrameMetres + _lag;
                double drift = (next - Current.positionInFrameMetres).Length;
                bool moved = drift > 0.0;
                if (moved)
                {
                    LastRebaseDeltaMetres = drift;
                    RebaseCount++;
                }
                Current = new RenderOrigin { frame = frame, positionInFrameMetres = next };
                if (drift > PeakDriftMetres) PeakDriftMetres = drift;
                return moved;
            }

            // Threshold: hold the origin, and only pay for a rebase when the drift is large enough
            // to matter for float precision. The world does not move until it has to.
            double distance = (anchorInFrameMetres - Current.positionInFrameMetres).Length;
            if (distance > PeakDriftMetres) PeakDriftMetres = distance;
            if (distance <= rebaseThresholdMetres) return false;

            LastRebaseDeltaMetres = distance;
            RebaseCount++;
            Current = new RenderOrigin { frame = frame, positionInFrameMetres = anchorInFrameMetres };
            PeakDriftMetres = 0.0;
            return true;
        }

        /// <summary>
        /// How much coarser the float grid is at the observer's current drift than it is at unit
        /// scale: the step at the drift distance divided by the step at 1 m.
        ///
        /// 1.0 means the drift is free - anything within a metre, where the grid is at its finest.
        /// It only starts to matter around 2, where the observer is far enough from the origin that
        /// the world's float grid has visibly coarsened.
        /// </summary>
        public double DriftPenalty(NumericFormat format)
        {
            double atUnit = SpacePrecision.ExactUlp(1.0, format);
            double atObserver = SpacePrecision.ExactUlp(Math.Max(PeakDriftMetres, 1.0), format);
            return atObserver / atUnit;
        }
    }
}
