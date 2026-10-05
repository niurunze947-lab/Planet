using UnityEngine;
using Planet.Procedural.Terrain;
using Planet.Solar;

namespace Planet.Interaction
{
    /// <summary>
    /// C5 — body-fixed camera anchor. While active the orbit yaw is expressed in the focused
    /// body's rotating frame so the ground does not slide under the player near the surface.
    /// </summary>
    [DisallowMultipleComponent]
    public class BodyFixedCameraAnchor : MonoBehaviour
    {
        public SolarSystemCamera orbitCamera;
        [Tooltip("Engage when clearanceOverViewHeight falls to this value (matches the surface-mode band).")]
        public float engageRatio = ViewDistanceBoundary.DefaultSurfaceModeEnterRatio;

        public bool IsAnchored { get { return _anchored; } }

        private bool _anchored;
        private Quaternion _baseBodyRot = Quaternion.identity;
        private float _baseYaw;
        private float _basePitch;

        private void Awake()
        {
            if (orbitCamera == null) orbitCamera = GetComponent<SolarSystemCamera>();
            if (orbitCamera == null) orbitCamera = FindObjectOfType<SolarSystemCamera>();
        }

        private void LateUpdate()
        {
            if (orbitCamera == null) return;
            // Surface free-roam (C6) owns the camera; orbit-yaw anchoring would fight it.
            if (orbitCamera.freeRoam) return;

            var sim = SolarSystemSimulation.Instance;
            CelestialBody body = sim != null ? sim.FocusView : null;
            if (body == null)
            {
                if (_anchored) Release();
                return;
            }

            float view = orbitCamera.CurrentViewHeightUnits;
            float radius = orbitCamera.FocusSurfaceRadiusUnits;
            bool shouldAnchor = ViewDistanceBoundary.IsOutsideSurface(view, radius)
                && ViewDistanceBoundary.ClearanceOverViewHeight(view, radius) <= engageRatio;

            if (shouldAnchor && !_anchored) Capture(body);
            else if (!shouldAnchor && _anchored) Release();
            else if (_anchored) Apply(body);
        }

        private void Capture(CelestialBody body)
        {
            _baseBodyRot = body.transform.rotation;
            _baseYaw = orbitCamera.yaw;
            _basePitch = orbitCamera.pitch;
            _anchored = true;
        }

        /// <summary>
        /// Follows the body's spin: the body's world rotation delta since capture is applied to
        /// the orbit yaw so the camera stays fixed relative to the ground.
        /// </summary>
        private void Apply(CelestialBody body)
        {
            Quaternion delta = body.transform.rotation * Quaternion.Inverse(_baseBodyRot);
            Vector3 pole = body.PoleUnity.sqrMagnitude > 1e-8f ? body.PoleUnity.normalized : Vector3.up;
            float deltaYaw = SignedAngleAroundAxis(delta, pole);
            orbitCamera.yaw = _baseYaw + deltaYaw;
            orbitCamera.pitch = _basePitch;
        }

        private void Release()
        {
            _anchored = false;
        }

        private static float SignedAngleAroundAxis(Quaternion delta, Vector3 axis)
        {
            delta.ToAngleAxis(out float angle, out Vector3 deltaAxis);
            if (float.IsNaN(deltaAxis.x)) return 0f;
            if (Vector3.Dot(deltaAxis, axis) < 0f) angle = -angle;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }
    }
}
