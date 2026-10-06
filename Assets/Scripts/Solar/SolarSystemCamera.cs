using UnityEngine;
using UnityEngine.InputSystem;

namespace Planet.Solar
{
    /// <summary>
    /// Viewer for a true-scale solar system.
    ///
    /// The camera never travels: the simulation keeps the focused body at the world origin, so this
    /// rig only has to orbit that origin, and the zoom is logarithmic because the interesting
    /// length scales run from a 2,440 km planet radius to a 50 AU orbit — a factor of three million.
    ///
    /// The near clip plane is recomputed every frame from the orbit distance for the same reason:
    /// one fixed near plane cannot serve both a 0.03-unit close-up of Mercury and a 15,000-unit
    /// view of the whole Kuiper belt.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class SolarSystemCamera : MonoBehaviour
    {
        [Header("Orbit")]
        public float yaw = 35f;
        public float pitch = 18f;
        public float distance = 1.2f;
        public float minDistance = 1e-5f;
        public float maxDistance = 30000f;
        public float dragSensitivity = 0.22f;
        [Tooltip("Log-scale zoom applied per mouse-wheel unit.")]
        public float zoomStep = 0.002f;
        [Tooltip("Multiplier applied per second while a zoom key is held.")]
        public float keyZoomRate = 2.2f;

        [Header("Framing")]
        [Tooltip("Distance at which a body is framed, as a multiple of its own radius.")]
        public float frameMultiplier = 4.5f;
        public float frameSmoothSeconds = 0.5f;
        [Header("Idle drift")]
        public bool autoOrbit = false;
        public float autoOrbitSpeed = 1.2f;
        [Tooltip("Turn off to freeze all keyboard control (useful when scripting a capture).")]
        public bool handleInput = true;
        [Tooltip("Turn off when an external interaction controller owns zoom (ViewDistanceController).")]
        public bool handleZoomInput = true;
        [Tooltip("When true the rig does not orbit the body centre; an external controller owns " +
                 "the transform (surface free-roam). Orbit look, zoom and auto-orbit are suppressed.")]
        public bool freeRoam;

        /// <summary>Distance the rig is easing towards; also the exact position when scripted.</summary>
        public float TargetDistance { get { return _targetDistance; } }
        public bool HasPreciseSurfaceDistance { get { return _precisionBody != null; } }
        public double SurfaceClearanceMetres { get { return _preciseClearance; } }
        public double TargetSurfaceClearanceMetres { get { return _targetClearance; } }
        public Vec3d CameraBodyFixedMetres { get; private set; }
        private CelestialBody _precisionBody;
        private double _preciseClearance, _targetClearance;
        private Vec3d _precisionDirection;
        private Vec3d _precisionUp;
        private float _precisionYaw, _precisionPitch;

        // Unity 2022's ScreenPointToRay becomes invalid below roughly 1e-7 scene units even when
        // far/near is capped. At the project scale this is about 100 m; smaller values produce the
        // exact SendMouseEvents error seen during high-speed surface travel.
        public const float MinProjectionNear = 1e-7f;
        public const float MaxProjectionRatio = 1e5f;

        /// <summary>Keeps local altitude in double. Never round-trips it through the float centre distance.</summary>
        public void SetSurfaceClearanceMetres(double metres, bool snap = false)
        {
            if (_sim == null) _sim = SolarSystemSimulation.Instance;
            if (_sim == null || _sim.FocusView == null) return;
            if (_precisionBody != _sim.FocusView)
            {
                double initialClearance = CurrentSurfaceClearanceMetres();
                _precisionBody = _sim.FocusView;
                _precisionYaw = yaw; _precisionPitch = pitch;
                Vector3 worldDirection = -(Quaternion.Euler(pitch, yaw, 0) * Vector3.forward);
                _precisionDirection = RotateDouble(Quaternion.Inverse(_precisionBody.transform.rotation),
                    new Vec3d(worldDirection.x, worldDirection.y, worldDirection.z));
                double len = _precisionDirection.Length;
                _precisionDirection = _precisionDirection * (1.0 / System.Math.Max(len, 1e-20));
                Vector3 cameraUpWorld = transform.up.sqrMagnitude > 1e-12f ? transform.up : Vector3.up;
                _precisionUp = RotateDouble(Quaternion.Inverse(_precisionBody.transform.rotation),
                    new Vec3d(cameraUpWorld.x, cameraUpWorld.y, cameraUpWorld.z));
                double upLen = _precisionUp.Length;
                _precisionUp = _precisionUp * (1.0 / System.Math.Max(upLen, 1e-20));
                _preciseClearance = initialClearance;
            }
            _targetClearance = System.Math.Max(0.01, metres);
            if (snap) _preciseClearance = _targetClearance;
            _lastFocusIndex = _sim.FocusIndex; _hasTarget = true;
        }

        public double CurrentSurfaceClearanceMetres()
        {
            if (_precisionBody != null) return _preciseClearance;
            if (_sim == null) _sim = SolarSystemSimulation.Instance;
            if (_sim == null || _sim.FocusView == null) return 0;
            var body = _sim.FocusView;
            double unitsPerMetre = body.RadiusUnits / (body.Definition.RadiusKm * 1000.0);
            Vector3 dir = -(Quaternion.Euler(pitch, yaw, 0) * Vector3.forward);
            var bf = RotateDouble(Quaternion.Inverse(body.transform.rotation), new Vec3d(dir.x, dir.y, dir.z));
            var terrain = body.GetComponent<Planet.Procedural.Terrain.RuntimeTerrainRenderer>();
            double ground = terrain != null && terrain.RadiusMetres > 0 ? terrain.DisplayRadiusMetres(bf.x, bf.y, bf.z) : body.Definition.RadiusKm * 1000;
            return System.Math.Max(0.01, distance / unitsPerMetre - ground);
        }

        private static Vec3d RotateDouble(Quaternion rotation, Vec3d v)
        {
            Matrix4x4 m = Matrix4x4.Rotate(rotation);
            return new Vec3d(m.m00 * v.x + m.m01 * v.y + m.m02 * v.z,
                m.m10 * v.x + m.m11 * v.y + m.m12 * v.z,
                m.m20 * v.x + m.m21 * v.y + m.m22 * v.z);
        }

        /// <summary>Distance from the camera to the focused body's center in scene units.</summary>
        public float CurrentViewHeightUnits { get { return distance; } }

        /// <summary>Current focused body's physical render radius in scene units.</summary>
        public float FocusSurfaceRadiusUnits
        {
            get
            {
                if (_sim == null) _sim = SolarSystemSimulation.Instance;
                return _sim != null ? _sim.FocusRadiusUnits : 0f;
            }
        }

        /// <summary>Non-negative distance from the camera to the body's solid surface.</summary>
        public float SurfaceClearanceUnits
        {
            get { return Mathf.Max(CurrentViewHeightUnits - FocusSurfaceRadiusUnits, 0f); }
        }

        /// <summary>Surface clearance normalized by the focused body's radius.</summary>
        public float SurfaceClearanceRatio
        {
            get
            {
                float radius = FocusSurfaceRadiusUnits;
                return radius > 1e-6f ? SurfaceClearanceUnits / radius : 0f;
            }
        }

        /// <summary>
        /// Surface clearance as a fraction of the camera-to-center view height (0..1).
        /// 0 on the surface, approaching 1 far away.
        /// </summary>
        public float SurfaceClearanceOverViewHeight
        {
            get
            {
                float view = CurrentViewHeightUnits;
                return view > 1e-12f ? SurfaceClearanceUnits / view : 0f;
            }
        }

        /// <summary>
        /// Jumps the rig to an exact distance with no easing. Used by capture scripts and by the
        /// editor tooling; the interactive path goes through <see cref="FrameDistance"/> instead.
        /// </summary>
        public void SnapToDistance(float units)
        {
            _precisionBody = null;
            _targetDistance = ClampToSurfaceFloor(units);
            distance = _targetDistance;
            _hasTarget = true;
            ApplyTransform();
        }

        /// <summary>
        /// Updates the orbit distance bookkeeping without asking the orbit rig to apply its
        /// transform. Use while an external surface controller owns the camera pose.
        /// </summary>
        public void SetExternalViewHeight(float units)
        {
            float clamped = ClampToSurfaceFloor(units);
            _targetDistance = clamped;
            distance = clamped;
            _hasTarget = true;
        }

        /// <summary>
        /// Clamps the eased distance so the camera cannot sit inside the solid surface.
        /// Called by the interaction layer after input; also applied during smoothing.
        /// </summary>
        public void ClampCurrentDistanceToSurface()
        {
            distance = ClampToSurfaceFloor(distance);
        }

        /// <summary>Legal center distance: never below the focused body's surface radius.</summary>
        public float ClampToSurfaceFloor(float units)
        {
            float floor = Mathf.Max(FocusSurfaceRadiusUnits, minDistance);
            return Mathf.Clamp(units, floor, Mathf.Max(maxDistance, floor));
        }

        /// <summary>Re-frames the focused body immediately, with no easing.</summary>
        public void SnapToFocus()
        {
            _lastFocusIndex = _sim != null ? _sim.FocusIndex : -1;
            SnapToDistance(FrameDistance());
        }

        private Camera _cam;
        private SolarSystemSimulation _sim;
        private float _targetDistance = 1.2f;
        private bool _hasTarget;
        private int _lastFocusIndex = -1;

        /// <summary>Keys 1-9 and 0 jump straight to these bodies.</summary>
        public static readonly string[] HotkeyBodies =
        {
            "Sun", "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto"
        };

        private void OnEnable()
        {
            _cam = GetComponent<Camera>();
            _cam.nearClipPlane = 1e-4f;
            _cam.farClipPlane = 40000f;
        }

        private void Update()
        {
            if (_sim == null) _sim = SolarSystemSimulation.Instance;
            if (_sim == null) return;

            HandleFocusInput();
            HandleOrbitInput();

            if (_lastFocusIndex != _sim.FocusIndex)
            {
                _precisionBody = null;
                _lastFocusIndex = _sim.FocusIndex;
                _hasTarget = false;              // re-frame the newly selected body
            }

            if (_precisionBody != null)
            {
                if (_precisionBody != _sim.FocusView) { _precisionBody = null; }
                else
                {
                    double kLocal = 1.0 - System.Math.Exp(-Time.deltaTime / System.Math.Max(frameSmoothSeconds, 0.01f));
                    _preciseClearance += (_targetClearance - _preciseClearance) * kLocal;
                    return;
                }
            }

            float wanted = _hasTarget ? _targetDistance : FrameDistance();
            if (!_hasTarget) { _targetDistance = wanted; _hasTarget = true; }
            _targetDistance = ClampToSurfaceFloor(_targetDistance);

            float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(frameSmoothSeconds, 0.01f));
            distance = ClampToSurfaceFloor(Mathf.Lerp(distance, _targetDistance, k));
        }

        /// <summary>Comfortable viewing distance for whatever body currently has focus.</summary>
        private float FrameDistance()
        {
            float radius = _sim.FocusRadiusUnits;
            float d = radius * Mathf.Max(frameMultiplier, 1.2f);
            return Mathf.Clamp(d, minDistance, maxDistance);
        }

        private void LateUpdate()
        {
            ApplyTransform();
        }

        /// <summary>
        /// Places the rig on its orbit around the origin. Also callable directly so a capture script
        /// can position the camera without waiting for a frame.
        /// </summary>
        public void ApplyTransform()
        {
            // Surface free-roam owns the transform: never re-centre on the body.
            if (freeRoam) return;

            if (_sim == null) _sim = SolarSystemSimulation.Instance;
            if (_sim == null) return;

            if (_precisionBody != null && _precisionBody == _sim.FocusView)
            {
                float dy = Mathf.DeltaAngle(_precisionYaw, yaw), dp = Mathf.DeltaAngle(_precisionPitch, pitch);
                if (dy != 0 || dp != 0)
                {
                    Quaternion delta = Quaternion.Euler(dp, dy, 0);
                    _precisionDirection = RotateDouble(delta, _precisionDirection);
                    _precisionDirection = _precisionDirection * (1.0 / _precisionDirection.Length);
                    _precisionUp = RotateDouble(delta, _precisionUp);
                    _precisionUp = _precisionUp * (1.0 / System.Math.Max(_precisionUp.Length, 1e-20));
                    _precisionYaw = yaw; _precisionPitch = pitch;
                }
                var terrain = _precisionBody.GetComponent<Planet.Procedural.Terrain.RuntimeTerrainRenderer>();
                double radiusMetres = _precisionBody.Definition.RadiusKm * 1000;
                double ground = terrain != null && terrain.RadiusMetres > 0
                    ? terrain.DisplayRadiusMetres(_precisionDirection.x, _precisionDirection.y, _precisionDirection.z) : radiusMetres;
                double unitsPerMetre = _precisionBody.RadiusUnits / radiusMetres;
                CameraBodyFixedMetres = _precisionDirection * (ground + _preciseClearance);
                Vec3d world = RotateDouble(_precisionBody.transform.rotation, CameraBodyFixedMetres);
                transform.position = _precisionBody.transform.position + new Vector3((float)(world.x * unitsPerMetre), (float)(world.y * unitsPerMetre), (float)(world.z * unitsPerMetre));
                Vector3 forward = new Vector3((float)-world.x, (float)-world.y, (float)-world.z);
                Vector3 upHint = ToVector3(RotateDouble(_precisionBody.transform.rotation, _precisionUp));
                transform.rotation = StableLookRotation(forward, upHint, _precisionBody.transform.up);
                distance = (float)((ground + _preciseClearance) * unitsPerMetre);
                _targetDistance = (float)((ground + _targetClearance) * unitsPerMetre);
                // Projection still uses scene units, but centimetre terrain is rendered camera-relative.
                // Same 1e5:1 frustum cap as the orbit path: an uncapped 4e10:1 near/far crashed IMGUI.
                float pNear = (float)System.Math.Max(1e-13, System.Math.Min(50, _preciseClearance * unitsPerMetre * 0.1));
                float pFar = 40000f;
                SanitizeProjectionPlanes(ref pNear, ref pFar);
                _cam.nearClipPlane = pNear;
                _cam.farClipPlane = pFar;
                return;
            }

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 dir = rot * Vector3.forward;

            // The focused body is held at the world origin by the floating origin, so the orbit
            // centre is always (0,0,0).
            transform.position = -dir * distance;
            transform.rotation = rot;

            // Near plane just in front of whatever we are looking at. Far covers the focused
            // body and the current orbit; the old hard 40000-unit floor (Kuiper belt) made a
            // 4e10:1 frustum at surface range and crashed IMGUI (Screen position out of view
            // frustum). System-scale views still get a large far via distance * 4.
            // (Sanitize is inlined: Planet.Solar must not reference Planet.Interaction.)
            float r = _sim.FocusRadiusUnits;
            float surfaceGap = Mathf.Max(distance - r, distance * 1e-3f);
            float near = Mathf.Clamp(surfaceGap * 0.02f, 1e-6f, 50f);
            float far = Mathf.Max(distance * 4f, r * 20f);
            if (distance > 1f) far = Mathf.Max(far, 40000f);

            SanitizeProjectionPlanes(ref near, ref far);

            _cam.nearClipPlane = near;
            _cam.farClipPlane = far;
        }

        /// <summary>Clamps a projection pair away from Unity's invalid tiny near/far range.</summary>
        public static void SanitizeProjectionPlanes(ref float near, ref float far)
        {
            if (float.IsNaN(near) || float.IsInfinity(near) || near <= 0f)
                near = MinProjectionNear;
            if (near < MinProjectionNear) near = MinProjectionNear;
            if (float.IsNaN(far) || float.IsInfinity(far) || far <= 0f)
                far = near * MaxProjectionRatio;
            if (far < near * 2f) far = near * 2f;
            float maxFar = near * MaxProjectionRatio;
            if (far > maxFar) far = maxFar;
        }

        /// <summary>
        /// Builds a stable camera rotation from a forward direction and a persistent up hint.
        /// The hint is projected onto the forward tangent plane so it cannot flip when the view
        /// direction becomes parallel to a planet pole or spin axis.
        /// </summary>
        public static Quaternion StableLookRotation(Vector3 forward, Vector3 upHint, Vector3 fallbackUp)
        {
            if (forward.sqrMagnitude < 1e-12f || float.IsNaN(forward.x) ||
                float.IsNaN(forward.y) || float.IsNaN(forward.z))
                return Quaternion.identity;

            forward.Normalize();
            Vector3 up = Vector3.ProjectOnPlane(upHint, forward);
            if (up.sqrMagnitude < 1e-10f)
                up = Vector3.ProjectOnPlane(fallbackUp, forward);
            if (up.sqrMagnitude < 1e-10f)
            {
                Vector3 axis = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) < 0.9f
                    ? Vector3.up : Vector3.right;
                up = Vector3.ProjectOnPlane(axis, forward);
            }
            return Quaternion.LookRotation(forward, up.normalized);
        }

        private static Vector3 ToVector3(Vec3d value)
        {
            return new Vector3((float)value.x, (float)value.y, (float)value.z);
        }

        private void HandleFocusInput()
        {
            if (!handleInput) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            // Keys 1-9 then 0: Sun, Mercury, Venus, Earth, Mars, Jupiter, Saturn, Uranus,
            // Neptune, Pluto. Key.Digit1..Digit9 sit on consecutive enum values, so the mapping
            // is just an offset.
            for (int i = 0; i < HotkeyBodies.Length; i++)
            {
                Key k = i == 9 ? Key.Digit0 : (Key)((int)Key.Digit1 + i);
                if (kb[k].wasPressedThisFrame)
                {
                    _sim.SetFocusByName(HotkeyBodies[i], false);
                    return;
                }
            }

            if (kb.tabKey.wasPressedThisFrame)
                _sim.CycleFocus(kb.shiftKey.isPressed ? -1 : 1);

            if (kb.fKey.wasPressedThisFrame)
            {
                _precisionBody = null;
                _targetDistance = FrameDistance();
            }

            // G toggles a 400x size exaggeration so the planets can actually be seen from a
            // distance; it is off by default because it destroys the true proportions.
            if (kb.gKey.wasPressedThisFrame)
            {
                _precisionBody = null;
                _sim.bodySizeExaggeration = Mathf.Approximately(_sim.bodySizeExaggeration, 1f) ? 400f : 1f;
                _targetDistance = FrameDistance();
            }

            // L toggles the angular reference grid on procedural surfaces and the fallback clay.
            if (kb.lKey.wasPressedThisFrame)
                SolarMaterials.SetGridStrength(SolarMaterials.CurrentGridStrength > 0.001f ? 0f : SolarMaterials.GridStrength);
        }

        private void HandleOrbitInput()
        {
            if (!handleInput) return;
            if (freeRoam) return;
            var mouse = Mouse.current;
            var kb = Keyboard.current;

            if (mouse != null)
            {
                if (mouse.leftButton.isPressed)
                {
                    Vector2 d = mouse.delta.ReadValue() * dragSensitivity;
                    yaw += d.x;
                    pitch = Mathf.Clamp(pitch - d.y, -89.9f, 89.9f);
                }

                if (handleZoomInput)
                {
                    float scroll = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.01f)
                        Zoom(System.Math.Exp(scroll * zoomStep));
                }
            }

            if (kb != null)
            {
                if (handleZoomInput)
                {
                    if (kb.pageUpKey.isPressed || kb.zKey.isPressed) Zoom(System.Math.Exp(-keyZoomRate * Time.deltaTime));
                    if (kb.pageDownKey.isPressed || kb.xKey.isPressed) Zoom(System.Math.Exp(keyZoomRate * Time.deltaTime));
                }
                if (kb.escapeKey.wasPressedThisFrame) autoOrbit = false;
            }

            if (autoOrbit && (mouse == null || !mouse.leftButton.isPressed))
                yaw += autoOrbitSpeed * Time.deltaTime;
        }

        private void Zoom(double factor)
        {
            _targetDistance = ClampToSurfaceFloor((float)(_targetDistance * factor));
            _hasTarget = true;
        }

        /// <summary>Sets the desired orbit distance in scene units (used by the HUD's "fit" button).</summary>
        public void SetDistance(float units)
        {
            _precisionBody = null;
            _targetDistance = ClampToSurfaceFloor(units);
            _hasTarget = true;
        }
    }
}
