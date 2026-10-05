using UnityEngine;

namespace Planet.Procedural
{
    /// <summary>Shared physical authority. Planet and star rendering have separate derived definitions.</summary>
    public abstract class CelestialDefinition : ScriptableObject
    {
        public string displayName = "Procedural Terra";
        public PlanetPhysicalParameters physical = new PlanetPhysicalParameters();
        public abstract string ContentFingerprint();
    }
}
