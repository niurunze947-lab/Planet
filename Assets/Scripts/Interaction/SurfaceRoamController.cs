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
        [Tooltip("Seconds to reach the altitude-bound target speed (weak inertia).")]
        public float speedResponseSeconds = 0.35f;

        /// <summary>True while this controller owns the camera transform.</summary>
        public bool IsRoaming { get { return _roaming; } }

        public float Yaw { get { return _yaw; } }
        public float Pitch { get { return _pitch; } }

        /// <summary>Player-to-surface distance in metres (the GUI figure).</summary>
        public double PlayerToSurfaceDistanceMetres
        {
            get
            {
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
            _roaming = true;
            SurfaceRoamHudState.Publish(true, PlayerToSurfaceDistanceMetres);
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
            HandleFly(Time.deltaTime);
            SurfaceRoamHudState.Publish(true, PlayerToSurfaceDistanceMetres);

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                ReleaseRoam();
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
            SurfaceRoamInput.ApplyLook(ref _yaw, ref _pitch, look);
        }

        /// <summary>
        /// Camera-relative QWEASDQE with altitude-bound speed and weak inertia.
        /// Unity-editor gesture: the right mouse button must be held. Movement is integrated
        /// directly in body-fixed metres along the body-fixed look, so W stays "where the camera
        /// looks" relative to the ground while the planet turns — never a world-space direction
        /// that the spinning body would then shear against the landscape.
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

            // Body-fixed look: the same Euler the camera write uses.
            Quaternion lookBody = Quaternion.Euler(_pitch, _yaw, 0f);
            double speed = TargetSpeedMetres;

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
            _space.playerLocalMetres = BodyFixedToLocal(_playerBfMetres);
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
            orbitCamera.transform.rotation = BodyFixedLookToWorld(bodyRot, _pitch, _yaw);
            ApplyClipPlanes();
        }

        /// <summary>
        /// Body-fixed free-look Euler → world rotation. The body's live rotation is the outer
        /// factor so a spinning planet carries the look with it instead of shearing past the camera.
        /// </summary>
        public static Quaternion BodyFixedLookToWorld(Quaternion bodyWorldRotation, float pitch, float yaw)
        {
            return bodyWorldRotation * Quaternion.Euler(pitch, yaw, 0f);
        }

        private void ApplyClipPlanes()
        {
            var cam = orbitCamera != null ? orbitCamera.GetComponent<Camera>() : null;
            if (cam == null) return;

            var sim = SolarSystemSimulation.Instance;
            CelestialBody body = sim != null ? sim.FocusView : null;
            float radiusUnits = body != null ? body.RadiusUnits : 0f;
            float kmPerUnit = KmPerUnit();
            double altitudeMetres = System.Math.Max(PlayerToSurfaceDistanceMetres, 1.0);
            float nearM = SurfaceCameraClip.NearMetres(altitudeMetres);
            float farM = SurfaceCameraClip.FarMetres(altitudeMetres, (double)radiusUnits * (double)kmPerUnit * 1000.0);
            cam.nearClipPlane = (float)SurfaceLocalSpace.MetresToSceneUnits(nearM, kmPerUnit);
            cam.farClipPlane = Mathf.Max(
                (float)SurfaceLocalSpace.MetresToSceneUnits(farM, kmPerUnit),
                radiusUnits * 8f);
            if (cam.nearClipPlane < 1e-8f) cam.nearClipPlane = 1e-8f;
            if (cam.nearClipPlane >= cam.farClipPlane * 1e-3f)
                cam.nearClipPlane = cam.farClipPlane * 1e-4f;
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
            if (units > 0.0) orbitCamera.SnapToDistance((float)units);
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

            // Keep the exact look the system view had, but express it in the body frame so the
            // free-look angles stay ground-relative while the planet turns.
            Quaternion camRotWorld = orbitCamera != null ? orbitCamera.transform.rotation : Quaternion.identity;
            Vector3 e = (Quaternion.Inverse(bodyRot) * camRotWorld).eulerAngles;
            _pitch = e.x; if (_pitch > 180f) _pitch -= 360f;
            _yaw = e.y;
            _velocityBodyMetresPerSecond = double3.zero;
        }

        private double3 BodyFixedToLocal(double3 playerBfMetres)
        {
            if (!_space.IsValid) return new double3(0.0, _fallbackAltitudeMetres, 0.0);
            return _space.BodyFixedToLocal(playerBfMetres);
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
