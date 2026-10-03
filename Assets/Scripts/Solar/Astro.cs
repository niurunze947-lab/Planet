using System;

namespace Planet.Solar
{
    /// <summary>
    /// Double-precision 3-vector. The solar system spans roughly 1e10 km end to end, which is far
    /// beyond what float can hold to useful precision, so every orbital computation happens in
    /// doubles and only the final, camera-relative offset is narrowed to float.
    /// </summary>
    public struct Vec3d
    {
        public double x, y, z;

        public Vec3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }

        public static readonly Vec3d Zero = new Vec3d(0.0, 0.0, 0.0);

        public static Vec3d operator +(Vec3d a, Vec3d b) { return new Vec3d(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vec3d operator -(Vec3d a, Vec3d b) { return new Vec3d(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vec3d operator *(Vec3d a, double s) { return new Vec3d(a.x * s, a.y * s, a.z * s); }
        public static Vec3d operator /(Vec3d a, double s) { return new Vec3d(a.x / s, a.y / s, a.z / s); }

        public double Length { get { return Math.Sqrt(x * x + y * y + z * z); } }

        public override string ToString() { return string.Format("({0:G8}, {1:G8}, {2:G8})", x, y, z); }
    }

    /// <summary>
    /// Classical Keplerian elements with secular (linear-in-time) rates.
    /// Heliocentric ecliptic frame, J2000 epoch. Distances in astronomical units,
    /// angles in degrees, rates per Julian century.
    /// </summary>
    [Serializable]
    public struct KeplerianElements
    {
        public double a;      // semi-major axis            (AU)
        public double e;      // eccentricity
        public double i;      // inclination to the ecliptic (deg)
        public double L;      // mean longitude              (deg)
        public double peri;   // longitude of perihelion     (deg)
        public double node;   // longitude of ascending node (deg)

        public double aDot, eDot, iDot, LDot, periDot, nodeDot;

        public KeplerianElements(
            double a, double e, double i, double L, double peri, double node,
            double aDot, double eDot, double iDot, double LDot, double periDot, double nodeDot)
        {
            this.a = a; this.e = e; this.i = i; this.L = L; this.peri = peri; this.node = node;
            this.aDot = aDot; this.eDot = eDot; this.iDot = iDot;
            this.LDot = LDot; this.periDot = periDot; this.nodeDot = nodeDot;
        }

        /// <summary>
        /// Heliocentric ecliptic position in astronomical units at the given Julian date.
        /// Solves Kepler's equation exactly (Newton-Raphson) rather than assuming a circle,
        /// so Mercury's 0.206 and Pluto's 0.249 eccentricities come out right.
        /// </summary>
        public Vec3d PositionAu(double jd)
        {
            double T = (jd - OrbitalMechanics.J2000) / 36525.0;

            double aa = a + aDot * T;
            double ee = e + eDot * T;
            double ii = i + iDot * T;
            double LL = L + LDot * T;
            double pp = peri + periDot * T;
            double nn = node + nodeDot * T;

            // Mean anomaly, then eccentric anomaly.
            double M = OrbitalMechanics.WrapDeg180(LL - pp) * OrbitalMechanics.Deg2Rad;
            double E = OrbitalMechanics.SolveKepler(M, ee);

            // Position in the orbital plane (x toward perihelion).
            double xp = aa * (Math.Cos(E) - ee);
            double yp = aa * Math.Sqrt(1.0 - ee * ee) * Math.Sin(E);

            double w = (pp - nn) * OrbitalMechanics.Deg2Rad;   // argument of perihelion
            double om = nn * OrbitalMechanics.Deg2Rad;
            double inc = ii * OrbitalMechanics.Deg2Rad;

            double cw = Math.Cos(w), sw = Math.Sin(w);
            double co = Math.Cos(om), so = Math.Sin(om);
            double ci = Math.Cos(inc), si = Math.Sin(inc);

            double x = (cw * co - sw * so * ci) * xp + (-sw * co - cw * so * ci) * yp;
            double y = (cw * so + sw * co * ci) * xp + (-sw * so + cw * co * ci) * yp;
            double z = (sw * si) * xp + (cw * si) * yp;

            return new Vec3d(x, y, z);
        }

        /// <summary>Sidereal orbital period in days, from the mean-longitude rate.</summary>
        public double PeriodDays
        {
            get
            {
                double n = Math.Abs(LDot) / 36525.0;   // deg per day
                return n > 0.0 ? 360.0 / n : 0.0;
            }
        }
    }

    /// <summary>Which plane a satellite's orbit is measured against.</summary>
    public enum MoonFrame
    {
        /// <summary>Inclination is measured from the ecliptic (Earth's Moon is the classic case).</summary>
        Ecliptic,
        /// <summary>Inclination is measured from the parent planet's equator (most regular satellites).</summary>
        ParentEquator
    }

    /// <summary>
    /// A satellite orbit around its parent. Same Kepler machinery as the planets, but the
    /// reference plane is selectable so Uranus' moons end up in its 98-degree tilted equator
    /// rather than in the ecliptic.
    /// </summary>
    [Serializable]
    public struct MoonOrbit
    {
        public double aKm;            // semi-major axis (km)
        public double e;              // eccentricity
        public double i;              // inclination to <see cref="frame"/> (deg)
        public double L;              // mean longitude at J2000 (deg)
        public double peri;           // longitude of perihelion at J2000 (deg)
        public double node;           // longitude of ascending node at J2000 (deg)
        public double LDot;           // deg per Julian century
        public double periDot;
        public double nodeDot;
        public MoonFrame frame;

        public MoonOrbit(double aKm, double e, double i, double L, double peri, double node,
                         double LDot, double periDot, double nodeDot, MoonFrame frame)
        {
            this.aKm = aKm; this.e = e; this.i = i; this.L = L; this.peri = peri; this.node = node;
            this.LDot = LDot; this.periDot = periDot; this.nodeDot = nodeDot; this.frame = frame;
        }

        /// <summary>
        /// Position relative to the parent, expressed in the reference frame's own axes
        /// (X toward that frame's vernal point, Z toward its north pole). Caller rotates it.
        /// </summary>
        public Vec3d PositionKm(double jd)
        {
            double T = (jd - OrbitalMechanics.J2000) / 36525.0;
            double ee = e;
            double ii = i;
            double LL = L + LDot * T;
            double pp = peri + periDot * T;
            double nn = node + nodeDot * T;

            double M = OrbitalMechanics.WrapDeg180(LL - pp) * OrbitalMechanics.Deg2Rad;
            double E = OrbitalMechanics.SolveKepler(M, ee);

            double xp = aKm * (Math.Cos(E) - ee);
            double yp = aKm * Math.Sqrt(1.0 - ee * ee) * Math.Sin(E);

            double w = (pp - nn) * OrbitalMechanics.Deg2Rad;
            double om = nn * OrbitalMechanics.Deg2Rad;
            double inc = ii * OrbitalMechanics.Deg2Rad;

            double cw = Math.Cos(w), sw = Math.Sin(w);
            double co = Math.Cos(om), so = Math.Sin(om);
            double ci = Math.Cos(inc), si = Math.Sin(inc);

            double x = (cw * co - sw * so * ci) * xp + (-sw * co - cw * so * ci) * yp;
            double y = (cw * so + sw * co * ci) * xp + (-sw * so + cw * co * ci) * yp;
            double z = (sw * si) * xp + (cw * si) * yp;

            return new Vec3d(x, y, z);
        }

        public double PeriodDays
        {
            get
            {
                double n = Math.Abs(LDot) / 36525.0;
                return n > 0.0 ? 360.0 / n : 0.0;
            }
        }
    }

    public static class OrbitalMechanics
    {
        /// <summary>Julian date of the J2000.0 epoch (2000-01-01 12:00 TT).</summary>
        public const double J2000 = 2451545.0;

        /// <summary>Julian date of the Unix epoch, for converting to and from wall-clock dates.</summary>
        public const double UnixEpochJd = 2440587.5;

        public const double AuKm = 149597870.7;
        public const double Deg2Rad = Math.PI / 180.0;
        public const double Rad2Deg = 180.0 / Math.PI;

        /// <summary>Obliquity of the ecliptic at J2000, in degrees.</summary>
        public const double EclipticObliquityDeg = 23.4392911;

        public static double SolveKepler(double M, double e)
        {
            // M in radians, already wrapped to [-pi, pi].
            double E = M + e * Math.Sin(M);
            for (int k = 0; k < 12; k++)
            {
                double d = (E - e * Math.Sin(E) - M) / (1.0 - e * Math.Cos(E));
                E -= d;
                if (Math.Abs(d) < 1e-13) break;
            }
            return E;
        }

        public static double WrapDeg180(double deg)
        {
            deg = deg % 360.0;
            if (deg > 180.0) deg -= 360.0;
            if (deg < -180.0) deg += 360.0;
            return deg;
        }

        public static double JulianDateFromUtc(DateTime utc)
        {
            DateTime u = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
            return UnixEpochJd + (u - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalDays;
        }

        public static DateTime UtcFromJulianDate(double jd)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(jd - UnixEpochJd);
        }

        /// <summary>
        /// Unit vector of a body's rotation pole, given its IAU J2000 pole in equatorial
        /// right ascension / declination, expressed in ecliptic Cartesian coordinates.
        /// </summary>
        public static Vec3d PoleFromRaDec(double raDeg, double decDeg)
        {
            double ra = raDeg * Deg2Rad;
            double dec = decDeg * Deg2Rad;
            double eps = EclipticObliquityDeg * Deg2Rad;

            // Equatorial unit vector.
            double qx = Math.Cos(dec) * Math.Cos(ra);
            double qy = Math.Cos(dec) * Math.Sin(ra);
            double qz = Math.Sin(dec);

            // Rotate the equatorial frame into the ecliptic frame about the vernal equinox.
            double ex = qx;
            double ey = qy * Math.Cos(eps) + qz * Math.Sin(eps);
            double ez = -qy * Math.Sin(eps) + qz * Math.Cos(eps);

            return new Vec3d(ex, ey, ez);
        }

        /// <summary>
        /// Unit normal of an orbit, in ecliptic Cartesian coordinates.
        /// h = (sin i sin Node, -sin i cos Node, cos i).
        /// </summary>
        public static Vec3d OrbitNormal(double inclinationDeg, double nodeDeg)
        {
            double i = inclinationDeg * Deg2Rad;
            double o = nodeDeg * Deg2Rad;
            return new Vec3d(Math.Sin(i) * Math.Sin(o), -Math.Sin(i) * Math.Cos(o), Math.Cos(i));
        }

        /// <summary>
        /// Maps an ecliptic-frame vector onto Unity's left-handed Y-up world.
        ///
        /// The mapping is (X, Y, Z)_ecliptic -> (x, z, -y)_unity. The sign flip on the last axis is
        /// what keeps the planets going anticlockwise as seen from ecliptic north: converting a
        /// right-handed frame into a left-handed one always mirrors one axis, and this choice
        /// mirrors the one that leaves the orbital direction looking correct on screen.
        /// </summary>
        public static UnityEngine.Vector3 ToUnity(Vec3d ecliptic, double unitsPerKm)
        {
            return new UnityEngine.Vector3(
                (float)(ecliptic.x * unitsPerKm),
                (float)(ecliptic.z * unitsPerKm),
                (float)(-ecliptic.y * unitsPerKm));
        }
    }
}
