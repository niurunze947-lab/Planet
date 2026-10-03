using UnityEngine;

namespace Planet.Solar
{
    /// <summary>
    /// A particle population rendered as GPU instances: the asteroid belt (2.06 – 3.27 AU) and the
    /// Kuiper belt (30 – 50 AU).
    ///
    /// Every particle carries real orbital elements and is propagated with the same Kepler solver
    /// the planets use, so the belts shear correctly: the inner edge laps the outer edge, exactly as
    /// a real debris disc does. Orbital radius and period come from Kepler's third law,
    /// P(years) = a(AU)^1.5, so nothing here is hand-tuned to "look right".
    ///
    /// The one deliberate exaggeration is the size of the marker glyph. A 100 km asteroid is
    /// 1e-4 scene units across at true scale, i.e. permanently invisible, so markers are scaled to
    /// hold a constant angular size instead. Their positions are still true to scale.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class BeltField : MonoBehaviour
    {
        [Header("Population")]
        public string fieldName = "Asteroid Belt";
        public int count = 2200;
        public double seed = 20261003.0;

        [Header("Orbit distribution (AU)")]
        public double innerAu = 2.06;
        public double outerAu = 3.27;
        [Tooltip("Bias of the radial distribution. 1 = uniform in radius, >1 crowds the outer edge.")]
        public double radialBias = 1.0;
        public double maxEccentricity = 0.22;

        [Header("Inclination (deg)")]
        [Range(0f, 1f)] public float coldFraction = 0.68f;
        public double coldMaxInclination = 4.0;
        public double hotMaxInclination = 22.0;

        [Header("Marker glyph (NOT to scale - see class comment)")]
        [Tooltip("Marker radius as a fraction of its distance from the camera, so markers hold a constant angular size. 0.0012 rad is about 2.5 pixels on a 900 px view at 50 degrees vertical FOV.")]
        public float markerAngularSize = 0.0012f;
        public float markerMinUnits = 2e-5f;
        [Tooltip("Per-particle size jitter, so a dense belt reads as grains rather than as flat texture.")]
        [Range(0f, 0.9f)] public float markerSizeJitter = 0.55f;
        public int markerLongitudes = 8;
        public int markerLatitudes = 5;

        private struct Particle
        {
            public double a;        // AU
            public double e;
            public double n;        // mean motion, deg/day
            public double m0;       // mean anomaly at J2000, deg
            public Vec3d px, py;    // perifocal basis vectors, ecliptic frame
            public float size;      // glyph size multiplier
        }

        private Particle[] _p;
        private Matrix4x4[] _matrices;
        private Mesh _markerMesh;
        private bool _built;
        private int _drivenFrame = -1;

        private void OnEnable() { Build(); }

        private void OnValidate() { if (!Application.isPlaying) _built = false; }

        public void Build()
        {
            _built = false;
            int n = Mathf.Clamp(count, 0, 40000);
            if (n == 0) return;

            _p = new Particle[n];
            _matrices = new Matrix4x4[1023];
            _markerMesh = SolarMeshFactory.Sphere(markerLongitudes, markerLatitudes, 0f, Vector3.one);

            // Deterministic 64-bit LCG: the same belt on every machine, every run.
            ulong state = (ulong)(seed * 1000.0) | 1UL;
            System.Func<double> rnd = delegate ()
            {
                state = state * 6364136223846793005UL + 1442695040888963407UL;
                return (double)((state >> 11) & ((1UL << 53) - 1)) / (double)(1UL << 53);
            };

            const double deg2rad = OrbitalMechanics.Deg2Rad;

            for (int k = 0; k < n; k++)
            {
                double u = rnd();
                double a = innerAu + (outerAu - innerAu) * System.Math.Pow(u, radialBias);
                double e = maxEccentricity * System.Math.Pow(rnd(), 2.0);   // clustered low

                bool cold = rnd() < coldFraction;
                double iMax = cold ? coldMaxInclination : hotMaxInclination;
                double inc = iMax * System.Math.Pow(rnd(), 2.0);

                double node = rnd() * 360.0;
                double peri = rnd() * 360.0;
                double m0 = rnd() * 360.0;

                // Kepler's third law for a heliocentric orbit: P(years) = a(AU)^1.5
                double periodDays = 365.2568983 * System.Math.Pow(a, 1.5);

                double w = (peri - node) * deg2rad;
                double om = node * deg2rad;
                double ic = inc * deg2rad;
                double cw = System.Math.Cos(w), sw = System.Math.Sin(w);
                double co = System.Math.Cos(om), so = System.Math.Sin(om);
                double ci = System.Math.Cos(ic), si = System.Math.Sin(ic);

                _p[k] = new Particle
                {
                    a = a,
                    e = e,
                    n = 360.0 / periodDays,
                    m0 = m0,
                    px = new Vec3d(cw * co - sw * so * ci, cw * so + sw * co * ci, sw * si),
                    py = new Vec3d(-sw * co - cw * so * ci, -sw * so + cw * co * ci, cw * si),
                    size = (float)(1.0 - markerSizeJitter * rnd())
                };
            }

            _built = true;
        }

        /// <summary>
        /// Places and submits the instances. Called by <see cref="SolarSystemSimulation"/> once the
        /// floating origin for this frame is known, so the belt moves with everything else.
        /// </summary>
        public void Render(Camera cam, Vec3d originKm, double unitsPerKm, double jd)
        {
            if (!_built) Build();
            if (!_built || _p == null || _markerMesh == null) return;

            Material mat = SolarMaterials.Marker;
            if (mat == null) return;

            _drivenFrame = Time.frameCount;

            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            double T = jd - OrbitalMechanics.J2000;
            float ang = Mathf.Max(markerAngularSize, 1e-6f);

            int drawn = 0;

            for (int k = 0; k < _p.Length; k++)
            {
                Particle pt = _p[k];

                double M = OrbitalMechanics.WrapDeg180(pt.m0 + pt.n * T) * OrbitalMechanics.Deg2Rad;
                double E = OrbitalMechanics.SolveKepler(M, pt.e);

                double cosE = System.Math.Cos(E);
                double sinE = System.Math.Sin(E);
                double xp = pt.a * (cosE - pt.e);
                double yp = pt.a * System.Math.Sqrt(1.0 - pt.e * pt.e) * sinE;

                // Heliocentric ecliptic in AU, converted to km.
                double x = (pt.px.x * xp + pt.py.x * yp) * OrbitalMechanics.AuKm;
                double y = (pt.px.y * xp + pt.py.y * yp) * OrbitalMechanics.AuKm;
                double z = (pt.px.z * xp + pt.py.z * yp) * OrbitalMechanics.AuKm;

                var world = new Vector3(
                    (float)((x - originKm.x) * unitsPerKm),
                    (float)((z - originKm.z) * unitsPerKm),
                    (float)(-(y - originKm.y) * unitsPerKm));

                float dist = (world - camPos).magnitude;
                float scale = Mathf.Max(markerMinUnits, dist * ang * pt.size);

                _matrices[drawn++] = Matrix4x4.TRS(world, Quaternion.identity, new Vector3(scale, scale, scale));

                if (drawn == 1023)
                {
                    Graphics.DrawMeshInstanced(_markerMesh, 0, mat, _matrices, drawn);
                    drawn = 0;
                }
            }

            if (drawn > 0) Graphics.DrawMeshInstanced(_markerMesh, 0, mat, _matrices, drawn);
        }

        /// <summary>
        /// Editor / idle preview. When the simulation is not running (scene not in play mode) the
        /// belt still draws at the J2000 epoch with no floating origin, so the scene view is not
        /// empty while you are building.
        /// </summary>
        private void Update()
        {
            if (_drivenFrame == Time.frameCount) return;
            Render(Camera.main, Vec3d.Zero, 1e-6, OrbitalMechanics.J2000);
        }
    }
}
