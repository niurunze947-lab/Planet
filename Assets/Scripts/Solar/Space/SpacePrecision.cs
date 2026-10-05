using System;

namespace Planet.Solar
{
    /// <summary>Which IEEE-754 format a coordinate is stored in.</summary>
    public enum NumericFormat
    {
        /// <summary>
        /// 24-bit significand. Unity's <c>Vector3</c>, every <c>Transform</c>, every vertex buffer -
        /// i.e. everything the GPU ever sees.
        /// </summary>
        Float32 = 32,

        /// <summary>
        /// 53-bit significand. Every orbit, every frame origin and every terrain query. Never
        /// uploaded to the GPU directly.
        /// </summary>
        Float64 = 64
    }

    /// <summary>
    /// What one coordinate step is worth, in metres, at a given magnitude in a given format.
    ///
    /// This is the number that decides whether a feature is representable at all. A body at
    /// distance <c>d</c> from a frame's origin cannot be placed more accurately than
    /// <see cref="ulpMetres"/>, no matter how good the physics behind it is.
    /// </summary>
    public struct PrecisionBudget
    {
        public NumericFormat format;

        /// <summary>Distance from the frame origin, in metres. Precision is always about |x|, not about the span.</summary>
        public double magnitudeMetres;

        /// <summary>Smallest representable step at that magnitude, in metres.</summary>
        public double ulpMetres;

        /// <summary>Relative precision: <c>ulp / magnitude</c>. Constant per format for normal numbers.</summary>
        public double relative;

        /// <summary>Largest magnitude this format can hold at all (float.MaxValue or double.MaxValue, in metres).</summary>
        public double ceilingMetres;

        /// <summary>True when a feature of this size can be placed somewhere in the frame at all.</summary>
        public bool CanPlace(double featureMetres) { return featureMetres >= ulpMetres; }

        public override string ToString()
        {
            return string.Format("{0} @ {1:N3} m: ulp {2:N6} m ({3:E2} relative)",
                format, magnitudeMetres, ulpMetres, relative);
        }
    }

    /// <summary>
    /// The numeric budget of the two coordinate systems.
    ///
    /// The whole point of splitting space into a double-precision cosmic frame and a
    /// float-precision chunk-local surface frame is that neither format can span "a pebble to a
    /// galaxy" on its own: that is 26 orders of magnitude and float32 carries 7 digits while
    /// double carries 15.9. This class makes the arithmetic explicit instead of leaving it as
    /// folklore, and the EditMode tests assert the numbers below.
    ///
    /// Everything here is exact integer/bit arithmetic - no <c>Math.Log</c> approximations - so the
    /// audit tool reports the same step the hardware actually takes.
    /// </summary>
    public static class SpacePrecision
    {
        /// <summary>Significand bits including the implicit leading one: 24 for float32, 53 for float64.</summary>
        public static int SignificandBits(NumericFormat format) { return format == NumericFormat.Float32 ? 24 : 53; }

        /// <summary>Relative precision of the format: 2^(1-bits). 1.19e-7 for float32, 2.22e-16 for float64.</summary>
        public static double RelativePrecision(NumericFormat format)
        {
            return Math.Pow(2.0, 1 - SignificandBits(format));
        }

        /// <summary>Largest finite magnitude in the format. Beyond this the value becomes infinity.</summary>
        public static double CeilingMetres(NumericFormat format)
        {
            return format == NumericFormat.Float32 ? (double)float.MaxValue : double.MaxValue;
        }

        /// <summary>
        /// Exact binary exponent of a double, from the raw bits. Handles subnormals. Never uses a
        /// logarithm: <c>Math.Log(x, 2)</c> rounds exactly at powers of two, which is precisely where
        /// a precision ladder puts its boundaries.
        /// </summary>
        public static int BinaryExponent(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(Math.Abs(value));
            int raw = (int)((bits >> 52) & 0x7FFL);
            if (raw != 0) return raw - 1023;

            // Subnormal: normalise the fraction by hand.
            long mantissa = bits & 0xFFFFFFFFFFFFFL;
            int exponent = -1074;
            while (mantissa != 0L && (mantissa & 0x10000000000000L) == 0L)
            {
                mantissa <<= 1;
                exponent++;
            }
            return exponent;
        }

        /// <summary>
        /// Exact spacing between adjacent representable values at <paramref name="magnitudeMetres"/>.
        /// For a subnormal result this is the smallest subnormal step, which is the correct answer.
        /// </summary>
        public static double ExactUlp(double magnitudeMetres, NumericFormat format)
        {
            double m = Math.Abs(magnitudeMetres);
            if (m == 0.0) return Math.Pow(2.0, -1074 + (format == NumericFormat.Float32 ? 0 : 0));

            // float32 has 8 exponent bits: below 2^-126 the step stops shrinking.
            int floorExponent = format == NumericFormat.Float32 ? -126 : -1022;
            int exponent = Math.Max(BinaryExponent(m), floorExponent);
            int stepExponent = exponent - (SignificandBits(format) - 1);

            if (format == NumericFormat.Float32 && stepExponent < -149) stepExponent = -149;
            if (format == NumericFormat.Float64 && stepExponent < -1074) stepExponent = -1074;
            return Math.Pow(2.0, stepExponent);
        }

        /// <summary>Budget at a distance from the frame origin, in metres.</summary>
        public static PrecisionBudget BudgetAt(double magnitudeMetres, NumericFormat format)
        {
            double m = Math.Abs(magnitudeMetres);
            return new PrecisionBudget
            {
                format = format,
                magnitudeMetres = m,
                ulpMetres = ExactUlp(m, format),
                relative = RelativePrecision(format),
                ceilingMetres = CeilingMetres(format)
            };
        }

        /// <summary>Measures the true step by incrementing the raw bits - used by the tests and the audit tool.</summary>
        public static double MeasuredFloatUlp(float value)
        {
            float next = BitConverter.ToSingle(BitConverter.GetBytes(
                BitConverter.ToInt32(BitConverter.GetBytes(value), 0) + 1), 0);
            return (double)next - (double)value;
        }

        /// <summary>Measures the true step by incrementing the raw bits - used by the tests and the audit tool.</summary>
        public static double MeasuredDoubleUlp(double value)
        {
            double next = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value) + 1L);
            return next - value;
        }

        /// <summary>
        /// True when <paramref name="featureMetres"/> is resolvable at <paramref name="distanceMetres"/>
        /// from the frame origin in <paramref name="format"/>. This is the single question that decides
        /// which of the two coordinate systems a piece of geometry belongs in.
        /// </summary>
        public static bool CanRepresent(double featureMetres, double distanceMetres, NumericFormat format)
        {
            return Math.Abs(featureMetres) >= ExactUlp(distanceMetres, format);
        }

        /// <summary>
        /// How many nested frames are needed to carry <paramref name="requiredPrecisionMetres"/>
        /// across <paramref name="spanMetres"/>, using one format per level.
        ///
        /// One level holds <c>span x relative</c>. Each further level re-centres, so it only has to
        /// cover the previous level's residual error, which multiplies the usable depth by
        /// <c>1/relative</c> every time.
        /// </summary>
        public static int LevelsRequired(double spanMetres, double requiredPrecisionMetres, NumericFormat format)
        {
            if (!(spanMetres > 0.0) || !(requiredPrecisionMetres > 0.0)) return 0;

            double relative = RelativePrecision(format);
            int levels = 1;
            double reach = spanMetres;
            while (reach * relative > requiredPrecisionMetres && levels < 64)
            {
                levels++;
                // A re-centred frame only has to resolve the previous frame's own step.
                reach = reach * relative;
            }
            return levels;
        }

        /// <summary>
        /// The finest surface chunk level whose float32 vertices still resolve
        /// <paramref name="requiredPrecisionMetres"/>, given a body radius.
        ///
        /// A chunk covers <c>radius x pi/2 / 2^level</c> metres; storing its vertices as floats
        /// relative to the chunk origin costs <c>chunkSize x 1.19e-7</c>.
        /// </summary>
        public static int ChunkLevelForPrecision(double bodyRadiusMetres, double requiredPrecisionMetres)
        {
            if (!(bodyRadiusMetres > 0.0) || !(requiredPrecisionMetres > 0.0)) return 0;
            double relative = RelativePrecision(NumericFormat.Float32);
            double maxChunkSize = requiredPrecisionMetres / relative;
            double faceArc = bodyRadiusMetres * Math.PI * 0.5;

            int level = 0;
            while (level < 30 && faceArc / (1 << level) > maxChunkSize) level++;
            return level;
        }

        /// <summary>Kilometres / AU / light-year formatting shared with the HUD and the audit tool.</summary>
        public static string FormatMetres(double metres)
        {
            double a = Math.Abs(metres);
            if (a >= 9.4607e15) return string.Format("{0:N4} ly", metres / 9.4607e15);
            if (a >= 1.495978707e11) return string.Format("{0:N4} AU", metres / 1.495978707e11);
            if (a >= 1e9) return string.Format("{0:N3} million km", metres / 1e9);
            if (a >= 1e3) return string.Format("{0:N1} km", metres / 1e3);
            if (a >= 1.0) return string.Format("{0:N3} m", metres);
            if (a >= 1e-2) return string.Format("{0:N3} cm", metres * 100.0);
            if (a >= 1e-3) return string.Format("{0:N3} mm", metres * 1000.0);
            if (a >= 1e-6) return string.Format("{0:N3} um", metres * 1e6);
            return string.Format("{0:E3} m", metres);
        }

        /// <summary>
        /// The largest distance from a frame origin at which <paramref name="featureMetres"/> is still
        /// representable in <paramref name="format"/>, i.e. where the step no longer exceeds it.
        ///
        /// This is the bound that matters when choosing a rung: float32 holds 1 m features out to
        /// 2^23 m (8,389 km), which is why a planet's surface cannot be stored in one float32 frame but
        /// a 38 m chunk can.
        /// </summary>
        public static double SpanForFeature(double featureMetres, NumericFormat format)
        {
            if (!(featureMetres > 0.0)) return 0.0;
            return featureMetres * Math.Pow(2.0, SignificandBits(format) - 1);
        }
    }
}
