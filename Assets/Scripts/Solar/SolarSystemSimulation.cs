using System.Collections.Generic;
using UnityEngine;

namespace Planet.Solar
{
    /// <summary>
    /// The revolution engine. Every frame it
    ///
    ///   1. advances a Julian date,
    ///   2. solves Kepler's equation for every body to get its true heliocentric position,
    ///   3. rebuilds satellite positions on top of their parent,
    ///   4. shifts the whole system so the focused body sits at the world origin (floating origin).
    ///
    /// Step 4 is what makes a true-scale solar system tractable in a float renderer. The scene
    /// spans 1.5e10 km tip to tip; float32 keeps about 7 significant digits, so a body parked at
    /// its absolute position would jitter by tens of thousands of kilometres. Keeping the camera
    /// pinned near the origin and moving the *system* instead means every distance the renderer
    /// ever sees is small.
    ///
    /// All orbital state is kept in doubles; only the final camera-relative offset becomes a float.
    /// </summary>
    [DisallowMultipleComponent]
    public class SolarSystemSimulation : MonoBehaviour
    {
        public static SolarSystemSimulation Instance { get; private set; }

        [Header("Scale")]
        [Tooltip("Kilometres represented by one scene unit. 1,000,000 gives: Sun radius 0.696, " +
                 "Earth orbit 149.6, Neptune orbit 4,495, Kuiper belt outer edge 7,480.")]
        public float kmPerUnit = 1000000f;

        [Tooltip("Multiplies every body radius. 1 = true scale. Raising it makes the planets " +
                 "visible from far away, at the cost of the proportions being a lie.")]
        public float bodySizeExaggeration = 1f;

        [Header("Epoch and time")]
        [Tooltip("Julian date. 2451545.0 = J2000.0 = 2000-01-01 12:00 TT.")]
        public double julianDate = OrbitalMechanics.J2000;
        [Tooltip("Simulated days per real second.")]
        public float daysPerSecond = 5f;
        public bool paused = false;

        [Header("Floating origin")]
        [Tooltip("Body the view is locked to. The whole system is offset so this body is at (0,0,0).")]
        public string focusBody = "Earth";
        [Tooltip("Approximate seconds the origin takes to catch up when the focus changes.")]
        public float originCatchUpSeconds = 0.35f;

        [Header("Lighting")]
        [Tooltip("Directional light intensity.")]
        public float intensityBase = 1.35f;

        [Tooltip("Orbit line width, as a fraction of the camera's distance from the Sun.")]
        public float lineWidthPerUnit = 0.0016f;

        [Tooltip("Opacity of orbit lines. The focused body's own orbit fades out as you close in, " +
                 "so the line does not score a scratch across the planet.")]
        public float orbitLineAlpha = 0.62f;

        [Header("Input")]
        [Tooltip("Turn off to freeze all keyboard control (useful when scripting a capture).")]
        public bool handleInput = true;

        [Header("References")]
        public Transform bodiesRoot;
        public Transform orbitsRoot;
        public Light sunLight;
        public Camera viewCamera;

        [Header("Space frames")]
        [Tooltip("Binds the simulation to the four-rung space frame tree (galaxy -> star -> body -> " +
                 "surface chunk) so surface detail can be expressed in its own coordinate system. " +
                 "Leaving this off keeps the scene exactly as it was before the framework existed.")]
        public bool enableSpaceFrames = true;

        [Tooltip("Disabled by default: the orbits root is shifted with the origin every frame, which " +
                 "is cheap, and hiding it changes what the HUD and the capture poses show.")]
        public bool cullOrbitsNearBody = false;

        // ---- runtime state ---------------------------------------------------------------
        private CelestialBody[] _views;
        private Body[] _defs;
        private Vec3d[] _posKm;                 // heliocentric (or parent-relative resolved) ecliptic km
        private Vec3d[] _basisX, _basisY, _basisZ;   // equatorial basis per body, ecliptic frame
        private int[] _parentIndex;
        private Vec3d _originKm;
        private Vec3d _lag;
        private bool _originReady;
        private bool _focusSwitched;
        private int _focusIndex;
        private OrbitPath[] _orbitPaths;
        private CircleLine[] _circleLines;
        private BeltField[] _belts;
        private SolarSpaceContext _space;
        private bool _built;
        private bool _orbitsVisible = true;

        public float KmPerUnit { get { return kmPerUnit > 0f ? kmPerUnit : 1e6f; } }
        public double UnitsPerKm { get { return 1.0 / KmPerUnit; } }
        public Vec3d OriginKm { get { return _originKm; } }
        public int BodyCount { get { return _views != null ? _views.Length : 0; } }
        public CelestialBody ViewAt(int i) { return _views[i]; }
        public Body DefinitionAt(int i) { return _defs[i]; }
        public Vec3d PositionKmAt(int i) { return _posKm[i]; }
        public Camera ViewCamera { get { return viewCamera; } }

        /// <summary>
        /// The frame tree binding, or null when <see cref="enableSpaceFrames"/> is off. This is the
        /// entry point for anything that needs to express a position in the cosmic coordinate
        /// system rather than in render units.
        /// </summary>
        public SolarSpaceContext SpaceContext { get { return _space; } }

        public int FocusIndex { get { return _focusIndex; } }
        public CelestialBody FocusView { get { return _views != null ? _views[_focusIndex] : null; } }
        public Body FocusDefinition { get { return _defs != null ? _defs[_focusIndex] : null; } }

        /// <summary>
        /// Scene-unit radius of the focused body, read from the body itself instead of being
        /// re-derived from the catalog, so the camera, the shells and the render scale can never
        /// disagree about how big the body is.
        ///
        /// <para>Returns 0 when there is no focused body or its scale has not been resolved.
        /// Callers that gate on "am I near the surface" must treat 0 as "unknown" — a fallback
        /// of 1 scene unit would put a close-orbiting camera "inside" a million-km body and
        /// permanently block the near-surface mode.</para>
        /// </summary>
        public float FocusRadiusUnits
        {
            get
            {
                CelestialBody view = FocusView;
                return view != null ? view.RadiusUnits : 0f;
            }
        }

        private void OnEnable()
        {
            Instance = this;
            Build();
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        public void Build()
        {
            _built = false;

            if (bodiesRoot == null) bodiesRoot = transform;
            if (viewCamera == null) viewCamera = Camera.main;

            _views = bodiesRoot.GetComponentsInChildren<CelestialBody>(true);
            if (_views.Length == 0) return;

            _defs = new Body[_views.Length];
            _posKm = new Vec3d[_views.Length];
            _basisX = new Vec3d[_views.Length];
            _basisY = new Vec3d[_views.Length];
            _basisZ = new Vec3d[_views.Length];
            _parentIndex = new int[_views.Length];

            var index = new Dictionary<string, int>();
            for (int i = 0; i < _views.Length; i++) index[_views[i].bodyName] = i;

            for (int i = 0; i < _views.Length; i++)
            {
                _defs[i] = SolarSystemCatalog.Find(_views[i].bodyName);
                _parentIndex[i] = -1;
                if (_defs[i] != null && !string.IsNullOrEmpty(_defs[i].Parent))
                {
                    int p;
                    if (index.TryGetValue(_defs[i].Parent, out p)) _parentIndex[i] = p;
                }

                // Equatorial basis of this body, expressed in the ecliptic frame.
                Vec3d pole = _defs[i] != null
                    ? OrbitalMechanics.PoleFromRaDec(_defs[i].PoleRaDeg, _defs[i].PoleDecDeg)
                    : new Vec3d(0, 0, 1);
                BuildBasis(pole, out _basisX[i], out _basisY[i], out _basisZ[i]);
            }

            // Satellites inherit the parent's equator, so their orbits lie in the planet's own
            // equatorial plane -- which is why Uranus' moons come out in its 98-degree tilted plane.
            for (int i = 0; i < _views.Length; i++)
            {
                Body def = _defs[i];
                if (def == null || def.Kind != BodyKind.Moon || _parentIndex[i] < 0) continue;
                if (def.MoonOrbit.frame != MoonFrame.ParentEquator) continue;
                int p = _parentIndex[i];
                _basisX[i] = _basisX[p];
                _basisY[i] = _basisY[p];
                _basisZ[i] = _basisZ[p];
            }

            if (orbitsRoot != null)
            {
                _orbitPaths = orbitsRoot.GetComponentsInChildren<OrbitPath>(true);
                _circleLines = orbitsRoot.GetComponentsInChildren<CircleLine>(true);
            }
            if (_orbitPaths == null) _orbitPaths = new OrbitPath[0];
            if (_circleLines == null) _circleLines = new CircleLine[0];
            _belts = GetComponentsInChildren<BeltField>(true);

            // The space-frame binding is created here rather than required in the scene so an
            // existing SolarSystem.unity keeps working untouched; the scene builder adds it
            // explicitly so a rebuilt scene has it visible in the Inspector. Auto-creation is
            // play-mode only: adding a component from OnEnable in edit mode would dirty the scene
            // every time the asset database refreshed.
            _space = GetComponent<SolarSpaceContext>();
            if (_space == null && enableSpaceFrames && Application.isPlaying)
                _space = gameObject.AddComponent<SolarSpaceContext>();
            if (_space != null)
            {
                _space.enabled = enableSpaceFrames;
                if (enableSpaceFrames) _space.Initialize(this);
                else _space = null;
            }

            SetFocusByName(focusBody, true);

            _built = true;
        }

        private static void BuildBasis(Vec3d pole, out Vec3d bx, out Vec3d by, out Vec3d bz)
        {
            bz = Normalize(pole);
            Vec3d helper = System.Math.Abs(bz.z) < 0.9 ? new Vec3d(0, 0, 1) : new Vec3d(1, 0, 0);
            bx = Normalize(Cross(helper, bz));
            by = Cross(bz, bx);
        }

        private static Vec3d Cross(Vec3d a, Vec3d b)
        {
            return new Vec3d(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        }

        private static Vec3d Normalize(Vec3d v)
        {
            double l = v.Length;
            return l > 1e-12 ? v / l : new Vec3d(0, 0, 1);
        }

        // ==================================================================================
        // Focus
        // ==================================================================================

        public void SetFocusByName(string name, bool snap)
        {
            if (_views == null) return;
            for (int i = 0; i < _views.Length; i++)
            {
                if (string.Equals(_views[i].bodyName, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    SetFocusIndex(i, snap);
                    return;
                }
            }
        }

        public void SetFocusIndex(int i, bool snap = false)
        {
            if (_views == null || _views.Length == 0) return;
            int next = ((i % _views.Length) + _views.Length) % _views.Length;
            if (next == _focusIndex && !snap) return;
            _focusIndex = next;
            focusBody = _views[_focusIndex].bodyName;
            _focusSwitched = !snap;
            if (snap) _originReady = false;
        }

        public void CycleFocus(int step)
        {
            if (_views == null) return;
            SetFocusIndex(_focusIndex + step, false);
        }

        // ==================================================================================
        // Frame update
        // ==================================================================================

        private void Update()
        {
            if (!_built) Build();
            if (!_built) return;

            HandleTimeInput();
            if (!paused) julianDate += daysPerSecond * Time.deltaTime;
            ComputePositions();
            UpdateOrigin();

            // The frame tree has to be in step with the origin before anything is laid out: the
            // surface rung's own origin and the observer's cosmic position both come from here.
            if (_space != null) _space.RefreshFrames(Time.deltaTime);

            ApplyTransforms();
            UpdateLightAndLines();
        }

        private void HandleTimeInput()        {
            if (!handleInput) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;

            if (kb.spaceKey.wasPressedThisFrame) paused = !paused;

            // N = jump to the real current date, R = back to the J2000 epoch.
            if (kb.nKey.wasPressedThisFrame) julianDate = OrbitalMechanics.JulianDateFromUtc(System.DateTime.UtcNow);
            if (kb.rKey.wasPressedThisFrame) { julianDate = OrbitalMechanics.J2000; paused = false; }

            // - / = halve or double the rate; \ snaps back to the 5 days-per-second default.
            if (kb.minusKey.wasPressedThisFrame) daysPerSecond = Mathf.Max(0.002f, daysPerSecond * 0.5f);
            if (kb.equalsKey.wasPressedThisFrame) daysPerSecond = Mathf.Min(2000000f, daysPerSecond * 2f);
            if (kb.backslashKey.wasPressedThisFrame) daysPerSecond = 5f;
        }

        /// <summary>
        /// Resolves every body's position for the current <see cref="julianDate"/> and advances the
        /// floating origin, without touching transforms.
        ///
        /// The per-frame path does this internally; this exists for the editor audit tool and for
        /// tests, which run outside play mode and would otherwise read an all-zero position array -
        /// which silently turns every downstream "how far away is this body" answer into garbage.
        /// </summary>
        public void RefreshPositions()
        {
            if (!_built) Build();
            if (!_built) return;
            ComputePositions();
            UpdateOrigin();
        }

        private void ComputePositions()
        {
            // Heliocentric bodies first, so satellites can reference a resolved parent.
            for (int i = 0; i < _views.Length; i++)
            {
                Body def = _defs[i];
                if (def == null || _parentIndex[i] >= 0) continue;
                Vec3d au = def.HasOrbit ? def.Orbit.PositionAu(julianDate) : Vec3d.Zero;
                _posKm[i] = au * OrbitalMechanics.AuKm;
            }

            for (int i = 0; i < _views.Length; i++)
            {
                Body def = _defs[i];
                if (def == null || _parentIndex[i] < 0) continue;

                Vec3d local = def.MoonOrbit.PositionKm(julianDate);   // in the reference frame's axes

                if (def.MoonOrbit.frame == MoonFrame.Ecliptic)
                {
                    _posKm[i] = _posKm[_parentIndex[i]] + local;
                }
                else
                {
                    Vec3d e = _basisX[i] * local.x + _basisY[i] * local.y + _basisZ[i] * local.z;
                    _posKm[i] = _posKm[_parentIndex[i]] + e;
                }
            }
        }

        /// <summary>
        /// Keeps the focused body at the origin: origin = focusPosition + lag, with the lag decaying
        /// to zero.
        ///
        /// It is tempting to exponentially smooth the origin *towards* the focus position instead,
        /// but that leaves a steady-state error of velocity x time-constant. At 5 days per second
        /// that is a harmless 4 scene units; at 36,500 days per second it is 30,000 units and the
        /// planet leaves the screen. Splitting the origin into "rigidly follow" plus "decaying
        /// residual" makes the tracking exact at any time scale while keeping focus changes smooth.
        /// </summary>
        private void UpdateOrigin()
        {
            Vec3d target = _posKm[_focusIndex];
            float tau = Mathf.Max(originCatchUpSeconds, 0.01f);

            if (!_originReady || _focusSwitched)
            {
                // Do not teleport the view when the focus changes: start the lag at whatever offset
                // the new target currently has, then let it decay.
                _lag = _originReady ? (_originKm - target) : Vec3d.Zero;
                _originReady = true;
                _focusSwitched = false;
            }
            else
            {
                _lag = _lag * System.Math.Exp(-Time.deltaTime / tau);
            }

            _originKm = target + _lag;
        }

        private void ApplyTransforms()
        {
            double unitsPerKm = UnitsPerKm;
            double daysSinceEpoch = julianDate - OrbitalMechanics.J2000;

            for (int i = 0; i < _views.Length; i++)
            {
                Body def = _defs[i];
                if (def == null) continue;

                Vec3d rel = _posKm[i] - _originKm;
                Vector3 world = OrbitalMechanics.ToUnity(rel, unitsPerKm);

                // Sidereal rotation: one full turn per |RotationHours|, negative for retrograde
                // bodies (Venus, Uranus, Pluto), which is why they come out turning backwards.
                double periodDays = def.RotationHours / 24.0;
                double spinDeg = periodDays != 0.0 ? 360.0 * daysSinceEpoch / periodDays : 0.0;

                // The radius is deliberately not passed in any more. Each body owns its scene-unit
                // scale in CelestialBody.sceneRadiusUnits and publishes it through
                // IPlanetRadiusProvider; the simulation only places and spins it. Deriving it here
                // on every frame was the last place the catalog's RadiusKm became a render fact.
                _views[i].ApplyState(world, spinDeg);
            }
        }

        private void UpdateLightAndLines()
        {
            // One directional light, aimed from the Sun at whatever we are looking at. For the
            // focused body that is exactly the real illumination geometry (the Sun subtends under
            // 1 degree from anywhere past Mercury). See the scene notes for the limitation when
            // viewing the whole system at once.
            if (sunLight != null)
            {
                Vec3d d = _posKm[_focusIndex] - Vec3d.Zero;
                Vector3 dir = OrbitalMechanics.ToUnity(d, 1.0);
                if (dir.sqrMagnitude > 1e-12f)
                    sunLight.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                sunLight.intensity = Mathf.Clamp(intensityBase, 0f, 20f);
                // The planet shader uses stable analytic lighting and deterministic cloud
                // shadows. The solar-system shadow atlas cannot cover these astronomical ranges.
                sunLight.shadows = LightShadows.None;
            }

            // Line widths are held at a constant fraction of the viewing distance so an orbit is
            // legible whether you are 0.01 or 10,000 units from the Sun.
            float camDist = 0f;
            if (viewCamera != null) camDist = viewCamera.transform.position.magnitude;
            float w = Mathf.Clamp(camDist * lineWidthPerUnit, 1e-5f, 40f);

            // The focused body's own orbit runs straight through it. Up close that reads as a
            // scratch across the disc, so fade it out below ~8 radii and back in past ~40.
            float focusRadius = Mathf.Max(FocusRadiusUnits, 1e-9f);
            float focusAlpha = Mathf.Clamp01((camDist / focusRadius - 8f) / 32f);

            if (_orbitPaths != null)
            {
                for (int i = 0; i < _orbitPaths.Length; i++)
                {
                    OrbitPath p = _orbitPaths[i];
                    if (p == null) continue;
                    p.SetWidth(w);
                    float a = orbitLineAlpha;
                    if (p.bodyName == focusBody) a *= focusAlpha;
                    p.SetAlpha(a);
                }
            }

            if (_circleLines != null)
            {
                for (int i = 0; i < _circleLines.Length; i++)
                    if (_circleLines[i] != null) _circleLines[i].SetWidth(w * 0.6f);
            }

            for (int i = 0; i < _belts.Length; i++)
            {
                if (_belts[i] == null) continue;
                // The time state lets the belt pick a solve cadence from how fast it is actually
                // moving, instead of re-solving 7,200 Kepler equations on a frame where nothing
                // visible changed.
                var time = new BeltTimeState { daysPerSecond = daysPerSecond, paused = paused };
                _belts[i].Render(viewCamera, _originKm, UnitsPerKm, julianDate, time);
            }

            ApplyFarFieldPolicy(camDist, focusRadius);

            // The orbit rings ride along with the floating origin.
            if (orbitsRoot != null)
                orbitsRoot.position = OrbitalMechanics.ToUnity(_originKm * -1.0, UnitsPerKm);
        }

        /// <summary>
        /// What happens to everything that is <i>not</i> the focused body, once the camera is close
        /// enough to a planet that the rest of the system is measured in fractions of a pixel.
        ///
        /// <para>At true scale there is nothing to simplify geometrically - every other body already
        /// covers less than a pixel, which is why the orbit lines exist at all. What is worth doing
        /// is refusing to <i>submit</i> geometry that cannot change a pixel:</para>
        ///
        /// <list type="bullet">
        /// <item><b>Body renderers.</b> A body whose disc projects under
        /// <c>SolarSpaceContext.subPixelThreshold</c> pixels is switched off. The 'G' exaggeration
        /// multiplies every radius by 400, so the classification re-derives itself the moment the
        /// user asks for a visible solar system - there is no second code path and no flag to keep
        /// in sync.</item>
        /// <item><b>Orbit lines.</b> Optional (<see cref="cullOrbitsNearBody"/>), because the orbit
        /// lines are the primary navigation aid and hiding them changes what the capture poses
        /// record. The hysteresis band stops the root flickering at the threshold.</item>
        /// <item><b>Debris belts.</b> Handled inside <see cref="BeltField"/> - see its class comment
        /// for why the win there is in the solve cadence rather than in the drawing.</item>
        /// </list>
        /// </summary>
        private void ApplyFarFieldPolicy(float camDist, float focusRadius)
        {
            if (_space == null) return;

            if (_space.cullSubPixelBodies)
            {
                for (int i = 0; i < _views.Length; i++)
                {
                    if (_views[i] == null) continue;
                    BodyRenderTier tier = _space.ClassifyBody(i);
                    _views[i].SetRenderersVisible(tier != BodyRenderTier.Skipped);
                }
            }

            if (!cullOrbitsNearBody || orbitsRoot == null) return;

            // Ratio form, so the band works for a 2,440 km Mercury and a 696,340 km Sun alike.
            float ratio = camDist / Mathf.Max(focusRadius, 1e-9f);
            if (_orbitsVisible)
            {
                if (ratio < 3f) _orbitsVisible = false;
            }
            else if (ratio > 4f)
            {
                _orbitsVisible = true;
            }
            if (orbitsRoot.gameObject.activeSelf != _orbitsVisible)
                orbitsRoot.gameObject.SetActive(_orbitsVisible);
        }

        // ==================================================================================
        // Helpers for the camera / HUD
        // ==================================================================================

        /// <summary>
        /// Advances nothing and simply lays every body out at the current Julian date.
        /// Used by the scene builder so the saved scene is not a pile of objects at the origin,
        /// and usable from the inspector to re-seed the layout after editing the epoch.
        /// </summary>
        [ContextMenu("Snap To Current Epoch")]
        public void SnapToEpoch()
        {
            Build();
            if (!_built) return;
            ComputePositions();
            _originReady = false;
            UpdateOrigin();
            ApplyTransforms();
            UpdateLightAndLines();
        }

        /// <summary>
        /// The whole per-frame pipeline for the current Julian date, without advancing time. Lets a
        /// capture script settle the scene in one call instead of waiting for frames.
        /// </summary>
        public void Tick()
        {
            if (!_built) Build();
            if (!_built) return;
            ComputePositions();
            UpdateOrigin();
            ApplyTransforms();
            UpdateLightAndLines();
        }

        /// <summary>Formats a kilometre count the way the HUD does.</summary>
        public static string FormatKm(double km)
        {
            if (km >= 1.496e8) return string.Format("{0:N3} AU", km / OrbitalMechanics.AuKm);
            if (km >= 1e6) return string.Format("{0:N3} million km", km / 1e6);
            if (km >= 1e3) return string.Format("{0:N0} km", km);
            return string.Format("{0:N1} km", km);
        }

        /// <summary>Sphere of influence radius in scene units, used to frame a body sensibly.</summary>
        public float FrameDistanceFor(int index)
        {
            Body b = _defs[index];
            if (b == null) return 1f;

            // Prefer the body's own published radius so framing follows the same scale the body
            // actually renders at. The catalog is only a fallback for a missing view.
            CelestialBody view = _views != null && index < _views.Length ? _views[index] : null;
            double bodyRadius = view != null && view.RadiusUnits > 0f
                ? view.RadiusUnits
                : b.RadiusKm * UnitsPerKm * Mathf.Max(bodySizeExaggeration, 1e-6f);

            // Give ring systems room, and give moons a wider berth so the parent is in frame.
            // The ring mesh carries unit outer radius and is scaled by outerKm / RadiusKm, so the
            // ring's scene-unit outer edge is that same ratio applied to the body radius.
            double r = bodyRadius;
            if (b.Rings.HasValue && b.RadiusKm > 0.0) r = bodyRadius * (b.Rings.Value.outerKm / b.RadiusKm);
            if (b.Kind == BodyKind.Moon) r *= 6.0;
            return Mathf.Max((float)r * 4.5f, 1e-4f);
        }

        /// <summary>Human-readable simulation date.</summary>
        public string DateString()
        {
            System.DateTime dt = OrbitalMechanics.UtcFromJulianDate(julianDate);
            return dt.ToString("yyyy-MM-dd HH:mm") + " UTC";
        }
    }
}
