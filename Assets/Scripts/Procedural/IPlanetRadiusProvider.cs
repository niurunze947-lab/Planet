namespace Planet.Procedural
{
    /// <summary>Supplies the current render radius without depending on the solar-system assembly.</summary>
    public interface IPlanetRadiusProvider
    {
        float RadiusUnits { get; }
    }
}
