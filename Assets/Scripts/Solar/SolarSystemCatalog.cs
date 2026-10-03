using System;
using System.Collections.Generic;
using UnityEngine;

namespace Planet.Solar
{
    public enum BodyKind { Star, Planet, DwarfPlanet, Moon }

    /// <summary>Saturn-style ring system. Radii are measured from the planet's centre, in km.</summary>
    [Serializable]
    public struct RingSet
    {
        public double innerKm;
        public double outerKm;

        /// <summary>
        /// Alternating inner/outer radii (km) of the individual rings, outermost last. Empty means
        /// one solid annulus from innerKm to outerKm.
        ///
        /// Saturn's rings are not one sheet: the 4,600 km Cassini division between the B and A rings
        /// is wider than the distance from London to New York, and leaving it out makes the planet
        /// look like a dinner plate.
        /// </summary>
        public double[] bands;

        public RingSet(double innerKm, double outerKm)
        {
            this.innerKm = innerKm;
            this.outerKm = outerKm;
            this.bands = null;
        }

        public RingSet(double innerKm, double outerKm, double[] bands)
        {
            this.innerKm = innerKm;
            this.outerKm = outerKm;
            this.bands = bands;
        }

        /// <summary>Ring radii as fractions of the outer radius, ready for the mesh factory.</summary>
        public float[] BandFractions()
        {
            if (bands == null || bands.Length < 2) return new[] { (float)(innerKm / outerKm), 1f };
            var f = new float[bands.Length];
            for (int i = 0; i < bands.Length; i++) f[i] = (float)(bands[i] / outerKm);
            return f;
        }
    }

    /// <summary>One body of the solar system: physical size, spin state, and orbit.</summary>
    [Serializable]
    public sealed class Body
    {
        public string Name;              // stable identifier / GameObject name
        public string Label;             // on-screen label
        public BodyKind Kind;

        // ---- physical ----
        public double RadiusKm;          // mean (volumetric) radius
        public double Flattening;        // (a - b) / a; 0 for a sphere
        public Vector3 ShapeScale = Vector3.one;   // extra triaxial stretch (Haumea)
        public double RotationHours;     // sidereal spin period; negative = retrograde
        public double PoleRaDeg;         // IAU J2000 north pole, right ascension
        public double PoleDecDeg;        // IAU J2000 north pole, declination

        // ---- orbit ----
        public bool HasOrbit;
        public string Parent;            // null/empty = orbits the Sun
        public KeplerianElements Orbit;  // heliocentric
        public MoonOrbit MoonOrbit;      // used when Parent is set

        public RingSet? Rings;

        public bool IsSun { get { return Kind == BodyKind.Star; } }

        /// <summary>Radius of the body in a given unit scale (scene units per kilometre).</summary>
        public float RadiusUnits(double unitsPerKm) { return (float)(RadiusKm * unitsPerKm); }
    }

    /// <summary>
    /// The whole solar system, Sun through Kuiper belt, in real units.
    ///
    /// Precision tiers, stated honestly:
    ///   * Sun, eight planets  -> JPL/Standish "Keplerian Elements for Approximate Positions of the
    ///     Major Planets", Table 1 (1800-2050 AD). Positions good to a few arcminutes.
    ///   * Earth's Moon        -> Meeus low-precision lunar elements (accurate to ~0.1 degree).
    ///   * Dwarf planets       -> JPL SBDB elements, truncated to the digits shown; good to
    ///     roughly a degree, which is far below one pixel at any sane zoom.
    ///   * Galilean moons, Titan, Triton, Charon -> real semi-major axes, eccentricities and
    ///     periods; the J2000 mean longitudes are NOT tabulated here, so their orbital phase
    ///     around the parent is arbitrary. Their size, distance and orbital speed are correct.
    /// </summary>
    public static class SolarSystemCatalog
    {
        private static Body[] _bodies;

        public static Body[] Bodies { get { if (_bodies == null) Build(); return _bodies; } }

        public static Body Find(string name)
        {
            Body[] all = Bodies;
            for (int k = 0; k < all.Length; k++)
                if (string.Equals(all[k].Name, name, StringComparison.OrdinalIgnoreCase)) return all[k];
            return null;
        }

        /// <summary>Bodies directly orbiting the Sun, in order of distance.</summary>
        public static List<Body> Heliocentric()
        {
            var list = new List<Body>();
            foreach (Body b in Bodies) if (string.IsNullOrEmpty(b.Parent)) list.Add(b);
            return list;
        }

        private static void Add(List<Body> l, Body b) { l.Add(b); }

        private static void Build()
        {
            var l = new List<Body>(32);

            // =====================================================================
            // Sun -- IAU 2015 nominal solar radius 695,700 km; the traditionally
            // quoted photospheric radius 696,340 km is used here.
            // =====================================================================
            Add(l, new Body
            {
                Name = "Sun", Label = "Sun", Kind = BodyKind.Star,
                RadiusKm = 696340.0, RotationHours = 609.12,
                PoleRaDeg = 286.13, PoleDecDeg = 63.87
            });

            // =====================================================================
            // Planets -- a, e, i, L, longitude of perihelion, longitude of node,
            // followed by the six secular rates per Julian century.
            // =====================================================================

            Add(l, new Body
            {
                Name = "Mercury", Label = "Mercury", Kind = BodyKind.Planet,
                RadiusKm = 2439.7, Flattening = 0.0000, RotationHours = 1407.6,
                PoleRaDeg = 281.01, PoleDecDeg = 61.45,
                HasOrbit = true,
                Orbit = new KeplerianElements(
                    0.38709927, 0.20563593, 7.00497902, 252.25032350, 77.45779628, 48.33076593,
                    0.00000037, 0.00001906, -0.00594749, 149472.67411175, 0.16047689, -0.12534081)
            });

            Add(l, new Body
            {
                Name = "Venus", Label = "Venus", Kind = BodyKind.Planet,
                RadiusKm = 6051.8, Flattening = 0.0000, RotationHours = -5832.5,  // retrograde, 243 d
                PoleRaDeg = 272.76, PoleDecDeg = 67.16,
                HasOrbit = true,
                Orbit = new KeplerianElements(
                    0.72333566, 0.00677672, 3.39467605, 181.97909950, 131.60246718, 76.67984255,
                    0.00000390, -0.00004107, -0.00078890, 58517.81538729, 0.00268329, -0.27769418)
            });

            Add(l, new Body
            {
                Name = "Earth", Label = "Earth", Kind = BodyKind.Planet,
                RadiusKm = 6371.0, Flattening = 1.0 / 298.257, RotationHours = 23.9344696,
                PoleRaDeg = 0.00, PoleDecDeg = 90.00,
                HasOrbit = true,
                Orbit = new KeplerianElements(
                    // Earth-Moon barycentre; the 4670 km offset is handled by giving the
                    // Earth a real Moon rather than by nudging the barycentre.
                    1.00000261, 0.01671123, -0.00001531, 100.46457166, 102.93768193, 0.0,
                    0.00000562, -0.00004392, -0.01294668, 35999.37244981, 0.32327364, 0.0)
            });

            // Earth's Moon -- Meeus low-precision elements, geocentric, referred to the ecliptic.
            Add(l, new Body
            {
                Name = "Moon", Label = "Moon", Kind = BodyKind.Moon, Parent = "Earth",
                RadiusKm = 1737.4, RotationHours = 655.728,     // synchronous: 27.322 d
                PoleRaDeg = 269.99, PoleDecDeg = 66.54,
                HasOrbit = true,
                MoonOrbit = new MoonOrbit(
                    384400.0, 0.0549, 5.145,
                    218.3164477, 83.3532465, 125.0445479,
                    481267.88123421, 4069.0137287, -1934.1362891,
                    MoonFrame.Ecliptic)
            });

            Add(l, new Body
            {
                Name = "Mars", Label = "Mars", Kind = BodyKind.Planet,
                RadiusKm = 3389.5, Flattening = 0.00589, RotationHours = 24.622962,
                PoleRaDeg = 317.68, PoleDecDeg = 52.89,
                HasOrbit = true,
                Orbit = new KeplerianElements(
                    1.52371034, 0.09339410, 1.84969142, -4.55343205, -23.94362959, 49.55953891,
                    0.00001847, 0.00007882, -0.00813131, 19140.30268499, 0.44441088, -0.29257343)
            });

            Add(l, new Body
            {
                Name = "Jupiter", Label = "Jupiter", Kind = BodyKind.Planet,
                RadiusKm = 69911.0, Flattening = 0.06487, RotationHours = 9.9250,   // fastest spin
                PoleRaDeg = 268.06, PoleDecDeg = 64.50,
                HasOrbit = true,
                Orbit = new KeplerianElements(
                    5.20288700, 0.04838624, 1.30439695, 34.39644051, 14.72847983, 100.47390909,
                    -0.00011607, -0.00013253, -0.00183714, 3034.74612775, 0.21252668, 0.20469106)
            });

            // Galilean satellites: real a / e / i / period, arbitrary epoch phase (see class note).
            AddMoon(l, "Io", "Jupiter", 1821.6, 421700.0, 0.0041, 0.036, 1.769138, 0.0);
            AddMoon(l, "Europa", "Jupiter", 1560.8, 671034.0, 0.0090, 0.466, 3.551181, 137.0);
            AddMoon(l, "Ganymede", "Jupiter", 2634.1, 1070412.0, 0.0013, 0.177, 7.154553, 251.0);
            AddMoon(l, "Callisto", "Jupiter", 2410.3, 1882709.0, 0.0074, 0.192, 16.689018, 74.0);

            Add(l, new Body
            {
                Name = "Saturn", Label = "Saturn", Kind = BodyKind.Planet,
                RadiusKm = 58232.0, Flattening = 0.09796, RotationHours = 10.656,
                PoleRaDeg = 40.59, PoleDecDeg = 83.54,
                HasOrbit = true,
                Rings = new RingSet(66900.0, 140580.0, new[]
                {
                    66900.0,  74500.0,    // D ring (very faint)
                    74500.0,  92000.0,    // C ring
                    92000.0, 117580.0,    // B ring (brightest, densest)
                    // 117,580 - 122,170 is the Cassini division
                    122170.0, 136775.0,   // A ring, with the 3,250 km Encke gap inside it
                    // 136,775 - 140,180 is the Roche division
                    140180.0, 140580.0    // F ring
                }),
                Orbit = new KeplerianElements(
                    9.53667594, 0.05386179, 2.48599187, 49.95424423, 92.59887831, 113.66242448,
                    -0.00125060, -0.00050991, 0.00193609, 1222.49362201, -0.41897216, -0.28867794)
            });

            AddMoon(l, "Titan", "Saturn", 2574.7, 1221870.0, 0.0288, 0.348, 15.945, 0.0);

            Add(l, new Body
            {
                Name = "Uranus", Label = "Uranus", Kind = BodyKind.Planet,
                RadiusKm = 25362.0, Flattening = 0.02293, RotationHours = -17.24,  // retrograde
                PoleRaDeg = 257.31, PoleDecDeg = -15.18,                            // 97.8 deg tilt
                HasOrbit = true,
                Orbit = new KeplerianElements(
                    19.18916464, 0.04725744, 0.77263783, 313.23810451, 170.95427630, 74.01692503,
                    -0.00196176, -0.00004397, -0.00242939, 428.48202785, 0.40805281, 0.04240589)
            });

            Add(l, new Body
            {
                Name = "Neptune", Label = "Neptune", Kind = BodyKind.Planet,
                RadiusKm = 24622.0, Flattening = 0.01708, RotationHours = 16.11,
                PoleRaDeg = 299.36, PoleDecDeg = 43.46,
                HasOrbit = true,
                Orbit = new KeplerianElements(
                    30.06992276, 0.00859048, 1.77004347, -55.12002969, 44.96476227, 131.78422574,
                    0.00026291, 0.00005105, 0.00035372, 218.45945325, -0.32241464, -0.00508664)
            });

            // Triton: the big retrograde one, inclined 157 deg to Neptune's equator.
            AddMoon(l, "Triton", "Neptune", 1353.4, 354759.0, 0.000016, 156.885, 5.876854, 0.0);

            // =====================================================================
            // Dwarf planets. Pluto carries the full Standish element set; the two
            // Kuiper-belt dwarfs use JPL SBDB elements rounded to a few digits.
            // =====================================================================

            Add(l, new Body
            {
                Name = "Pluto", Label = "Pluto", Kind = BodyKind.DwarfPlanet,
                RadiusKm = 1188.3, RotationHours = -153.2928,   // retrograde, 6.387 d
                PoleRaDeg = 132.99, PoleDecDeg = -6.16,          // 122.5 deg tilt
                HasOrbit = true,
                Orbit = new KeplerianElements(
                    39.48211675, 0.24882730, 17.14001206, 238.92903833, 224.06891629, 110.30393684,
                    -0.00031596, 0.00005170, 0.00004818, 145.20780515, -0.04062942, -0.01183482)
            });

            // Charon: half Pluto's diameter, mutually tidally locked, 19591 km apart.
            AddMoon(l, "Charon", "Pluto", 606.0, 19591.0, 0.0002, 0.080, 6.3872, 0.0);

            Add(l, new Body
            {
                Name = "Haumea", Label = "Haumea", Kind = BodyKind.DwarfPlanet,
                // 1050 x 840 x 537 km: the most oblate large body known, spun up to a 3.9 h day.
                RadiusKm = 780.0,
                ShapeScale = new Vector3(1.346f, 0.688f, 1.077f),
                RotationHours = 3.9155,
                PoleRaDeg = 282.0, PoleDecDeg = -13.0,          // approximate
                HasOrbit = true,
                Orbit = new KeplerianElements(
                    43.182, 0.191, 28.21, 219.7, 1.34, 122.16,
                    0.0, 0.0, 0.0, 126.72, 0.0, 0.0)
            });

            Add(l, new Body
            {
                Name = "Makemake", Label = "Makemake", Kind = BodyKind.DwarfPlanet,
                RadiusKm = 715.0, RotationHours = 22.83,
                PoleRaDeg = 240.0, PoleDecDeg = -10.0,          // approximate
                HasOrbit = true,
                Orbit = new KeplerianElements(
                    45.79, 0.159, 28.98, 179.7, 14.19, 79.36,
                    0.0, 0.0, 0.0, 117.57, 0.0, 0.0)
            });

            // =====================================================================
            // Ceres: the asteroid belt's only dwarf planet, included so the belt has
            // at least one named member.
            // =====================================================================
            Add(l, new Body
            {
                Name = "Ceres", Label = "Ceres", Kind = BodyKind.DwarfPlanet,
                RadiusKm = 469.7, Flattening = 0.068, RotationHours = 9.074170,
                PoleRaDeg = 291.42, PoleDecDeg = 66.51,
                HasOrbit = true,
                Orbit = new KeplerianElements(
                    2.7658, 0.0785, 10.593, 249.89, 153.90, 80.39,
                    0.0, 0.0, 0.0, 7820.6, 0.0, 0.0)
            });

            _bodies = l.ToArray();
        }

        /// <summary>
        /// Adds a satellite. Phase is spread deterministically so the moons of one planet do not
        /// all line up; see the class comment for what is and is not real here.
        /// </summary>
        private static void AddMoon(List<Body> l, string name, string parent, double radiusKm,
                                    double aKm, double e, double incDeg, double periodDays,
                                    double phaseDeg)
        {
            double nPerCentury = 360.0 / periodDays * 36525.0;
            Add(l, new Body
            {
                Name = name, Label = name, Kind = BodyKind.Moon, Parent = parent,
                RadiusKm = radiusKm,
                RotationHours = periodDays * 24.0,        // tidally locked: spin == orbit
                PoleRaDeg = 0.0, PoleDecDeg = 90.0,       // overwritten with the parent's pole
                HasOrbit = true,
                MoonOrbit = new MoonOrbit(
                    aKm, e, incDeg,
                    phaseDeg, phaseDeg, 0.0,
                    nPerCentury, 0.0, 0.0,
                    MoonFrame.ParentEquator)
            });
        }
    }
}
