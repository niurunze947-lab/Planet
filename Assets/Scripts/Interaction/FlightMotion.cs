using System;
using Unity.Mathematics;

namespace Planet.Interaction
{
    public static class FlightMotion
    {
        public static double3 Accelerate(double3 velocity, double3 target, double acceleration, double dt)
        {
            double3 delta = target - velocity;
            double length = math.length(delta);
            return length > 0 ? velocity + delta * Math.Min(1.0, Math.Max(0, acceleration * dt) / length) : velocity;
        }

        /// <summary>First entering contact along a complete segment; also catches a transit through a body.</summary>
        public static bool SphereEntry(double3 from, double3 to, double radius, out double fraction)
        {
            fraction = 1;
            double3 delta = to - from;
            double a = math.dot(delta, delta);
            double c = math.dot(from, from) - radius * radius;
            if (a < 1e-20 || radius <= 0) return false;
            double b = math.dot(from, delta);
            if (c <= 0)
            {
                if (b < 0) { fraction = 0; return true; }
                return false;
            }
            double discriminant = b * b - a * c;
            if (discriminant < 0 || b >= 0) return false;
            fraction = (-b - Math.Sqrt(discriminant)) / a;
            return fraction >= 0 && fraction <= 1;
        }

        public static double SafeApproachSpeed(double clearance, double brakingAcceleration)
        {
            return Math.Sqrt(2 * Math.Max(0, brakingAcceleration) * Math.Max(0, clearance));
        }

        public static bool NearSurface(double actualDistance, double radius, bool active,
            double enterRatio = 0.05, double exitRatio = 0.08)
        {
            if (!(radius > 0) || !math.isfinite(actualDistance)) return false;
            // Relief can lie below the mean reference radius; already-active terrain stays active.
            if (actualDistance < radius) return active;
            double ratio = (actualDistance - radius) / actualDistance;
            return active ? ratio < exitRatio : ratio <= enterRatio;
        }
    }
}
