using UnityEngine;
using Planet.Procedural.Terrain;

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
        /// Earth calibration (original): center height 6562 km, surface height ~100 km
        /// → enter when surfaceHeight / centerHeight ≤ 100/6562.
        /// Widened for playability: 5% of centre distance (Earth ≈ 335 km altitude) so the
        /// gate is reachable and the free-roam band has usable headroom.
        /// </summary>
        public const float DefaultSurfaceModeEnterRatio = 0.05f;

        /// <summary>Hysteresis: leave surface mode only after climbing well above the enter band.</summary>
        public const float DefaultSurfaceModeExitRatio = 0.08f;

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
        /// <paramref name="farRate"/>.
        ///
        /// <para><b>The damper only applies inside the near-surface band.</b> Above
        /// <c>DefaultSurfaceModeEnterRatio</c> the rate stays at <paramref name="farRate"/>.
        /// Damping the whole approach made the enter gate unreachable: at nearRate=0.08 the
        /// last 10% → 1.524% of centre height needed on the order of a hundred wheel notches,
        /// so surface mode never fired no matter how hard the player zoomed.</para>
        /// </summary>
        public static float ZoomRateMultiplier(
            float viewHeight,
            float surfaceRadius,
            float nearRate = DefaultNearRate,
            float farRate = DefaultFarRate)
        {
            float t = Mathf.Clamp01(ClearanceOverViewHeight(viewHeight, surfaceRadius));
            float band = DefaultSurfaceModeEnterRatio;
            if (t >= band) return farRate;
            float s = band > 1e-6f ? t / band : 0f;
            s = s * s * (3f - 2f * s);
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
        /// C4 — log-altitude zoom in scene units: <c>h' = h · exp(δ · rate)</c>.
        /// Prefer <see cref="ApplyLogAltitudeZoomMetres"/> near the surface: at true Earth scale
        /// the float centre-distance has ~0.47 m ULP (PLANET-SPACE.md §1) and cannot resolve cm.
        /// This path is exact while altitude is representable in float.
        /// </summary>
        public static float ApplyLogAltitudeZoom(
            float viewHeight,
            float surfaceRadius,
            float logDelta,
            float minAltitude,
            float maxViewHeight,
            float nearRate = DefaultNearRate,
            float farRate = DefaultFarRate)
        {
            float radius = Mathf.Max(surfaceRadius, 0f);
            float floorAlt = Mathf.Max(minAltitude, 0f);
            float altitude = Mathf.Max(viewHeight - radius, floorAlt);
            float rate = ZoomRateMultiplier(viewHeight, radius, nearRate, farRate);
            // Outward steps stay full rate; inward steps use the damper, then the log-altitude map.
            float applied = logDelta < 0f ? logDelta * rate : logDelta;
            float nextAlt = Mathf.Max(altitude * Mathf.Exp(applied), floorAlt);
            float next = radius + nextAlt;
            // Guard against float cancellation pinning next on radius when nextAlt << radius.
            if (next <= radius && nextAlt > 0f) next = radius + floorAlt;
            return Mathf.Min(next, Mathf.Max(maxViewHeight, radius + floorAlt));
        }

        /// <summary>
        /// C4 — log-altitude zoom in <b>double metres</b> (PLANET-SPACE rule: subtract and
        /// integrate in the owning frame, drop to float only at the end). This is the path that
        /// can honour a centimetre clearance floor.
        /// </summary>
        public static double ApplyLogAltitudeZoomMetres(
            double altitudeMetres,
            double logDelta,
            double minAltitudeMetres,
            double clearanceOverViewHeight,
            double nearRate = DefaultNearRate,
            double farRate = DefaultFarRate)
        {
            double floor = System.Math.Max(minAltitudeMetres, 0.0);
            double altitude = System.Math.Max(altitudeMetres, floor);
            double t = System.Math.Max(0.0, System.Math.Min(1.0, clearanceOverViewHeight));
            double s = t * t * (3.0 - 2.0 * t);
            double rate = nearRate + (farRate - nearRate) * s;
            double applied = logDelta < 0.0 ? logDelta * rate : logDelta;
            return System.Math.Max(altitude * System.Math.Exp(applied), floor);
        }

        /// <summary>Convert metres to scene units for the given km-per-unit scale.</summary>
        public static float MetresToSceneUnits(float metres, float kmPerUnit)
        {
            return metres / (Mathf.Max(kmPerUnit, 1e-3f) * 1000f);
        }

        /// <summary>C4 — centimetre-level minimum clearance floor in scene units.</summary>
        public static float MinClearanceUnits(float kmPerUnit, float minClearanceMetres = 0.01f)
        {
            return MetresToSceneUnits(minClearanceMetres, kmPerUnit);
        }

        /// <summary>
        /// C4 — terrain-aware floor in metres: ground along the view direction plus a safety
        /// margin. Falls back to mean radius + min clearance when no query is bound.
        /// </summary>
        public static double TerrainAwareFloorMetres(
            double meanRadiusMetres,
            Unity.Mathematics.double3 directionBf,
            IPlanetSurfaceQuery query,
            double safetyMarginMetres = 2.0,
            double minClearanceMetres = 0.01)
        {
            double sphereFloor = meanRadiusMetres + System.Math.Max(minClearanceMetres, 0.0);
            if (query == null) return sphereFloor;
            double ground = query.SampleSurfaceRadiusMetres(directionBf, 1.0);
            double floor = ground + System.Math.Max(safetyMarginMetres, minClearanceMetres);
            return System.Math.Max(floor, sphereFloor);
        }

        /// <summary>
        /// C4 — terrain-aware distance floor: ground radius along the current view direction
        /// plus a safety margin. Falls back to the sphere radius when no query is bound.
        /// </summary>
        public static float TerrainAwareFloor(
            float viewHeight,
            float surfaceRadius,
            Unity.Mathematics.double3 directionBf,
            IPlanetSurfaceQuery query,
            float kmPerUnit,
            float safetyMarginMetres = 2f)
        {
            float sphereFloor = surfaceRadius + MinClearanceUnits(kmPerUnit);
            if (query == null) return sphereFloor;

            double ground = query.SampleSurfaceRadiusMetres(directionBf, sampleSpacingMetres: 1.0);
            float groundUnits = (float)(ground / (double)Mathf.Max(kmPerUnit, 1e-3f) / 1000.0);
            float floor = groundUnits + MetresToSceneUnits(Mathf.Max(safetyMarginMetres, 0.01f), kmPerUnit);
            // Never below the solid sphere + cm margin; never force the camera underground.
            return Mathf.Max(floor, sphereFloor, Mathf.Min(viewHeight, sphereFloor));
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
