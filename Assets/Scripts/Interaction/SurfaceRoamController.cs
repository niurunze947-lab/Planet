using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Planet.Solar;
using Planet.Procedural.Terrain;
using Unity.Mathematics;

namespace Planet.Interaction
{
    /// <summary>
    /// C6 — surface free-fly. Takes over the camera when the near-surface gate opens.
    ///
    /// <para><b>Visuals are continuous.</b> Entering this mode does not move the camera, does
    /// not rotate the body, and does not re-aim the look. The system-view picture stays
    /// identical; only the input map changes (orbit look/zoom → free look + QWEASD).</para>
    ///
    /// <para><b>Body-fixed tracking.</b> The player's truth is a body-fixed offset in metres,
    /// and the free-look angles are body-fixed too. Each frame the camera is written from that
    /// offset and those angles through the body's live rotation, so both the ground under the
    /// player and the landscape in view stay put while the planet spins (相对地表静止).
    /// Writing only the position through the body while leaving the look in world space is what
    /// makes the planet appear to rotate around the camera.</para>
    ///
    /// <para><b>Speed</b> is bound to height above ground on one continuous linear curve
    /// (ground cap 100 m/s) with a short linear-acceleration lag (weak inertia).</para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(40)] // after simulation ApplyState and after SolarSystemCamera.LateUpdate
    public class SurfaceRoamController : MonoBehaviour
    {
        [Header("Wiring")]
        public SolarSystemCamera orbitCamera;
        public SurfaceModeController surfaceMode;
        public ViewDistanceController viewDistance;

        [Header("Free-look")]
        public float lookSensitivity = SurfaceRoamInput.DefaultLookSensitivity;
        [Tooltip("Seconds to reach the target speed (weak inertia).")]
        public float speedResponseSeconds = 0.35f;
        [Tooltip("Speed level at engage. Wheel and +/- change it; speed = 10 m/s × 2^level.")]
        public int initialSpeedLevel = 3;

        /// <summary>True while this controller owns the camera transform.</summary>
        public bool IsRoaming { get { return _roaming; } }

        public float Yaw { get { return _yaw; } }
        public float Pitch { get { return _pitch; } }

        /// <summary>Current fly speed level. Speed is 10 m/s × 2^level.</summary>
        public int SpeedLevel { get { return _speedLevel; } }

        /// <summary>Current fly speed in metres per second.</summary>
        public double FlySpeedMetresPerSecond
        {
            get { return SurfaceRoamInput.SpeedForLevel(_speedLevel); }
        }

        /// <summary>Player-to-surface distance in metres (the GUI figure).</summary>
        public double PlayerToSurfaceDistanceMetres
        {
            get
            {
                double radial = CurrentRadialClearanceMetres();
                if (radial >= 0.0) return radial;
                if (_space.IsValid) return math.max(_space.PlayerToSurfaceDistanceMetres, 0.0);
                return _fallbackAltitudeMetres;
            }
        }

        /// <summary>Player position in body-fixed metres — the roaming truth.</summary>
        public double3 PlayerBodyFixedMetres
        {
            get { return _playerBfMetres; }
        }

        public SurfaceLocalSpace LocalSpace { get { return _space; } }

        public double MoveSpeedMetres
        {
            get { return math.length(_velocityBodyMetresPerSecond); }
        }

        public double TargetSpeedMetres
        {
            get
            {
                double ceilingAlt = _ceilingAltitudeMetres > 1e-6
                    ? _ceilingAltitudeMetres
                    : SurfaceRoamSpeed.CeilingAltitudeMetres(FallbackRadiusMetres());
                double ceilingSpd = _ceilingSpeedMetresPerSecond > 1e-6
                    ? _ceilingSpeedMetresPerSecond
                    : SurfaceRoamSpeed.CeilingSpeedForRadius(FallbackRadiusMetres());
                return SurfaceRoamSpeed.TargetSpeedMetresPerSecond(
                    PlayerToSurfaceDistanceMetres, ceilingAlt, ceilingSpd);
            }
        }

        public bool ZoomSuppressed { get { return _roaming; } }

        public event Action RoamEngaged;
        public event Action RoamReleased;

        private bool _roaming;
        private SurfaceLocalSpace _space;
        private double3 _playerBfMetres;
        private float _yaw;
        private float _pitch;
        // The exact body-fixed camera orientation captured on entry. Keeping this as a quaternion
        // preserves the existing roll/attitude; converting it to Euler X/Y loses a component when
        // the orbit camera is viewed from an arbitrary point on the planet.
        private Quaternion _lookBodyRotation = Quaternion.identity;
        private int _speedLevel;
        private double _fallbackAltitudeMetres;
        private double _ceilingAltitudeMetres;
        private double _ceilingSpeedMetresPerSecond;
        /// <summary>Velocity in body-fixed metres per second (spin-invariant with the player truth).</summary>
        private double3 _velocityBodyMetresPerSecond;
        private bool _restoredOrbitInput = true;
        private bool _restoredZoomInput = true;
        private bool _restoredViewDistanceZoom = true;

        private void Awake()
        {
            if (orbitCamera == null) orbitCamera = GetComponent<SolarSystemCamera>();
            if (orbitCamera == null) orbitCamera = FindObjectOfType<SolarSystemCamera>();
            if (surfaceMode == null) surfaceMode = GetComponent<SurfaceModeController>();
            if (surfaceMode == null) surfaceMode = FindObjectOfType<SurfaceModeController>();
            if (viewDistance == null) viewDistance = GetComponent<ViewDistanceController>();
            if (viewDistance == null) viewDistance = FindObjectOfType<ViewDistanceController>();
        }

        private void OnEnable()
        {
            if (surfaceMode != null)
            {
                surfaceMode.EnteredSurfaceMode += OnEnteredSurfaceMode;
                surfaceMode.ExitedSurfaceMode += OnExitedSurfaceMode;
            }
        }

        private void OnDisable()
        {
            if (surfaceMode != null)
            {
                surfaceMode.EnteredSurfaceMode -= OnEnteredSurfaceMode;
                surfaceMode.ExitedSurfaceMode -= OnExitedSurfaceMode;
            }
            if (_roaming) ReleaseRoam();
        }

        private void OnEnteredSurfaceMode()
        {
            EngageRoam();
        }

        private void OnExitedSurfaceMode()
        {
            ReleaseRoam();
        }

        /// <summary>
        /// Switches input to free-fly. Does not touch the camera transform — the frame the
        /// player is already looking at is the frame they keep.
        /// </summary>
        public void EngageRoam()
        {
            if (_roaming || orbitCamera == null) return;
            if (orbitCamera.externalObserver) return;

            CapturePoseFromCamera();

            _restoredOrbitInput = orbitCamera.handleInput;
            _restoredZoomInput = orbitCamera.handleZoomInput;
            orbitCamera.handleInput = false;
            orbitCamera.handleZoomInput = false;
            orbitCamera.freeRoam = true;

            if (viewDistance != null)
            {
                _restoredViewDistanceZoom = viewDistance.zoomEnabled;
                viewDistance.zoomEnabled = false;
            }

            _velocityBodyMetresPerSecond = double3.zero;
            _speedLevel = initialSpeedLevel;
            _roaming = true;
            PublishHud();
            if (RoamEngaged != null) RoamEngaged();
        }

        public void ReleaseRoam()
        {
            if (!_roaming) return;

            if (orbitCamera != null)
            {
                // Keep the view height the player has now; only hand input back.
                if (math.length(_playerBfMetres) > 1e-6)
                {
                    orbitCamera.SnapToDistance((float)SurfaceLocalSpace.MetresToSceneUnits(
                        math.length(_playerBfMetres), KmPerUnit()));
                }
                var sim = SolarSystemSimulation.Instance;
                Vector3 bodyPosition = sim != null && sim.FocusView != null
                    ? sim.FocusView.transform.position
                    : Vector3.zero;
                Vector3 inward = bodyPosition - orbitCamera.transform.position;
                if (inward.sqrMagnitude > 1e-12f)
                {
                    Vector3 orbitEuler = Quaternion.LookRotation(inward.normalized, Vector3.up).eulerAngles;
                    orbitCamera.pitch = orbitEuler.x > 180f ? orbitEuler.x - 360f : orbitEuler.x;
                    orbitCamera.yaw = orbitEuler.y > 180f ? orbitEuler.y - 360f : orbitEuler.y;
                }
                orbitCamera.freeRoam = false;
                orbitCamera.handleInput = _restoredOrbitInput;
                orbitCamera.handleZoomInput = _restoredZoomInput;
            }

            if (viewDistance != null)
                viewDistance.zoomEnabled = _restoredViewDistanceZoom;

            _roaming = false;
            _velocityBodyMetresPerSecond = double3.zero;
            SurfaceRoamHudState.Clear();
            if (RoamReleased != null) RoamReleased();
        }

        private void Update()
        {
            if (!_roaming) return;

            HandleLook();
            HandleSpeed();
            HandleFly(Time.deltaTime);
            PublishHud();

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                ReleaseRoam();
        }

        private void PublishHud()
        {
            SurfaceRoamHudState.Publish(
                true, PlayerToSurfaceDistanceMetres, FlySpeedMetresPerSecond);
        }

        /// <summary>
        /// Mouse wheel changes the fly speed level (10 m/s × 2^level). The orbit view-height
        /// zoom stays disabled; the wheel is rebound to speed, not altitude. Keyboard −/=
        /// is left to the simulation's time-rate (it is bound there and would fight this).
        /// </summary>
        private void HandleSpeed()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                _speedLevel = SurfaceRoamInput.AdjustSpeedLevel(_speedLevel, scroll / 120f);
        }

        private void LateUpdate()
        {
            if (!_roaming || orbitCamera == null) return;
            WriteCameraFromBodyFixed();
            SyncViewHeightForGate();
        }

        private void HandleLook()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            bool rmb = mouse.rightButton.isPressed;
            Vector2 look = SurfaceRoamInput.LookDeltaDegrees(
                mouse.delta.ReadValue(), rmb, lookSensitivity);
            if (look.sqrMagnitude <= 0f) return;

            Quaternion enu = _space.IsValid ? _space.BodyFromEnu() : Quaternion.identity;
            Vector3 upBody = enu * Vector3.up;
            if (upBody.sqrMagnitude < 1e-12f) upBody = Vector3.up;
            _lookBodyRotation = ApplyLookDelta(_lookBodyRotation, upBody, look);

            Vector3 e = (Quaternion.Inverse(enu) * _lookBodyRotation).eulerAngles;
            _pitch = e.x > 180f ? e.x - 360f : e.x;
            _yaw = e.y > 180f ? e.y - 360f : e.y;
        }

        /// <summary>
        /// Applies local-up yaw and camera-right pitch while preserving the captured quaternion.
        /// Pitch is clamped by forward elevation so repeated mouse motion cannot flip the camera.
        /// </summary>
        public static Quaternion ApplyLookDelta(Quaternion bodyLook, Vector3 upBody, Vector2 delta)
        {
            bodyLook = NormalizeRotation(bodyLook);
            upBody = upBody.sqrMagnitude > 1e-12f ? upBody.normalized : Vector3.up;
            if (delta.sqrMagnitude <= 0f) return bodyLook;

            bodyLook = Quaternion.AngleAxis(delta.x, upBody) * bodyLook;
            Vector3 forward = bodyLook * Vector3.forward;
            float elevation = Mathf.Asin(Mathf.Clamp(Vector3.Dot(forward, upBody), -1f, 1f)) * Mathf.Rad2Deg;
            float nextElevation = Mathf.Clamp(elevation + delta.y, -89.9f, 89.9f);
            float appliedPitch = nextElevation - elevation;

            Vector3 rightBody = bodyLook * Vector3.right;
            if (Mathf.Abs(appliedPitch) > 1e-6f && rightBody.sqrMagnitude > 1e-12f)
                bodyLook = Quaternion.AngleAxis(-appliedPitch, rightBody) * bodyLook;

            return NormalizeRotation(bodyLook);
        }

        /// <summary>
        /// Camera-relative QWEASDQE with user speed level and weak inertia.
        /// Unity-editor gesture: the right mouse button must be held. Movement is integrated
        /// in body-fixed metres along the body-fixed look, so W is always "where the camera
        /// looks" relative to the ground while the planet turns.
        /// </summary>
        private void HandleFly(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            var mouse = Mouse.current;
            bool rmb = mouse != null && mouse.rightButton.isPressed;
            double3 axes = SurfaceRoamInput.FlyLocalAxes(
                kb.wKey.isPressed, kb.aKey.isPressed, kb.sKey.isPressed,
                kb.dKey.isPressed, kb.qKey.isPressed, kb.eKey.isPressed, rmb);

            Quaternion lookBody = LookBodyRotation();
            double speed = FlySpeedMetresPerSecond;

            // Unity-editor style: W/S follow the look direction (including pitch).
            double3 targetVel = double3.zero;
            if (math.any(axes != double3.zero))
            {
                Vector3 wish = lookBody * new Vector3((float)axes.x, (float)axes.y, (float)axes.z);
                if (wish.sqrMagnitude > 1e-12f) wish.Normalize();
                targetVel = SurfaceLocalSpace.FromVector3(wish) * speed;
            }

            // Weak inertia: reach the target in ~speedResponseSeconds (linear accel).
            double accel = speedResponseSeconds > 1e-3f
                ? System.Math.Max(speed, 1.0) / speedResponseSeconds
                : SurfaceRoamSpeed.DefaultAccelerationMetresPerSecondSquared;
            SurfaceRoamSpeed.IntegrateVelocity(
                ref _velocityBodyMetresPerSecond.x,
                ref _velocityBodyMetresPerSecond.y,
                ref _velocityBodyMetresPerSecond.z,
                targetVel.x, targetVel.y, targetVel.z,
                accel, deltaTime);

            // Body-fixed metres are already the spin-invariant truth — integrate in place.
            _playerBfMetres += _velocityBodyMetresPerSecond * (double)deltaTime;
            ClampPlayerOutsideSurface();
            _space.playerLocalMetres = BodyFixedToLocal(_playerBfMetres);
        }

        /// <summary>
        /// Free-look in the body-fixed ENU frame (east/up/north). Yaw therefore turns about
        /// the local surface normal — head left/right — not about the body's polar axis.
        /// </summary>
        private Quaternion LookBodyRotation()
        {
            return _lookBodyRotation;
        }

        /// <summary>
        /// Writes the camera from body-fixed truth and body-fixed free-look angles through the
        /// body's live rotation. Position and orientation rotate together with the planet, so the
        /// landscape stays put under and in front of the player (相对地表静止).
        /// </summary>
        private void WriteCameraFromBodyFixed()
        {
            var sim = SolarSystemSimulation.Instance;
            CelestialBody body = sim != null ? sim.FocusView : null;
            if (body == null) return;

            Quaternion bodyRot = body.transform.rotation;
            double units = SurfaceLocalSpace.MetresToSceneUnits(1.0, KmPerUnit());
            Vector3 localUnits = SurfaceLocalSpace.ToVector3(_playerBfMetres * units);
            Vector3 bodyPos = body.transform.position;

            Vector3 playerWorld = bodyPos + bodyRot * localUnits;

            // Never collapse into the solid body (float at planetary radius).
            float radiusUnits = body.RadiusUnits;
            if (radiusUnits > 0f)
            {
                Vector3 offset = playerWorld - bodyPos;
                float minDist = radiusUnits * 0.999f;
                if (offset.sqrMagnitude < minDist * minDist && offset.sqrMagnitude > 1e-20f)
                    playerWorld = bodyPos + offset.normalized * minDist;
                else if (offset.sqrMagnitude <= 1e-20f)
                    playerWorld = bodyPos + Vector3.up * minDist;
            }

            orbitCamera.transform.position = playerWorld;
            orbitCamera.transform.rotation = bodyRot * LookBodyRotation();
            ApplyClipPlanes();
        }

        /// <summary>
        /// Body-fixed free-look Euler → world rotation, given the body-fixed ENU basis.
        /// The body's live rotation is the outer factor so a spinning planet carries the look
        /// with it; the ENU factor makes yaw turn about the local surface normal.
        /// </summary>
        public static Quaternion BodyFixedLookToWorld(
            Quaternion bodyWorldRotation, Quaternion bodyFromEnu, float pitch, float yaw)
        {
            return bodyWorldRotation * bodyFromEnu * Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>Composes an exact body-fixed camera pose without an Euler round-trip.</summary>
        public static Quaternion BodyFixedLookToWorld(
            Quaternion bodyWorldRotation, Quaternion bodyFixedLook)
        {
            return bodyWorldRotation * bodyFixedLook;
        }

        private void ApplyClipPlanes()
        {
            var cam = orbitCamera != null ? orbitCamera.GetComponent<Camera>() : null;
            if (cam == null) return;

            var sim = SolarSystemSimulation.Instance;
            CelestialBody body = sim != null ? sim.FocusView : null;
            float radiusUnits = body != null ? body.RadiusUnits : 0f;
            float kmPerUnit = KmPerUnit();
            // The ENU frame is anchored at entry. At high fly speeds the player can travel far
            // enough that its local y component is no longer the radial altitude; derive clipping
            // from the current body-fixed radius so near/far remain valid during 40 km/s travel.
            double altitudeMetres = System.Math.Max(CurrentRadialClearanceMetres(), 1.0);
            float nearM = SurfaceCameraClip.NearMetres(altitudeMetres);
            float farM = SurfaceCameraClip.FarMetres(altitudeMetres, (double)radiusUnits * (double)kmPerUnit * 1000.0);
            float near = (float)SurfaceLocalSpace.MetresToSceneUnits(nearM, kmPerUnit);
            float far = Mathf.Max(
                (float)SurfaceLocalSpace.MetresToSceneUnits(farM, kmPerUnit),
                radiusUnits * 8f);
            SurfaceCameraClip.SanitizeClipPlanes(ref near, ref far);
            cam.nearClipPlane = near;
            cam.farClipPlane = far;
        }

        /// <summary>
        /// Publishes the free-roam player's centre distance as the camera's view height so the
        /// surface-mode gate (which reads min(current, target)) sees the true altitude and can
        /// still exit when the player climbs out of the band.
        /// </summary>
        private void SyncViewHeightForGate()
        {
            if (orbitCamera == null) return;
            double units = SurfaceLocalSpace.MetresToSceneUnits(math.length(_playerBfMetres), KmPerUnit());
            if (units > 0.0) orbitCamera.SetExternalViewHeight((float)units);
        }

        /// <summary>
        /// Takes the live camera pose as body-fixed truth and free-look angles.
        /// Nothing about the picture changes here — only the input map will.
        /// </summary>
        private void CapturePoseFromCamera()
        {
            float kmPerUnit = KmPerUnit();
            double radiusMetres = FallbackRadiusMetres();
            double flattening = 0.0;
            double3 shapeScale = new double3(1.0, 1.0, 1.0);

            var sim = SolarSystemSimulation.Instance;
            CelestialBody body = sim != null ? sim.FocusView : null;
            if (body != null && body.sceneRadiusUnits > 0f)
                radiusMetres = SurfaceLocalSpace.SceneUnitsToMetres(body.sceneRadiusUnits, kmPerUnit);
            if (sim != null && sim.FocusDefinition != null)
            {
                flattening = sim.FocusDefinition.Flattening;
                Vector3 axes = sim.FocusDefinition.ShapeScale;
                shapeScale = new double3(axes.x, axes.y, axes.z);
            }

            Vector3 camPosWorld = orbitCamera != null ? orbitCamera.transform.position : Vector3.zero;
            Quaternion bodyRot = body != null ? body.transform.rotation : Quaternion.identity;
            Vector3 bodyPos = body != null ? body.transform.position : Vector3.zero;
            Vector3 camInBodyUnits = Quaternion.Inverse(bodyRot) * (camPosWorld - bodyPos);
            double metresPerUnit = SurfaceLocalSpace.SceneUnitsToMetres(1.0, kmPerUnit);
            _playerBfMetres = SurfaceLocalSpace.FromVector3(camInBodyUnits) * metresPerUnit;

            double len = math.length(_playerBfMetres);
            double3 dir = len > 1e-6 ? _playerBfMetres / len : new double3(0.0, 1.0, 0.0);

            _space = SurfaceLocalSpace.CreateFromDirection(dir, radiusMetres, flattening, shapeScale);
            _space.playerLocalMetres = _space.BodyFixedToLocal(_playerBfMetres);
            _fallbackAltitudeMetres = math.max(_space.PlayerToSurfaceDistanceMetres, 0.0);

            IPlanetSurfaceQuery query = body != null ? body.GetComponent<IPlanetSurfaceQuery>() : null;
            if (query != null && _space.IsValid)
            {
                // Re-anchor on the terrain under the player so the HUD height is surface-referenced.
                double ground = query.SampleSurfaceRadiusMetres(dir, 1.0);
                if (ground > 0.0)
                {
                    _space = SurfaceLocalSpace.CreateAtAnchor(dir * ground, radiusMetres, flattening, shapeScale);
                    _space.playerLocalMetres = _space.BodyFixedToLocal(_playerBfMetres);
                    _fallbackAltitudeMetres = math.max(_space.PlayerToSurfaceDistanceMetres, 0.0);
                }
            }

            _ceilingAltitudeMetres = SurfaceRoamSpeed.CeilingAltitudeMetres(
                radiusMetres, ViewDistanceBoundary.DefaultSurfaceModeEnterRatio);
            _ceilingSpeedMetresPerSecond = SurfaceRoamSpeed.MarginalZoomSpeedMetresPerSecond(
                _ceilingAltitudeMetres, ViewDistanceBoundary.DefaultNearRate);
            if (!(_ceilingSpeedMetresPerSecond > SurfaceRoamSpeed.GroundMaxMetresPerSecond))
                _ceilingSpeedMetresPerSecond = SurfaceRoamSpeed.GroundMaxMetresPerSecond;

            // Keep the exact look the system view had, expressed in the local ENU frame so the
            // free-look angles stay ground-relative (yaw about the surface normal) while the
            // planet turns.
            Quaternion camRotWorld = orbitCamera != null ? orbitCamera.transform.rotation : Quaternion.identity;
            Quaternion enu = _space.IsValid ? _space.BodyFromEnu() : Quaternion.identity;
            _lookBodyRotation = NormalizeRotation(Quaternion.Inverse(bodyRot) * camRotWorld);

            // Keep the legacy angle properties meaningful for diagnostics/HUD consumers. They no
            // longer drive the pose; the quaternion above is authoritative and preserves roll.
            Vector3 e = (Quaternion.Inverse(enu) * _lookBodyRotation).eulerAngles;
            _pitch = e.x; if (_pitch > 180f) _pitch -= 360f;
            _yaw = e.y; if (_yaw > 180f) _yaw -= 360f;
            _speedLevel = initialSpeedLevel;
            _velocityBodyMetresPerSecond = double3.zero;
        }

        private static Quaternion NormalizeRotation(Quaternion rotation)
        {
            float length = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y +
                rotation.z * rotation.z + rotation.w * rotation.w);
            return length > 1e-6f && !float.IsNaN(length) && !float.IsInfinity(length)
                ? new Quaternion(rotation.x / length, rotation.y / length,
                    rotation.z / length, rotation.w / length)
                : Quaternion.identity;
        }

        private double3 BodyFixedToLocal(double3 playerBfMetres)
        {
            if (!_space.IsValid) return new double3(0.0, _fallbackAltitudeMetres, 0.0);
            return _space.BodyFixedToLocal(playerBfMetres);
        }

        /// <summary>Returns radial body-fixed clearance from the current terrain surface.</summary>
        private double CurrentRadialClearanceMetres()
        {
            double length = math.length(_playerBfMetres);
            if (!(length > 1e-6) || !math.isfinite(length)) return -1.0;

            var sim = SolarSystemSimulation.Instance;
            CelestialBody body = sim != null ? sim.FocusView : null;
            if (body == null) return -1.0;

            double3 direction = _playerBfMetres / length;
            IPlanetSurfaceQuery query = body.GetComponent<IPlanetSurfaceQuery>();
            double ground = SurfaceRadiusAt(direction, query);
            if (!(ground > 0.0) || double.IsNaN(ground) || double.IsInfinity(ground)) return -1.0;
            return math.max(length - ground, 0.0);
        }

        /// <summary>
        /// Prevents a high-speed Q/W/S/E step from tunnelling through the solid body. The previous
        /// visual-only radius clamp left the body-fixed truth underground, which then produced a
        /// near clip below Unity's ScreenPointToRay precision floor.
        /// </summary>
        private void ClampPlayerOutsideSurface()
        {
            double length = math.length(_playerBfMetres);
            double3 direction = length > 1e-6 && math.all(math.isfinite(_playerBfMetres))
                ? _playerBfMetres / length
                : new double3(0.0, 1.0, 0.0);
            double ground = SurfaceRadiusAt(direction, null);
            if (!(ground > 0.0) || double.IsNaN(ground) || double.IsInfinity(ground)) return;

            double safety = viewDistance != null
                ? System.Math.Max(viewDistance.minClearanceMetres, 0.01)
                : 0.01;
            double floor = ground + safety;
            if (length < floor)
            {
                _playerBfMetres = direction * floor;
                double inward = math.dot(_velocityBodyMetresPerSecond, direction);
                if (inward < 0.0)
                    _velocityBodyMetresPerSecond -= direction * inward;
            }
        }

        private double SurfaceRadiusAt(double3 direction, IPlanetSurfaceQuery knownQuery)
        {
            var sim = SolarSystemSimulation.Instance;
            CelestialBody body = sim != null ? sim.FocusView : null;
            IPlanetSurfaceQuery query = knownQuery ?? (body != null
                ? body.GetComponent<IPlanetSurfaceQuery>() : null);
            double ground = query != null
                ? query.SampleSurfaceRadiusMetres(direction, 1.0)
                : FallbackRadiusMetres();
            return ground > 0.0 && !double.IsNaN(ground) && !double.IsInfinity(ground)
                ? ground : FallbackRadiusMetres();
        }

        private double FallbackRadiusMetres()
        {
            var sim = SolarSystemSimulation.Instance;
            CelestialBody body = sim != null ? sim.FocusView : null;
            float kmPerUnit = KmPerUnit();
            if (body != null && body.sceneRadiusUnits > 0f)
                return SurfaceLocalSpace.SceneUnitsToMetres(body.sceneRadiusUnits, kmPerUnit);
            if (sim != null && sim.FocusDefinition != null)
                return sim.FocusDefinition.RadiusKm * 1000.0;
            return 6371000.0;
        }

        private float KmPerUnit()
        {
            var sim = SolarSystemSimulation.Instance;
            return sim != null ? sim.KmPerUnit : 1000000f;
        }

        /// <summary>Chunk-relative player offset in body-fixed metres (spin-invariant).</summary>
        public double3 ChunkRelativeMetres(double3 chunkOriginBodyFixedMetres)
        {
            return _playerBfMetres - chunkOriginBodyFixedMetres;
        }
    }
}
