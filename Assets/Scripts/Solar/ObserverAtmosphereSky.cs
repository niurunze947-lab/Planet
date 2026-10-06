using System;
using Planet.Procedural;
using UnityEngine;

namespace Planet.Solar
{
    /// <summary>Observer-local air column for the starfield compositor, independent of navigation focus.</summary>
    public static class ObserverAtmosphereSky
    {
        private static readonly int ParametersId = Shader.PropertyToID("_PlanetAtmosphereParams");
        private static readonly int UpId = Shader.PropertyToID("_PlanetAtmosphereUp");
        private static readonly int SunId = Shader.PropertyToID("_PlanetAtmosphereSun");
        private static readonly int ColorId = Shader.PropertyToID("_PlanetAtmosphereColor");

        public static double AirColumn(double altitudeMetres, double atmosphereHeightMetres)
        {
            if (!(atmosphereHeightMetres > 0) || altitudeMetres >= atmosphereHeightMetres) return 0;
            double fraction = Math.Max(0, altitudeMetres) / atmosphereHeightMetres;
            double edge = Math.Min(1, (1 - fraction) / 0.2);
            return Math.Exp(-fraction / 0.18) * edge * edge * (3 - 2 * edge);
        }

        public static float Daylight(float sunElevationSine)
        {
            return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-0.12f, 0.18f, sunElevationSine));
        }

        public static void Clear() { Shader.SetGlobalVector(ParametersId, Vector4.zero); }

        public static void Sync(SolarSystemSimulation sim)
        {
            Clear();
            if (sim == null || !sim.HasObserver) return;
            int sunIndex = -1;
            for (int i = 0; i < sim.BodyCount; i++)
                if (sim.DefinitionAt(i).IsSun) { sunIndex = i; break; }
            if (sunIndex < 0) return;

            double bestColumn = 0;
            for (int i = 0; i < sim.BodyCount; i++)
            {
                var surface = sim.ViewAt(i).GetComponent<PlanetSurfaceView>();
                PlanetDefinition definition = surface != null ? surface.definition : null;
                if (definition == null || !definition.features.hasAtmosphere) continue;
                var physical = definition.physical;
                Vec3d local = sim.ObserverRelativeToBody(i);
                double distance = local.Length;
                if (distance < 1 || distance > physical.radiusMetres * 2 + physical.atmosphereHeightMetres) continue;
                Vec3d direction = local / distance;
                double ax = physical.shapeScale.x;
                double ay = physical.shapeScale.y * (1.0 - physical.flattening);
                double az = physical.shapeScale.z;
                double reference = physical.radiusMetres / Math.Sqrt(direction.x * direction.x / (ax * ax) +
                    direction.y * direction.y / (ay * ay) + direction.z * direction.z / (az * az));
                // Altitude above the reference sea surface, not above the current mountain summit.
                double altitude = distance - reference - physical.seaLevelMetres;
                double column = AirColumn(altitude, physical.atmosphereHeightMetres) *
                    Math.Max(0, definition.appearance.atmosphereDensity) / 0.22;
                if (column <= bestColumn) continue;
                bestColumn = column;
                Vec3d upD = SolarSystemSimulation.RotateMetres(sim.ViewAt(i).transform.rotation, direction);
                Vector3 up = new Vector3((float)upD.x, (float)upD.y, (float)upD.z).normalized;
                Vec3d sunD = sim.PositionWorldMetresAt(sunIndex) - sim.ObserverWorldMetres;
                sunD /= Math.Max(1, sunD.Length);
                Vector3 sun = new Vector3((float)sunD.x, (float)sunD.y, (float)sunD.z).normalized;
                float elevation = Vector3.Dot(up, sun);
                float twilight = Mathf.Exp(-Mathf.Pow((elevation + 0.025f) / 0.09f, 2));
                Shader.SetGlobalVector(ParametersId, new Vector4((float)column, Daylight(elevation), twilight, 1));
                Shader.SetGlobalVector(UpId, up);
                Shader.SetGlobalVector(SunId, sun);
                Shader.SetGlobalColor(ColorId, definition.appearance.atmosphereColor.linear);
            }
        }
    }
}
