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

            float logDelta = ReadZoomLogDelta();
            if (Mathf.Abs(logDelta) > 1e-8f)
                ApplyZoomLogDelta(logDelta);

            // Surface radius can change with focus or the size-exaggeration toggle.
            ClampCameraDistances();
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
        /// Applies one log-scale zoom step with the near-surface damper, then publishes the
        /// clamped target to the camera. Order: read radius → input → clamp target → smooth.
        /// </summary>
        public void ApplyZoomLogDelta(float logDelta)
        {
            if (_camera == null) return;

            float radius = _camera.FocusSurfaceRadiusUnits;
            float next = ViewDistanceBoundary.ApplyLogZoom(
                _camera.TargetDistance,
                logDelta,
                radius,
                _camera.minDistance,
                _camera.maxDistance,
                nearRate,
                farRate);
            _camera.SetDistance(next);
        }

        /// <summary>
        /// Forces both the eased distance and the target onto the legal range.
        /// Only writes the target when it is actually illegal, so focus re-framing is preserved.
        /// </summary>
        public void ClampCameraDistances()
        {
            if (_camera == null) return;

            float radius = _camera.FocusSurfaceRadiusUnits;
            float clamped = ViewDistanceBoundary.ClampToSurface(
                _camera.TargetDistance, radius, _camera.minDistance, _camera.maxDistance);
            if (clamped != _camera.TargetDistance)
                _camera.SetDistance(clamped);
            _camera.ClampCurrentDistanceToSurface();
        }
    }
}
