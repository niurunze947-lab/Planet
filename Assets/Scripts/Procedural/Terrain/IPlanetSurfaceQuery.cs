using Unity.Mathematics;

namespace Planet.Procedural.Terrain
{
    /// <summary>
    /// C2 — full-frequency surface query plus streaming back-pressure.
    /// Exposed by <c>CelestialBody</c> (Solar) and implemented by the terrain system
    /// (planet-definition). Interaction and camera bounds use this instead of reading meshes.
    /// </summary>
    public interface IPlanetSurfaceQuery
    {
        /// <summary>Mean sphere radius in metres.</summary>
        double RadiusMetres { get; }

        /// <summary>
        /// Height above mean radius along a body-fixed unit direction, in metres.
        /// Negative over oceans. Full frequency when detail is ready; lower octaves only while
        /// streaming (deterministic band-limit by requested spacing).
        /// </summary>
        double SampleHeightMetres(double3 directionBf, double sampleSpacingMetres);

        /// <summary>Surface radius along a direction: <c>RadiusMetres + SampleHeightMetres</c>.</summary>
        double SampleSurfaceRadiusMetres(double3 directionBf, double sampleSpacingMetres);

        /// <summary>
        /// Number of queued detail jobs. Callers that drive camera motion should throttle while
        /// this is above zero so the streamer can catch up (C4 back-pressure).
        /// </summary>
        int PendingDetailWork { get; }
    }
}
