using System;
using UnityEngine;
using Planet.Solar;

namespace Planet.Interaction
{
    /// <summary>
    /// Near-surface mode detector. Publishes the two-height ratio gate with hysteresis
    /// and a unified [SurfaceMode] debug log. Does not take over camera control.
    ///
    /// The 1.524% band is the player's POSITIVE distance to the surface as a fraction of the
    /// centre distance (Earth: 100 km / 6562 km). It fires while approaching from outside
    /// the body — never from the interior or near the centre.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-60)]
    public class SurfaceModeController : MonoBehaviour
    {
        [Header("Gate (surfaceHeight / centerHeight, outside only)")]
        [Tooltip("Enter near-surface mode when the positive surface height falls to this fraction of the centre distance (Earth: 100 km / 6562 km ≈ 1.524%).")]
        public float enterRatio = ViewDistanceBoundary.DefaultSurfaceModeEnterRatio;
        [Tooltip("Leave near-surface mode only after the ratio climbs above this value.")]
        public float exitRatio = ViewDistanceBoundary.DefaultSurfaceModeExitRatio;

        [Header("Debug")]
        [Tooltip("Log enter/exit and near-surface gate state to the console.")]
        public bool debugLog = true;

        [Header("Wiring")]
        public SolarSystemCamera orbitCamera;

        /// <summary>True while the near-surface band is active.</summary>
        public bool InSurfaceMode { get { return _inSurfaceMode; } }

        /// <summary>Two-height percentage: altitude / center distance (0..1).</summary>
        public float SurfaceModeRatio
        {
            get
            {
                float view = orbitCamera != null ? orbitCamera.CurrentViewHeightUnits : 0f;
                float radius = orbitCamera != null ? orbitCamera.FocusSurfaceRadiusUnits : 0f;
                return ViewDistanceBoundary.SurfaceModeRatio(view, radius);
            }
        }

        /// <summary>Raised once when the near-surface band is entered.</summary>
        public event Action EnteredSurfaceMode;
        /// <summary>Raised once when the near-surface band is left.</summary>
        public event Action ExitedSurfaceMode;

        private bool _inSurfaceMode;
        private float _nextApproachLogTime;

        private void Awake()
        {
            if (orbitCamera == null) orbitCamera = GetComponent<SolarSystemCamera>();
            if (orbitCamera == null) orbitCamera = FindObjectOfType<SolarSystemCamera>();
        }

        private void OnEnable()
        {
            _inSurfaceMode = false;
            if (debugLog)
            {
                Debug.Log(string.Format(
                    "[SurfaceMode] ready enterRatio={0:F5} ({1:P3}) exitRatio={2:F5} camera={3}",
                    enterRatio, enterRatio, exitRatio,
                    orbitCamera != null ? orbitCamera.name : "null"));
            }
        }

        private void Update()
        {
            if (orbitCamera == null) return;
            if (orbitCamera.externalObserver) return;

            float radius = orbitCamera.FocusSurfaceRadiusUnits;
            float current = orbitCamera.CurrentViewHeightUnits;
            float target = orbitCamera.TargetDistance;
            // Closer of the two so a decisive zoom-in still crosses the band
            // even before the eased distance catches up.
            float gateView = Mathf.Min(current, target);

            // Unknown / broken scale: never open the gate, and say so once. A zero radius would
            // make every distance look "far"; a huge fallback would make every distance look
            // "inside". Both silently disable surface mode.
            if (radius <= 0f)
            {
                WarnInvalidRadiusOnce(radius, current);
                return;
            }

            bool outside = ViewDistanceBoundary.IsOutsideSurface(gateView, radius);
            bool next = ViewDistanceBoundary.EvaluateSurfaceMode(
                gateView, radius, _inSurfaceMode, enterRatio, exitRatio);

            if (debugLog)
                LogApproachProgress(current, target, radius, outside, next);

            if (next != _inSurfaceMode)
                ApplyMode(next, gateView);
        }

        private bool _warnedInvalidRadius;

        private void WarnInvalidRadiusOnce(float radius, float view)
        {
            if (_warnedInvalidRadius) return;
            _warnedInvalidRadius = true;
            Debug.LogWarning(string.Format(
                "[SurfaceMode] focused body radius is {0} scene units (view={1}); " +
                "near-surface mode stays off until the body scale is valid " +
                "(Tools > Planet > Bake Body Scales).",
                radius, view), this);
        }

        private void LogApproachProgress(float current, float target, float radius, bool outside, bool next)
        {
            if (_inSurfaceMode) return;

            float gateView = Mathf.Min(current, target);
            float ratio = ViewDistanceBoundary.SurfaceModeRatio(gateView, radius);
            if (ratio > 0.10f) return;
            if (Time.unscaledTime < _nextApproachLogTime) return;
            _nextApproachLogTime = Time.unscaledTime + 0.5f;

            Debug.Log(string.Format(
                "[SurfaceMode] approach view={0:F6} target={1:F6} radius={2:F6} alt={3:F6} ({4:F2} km) " +
                "ratio={5:P3} enterAt={6:P3} outside={7} wouldEnter={8}",
                current, target, radius, gateView - radius, (gateView - radius) * 1e6f,
                ratio, enterRatio, outside, next));
        }

        private void ApplyMode(bool inSurface, float gateView)
        {
            float radius = orbitCamera.FocusSurfaceRadiusUnits;
            float ratio = ViewDistanceBoundary.SurfaceModeRatio(gateView, radius);
            float altitude = Mathf.Max(gateView - radius, 0f);

            _inSurfaceMode = inSurface;

            if (inSurface)
            {
                if (debugLog)
                {
                    Debug.Log(string.Format(
                        "[SurfaceMode] ENTER gateView={0:F6} radius={1:F6} " +
                        "alt={2:F6} ({3:F2} km) ratio={4:P3} (threshold {5:P3}, positive surface distance)",
                        gateView, radius, altitude, altitude * 1e6f, ratio, enterRatio));
                }
                if (EnteredSurfaceMode != null) EnteredSurfaceMode();
            }
            else
            {
                if (debugLog)
                {
                    Debug.Log(string.Format(
                        "[SurfaceMode] EXIT gateView={0:F6} radius={1:F6} alt={2:F6} ({3:F2} km) ratio={4:P3} (exitAt={5:P3})",
                        gateView, radius, altitude, altitude * 1e6f, ratio, exitRatio));
                }
                if (ExitedSurfaceMode != null) ExitedSurfaceMode();
            }
        }

        /// <summary>
        /// Altitude at the enter threshold, in scene units:
        /// surfaceHeight / centerHeight = enterRatio
        /// ⇒ altitude = enterRatio / (1 - enterRatio) * radius.
        /// Positive distance above the solid surface.
        /// </summary>
        public float ComputeCeilingAltitude(float surfaceRadius)
        {
            float r = Mathf.Clamp01(enterRatio);
            if (r >= 0.999f) return surfaceRadius;
            return surfaceRadius * (r / (1f - r));
        }

        [ContextMenu("Debug Surface Mode State")]
        public void DebugSurfaceModeState()
        {
            float radius = orbitCamera != null ? orbitCamera.FocusSurfaceRadiusUnits : 0f;
            float view = orbitCamera != null ? orbitCamera.CurrentViewHeightUnits : 0f;
            float target = orbitCamera != null ? orbitCamera.TargetDistance : 0f;
            float gateView = Mathf.Min(view, target);
            float ratio = ViewDistanceBoundary.SurfaceModeRatio(gateView, radius);
            Debug.Log(string.Format(
                "[SurfaceMode] state inSurface={0} view={1:F6} target={2:F6} radius={3:F6} " +
                "alt={4:F6} ({5:F2} km) ratio={6:P3} outside={7} enter={8:P3} exit={9:P3}",
                _inSurfaceMode, view, target, radius, gateView - radius, (gateView - radius) * 1e6f,
                ratio, ViewDistanceBoundary.IsOutsideSurface(gateView, radius), enterRatio, exitRatio));
        }
    }
}
