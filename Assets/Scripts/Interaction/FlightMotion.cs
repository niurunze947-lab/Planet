using System;
using Unity.Mathematics;

namespace Planet.Interaction
{
    public static class FlightMotion
    {
        public const double GravitationalConstant = 6.67430e-11;

        /// <summary>Point-mass acceleration in real metres and seconds, without a gameplay speed cap.</summary>
        public static double3 GravityAcceleration(double3 fromCenterMetres, double massKilograms)
        {
            double radiusSquared = math.lengthsq(fromCenterMetres);
            if (radiusSquared < 1 || !(massKilograms > 0)) return double3.zero;
            return -fromCenterMetres * (GravitationalConstant * massKilograms /
                (radiusSquared * Math.Sqrt(radiusSquared)));
        }
        /// <summary>Bound peak fractional growth of the planet on screen, independent of trip length.</summary>
        public static double FocusDuration(double startRange, double endRange, double maxLogRate = 4.0)
        {
            double span = Math.Abs(Math.Log(Math.Max(startRange, 0.01) / Math.Max(endRange, 0.01)));
            // The quintic smoothstep below has maximum derivative 1.875.
            return Math.Max(0.6, 1.875 * span / Math.Max(0.1, maxLogRate));
        }

        public static double FocusRange(double startRange, double endRange, double progress,
            double duration, out double radialVelocity)
        {
            startRange = Math.Max(startRange, 0.01);
            endRange = Math.Max(endRange, 0.01);
            double t = math.clamp(progress, 0, 1);
            double eased = t * t * t * (10 + t * (-15 + 6 * t));
            double span = Math.Log(endRange / startRange);
            double range = startRange * Math.Exp(span * eased);
            double derivative = 30 * t * t * (1 - t) * (1 - t);
            radialVelocity = range * span * derivative / Math.Max(duration, 0.01);
            return t >= 1 ? endRange : range;
        }

        public static double3 LimitMagnitude(double3 velocity, double speed)
        {
            double length = math.length(velocity);
            return length > speed && length > 0 ? velocity * (Math.Max(0, speed) / length) : velocity;
        }

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
