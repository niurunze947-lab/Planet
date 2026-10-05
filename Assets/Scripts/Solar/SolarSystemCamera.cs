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
            _targetDistance = ClampToSurfaceFloor(units);
            distance = _targetDistance;
            _hasTarget = true;
            ApplyTransform();
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
                _lastFocusIndex = _sim.FocusIndex;
                _hasTarget = false;              // re-frame the newly selected body
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

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 dir = rot * Vector3.forward;

            // The focused body is held at the world origin by the floating origin, so the orbit
            // centre is always (0,0,0).
            transform.position = -dir * distance;
            transform.rotation = rot;

            // Near plane just in front of whatever we are looking at; far plane has to reach the
            // far side of the Kuiper belt from anywhere inside it.
            float r = _sim.FocusRadiusUnits;
            float surfaceGap = Mathf.Max(distance - r, distance * 1e-3f);
            _cam.nearClipPlane = Mathf.Clamp(surfaceGap * 0.02f, 1e-6f, 50f);
            _cam.farClipPlane = Mathf.Max(40000f, distance * 4f);
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
                _targetDistance = FrameDistance();

            // G toggles a 400x size exaggeration so the planets can actually be seen from a
            // distance; it is off by default because it destroys the true proportions.
            if (kb.gKey.wasPressedThisFrame)
            {
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
            _targetDistance = ClampToSurfaceFloor(units);
            _hasTarget = true;
        }
    }
}
