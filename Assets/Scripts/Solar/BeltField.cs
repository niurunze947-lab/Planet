using System;
using UnityEngine;

namespace Planet.Solar
{
    /// <summary>
    /// A particle population rendered as GPU instances: the asteroid belt (2.06 - 3.27 AU) and the
    /// Kuiper belt (30 - 50 AU).
    ///
    /// Every particle carries real orbital elements and is propagated with the same Kepler solver
    /// the planets use, so the belts shear correctly: the inner edge laps the outer edge, exactly as
    /// a real debris disc does. Orbital radius and period come from Kepler's third law,
    /// P(years) = a(AU)^1.5, so nothing here is hand-tuned to "look right".
    ///
    /// The one deliberate exaggeration is the size of the marker glyph. A 100 km asteroid is
    /// 1e-4 scene units across at true scale, i.e. permanently invisible, so markers are scaled to
    /// hold a constant angular size instead. Their positions are still true to scale.
    ///
    /// <para><b>Cost model.</b> The 7,200 particles are the single most expensive per-frame job in
    /// the scene, and the work splits into two halves that have completely different update
    /// requirements:</para>
    /// <list type="bullet">
    /// <item><b>Solve</b> - Kepler's equation per particle. Depends only on the Julian date, so it
    /// is a pure function of time.</item>
    /// <item><b>Submit</b> - a world position, a camera distance and a TRS matrix per particle.
    /// Depends on the solve, the floating origin and where the camera is looking.</item>
    /// </list>
    /// <para>Running both every frame spends most of its time recomputing a function of a date that
    /// changed by 0.02 degrees. So the solve runs on an <b>adaptive cadence</b> sized to keep the
    /// orbital staleness under <see cref="angularStalenessDegrees"/>, and the submit re-uses the
    /// cached positions, rejecting everything behind the camera before it builds a matrix. When time
    /// is paused the solve runs once and then never again for the rest of the session.</para>
    ///
    /// <para>The cadence is derived, not tuned: <c>interval = tolerance / (mean motion x time
    /// rate)</c>. At 5 days/second the asteroid belt genuinely moves half a pixel per frame, so the
    /// cadence collapses to "every frame" and nothing is skipped; at 0.002 days/second - the rate
    /// you would use while looking at a planet's surface - it refreshes every few seconds.</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class BeltField : MonoBehaviour
    {
        // The presentation no longer includes the instanced asteroid/Kuiper marker clouds. Keep
        // the implementation available for tooling/tests, but suppress legacy components that may
        // still live in an already-open Unity scene until that scene is reloaded from disk.
        private bool IsRemovedPresentationField
        {
            get
            {
                bool knownField = string.Equals(fieldName, "Asteroid Belt", StringComparison.Ordinal) ||
                    string.Equals(fieldName, "Kuiper Belt", StringComparison.Ordinal);
                string objectName = gameObject != null ? gameObject.name : string.Empty;
                bool knownObject = objectName.IndexOf("Asteroid Belt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    objectName.IndexOf("Kuiper Belt", StringComparison.OrdinalIgnoreCase) >= 0;
                string scenePath = gameObject != null ? gameObject.scene.path : string.Empty;
                bool removedFromSolarSystemScene = scenePath.EndsWith(
                    "/SolarSystem.unity", StringComparison.OrdinalIgnoreCase) ||
                    scenePath.EndsWith("\\SolarSystem.unity", StringComparison.OrdinalIgnoreCase);
                return knownField || knownObject || removedFromSolarSystemScene;
            }
        }

        [Header("Population")]
        public string fieldName = "Asteroid Belt";
        public int count = 2200;
        public double seed = 20261003.0;

        [Header("Orbit distribution (AU)")]
        public double innerAu = 2.06;
        public double outerAu = 3.27;
        [Tooltip("Bias of the radial distribution. 1 = uniform in radius, >1 crowds the outer edge.")]
        public double radialBias = 1.0;
        public double maxEccentricity = 0.22;

        [Header("Inclination (deg)")]
        [Range(0f, 1f)] public float coldFraction = 0.68f;
        public double coldMaxInclination = 4.0;
        public double hotMaxInclination = 22.0;

        [Header("Marker glyph (NOT to scale - see class comment)")]
        [Tooltip("Marker radius as a fraction of its distance from the camera, so markers hold a constant angular size. 0.0012 rad is about 2.5 pixels on a 900 px view at 50 degrees vertical FOV.")]
        public float markerAngularSize = 0.0012f;
        public float markerMinUnits = 2e-5f;
        [Tooltip("Per-particle size jitter, so a dense belt reads as grains rather than as flat texture.")]
        [Range(0f, 0.9f)] public float markerSizeJitter = 0.55f;
        public int markerLongitudes = 8;
        public int markerLatitudes = 5;

        [Header("Update cadence")]
        [Tooltip("How far a particle may drift along its orbit before the solve has to run again, " +
                 "in degrees of mean anomaly. 0.01 deg is well under a tenth of a pixel for a " +
                 "marker that holds a 2.5 px angular size.")]
        public double angularStalenessDegrees = 0.01;

        [Tooltip("Set false to solve every frame regardless of the cadence. Diagnostics only - it " +
                 "puts the 4 ms/frame cost back.")]
        public bool adaptSolveCadence = true;

        [Header("Culling")]
        [Tooltip("Reject markers behind the camera before building their matrix.")]
        public bool cullBehindCamera = true;
        [Tooltip("Extra margin on the view cone, as a fraction of the half-FOV, so a marker does " +
                 "not pop as it crosses the screen edge.")]
        [Range(0f, 2f)] public float viewConeMargin = 0.35f;
        [Tooltip("Also cull markers beyond the camera's far plane.")]
        public bool cullBeyondFarPlane = true;

        private struct Particle
        {
            public double a;        // AU
            public double e;
            public double n;        // mean motion, deg/day
            public double m0;       // mean anomaly at J2000, deg
            public Vec3d px, py;    // perifocal basis vectors, ecliptic frame
            public float size;      // glyph size multiplier
        }

        private Particle[] _p;
        private Vec3d[] _positionKm;      // cached heliocentric positions, refreshed on the cadence
        private Matrix4x4[] _matrices;
        private Mesh _markerMesh;
        private bool _built;
        private int _drivenFrame = -1;

        // ---- cadence / telemetry state -------------------------------------------------------
        private double _maxMeanMotion;        // deg/day, the fastest particle in this field
        private double _nextSolveTime;
        private double _lastSolvedJd = double.NaN;
        private bool _lastPaused;
        private Vector3 _lastCameraPosition;
        private Quaternion _lastCameraRotation;
        private float _lastFarPlane = -1f;
        private Vec3d _lastOriginKm;

        /// <summary>
        /// Orbital elements of one particle, as a read-only view for tooling.
        ///
        /// The runtime never needs these - propagation reads the private array directly - but the
        /// audit tool and the tests use them to verify that a generated field is physically
        /// plausible: elliptic, inside its annulus, and slower than any planet.
        /// </summary>
        public struct BeltParticle
        {
            /// <summary>Semi-major axis, AU.</summary>
            public double semiMajorAu;
            /// <summary>Eccentricity, in [0, 1) for an ellipse.</summary>
            public double eccentricity;
            /// <summary>Mean motion, degrees per day. The fastest particle sets the solve cadence.</summary>
            public double meanMotionDegPerDay;
            /// <summary>Mean anomaly at J2000, degrees.</summary>
            public double meanAnomalyAtJ2000Deg;
            /// <summary>Perifocal basis vectors, ecliptic frame. Orthonormal, or orbits come out skewed.</summary>
            public Vec3d periapsisBasis;
            public Vec3d apoapsisBasis;

            /// <summary>Closest approach to the Sun, AU.</summary>
            public double PeriapsisAu { get { return semiMajorAu * (1.0 - eccentricity); } }
            /// <summary>Farthest distance from the Sun, AU.</summary>
            public double ApoapsisAu { get { return semiMajorAu * (1.0 + eccentricity); } }
        }

        /// <summary>Number of particles. Zero until <see cref="Build"/> has run.</summary>
        public int ParticleCount { get { return _p != null ? _p.Length : 0; } }

        /// <summary>The fastest particle's mean motion, degrees per day. Sets the solve interval.</summary>
        public double MaxMeanMotionDegPerDay { get { return _maxMeanMotion; } }

        /// <summary>Reads one particle's elements. Throws if <paramref name="index"/> is out of range.</summary>
        public BeltParticle ParticleAt(int index)
        {
            if (_p == null || index < 0 || index >= _p.Length)
                throw new ArgumentOutOfRangeException("index");

            Particle p = _p[index];
            return new BeltParticle
            {
                semiMajorAu = p.a,
                eccentricity = p.e,
                meanMotionDegPerDay = p.n,
                meanAnomalyAtJ2000Deg = p.m0,
                periapsisBasis = p.px,
                apoapsisBasis = p.py
            };
        }

        /// <summary>Particles whose orbit was solved on the last refresh.</summary>
        public int LastSolvedCount { get; private set; }
        /// <summary>
        /// Monotonic count of orbital refreshes since the last <see cref="Build"/>. Divide by the
        /// number of frames to get the cadence the simulation is actually running at.
        /// </summary>
        public int SolveCount { get; private set; }
        /// <summary>Markers that survived culling and were submitted on the last Render.</summary>
        public int LastSubmittedCount { get; private set; }
        /// <summary>Milliseconds spent in the orbital solve on the last refresh.</summary>
        public double LastSolveMilliseconds { get; private set; }
        /// <summary>Milliseconds spent in the submit (matrix build) phase of the last Render.</summary>
        public double LastSubmitMilliseconds { get; private set; }
        /// <summary>True when the last Render re-used cached positions instead of re-solving.</summary>
        public bool LastRenderUsedCache { get; private set; }
        /// <summary>True when the last Render submitted nothing because neither the belt nor the camera moved.</summary>
        public bool LastRenderWasIdle { get; private set; }

        private void OnEnable()
        {
            string objectName = gameObject != null ? gameObject.name : string.Empty;
            bool removedSceneObject =
                objectName.IndexOf("Asteroid Belt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf("Kuiper Belt", StringComparison.OrdinalIgnoreCase) >= 0;
            if (removedSceneObject)
            {
                enabled = false;
                return;
            }
            Build();
        }

        private void OnValidate() { if (!Application.isPlaying) _built = false; }

        public void Build()
        {
            _built = false;
            int n = Mathf.Clamp(count, 0, 40000);
            if (n == 0) return;

            _p = new Particle[n];
            _positionKm = new Vec3d[n];
            _matrices = new Matrix4x4[1023];
            _markerMesh = SolarMeshFactory.Sphere(markerLongitudes, markerLatitudes, 0f, Vector3.one);
            _maxMeanMotion = 0.0;
            _nextSolveTime = 0.0;
            _lastSolvedJd = double.NaN;
            _lastOriginKm = new Vec3d(double.NaN, double.NaN, double.NaN);
            SolveCount = 0;
            LastSolvedCount = 0;
            LastSubmittedCount = 0;
            LastSolveMilliseconds = 0.0;
            LastSubmitMilliseconds = 0.0;

            // Deterministic 64-bit LCG: the same belt on every machine, every run.
            ulong state = (ulong)(seed * 1000.0) | 1UL;
            System.Func<double> rnd = delegate ()
            {
                state = state * 6364136223846793005UL + 1442695040888963407UL;
                return (double)((state >> 11) & ((1UL << 53) - 1)) / (double)(1UL << 53);
            };

            const double deg2rad = OrbitalMechanics.Deg2Rad;

            for (int k = 0; k < n; k++)
            {
                double u = rnd();
                double a = innerAu + (outerAu - innerAu) * System.Math.Pow(u, radialBias);
                double e = maxEccentricity * System.Math.Pow(rnd(), 2.0);   // clustered low

                bool cold = rnd() < coldFraction;
                double iMax = cold ? coldMaxInclination : hotMaxInclination;
                double inc = iMax * System.Math.Pow(rnd(), 2.0);

                double node = rnd() * 360.0;
                double peri = rnd() * 360.0;
                double m0 = rnd() * 360.0;

                // Kepler's third law for a heliocentric orbit: P(years) = a(AU)^1.5
                double periodDays = 365.2568983 * System.Math.Pow(a, 1.5);
                double meanMotion = 360.0 / periodDays;
                if (meanMotion > _maxMeanMotion) _maxMeanMotion = meanMotion;

                double w = (peri - node) * deg2rad;
                double om = node * deg2rad;
                double ic = inc * deg2rad;
                double cw = System.Math.Cos(w), sw = System.Math.Sin(w);
                double co = System.Math.Cos(om), so = System.Math.Sin(om);
                double ci = System.Math.Cos(ic), si = System.Math.Sin(ic);

                _p[k] = new Particle
                {
                    a = a,
                    e = e,
                    n = meanMotion,
                    m0 = m0,
                    px = new Vec3d(cw * co - sw * so * ci, cw * so + sw * co * ci, sw * si),
                    py = new Vec3d(-sw * co - cw * so * ci, -sw * so + cw * co * ci, cw * si),
                    size = (float)(1.0 - markerSizeJitter * rnd())
                };
            }

            _built = true;
        }

        /// <summary>
        /// Advances every particle to <paramref name="jd"/> and caches the heliocentric position.
        /// This is the expensive half; the caller decides when it is worth paying for.
        /// </summary>
        private void SolveOrbits(double jd, out double milliseconds)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            double T = jd - OrbitalMechanics.J2000;

            for (int k = 0; k < _p.Length; k++)
            {
                Particle pt = _p[k];

                double M = OrbitalMechanics.WrapDeg180(pt.m0 + pt.n * T) * OrbitalMechanics.Deg2Rad;
                double E = OrbitalMechanics.SolveKepler(M, pt.e);

                double cosE = System.Math.Cos(E);
                double sinE = System.Math.Sin(E);
                double xp = pt.a * (cosE - pt.e);
                double yp = pt.a * System.Math.Sqrt(1.0 - pt.e * pt.e) * sinE;

                _positionKm[k] = new Vec3d(
                    (pt.px.x * xp + pt.py.x * yp) * OrbitalMechanics.AuKm,
                    (pt.px.y * xp + pt.py.y * yp) * OrbitalMechanics.AuKm,
                    (pt.px.z * xp + pt.py.z * yp) * OrbitalMechanics.AuKm);
            }

            long end = System.Diagnostics.Stopwatch.GetTimestamp();
            milliseconds = (end - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            LastSolvedCount = _p.Length;
            LastSolveMilliseconds = milliseconds;
            SolveCount++;
            _lastSolvedJd = jd;
        }

        /// <summary>
        /// Seconds between orbital solves, sized so no particle drifts more than
        /// <see cref="angularStalenessDegrees"/> of mean anomaly.
        ///
        /// This is the whole optimisation in one line: the required refresh rate is proportional to
        /// how fast the belt is actually moving, which is proportional to the time rate. A paused
        /// simulation needs an infinite interval, and a fast clock collapses it to zero.
        ///
        /// Public because it is the derivation rather than an implementation detail: the audit tool
        /// and <c>BeltCadenceTests</c> assert the cadence against this, not against a frame count.
        /// </summary>
        public double SolveIntervalSeconds(double daysPerSecond)
        {
            if (!adaptSolveCadence) return 0.0;
            if (daysPerSecond <= 0.0) return double.PositiveInfinity;

            double tolerance = System.Math.Max(angularStalenessDegrees, 1e-6);
            double driftPerSecond = _maxMeanMotion * daysPerSecond;   // degrees of mean anomaly per second
            if (driftPerSecond <= 0.0) return double.PositiveInfinity;
            return tolerance / driftPerSecond;
        }

        /// <summary>
        /// Places and submits the instances. Called by <see cref="SolarSystemSimulation"/> once the
        /// floating origin for this frame is known, so the belt moves with everything else.
        /// </summary>
        /// <param name="timeState">
        /// Time and pause state, used only to pick the solve cadence. Pass null to force a solve,
        /// which is what the editor preview and the capture poses want.
        /// </param>
        public void Render(Camera cam, Vec3d originKm, double unitsPerKm, double jd, BeltTimeState? timeState)
        {
            if (!isActiveAndEnabled || IsRemovedPresentationField)
            {
                LastSolvedCount = 0;
                LastSubmittedCount = 0;
                LastSolveMilliseconds = 0.0;
                LastSubmitMilliseconds = 0.0;
                LastRenderUsedCache = false;
                LastRenderWasIdle = true;
                return;
            }
            if (!_built) Build();
            if (!_built || _p == null || _markerMesh == null) return;

            Material mat = SolarMaterials.Marker;
            if (mat == null) return;

            _drivenFrame = Time.frameCount;
            LastRenderWasIdle = false;

            double daysPerSecond = timeState.HasValue ? timeState.Value.daysPerSecond : double.PositiveInfinity;
            bool paused = timeState.HasValue && timeState.Value.paused;

            // ---- phase A: orbital solve, on the derived cadence ----------------------------------
            double now = timeState.HasValue && timeState.Value.nowSeconds > 0.0
                ? timeState.Value.nowSeconds
                : Time.realtimeSinceStartupAsDouble;
            bool neverSolved = double.IsNaN(_lastSolvedJd);
            bool dateMoved = neverSolved || jd != _lastSolvedJd;
            bool resolvedThisCall = false;

            if (dateMoved)
            {
                bool solve;
                if (neverSolved)
                {
                    solve = true;
                }
                else if (paused)
                {
                    // The date can only have changed by an explicit jump (the N and R keys), so
                    // re-solve immediately. Otherwise a paused belt is solved once and left alone.
                    solve = true;
                }
                else
                {
                    double interval = SolveIntervalSeconds(daysPerSecond);

                    // A discontinuous jump of the epoch must not be throttled: the cache would hold
                    // positions for a completely different date. The threshold is four cadence
                    // steps, so a fast-forward is treated as a jump and ordinary advance is not.
                    double jumpDays = System.Math.Abs(jd - _lastSolvedJd);
                    double jumpThreshold = JumpThresholdDays(interval, daysPerSecond);
                    solve = jumpDays > jumpThreshold || now >= _nextSolveTime;
                    if (solve) _nextSolveTime = now + interval;
                }

                if (solve)
                {
                    double milliseconds;
                    SolveOrbits(jd, out milliseconds);
                    resolvedThisCall = true;
                }
            }
            _lastPaused = paused;

            // ---- phase B: submit ------------------------------------------------------------------
            long submitStart = System.Diagnostics.Stopwatch.GetTimestamp();

            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            Vector3 camFwd = cam != null ? cam.transform.forward : Vector3.forward;
            Quaternion camRot = cam != null ? cam.transform.rotation : Quaternion.identity;
            float farPlane = cam != null ? cam.farClipPlane : 0f;
            float ang = Mathf.Max(markerAngularSize, 1e-6f);

            // The view cone's cosine. 1.0 would be a perfect frustum side plane; the margin widens it
            // so a marker does not pop into existence exactly at the screen edge.
            float halfFovRad = (cam != null ? cam.fieldOfView : 50f) * 0.5f * Mathf.Deg2Rad;
            float coneCos = Mathf.Cos(Mathf.Min(halfFovRad * (1f + viewConeMargin), Mathf.PI * 0.5f));

            // Nothing to do at all when the cached positions, the floating origin and the camera are
            // all unchanged. Note the test is on whether the *solve* ran, not on whether the date
            // moved: on a throttled frame the date advanced but every position is bit-identical, so
            // the instance buffer would come out byte-for-byte the same. This is the frame that
            // matters for a planet-surface view - slow time, still camera - and it costs nothing.
            bool worldStill = camPos == _lastCameraPosition && camRot == _lastCameraRotation &&
                              Mathf.Approximately(farPlane, _lastFarPlane) &&
                              _lastOriginKm.x == originKm.x && _lastOriginKm.y == originKm.y &&
                              _lastOriginKm.z == originKm.z;
            if (worldStill && !resolvedThisCall && !neverSolved)
            {
                LastRenderWasIdle = true;
                LastSubmittedCount = 0;
                LastSubmitMilliseconds = 0.0;
                LastRenderUsedCache = true;
                return;
            }

            _lastCameraPosition = camPos;
            _lastCameraRotation = camRot;
            _lastFarPlane = farPlane;
            _lastOriginKm = originKm;
            LastRenderUsedCache = !resolvedThisCall;

            int drawn = 0;
            int submitted = 0;
            double farPlaneUnits = farPlane;

            for (int k = 0; k < _p.Length; k++)
            {
                Vec3d km = _positionKm[k];

                var world = new Vector3(
                    (float)((km.x - originKm.x) * unitsPerKm),
                    (float)((km.z - originKm.z) * unitsPerKm),
                    (float)(-(km.y - originKm.y) * unitsPerKm));

                Vector3 toMarker = world - camPos;
                float dist = toMarker.magnitude;

                if (cullBeyondFarPlane && dist > farPlaneUnits) continue;
                if (cullBehindCamera && dist > 1e-9f &&
                    Vector3.Dot(toMarker, camFwd) / dist < coneCos) continue;

                float scale = Mathf.Max(markerMinUnits, dist * ang * _p[k].size);

                _matrices[drawn++] = Matrix4x4.TRS(world, Quaternion.identity, new Vector3(scale, scale, scale));
                submitted++;

                if (drawn == 1023)
                {
                    Graphics.DrawMeshInstanced(_markerMesh, 0, mat, _matrices, drawn);
                    drawn = 0;
                }
            }

            if (drawn > 0) Graphics.DrawMeshInstanced(_markerMesh, 0, mat, _matrices, drawn);

            long submitEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            LastSubmitMilliseconds = (submitEnd - submitStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            LastSubmittedCount = submitted;
        }

        /// <summary>
        /// Guard for the "is this a discontinuous jump in date" test: how many days the date may
        /// advance between two calls before the cached positions are treated as belonging to a
        /// different epoch and a solve is forced regardless of the cadence.
        ///
        /// Four cadence intervals of simulated time, floored at one day. The floor matters at the
        /// extremes: when the cadence is infinite (paused) the natural threshold would be zero, which
        /// would classify every frame as a jump and defeat the whole throttle.
        ///
        /// Public for the same reason as <see cref="SolveIntervalSeconds"/>: it is the rule a test
        /// verifies, not a private detail.
        /// </summary>
        public static double JumpThresholdDays(double intervalSeconds, double daysPerSecond)
        {
            if (double.IsInfinity(intervalSeconds) || double.IsNaN(intervalSeconds)) return 1.0;
            return Math.Max(1.0, intervalSeconds * daysPerSecond * 4.0);
        }

        /// <summary>Legacy overload: solves unconditionally, the way the class behaved before the cadence existed.</summary>
        public void Render(Camera cam, Vec3d originKm, double unitsPerKm, double jd)
        {
            Render(cam, originKm, unitsPerKm, jd, null);
        }

        /// <summary>
        /// Editor / idle preview. When the simulation is not running (scene not in play mode) the
        /// belt still draws at the J2000 epoch with no floating origin, so the scene view is not
        /// empty while you are building.
        /// </summary>
        private void Update()
        {
            if (IsRemovedPresentationField) return;
            if (_drivenFrame == Time.frameCount) return;
            if (!Application.isPlaying)
            {
                // Edit mode: no simulation to ask, so draw at the J2000 epoch. The clock is injected
                // from the editor's own frame time so the cadence behaves as it does in play mode -
                // which is what the belt's editor preview is for.
                var state = new BeltTimeState { paused = false, daysPerSecond = 5f };
#if UNITY_EDITOR
                state.nowSeconds = UnityEditor.EditorApplication.timeSinceStartup;
#endif
                Render(Camera.main, Vec3d.Zero, 1e-6, OrbitalMechanics.J2000, state);
                return;
            }
            Render(Camera.main, Vec3d.Zero, 1e-6, OrbitalMechanics.J2000);
        }
    }

    /// <summary>What the belt needs to know about time to choose a solve cadence.</summary>
    public struct BeltTimeState
    {
        /// <summary>Simulated days per real second. Ignored when <see cref="paused"/>.</summary>
        public float daysPerSecond;
        public bool paused;

        /// <summary>
        /// Wall clock to schedule the cadence against, in seconds. Zero or negative means "read the
        /// real clock".
        ///
        /// Injected rather than read directly so the cadence is testable: a benchmark or an EditMode
        /// test can step a synthetic clock at exactly 1/60 s per frame and get the cadence a real
        /// player would see, instead of whatever the harness's loop happens to run at.
        /// </summary>
        public double nowSeconds;
    }
}
