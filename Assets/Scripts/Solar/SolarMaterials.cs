using UnityEngine;

namespace Planet.Solar
{
    /// <summary>
    /// The white-model material palette. Assets live in Assets/Resources/Solar so the scene does
    /// not have to hold references to them and a freshly rebuilt scene still renders correctly.
    /// </summary>
    public static class SolarMaterials
    {
        private static Material _clay;
        private static Material _ring;
        private static Material _sun;
        private static Material _marker;
        private static Material _orbit;

        public const string ResourceFolder = "Solar/";
        public const string ClayPath = "Assets/Resources/Solar/WhiteClay.mat";
        public const string RingPath = "Assets/Resources/Solar/WhiteRing.mat";
        public const string SunPath = "Assets/Resources/Solar/WhiteSun.mat";
        public const string MarkerPath = "Assets/Resources/Solar/BeltMarker.mat";
        public const string OrbitPath = "Assets/Resources/Solar/OrbitLine.mat";

        /// <summary>Default graticule strength of the clay shader (see Planet/ClayGrid).</summary>
        public const float GridStrength = 0.30f;

        private static float _gridStrength = -1f;

        /// <summary>Matte white clay: the default surface of every body.</summary>
        public static Material Clay { get { return _clay != null ? _clay : (_clay = Load("WhiteClay")); } }

        /// <summary>Translucent-looking flat white for ring systems.</summary>
        public static Material Ring { get { return _ring != null ? _ring : (_ring = Load("WhiteRing")); } }

        /// <summary>Emissive white so the Sun reads as the light source and feeds bloom.</summary>
        public static Material Sun { get { return _sun != null ? _sun : (_sun = Load("WhiteSun")); } }

        /// <summary>Unlit grey for asteroid / Kuiper-belt markers.</summary>
        public static Material Marker { get { return _marker != null ? _marker : (_marker = Load("BeltMarker")); } }

        /// <summary>Unlit, faint line material for orbit paths.</summary>
        public static Material OrbitLine { get { return _orbit != null ? _orbit : (_orbit = Load("OrbitLine")); } }

        public static void ClearCache()
        {
            _clay = _ring = _sun = _marker = _orbit = null;
            _gridStrength = -1f;
        }

        /// <summary>
        /// Turns the clay graticule on or off. Driven by the L hotkey; without it a featureless
        /// white sphere gives no visual evidence that it is rotating at all.
        /// </summary>
        public static void SetGridStrength(float strength)
        {
            _gridStrength = strength;
            ApplyGridStrength(Clay);
            ApplyGridStrength(Ring);
        }

        /// <summary>Current graticule strength, or the shader default if never set.</summary>
        public static float CurrentGridStrength
        {
            get { return _gridStrength >= 0f ? _gridStrength : GridStrength; }
        }

        public static void ResetGridStrength()
        {
            SetGridStrength(GridStrength);
        }

        private static void ApplyGridStrength(Material m)
        {
            if (m == null || !m.HasProperty("_GridStrength")) return;
            m.SetFloat("_GridStrength", _gridStrength < 0f ? GridStrength : _gridStrength);
        }

        private static Material Load(string name)
        {
            Material m = Resources.Load<Material>(ResourceFolder + name);
            if (m == null)
                Debug.LogWarning("[Solar] Missing material Resources/" + ResourceFolder + name +
                                 " – run Tools ▸ Planet ▸ Rebuild Solar System to regenerate the palette.");
            return m;
        }
    }
}
