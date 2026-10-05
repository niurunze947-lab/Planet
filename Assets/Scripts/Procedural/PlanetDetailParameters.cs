using System;
using UnityEngine;

namespace Planet.Procedural
{
    /// <summary>
    /// Fine relief added on top of the macro surface by the terrain quadtree only. Baked shell meshes
    /// and textures never see it, so it is not part of the content fingerprint.
    /// </summary>
    [Serializable]
    public sealed class PlanetDetailParameters
    {
        public bool enabled = true;
        [Tooltip("Amplitude of the 4096 m octave. Each finer octave is scaled by gain.")]
        [Min(0f)] public float amplitudeMetres = 40f;
        [Range(0.3f, 0.75f)] public float gain = 0.55f;
        [Tooltip("Shortest wavelength generated. Octaves below twice the vertex spacing are never evaluated.")]
        [Min(0.005f)] public float minimumWavelengthMetres = 0.02f;

        public void Validate()
        {
            amplitudeMetres = Mathf.Max(0f, amplitudeMetres);
            gain = Mathf.Clamp(gain, 0.3f, 0.75f);
            minimumWavelengthMetres = Mathf.Max(0.005f, minimumWavelengthMetres);
        }
    }
}
