using UnityEngine;

namespace Planet.Procedural.Terrain
{
    /// <summary>
    /// Canonical two-height metrics and surface-mode ratio definitions (C-ratio).
    /// All heights are centre-referenced scene units unless noted.
    /// </summary>
    public static class ViewHeightRatios
    {
        /// <summary>Positive distance from the camera to the solid surface.</summary>
        public static float Clearance(float viewHeight, float surfaceRadius)
        {
            return Mathf.Max(viewHeight - surfaceRadius, 0f);
        }

        /// <summary>
        /// Surface-relative height as a fraction of centre distance (0 on surface, →1 far).
        /// <b>Surface-mode gate</b> uses this (Earth: enter 100/6562 ≈ 1.524%, exit 3%).
        /// </summary>
        public static float ClearanceOverViewHeight(float viewHeight, float surfaceRadius)
        {
            if (viewHeight <= 1e-12f) return 0f;
            return Clearance(viewHeight, surfaceRadius) / viewHeight;
        }

        /// <summary>
        /// Surface-relative height as a fraction of body radius (0 on surface, 1 at 2R).
        /// <b>Near-surface debug / resource-preload hysteresis</b> uses this
        /// (DEVELOPMENT-WORKFLOW: enter ≤ 0.25, leave ≥ 0.35).
        /// </summary>
        public static float ClearanceOverRadius(float viewHeight, float surfaceRadius)
        {
            if (surfaceRadius <= 1e-12f) return 0f;
            return Clearance(viewHeight, surfaceRadius) / surfaceRadius;
        }

        /// <summary>
        /// True only while approaching from outside the body (positive surface distance).
        /// Interior / near-centre snapshots never open the gate.
        /// </summary>
        public static bool IsOutsideSurface(float viewHeight, float surfaceRadius)
        {
            return viewHeight >= surfaceRadius;
        }
    }
}
