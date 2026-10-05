using UnityEngine;

namespace Planet.Interaction
{
    /// <summary>
    /// View-distance boundary math for the interaction layer.
    /// Distances are scene units along the camera-to-body-center axis (viewHeight).
    /// </summary>
    public static class ViewDistanceBoundary
    {
        public const float DefaultNearRate = 0.08f;
        public const float DefaultFarRate = 1f;

        /// <summary>
        /// Earth calibration: center height 6562 km, surface height ~100 km
        /// → enter when surfaceHeight / centerHeight ≤ 100/6562.
        /// </summary>
        public const float DefaultSurfaceModeEnterRatio = 100f / 6562f;

        /// <summary>Hysteresis: leave surface mode only after climbing well above the enter band.</summary>
        public const float DefaultSurfaceModeExitRatio = 0.030f;

        /// <summary>Non-negative distance from the camera to the solid surface.</summary>
        public static float SurfaceClearance(float viewHeight, float surfaceRadius)
        {
            return Mathf.Max(viewHeight - surfaceRadius, 0f);
        }

        /// <summary>
        /// Camera-to-surface distance as a fraction of camera-to-center view height.
        /// 0 on the surface, approaching 1 far away. Used to drive the zoom rate damper.
        /// </summary>
        public static float ClearanceOverViewHeight(float viewHeight, float surfaceRadius)
        {
            if (viewHeight <= 1e-12f) return 0f;
            return SurfaceClearance(viewHeight, surfaceRadius) / viewHeight;
        }

        /// <summary>
        /// Two-height percentage: surface-referenced altitude over center-referenced view height.
        /// Identical to <see cref="ClearanceOverViewHeight"/>; named for the surface-mode gate.
        /// </summary>
        public static float SurfaceModeRatio(float viewHeight, float surfaceRadius)
        {
            return ClearanceOverViewHeight(viewHeight, surfaceRadius);
        }

        /// <summary>
        /// Smooth log-zoom rate multiplier. Near the surface the change rate falls toward
        /// <paramref name="nearRate"/> (marginal effect); far away it recovers to
        /// <paramref name="farRate"/>. Smoothstep keeps the rate continuous at both ends.
        /// </summary>
        public static float ZoomRateMultiplier(
            float viewHeight,
            float surfaceRadius,
            float nearRate = DefaultNearRate,
            float farRate = DefaultFarRate)
        {
            float t = Mathf.Clamp01(ClearanceOverViewHeight(viewHeight, surfaceRadius));
            float s = t * t * (3f - 2f * t);
            return Mathf.Lerp(nearRate, farRate, s);
        }

        /// <summary>
        /// Clamps a center distance so the camera never enters the solid surface.
        /// Equality with the radius is allowed; the floor never drops below <paramref name="minDistance"/>.
        /// </summary>
        public static float ClampToSurface(
            float viewHeight,
            float surfaceRadius,
            float minDistance,
            float maxDistance)
        {
            float floor = Mathf.Max(surfaceRadius, minDistance);
            return Mathf.Clamp(viewHeight, floor, Mathf.Max(maxDistance, floor));
        }

        /// <summary>
        /// Applies a log-scale zoom delta with the near-surface rate damper, then clamps.
        /// Negative <paramref name="logDelta"/> zooms in (shorter distance).
        /// Zoom-out keeps full rate so the player can always back away from the surface.
        /// </summary>
        public static float ApplyLogZoom(
            float viewHeight,
            float logDelta,
            float surfaceRadius,
            float minDistance,
            float maxDistance,
            float nearRate = DefaultNearRate,
            float farRate = DefaultFarRate)
        {
            float rate = ZoomRateMultiplier(viewHeight, surfaceRadius, nearRate, farRate);
            float applied = logDelta < 0f ? logDelta * rate : logDelta;
            float next = viewHeight * Mathf.Exp(applied);
            return ClampToSurface(next, surfaceRadius, minDistance, maxDistance);
        }

        /// <summary>
        /// Surface-mode hysteresis. The gate fires only while approaching the solid surface
        /// from OUTSIDE the body (positive surface-relative distance), never from the interior
        /// or near the centre. Enter below the enter ratio; leave only above the exit ratio.
        /// Returning the current state inside the dead band (and while interior) prevents chatter.
        /// </summary>
        public static bool EvaluateSurfaceMode(
            float viewHeight,
            float surfaceRadius,
            bool currentlyInSurfaceMode,
            float enterRatio = DefaultSurfaceModeEnterRatio,
            float exitRatio = DefaultSurfaceModeExitRatio)
        {
            // Interior / centre: do not (re)enter. Keep an already-active mode.
            if (viewHeight < surfaceRadius)
                return currentlyInSurfaceMode;

            float ratio = SurfaceModeRatio(viewHeight, surfaceRadius);
            if (!currentlyInSurfaceMode) return ratio <= enterRatio;
            return ratio < exitRatio;
        }

        /// <summary>True when the camera is outside (or on) the solid surface, not in the interior.</summary>
        public static bool IsOutsideSurface(float viewHeight, float surfaceRadius)
        {
            return viewHeight >= surfaceRadius;
        }
    }
}
