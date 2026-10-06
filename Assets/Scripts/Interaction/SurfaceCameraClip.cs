using System;
using Planet.Solar;

namespace Planet.Interaction
{
    /// <summary>
    /// Clip planes for surface free-roam at true solar-system scale.
    ///
    /// <para>Scene units are millions of kilometres (kmPerUnit = 1e6 ⇒ 1 unit = 1e9 m), so a
    /// hard-coded near plane in "normal game" units swallows the whole planet. These helpers
    /// derive near/far from metres and from the current altitude above ground.</para>
    /// </summary>
    public static class SurfaceCameraClip
    {
        /// <summary>Near plane: a thin slice of the current altitude, with a metre-class floor.</summary>
        public const double MinNearMetres = 0.05;

        /// <summary>Near plane is this fraction of the height above ground.</summary>
        public const double NearAltitudeFraction = 0.01;

        /// <summary>Far plane is this fraction of the body radius (so the globe always fits).</summary>
        public const double FarRadiusFraction = 20.0;

        /// <summary>Far plane is at least this many times the altitude (horizon + sky).</summary>
        public const double FarAltitudeFraction = 8.0;

        /// <summary>
        /// Hard cap on far/near. A 4e10:1 frustum (near 1e-6, far 40000 — the old orbit
        /// close-up) makes the projection matrix numerically singular; IMGUI then throws
        /// "Screen position out of view frustum" from SendMouseEvents and the play session
        /// dies. 1e5:1 is about the practical float limit for a usable depth buffer.
        /// </summary>
        public const float MaxNearFarRatio = SolarSystemCamera.MaxProjectionRatio;

        /// <summary>
        /// Smallest scene-unit near plane accepted by the runtime projection path (0.1 m at the
        /// solar-system scale). This keeps the projection depth away from zero without clipping
        /// the close ground view.
        /// </summary>
        public const float MinNearSceneUnits = SolarSystemCamera.MinProjectionNear;

        /// <summary>Near clip in metres. Never large enough to eat the planet at any altitude.</summary>
        public static float NearMetres(double altitudeMetres)
        {
            if (!(altitudeMetres > 0.0)) return (float)MinNearMetres;
            double near = altitudeMetres * NearAltitudeFraction;
            return (float)System.Math.Max(near, MinNearMetres);
        }

        /// <summary>
        /// Far clip in metres: enough to see the globe and the sky from the current height.
        /// <paramref name="bodyRadiusMetres"/> keeps the full disc visible when high up.
        /// </summary>
        public static float FarMetres(double altitudeMetres, double bodyRadiusMetres)
        {
            double alt = Math.Max(altitudeMetres, 0.0);
            double radius = Math.Max(bodyRadiusMetres, 1.0);
            double far = Math.Max(radius * FarRadiusFraction, alt * FarAltitudeFraction + radius * 2.0);
            return (float)far;
        }

        /// <summary>
        /// Sanitises a near/far pair in the same units. Guarantees near &gt; 0, far &gt; near,
        /// and far/near ≤ <see cref="MaxNearFarRatio"/> (far is shortened first — a clipped
        /// distant body is recoverable, a broken frustum is not).
        /// </summary>
        public static void SanitizeClipPlanes(ref float near, ref float far)
        {
            SolarSystemCamera.SanitizeProjectionPlanes(ref near, ref far);
        }
    }
}
