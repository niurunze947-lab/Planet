using UnityEngine;
using UnityEngine.InputSystem;
using Planet.Solar;

namespace Planet.Interaction
{
    /// <summary>
    /// Owns mouse-wheel / key zoom and the view-distance boundary.
    /// Applies a near-surface rate damper (marginal effect) and never lets the target or
    /// current distance drop below the focused body's surface radius.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public class ViewDistanceController : MonoBehaviour
    {
        [Header("Zoom input")]
        [Tooltip("Log-scale zoom applied per mouse-wheel unit (before the rate damper).")]
        public float zoomStep = 0.002f;
        [Tooltip("Log-scale zoom per second while a zoom key is held.")]
        public float keyZoomRate = 2.2f;

        [Header("Near-surface rate")]
        [Tooltip("Zoom-in rate multiplier when sitting on the surface.")]
        [Range(0.01f, 1f)] public float nearRate = ViewDistanceBoundary.DefaultNearRate;
        [Tooltip("Zoom-in rate multiplier far from the surface.")]
        [Range(0.01f, 1f)] public float farRate = ViewDistanceBoundary.DefaultFarRate;

        [Header("C4 near-surface")]
        [Tooltip("Minimum surface clearance in metres (centimetre class).")]
        public float minClearanceMetres = 0.01f;
        [Tooltip("Safety margin above the terrain-aware floor, in metres.")]
        public float terrainSafetyMetres = 0.01f;
        [Tooltip("Use log-altitude zoom once clearanceOverViewHeight falls below this.")]
        [Range(0.001f, 1f)] public float altitudeZoomRatio = 0.10f;

        [Tooltip("Master switch for wheel / key zoom. Surface free-roam turns this off and " +
                 "rebinds the wheel to fly speed.")]
        public bool zoomEnabled = true;

        /// <summary>Optional full-frequency surface query (C2) for the terrain-aware floor and back-pressure.</summary>
        public Planet.Procedural.Terrain.IPlanetSurfaceQuery SurfaceQuery
        {
            get { return ResolveQuery(); }
            set { _surfaceQuery = value; }
        }

        private Planet.Procedural.Terrain.IPlanetSurfaceQuery _surfaceQuery;

        /// <summary>
        /// Camera-to-surface distance as a fraction of camera-to-center view height (0..1).
        /// </summary>
        public float SurfaceClearanceOverViewHeight
        {
            get
            {
                float view = _camera != null ? _camera.CurrentViewHeightUnits : 0f;
                float radius = _camera != null ? _camera.FocusSurfaceRadiusUnits : 0f;
                return ViewDistanceBoundary.ClearanceOverViewHeight(view, radius);
            }
        }

        /// <summary>Rate multiplier that will be applied to the next zoom-in step.</summary>
        public float CurrentZoomRateMultiplier
        {
            get
            {
                float view = _camera != null ? _camera.CurrentViewHeightUnits : 0f;
                float radius = _camera != null ? _camera.FocusSurfaceRadiusUnits : 0f;
                return ViewDistanceBoundary.ZoomRateMultiplier(view, radius, nearRate, farRate);
            }
        }

        private SolarSystemCamera _camera;
        private bool _restoredZoomInput;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var cam = Object.FindObjectOfType<SolarSystemCamera>();
            if (cam == null) return;

            if (Object.FindObjectOfType<ViewDistanceController>() == null)
                cam.gameObject.AddComponent<ViewDistanceController>();
            if (Object.FindObjectOfType<SurfaceModeController>() == null)
                cam.gameObject.AddComponent<SurfaceModeController>();
            if (Object.FindObjectOfType<BodyFixedCameraAnchor>() == null)
                cam.gameObject.AddComponent<BodyFixedCameraAnchor>();
            if (Object.FindObjectOfType<SurfaceRoamController>() == null)
                cam.gameObject.AddComponent<SurfaceRoamController>();
        }

        private void OnEnable()
        {
            _camera = GetComponent<SolarSystemCamera>();
            if (_camera == null) _camera = FindObjectOfType<SolarSystemCamera>();
            if (_camera != null)
            {
                _restoredZoomInput = _camera.handleZoomInput;
                _camera.handleZoomInput = false;
            }
        }

        private void OnDisable()
        {
            if (_camera != null && _restoredZoomInput)
                _camera.handleZoomInput = true;
        }

        private void Update()
        {
            if (_camera == null) return;
            if (!_camera.handleInput) return;

            // Surface free-roam suppresses view-height zoom entirely (C6).
            // Clamping is the orbit rig's job; free-roam owns its own floor.
            if (zoomEnabled && !_camera.freeRoam)
            {
                float logDelta = ReadZoomLogDelta();
                if (Mathf.Abs(logDelta) > 1e-8f)
                    ApplyZoomLogDelta(logDelta);

                // Surface radius can change with focus or the size-exaggeration toggle.
                ClampCameraDistances();
            }
        }

        private float ReadZoomLogDelta()
        {
            float delta = 0f;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    delta += scroll * zoomStep;
            }

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.pageUpKey.isPressed || kb.zKey.isPressed)
                    delta += -keyZoomRate * Time.deltaTime;
                if (kb.pageDownKey.isPressed || kb.xKey.isPressed)
                    delta += keyZoomRate * Time.deltaTime;
            }

            return delta;
        }

        /// <summary>
        /// Applies one zoom step. Near the surface this switches to log-altitude zoom (C4).
        /// When a C2 surface query is bound, altitude is integrated in <b>double metres</b>
        /// (PLANET-SPACE rule) so a centimetre floor is meaningful; otherwise the float
        /// scene-unit path is used (limited by ULP at true scale). PendingDetailWork throttles.
        /// </summary>
        public void ApplyZoomLogDelta(float logDelta)
        {
            if (_camera == null) return;

            var query = SurfaceQuery;

            float radius = _camera.FocusSurfaceRadiusUnits;
            float view = _camera.TargetDistance;
            float clearanceOverView = ViewDistanceBoundary.ClearanceOverViewHeight(view, radius);
            float kmPerUnit = KmPerUnit();

            if (query != null && (clearanceOverView <= altitudeZoomRatio || _camera.HasPreciseSurfaceDistance))
            {
                double altitude = _camera.HasPreciseSurfaceDistance ? _camera.TargetSurfaceClearanceMetres : _camera.CurrentSurfaceClearanceMetres();
                double nextAlt = ViewDistanceBoundary.ApplySurfaceClearanceZoomMetres(altitude, logDelta,
                    System.Math.Max(minClearanceMetres, terrainSafetyMetres), query.PendingDetailWork > 0);
                if (_camera.HasPreciseSurfaceDistance && nextAlt > query.RadiusMetres * 0.2)
                    _camera.SetDistance((float)((query.RadiusMetres + nextAlt) * radius / query.RadiusMetres));
                else _camera.SetSurfaceClearanceMetres(nextAlt);
                return;
            }

            float minAlt = ViewDistanceBoundary.MinClearanceUnits(kmPerUnit, minClearanceMetres);
            float nextScene = clearanceOverView <= altitudeZoomRatio
                ? ViewDistanceBoundary.ApplyLogAltitudeZoom(
                    view, radius, logDelta, minAlt, _camera.maxDistance, nearRate, farRate)
                : ViewDistanceBoundary.ApplyLogZoom(
                    view, logDelta, radius, _camera.minDistance, _camera.maxDistance, nearRate, farRate);

            _camera.SetDistance(Mathf.Max(nextScene, FloorUnits(radius)));
        }

        /// <summary>
        /// Forces both the eased distance and the target onto the legal range.
        /// Only writes the target when it is actually illegal, so focus re-framing is preserved.
        /// </summary>
        public void ClampCameraDistances()
        {
            if (_camera == null) return;
            if (_camera.HasPreciseSurfaceDistance) return;

            float radius = _camera.FocusSurfaceRadiusUnits;
            float floor = FloorUnits(radius);
            float clamped = Mathf.Clamp(_camera.TargetDistance, floor, _camera.maxDistance);
            if (clamped != _camera.TargetDistance)
                _camera.SetDistance(clamped);
            if (_camera.distance < floor)
                _camera.distance = floor;
            _camera.ClampCurrentDistanceToSurface();
        }

        /// <summary>C4 — terrain-aware floor (ground + safety), else sphere radius + cm margin.</summary>
        private float FloorUnits(float surfaceRadius)
        {
            float kmPerUnit = KmPerUnit();
            var query = SurfaceQuery;
            if (query == null)
            {
                return surfaceRadius + ViewDistanceBoundary.MinClearanceUnits(kmPerUnit, minClearanceMetres);
            }

            Vector3 pos = _camera.transform.position;
            Vector3 n = pos.sqrMagnitude > 1e-16f ? pos.normalized : Vector3.up;
            var dir = new Unity.Mathematics.double3(n.x, n.y, n.z);
            return ViewDistanceBoundary.TerrainAwareFloor(
                _camera.TargetDistance, surfaceRadius, dir, query, kmPerUnit, terrainSafetyMetres);
        }

        private Planet.Procedural.Terrain.IPlanetSurfaceQuery ResolveQuery()
        {
            var sim = SolarSystemSimulation.Instance;
            if (sim != null && sim.FocusView != null)
                _surfaceQuery = sim.FocusView.GetComponent<Planet.Procedural.Terrain.IPlanetSurfaceQuery>();
            return _surfaceQuery;
        }

        private float KmPerUnit()
        {
            var sim = SolarSystemSimulation.Instance;
            return sim != null ? sim.KmPerUnit : 1000000f;
        }
    }
}
