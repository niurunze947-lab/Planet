using UnityEngine;

namespace Planet.Solar
{
    /// <summary>
    /// The full orbital ellipse of a heliocentric body, sampled straight out of Kepler's equation.
    ///
    /// At true scale the planets themselves are sub-pixel dots from anywhere except right on top of
    /// them, so the orbit lines are what actually makes the solar system legible. They are drawn in
    /// the local space of an "Orbits" root that the simulation shifts along with the floating
    /// origin, which keeps them glued to the planets without recomputing a single vertex.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class OrbitPath : MonoBehaviour
    {
        public string bodyName = "Earth";
        public int samples = 720;
        public Color color = new Color(0.42f, 0.52f, 0.72f, 0.55f);

        [Tooltip("Kilometres per scene unit. Must match SolarSystemSimulation.kmPerUnit.")]
        public float kmPerUnit = 1000000f;

        [System.NonSerialized] public float widthMultiplier = 1f;

        private LineRenderer _line;
        private string _builtFor;
        private int _builtSamples = -1;
        private float _builtKmPerUnit = -1f;
        private float _alpha = -1f;

        private void OnEnable() { Rebuild(); }

        private void Update()
        {
            if (_line == null || _builtFor != bodyName || _builtSamples != samples ||
                !Mathf.Approximately(_builtKmPerUnit, kmPerUnit))
            {
                Rebuild();
            }
        }

        /// <summary>
        /// Line width, as a fraction of the camera's distance. Written straight through to the
        /// LineRenderer rather than latched in a field for Update() to copy.
        ///
        /// The earlier field-only version silently did nothing whenever this component's Update()
        /// did not run that frame - which is exactly what happens while the editor is paused, and
        /// it left every orbit at the width computed on frame one, i.e. invisible.
        /// </summary>
        public void SetWidth(float w)
        {
            widthMultiplier = w;
            if (_line == null) Rebuild();
            if (_line != null) _line.widthMultiplier = w;
        }

        /// <summary>Sets the whole line's opacity (the material is shared, so it rides vertex colour).</summary>
        public void SetAlpha(float a)
        {
            if (_line == null || Mathf.Approximately(_alpha, a)) return;
            _alpha = a;
            var c = color;
            c.a = a;
            _line.startColor = c;
            _line.endColor = c;
        }

        public void Rebuild()
        {
            Body body = SolarSystemCatalog.Find(bodyName);
            if (body == null || !body.HasOrbit) return;

            if (_line == null)
            {
                _line = GetComponent<LineRenderer>();
                if (_line == null) _line = gameObject.AddComponent<LineRenderer>();
            }

            double unitsPerKm = kmPerUnit > 0f ? 1.0 / kmPerUnit : 1e-6;

            _line.useWorldSpace = false;
            _line.loop = true;
            _line.alignment = LineAlignment.View;
            _line.textureMode = LineTextureMode.Stretch;
            _line.numCornerVertices = 0;
            _line.numCapVertices = 0;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _line.sharedMaterial = SolarMaterials.OrbitLine;
            _line.startColor = color;
            _line.endColor = color;
            _line.widthMultiplier = widthMultiplier;

            int n = Mathf.Clamp(samples, 64, 4096);
            double period = body.Orbit.PeriodDays;
            if (period <= 0.0) period = 365.25;

            _line.positionCount = n;
            for (int k = 0; k < n; k++)
            {
                double jd = OrbitalMechanics.J2000 + period * k / (double)n;
                Vec3d au = body.Orbit.PositionAu(jd);
                // AU -> km -> scene units, then ecliptic -> Unity axes.
                var p = new Vector3(
                    (float)(au.x * OrbitalMechanics.AuKm * unitsPerKm),
                    (float)(au.z * OrbitalMechanics.AuKm * unitsPerKm),
                    (float)(-au.y * OrbitalMechanics.AuKm * unitsPerKm));
                _line.SetPosition(k, p);
            }

            _builtFor = bodyName;
            _builtSamples = samples;
            _builtKmPerUnit = kmPerUnit;
        }
    }
}
