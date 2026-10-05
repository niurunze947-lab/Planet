using System;

namespace Planet.Interaction
{
    /// <summary>
    /// C6 free-roam speed bound to height above ground.
    ///
    /// <para><b>One continuous curve, no hard bands.</b> Speed is linear in altitude between
    /// the ground cap (≤ 100 m/s) and the surface-mode ceiling speed. The ceiling speed is the
    /// near-surface marginal wheel-zoom altitude rate at the enter percentage — flying at the
    /// height limit of surface mode matches what log-zoom does at that same judgment band.</para>
    ///
    /// <para><b>Weak inertia.</b> Velocity approaches the target with a linear acceleration
    /// (MoveTowards), so speed changes are continuous rather than snapped.</para>
    /// </summary>
    public static class SurfaceRoamSpeed
    {
        /// <summary>Hard cap when fully on the ground.</summary>
        public const double GroundMaxMetresPerSecond = 100.0;

        /// <summary>Weak inertia: m/s² used to accelerate toward the altitude-bound target speed.</summary>
        public const double DefaultAccelerationMetresPerSecondSquared = 60.0;

        /// <summary>Input System scroll units per wheel notch.</summary>
        public const double ScrollUnitsPerNotch = 120.0;

        /// <summary>
        /// Near-surface marginal zoom altitude rate, in m/s for one wheel notch.
        /// From C4 log-altitude zoom: Δh ≈ h · nearRate · (zoomStep · scrollUnitsPerNotch).
        /// This is the "近地边际速度" at the enter percentage.
        /// </summary>
        public static double MarginalZoomSpeedMetresPerSecond(
            double altitudeMetres,
            double nearRate = ViewDistanceBoundary.DefaultNearRate,
            double zoomStep = 0.002)
        {
            if (!(altitudeMetres > 0.0)) return 0.0;
            double logPerNotch = Math.Abs(zoomStep) * ScrollUnitsPerNotch;
            return altitudeMetres * Math.Abs(nearRate) * logPerNotch;
        }

        /// <summary>
        /// Altitude at which surface mode starts, for a sphere of the given radius:
        /// alt / (R + alt) = enterRatio  ⇒  alt = enterRatio/(1−enterRatio) · R.
        /// </summary>
        public static double CeilingAltitudeMetres(
            double radiusMetres,
            double enterRatio = ViewDistanceBoundary.DefaultSurfaceModeEnterRatio)
        {
            double r = Math.Max(0.0, Math.Min(0.95, enterRatio));
            if (!(radiusMetres > 0.0) || r >= 0.95) return 0.0;
            return radiusMetres * (r / (1.0 - r));
        }

        /// <summary>
        /// Target fly speed at the given height above ground. Linear in altitude:
        /// ground cap at h=0, ceiling speed at h=ceilingAltitude. No piecewise bands.
        /// </summary>
        public static double TargetSpeedMetresPerSecond(
            double altitudeMetres,
            double ceilingAltitudeMetres,
            double ceilingSpeedMetresPerSecond)
        {
            double ground = Math.Min(GroundMaxMetresPerSecond,
                Math.Max(0.0, ceilingSpeedMetresPerSecond));
            if (!(ceilingAltitudeMetres > 1e-6))
                return ground;

            double t = altitudeMetres / ceilingAltitudeMetres;
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;

            double ceiling = Math.Max(ground, ceilingSpeedMetresPerSecond);
            return ground + (ceiling - ground) * t;
        }

        /// <summary>
        /// Convenience: ceiling speed derived from the zoom marginal rate at the enter altitude.
        /// </summary>
        public static double CeilingSpeedForRadius(
            double radiusMetres,
            double enterRatio = ViewDistanceBoundary.DefaultSurfaceModeEnterRatio,
            double nearRate = ViewDistanceBoundary.DefaultNearRate,
            double zoomStep = 0.002)
        {
            double h = CeilingAltitudeMetres(radiusMetres, enterRatio);
            return Math.Min(
                Math.Max(MarginalZoomSpeedMetresPerSecond(h, nearRate, zoomStep), GroundMaxMetresPerSecond),
                1e6);
        }

        /// <summary>
        /// Linear-acceleration step (weak inertia). Moves <paramref name="currentSpeed"/>
        /// toward <paramref name="targetSpeed"/> by at most accel·dt.
        /// </summary>
        public static double IntegrateSpeed(
            double currentSpeed,
            double targetSpeed,
            double accelerationMetresPerSecondSquared,
            double deltaTime)
        {
            if (!(deltaTime > 0.0)) return currentSpeed;
            double accel = Math.Max(1e-3, accelerationMetresPerSecondSquared);
            double maxDelta = accel * deltaTime;
            double delta = targetSpeed - currentSpeed;
            if (delta > maxDelta) return currentSpeed + maxDelta;
            if (delta < -maxDelta) return currentSpeed - maxDelta;
            return targetSpeed;
        }

        /// <summary>
        /// Accelerates a velocity vector toward <paramref name="targetVelocity"/> with the same
        /// linear accel cap. Direction can change continuously; magnitude never snaps.
        /// </summary>
        public static void IntegrateVelocity(
            ref double vx, ref double vy, ref double vz,
            double targetVx, double targetVy, double targetVz,
            double accelerationMetresPerSecondSquared,
            double deltaTime)
        {
            if (!(deltaTime > 0.0)) return;
            double accel = Math.Max(1e-3, accelerationMetresPerSecondSquared);
            double maxDelta = accel * deltaTime;

            double dx = targetVx - vx;
            double dy = targetVy - vy;
            double dz = targetVz - vz;
            double mag = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (mag <= maxDelta || mag < 1e-12)
            {
                vx = targetVx; vy = targetVy; vz = targetVz;
                return;
            }

            double s = maxDelta / mag;
            vx += dx * s; vy += dy * s; vz += dz * s;
        }
    }
}
