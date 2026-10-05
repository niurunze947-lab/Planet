using Unity.Mathematics;

namespace Planet.Procedural.Terrain
{
    /// <summary>
    /// Band-limited fine relief evaluated in double body coordinates (metres), so octaves down to
    /// centimetres stay exact at any planet radius. Octave k has wavelength 4096 m / 2^k. Octaves
    /// shorter than twice the vertex spacing are skipped and the next octave up fades in with
    /// smoothstep(2s, 4s, wavelength), so a level never aliases and the parent band is a strict
    /// subset of the child band. Blittable and exception free for Burst.
    /// </summary>
    public readonly struct PlanetDetailSampler
    {
        public const int GeneratorVersion = 2;
        public const double BaseWavelengthMetres = 4096.0;
        public const int MaxOctaves = 24;
        /// <summary>Normalises the 0.5-radius simplex kernel to about [-1, 1] (worst case 0.99).</summary>
        private const double NoiseScale = 76.0;
        /// <summary>Margin on the noise bound used by the analytic amplitude sums.</summary>
        private const double NoiseBound = 1.25;
        /// <summary>Worst triangle interpolation error of one unit octave, per (spacing / wavelength)^2.</summary>
        private const double CurvatureCoefficient = 24.0;

        public readonly ulong seed;
        public readonly double amplitudeMetres;
        public readonly double gain;
        public readonly int octaveCount;

        public PlanetDetailSampler(int generationSeed, double amplitudeMetres, double gain, double minimumWavelengthMetres)
        {
            seed = Mix(unchecked((ulong)(uint)generationSeed) ^ 0x5DEECE66DUL);
            this.amplitudeMetres = math.max(0.0, amplitudeMetres);
            this.gain = math.clamp(gain, 0.3, 0.75);
            // Never include a wavelength shorter than the configured cutoff.
            double octaves = math.floor(math.log2(BaseWavelengthMetres / math.max(minimumWavelengthMetres, 1e-3))) + 1.0;
            octaveCount = this.amplitudeMetres > 0.0 ? (int)math.clamp(octaves, 0.0, MaxOctaves) : 0;
        }

        public static PlanetDetailSampler FromDefinition(PlanetDefinition definition)
        {
            PlanetDetailParameters p = definition.detail;
            if (p == null || !p.enabled) return new PlanetDetailSampler(definition.generation.seed, 0.0, 0.55, 1.0);
            return new PlanetDetailSampler(definition.generation.seed, p.amplitudeMetres, p.gain, p.minimumWavelengthMetres);
        }

        public bool IsEnabled => octaveCount > 0;

        /// <summary>Upper bound of |height| over all octaves.</summary>
        public double AmplitudeBound
        {
            get
            {
                double sum = 0.0, amplitude = amplitudeMetres;
                for (int k = 0; k < octaveCount; k++) { sum += amplitude; amplitude *= gain; }
                return NoiseBound * sum;
            }
        }

        /// <summary>Detail height for a vertex spacing s at body point p (metres).</summary>
        public double Evaluate(double3 p, double spacing)
        {
            Evaluate2(p, spacing, spacing, out double own, out _);
            return own;
        }

        /// <summary>
        /// Own and parent band in one octave loop. The parent result is bit-identical to
        /// Evaluate(p, parentSpacing) because skipped and zero-weight octaves add exact zeros.
        /// </summary>
        public void Evaluate2(double3 p, double ownSpacing, double parentSpacing, out double own, out double parent)
        {
            own = 0.0;
            parent = 0.0;
            if (!math.all(math.isfinite(p))) return;
            ownSpacing = SafeSpacing(ownSpacing);
            parentSpacing = SafeSpacing(parentSpacing);
            double finestSpacing = math.min(ownSpacing, parentSpacing);
            double amplitude = amplitudeMetres, wavelength = BaseWavelengthMetres;
            for (int k = 0; k < octaveCount; k++)
            {
                if (wavelength <= 2.0 * finestSpacing) break;
                double wOwn = BandWeight(wavelength, ownSpacing);
                double wParent = BandWeight(wavelength, parentSpacing);
                double n = Noise(p / wavelength, OctaveSeed(k)) * amplitude;
                own += wOwn * n;
                parent += wParent * n;
                amplitude *= gain;
                wavelength *= 0.5;
            }
        }

        /// <summary>1 when the octave is fully resolved by spacing s, 0 at or below the Nyquist limit.</summary>
        public static double BandWeight(double wavelength, double spacing)
        {
            spacing = SafeSpacing(spacing);
            double t = math.saturate((wavelength - 2.0 * spacing) / (2.0 * spacing));
            return t * t * (3.0 - 2.0 * t);
        }

        /// <summary>
        /// Bound on |full detail - linear interpolation of the spacing-s band| between vertices:
        /// unresolved and faded octaves count fully, resolved ones by their curvature error.
        /// </summary>
        public double ResidualBound(double spacing)
        {
            spacing = SafeSpacing(spacing);
            double sum = 0.0, amplitude = amplitudeMetres, wavelength = BaseWavelengthMetres;
            for (int k = 0; k < octaveCount; k++)
            {
                double w = wavelength <= 2.0 * spacing ? 0.0 : BandWeight(wavelength, spacing);
                double ratio = spacing / wavelength;
                sum += amplitude * ((1.0 - w) + w * math.min(2.0, CurvatureCoefficient * ratio * ratio));
                amplitude *= gain;
                wavelength *= 0.5;
            }
            return NoiseBound * sum;
        }

        private static double SafeSpacing(double spacing)
        {
            return math.isfinite(spacing) ? math.max(spacing, 1e-6) : BaseWavelengthMetres;
        }

        private ulong OctaveSeed(int octave)
        {
            return Mix(seed + (ulong)(octave + 1) * 0x9E3779B97F4A7C15UL);
        }

        /// <summary>3D simplex noise with a 0.5 kernel radius and 12 cube-edge gradients, in double.</summary>
        public static double Noise(double3 p, ulong octaveSeed)
        {
            const double F3 = 1.0 / 3.0, G3 = 1.0 / 6.0;
            double s = (p.x + p.y + p.z) * F3;
            double fi = math.floor(p.x + s), fj = math.floor(p.y + s), fk = math.floor(p.z + s);
            double t = (fi + fj + fk) * G3;
            double3 x0 = new double3(p.x - (fi - t), p.y - (fj - t), p.z - (fk - t));
            long i = (long)fi, j = (long)fj, k = (long)fk;

            int3 o1, o2;
            if (x0.x >= x0.y)
            {
                if (x0.y >= x0.z) { o1 = new int3(1, 0, 0); o2 = new int3(1, 1, 0); }
                else if (x0.x >= x0.z) { o1 = new int3(1, 0, 0); o2 = new int3(1, 0, 1); }
                else { o1 = new int3(0, 0, 1); o2 = new int3(1, 0, 1); }
            }
            else
            {
                if (x0.y < x0.z) { o1 = new int3(0, 0, 1); o2 = new int3(0, 1, 1); }
                else if (x0.x < x0.z) { o1 = new int3(0, 1, 0); o2 = new int3(0, 1, 1); }
                else { o1 = new int3(0, 1, 0); o2 = new int3(1, 1, 0); }
            }

            double sum = Corner(x0, i, j, k, octaveSeed);
            sum += Corner(x0 - (double3)o1 + G3, i + o1.x, j + o1.y, k + o1.z, octaveSeed);
            sum += Corner(x0 - (double3)o2 + 2.0 * G3, i + o2.x, j + o2.y, k + o2.z, octaveSeed);
            sum += Corner(x0 - 1.0 + 3.0 * G3, i + 1, j + 1, k + 1, octaveSeed);
            return NoiseScale * sum;
        }

        private static double Corner(double3 x, long i, long j, long k, ulong octaveSeed)
        {
            double t = 0.5 - math.dot(x, x);
            if (t <= 0.0) return 0.0;
            t *= t;
            return t * t * Gradient(Hash(i, j, k, octaveSeed), x);
        }

        private static double Gradient(ulong hash, double3 x)
        {
            switch ((int)(hash % 12UL))
            {
                case 0: return x.x + x.y;
                case 1: return -x.x + x.y;
                case 2: return x.x - x.y;
                case 3: return -x.x - x.y;
                case 4: return x.x + x.z;
                case 5: return -x.x + x.z;
                case 6: return x.x - x.z;
                case 7: return -x.x - x.z;
                case 8: return x.y + x.z;
                case 9: return -x.y + x.z;
                case 10: return x.y - x.z;
                default: return -x.y - x.z;
            }
        }

        private static ulong Hash(long i, long j, long k, ulong octaveSeed)
        {
            unchecked
            {
                ulong h = Mix(octaveSeed ^ ((ulong)i * 0x8DA6B343UL));
                h = Mix(h ^ ((ulong)j * 0xD8163841UL));
                return Mix(h ^ ((ulong)k * 0xCB1AB31FUL));
            }
        }

        /// <summary>splitmix64 finaliser.</summary>
        private static ulong Mix(ulong z)
        {
            unchecked
            {
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
