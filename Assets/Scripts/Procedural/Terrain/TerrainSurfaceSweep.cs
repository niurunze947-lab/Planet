using System;
using Unity.Mathematics;

namespace Planet.Procedural.Terrain
{
    /// <summary>Height-field fallback while mesh colliders are cooking. Missing data is not a wall.</summary>
    public static class TerrainSurfaceSweep
    {
        public static bool Sweep(double3 from, double3 to, double radius, double envelope,
            Func<double3, double> surfaceRadius, out double3 resolved, out double3 normal)
        {
            resolved = to; normal = double3.zero;
            double3 delta = to - from;
            double length = math.length(delta);
            if (length < 1e-7) return false;
            double closest = math.clamp(-math.dot(from, delta) / math.lengthsq(delta), 0, 1);
            if (math.length(from + delta * closest) > envelope + radius) return false;

            // Always test the closest approach, even for a transit whose endpoints are outside.
            // Each half samples the whole corridor; fine collider triangles remain the contact authority.
            double previous = 0;
            double startGap = Gap(from, radius, surfaceRadius);
            for (int half = 0; half < 2; half++)
            {
                double end = half == 0 ? closest : 1;
                double begin = previous;
                int steps = (int)math.clamp(math.ceil((end - begin) * length / math.max(0.5, radius)), 1, 128);
                for (int i = 1; i <= steps; i++)
                {
                    double t = math.lerp(begin, end, (double)i / steps);
                    double gap = Gap(from + delta * t, radius, surfaceRadius);
                    if (gap <= 0 && (startGap > 0 || gap < startGap - 1e-5))
                    {
                        double lo = previous, hi = t;
                        for (int k = 0; k < 24; k++)
                        {
                            double mid = (lo + hi) * 0.5;
                            if (Gap(from + delta * mid, radius, surfaceRadius) > 0) lo = mid;
                            else hi = mid;
                        }
                        resolved = from + delta * math.max(0, lo - 0.02 / length);
                        normal = SurfaceNormal(resolved, surfaceRadius);
                        return true;
                    }
                    previous = t;
                    startGap = gap;
                }
            }
            return false;
        }

        private static double Gap(double3 point, double radius, Func<double3, double> surface)
        {
            double range = math.length(point);
            return range - surface(math.normalizesafe(point, new double3(0, 1, 0))) - radius;
        }

        public static double3 SurfaceNormal(double3 point, Func<double3, double> surface)
        {
            double3 up = math.normalizesafe(point, new double3(0, 1, 0));
            double3 east = math.normalizesafe(math.cross(up, math.abs(up.y) < 0.9 ? new double3(0, 1, 0) : new double3(1, 0, 0)));
            double3 north = math.cross(up, east);
            double range = math.max(1, math.length(point));
            const double step = 0.5;
            double slopeE = (surface(math.normalize(up + east * (step / range))) -
                surface(math.normalize(up - east * (step / range)))) / (2 * step);
            double slopeN = (surface(math.normalize(up + north * (step / range))) -
                surface(math.normalize(up - north * (step / range)))) / (2 * step);
            return math.normalizesafe(up - east * slopeE - north * slopeN, up);
        }
    }
}
