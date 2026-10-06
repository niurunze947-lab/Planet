using Unity.Mathematics;

namespace Planet.Procedural.Terrain
{
    /// <summary>Displayed terrain and local metre-scale collision, independent of physical height data.</summary>
    public interface IPlanetCollisionSurfaceQuery
    {
        double SampleCollisionRadiusMetres(double3 directionBf);
        bool CollisionCoverageReady { get; }
        bool SweepSphere(double3 fromBfMetres, double3 toBfMetres, double radiusMetres,
            out double3 resolvedBfMetres, out double3 normalBf);
    }
}
