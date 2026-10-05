using System;
using NUnit.Framework;
using Planet.Solar;

/// <summary>
/// The numeric facts the two-coordinate-system design rests on.
///
/// Every assertion here is a hard number, not an inequality that happens to hold: these are the
/// figures quoted in PLANET-SPACE.md and in the audit tool's report, so if a future change moves one
/// of them the test has to fail rather than the document quietly going stale.
/// </summary>
public sealed class SpacePrecisionTests
{
    private const double EarthRadiusMetres = 6371000.0;
    private const double AuMetres = 1.495978707e11;
    private const double LightYearMetres = 9.4607304725808e15;

    [Test]
    public void SignificandBitsAreTheRealIeeeWidths()
    {
        Assert.AreEqual(24, SpacePrecision.SignificandBits(NumericFormat.Float32));
        Assert.AreEqual(53, SpacePrecision.SignificandBits(NumericFormat.Float64));
        Assert.AreEqual(7.2, Math.Log10(Math.Pow(2.0, 24)), 0.05, "float32 carries about 7 decimal digits");
        Assert.AreEqual(15.95, Math.Log10(Math.Pow(2.0, 53)), 0.01, "float64 carries about 16 decimal digits");
    }

    [Test]
    public void RelativePrecisionMatchesTwoToTheOneMinusBits()
    {
        Assert.AreEqual(Math.Pow(2.0, -23), SpacePrecision.RelativePrecision(NumericFormat.Float32));
        Assert.AreEqual(Math.Pow(2.0, -52), SpacePrecision.RelativePrecision(NumericFormat.Float64));
    }

    [Test]
    public void ExactUlpAgreesWithTheHardwareAtEveryRungOfTheSolarSystem()
    {
        // The point of ExactUlp is that it never calls Math.Log, which rounds exactly at the powers of
        // two where a precision ladder puts its boundaries. So it has to agree exactly with a
        // measurement that increments the raw bits - the definition of the step rather than a model of
        // it. Exact, not approximate: both sides are powers of two and the subtraction is exact.
        for (int exponent = -20; exponent <= 40; exponent++)
        {
            double magnitude = Math.Pow(2.0, exponent);

            Assert.AreEqual(SpacePrecision.MeasuredFloatUlp((float)magnitude),
                            SpacePrecision.ExactUlp(magnitude, NumericFormat.Float32),
                            "float32 step at 2^" + exponent);

            Assert.AreEqual(SpacePrecision.MeasuredDoubleUlp(magnitude),
                            SpacePrecision.ExactUlp(magnitude, NumericFormat.Float64),
                            "float64 step at 2^" + exponent);
        }
    }

    [Test]
    public void ExactUlpIsExactlyTwiceAsLargeOneBitAbove()
    {
        // A step function that doubles at every power of two is the whole reason a ladder works:
        // one rung down halves the step, so it buys exactly one bit.
        for (int exponent = -100; exponent <= 100; exponent++)
        {
            double below = SpacePrecision.ExactUlp(Math.Pow(2.0, exponent), NumericFormat.Float32);
            double at = SpacePrecision.ExactUlp(Math.Pow(2.0, exponent + 1), NumericFormat.Float32);
            Assert.AreEqual(below * 2.0, at, "step doubling at 2^" + exponent);
        }
    }

    [Test]
    public void EarthRadiusFloat32StepIsUnderHalfAMetre()
    {
        // 1 scene unit is 1e9 m at kmPerUnit = 1e6, so the body's render radius is 0.006371 units and
        // the float32 step in METRES on that value is 4.66e-10 units = 0.4657 m. That is the number a
        // unit-sphere globe is stuck with, and it is why terrain needs chunk-local coordinates.
        double stepUnits = SpacePrecision.MeasuredFloatUlp((float)(EarthRadiusMetres / 1e9));
        double stepMetres = stepUnits * 1e9;

        Assert.AreEqual(0.4657, stepMetres, 0.001);
        Assert.IsTrue(stepMetres > 0.4, "the globe representation cannot do better than ~0.47 m at Earth");
    }

    [Test]
    public void SolarSystemScaleNeedsOneRungAboveTheBody()
    {
        // The span a body's own frame has to cover is its heliocentric distance (50 AU for the Kuiper
        // belt). Asking for 1 cm there is exactly what a chunk-local surface frame then supplies.
        int required = SpacePrecision.LevelsRequired(50.0 * AuMetres, 0.01, NumericFormat.Float64);
        Assert.AreEqual(1, required, "50 AU / 1 cm fits in one re-centred double frame");

        // And the whole Milky Way does not, in either format.
        Assert.AreEqual(4, SpacePrecision.LevelsRequired(1e5 * LightYearMetres, 0.01, NumericFormat.Float32));
        Assert.AreEqual(2, SpacePrecision.LevelsRequired(1e5 * LightYearMetres, 0.01, NumericFormat.Float64));
    }

    [Test]
    public void SpanForFeatureIsTheBoundaryWhereTheStepExceedsTheFeature()
    {
        double span = SpacePrecision.SpanForFeature(1.0, NumericFormat.Float32);
        Assert.AreEqual(8388608.0, span, 1.0, "float32 holds 1 m features out to 2^23 m = 8,389 km");

        // Just inside the boundary the step is at or below the feature; just outside it is not.
        Assert.IsTrue(SpacePrecision.CanRepresent(1.0, span, NumericFormat.Float32));
        Assert.IsFalse(SpacePrecision.CanRepresent(1.0, span * 4.0, NumericFormat.Float32));
    }

    [Test]
    public void CanRepresentIsTheQuestionThatPicksTheCoordinateSystem()
    {
        // A 1 m rock at 50 AU in one double frame: fine.
        Assert.IsTrue(SpacePrecision.CanRepresent(1.0, 50.0 * AuMetres, NumericFormat.Float64));

        // The same rock in a float32 frame anchored on the Sun: the step there is 1.2 km.
        Assert.IsFalse(SpacePrecision.CanRepresent(1.0, 50.0 * AuMetres, NumericFormat.Float32));

        // Chunk-local float32 on Earth: fine, with room to spare.
        Assert.IsTrue(SpacePrecision.CanRepresent(1.0, 38.0, NumericFormat.Float32));
    }

    [Test]
    public void ChunkLevelForPrecisionMatchesTheMeasuredLadder()
    {
        // These are the levels the audit tool prints for Earth; a 1 cm feature is reachable at L7 and a
        // 1 mm feature at L11. Both are far coarser than the RESOLUTION ceiling, which is a different
        // and much more expensive question - the test pins the precision one.
        Assert.AreEqual(7, SpacePrecision.ChunkLevelForPrecision(EarthRadiusMetres, 0.01));
        Assert.AreEqual(11, SpacePrecision.ChunkLevelForPrecision(EarthRadiusMetres, 0.001));
        Assert.AreEqual(14, SpacePrecision.ChunkLevelForPrecision(EarthRadiusMetres, 0.0001));

        // And the claim those levels make is verifiable directly.
        for (int level = 0; level <= 20; level++)
        {
            double chunkMetres = EarthRadiusMetres * Math.PI * 0.5 / Math.Pow(2.0, level);
            double stepMetres = SpacePrecision.ExactUlp(chunkMetres, NumericFormat.Float32);
            bool expectOneCentimetre = level >= 7;
            Assert.AreEqual(expectOneCentimetre, stepMetres <= 0.01,
                            "float32 step inside a level-" + level + " chunk (" + chunkMetres + " m)");
        }
    }

    [Test]
    public void FormatMetresStaysReadableAcrossTheWholeRange()
    {
        // Only the unit is asserted: the numeric part goes through the current culture's group
        // separator, which is not what this test is about.
        Assert.IsTrue(SpacePrecision.FormatMetres(EarthRadiusMetres).EndsWith("km"));
        Assert.IsTrue(SpacePrecision.FormatMetres(50.0 * AuMetres).EndsWith("AU"));
        Assert.IsTrue(SpacePrecision.FormatMetres(1e5 * LightYearMetres).EndsWith("ly"));
        Assert.IsTrue(SpacePrecision.FormatMetres(0.5).EndsWith("cm"));
        Assert.IsTrue(SpacePrecision.FormatMetres(0.005).EndsWith("mm"));
        Assert.IsTrue(SpacePrecision.FormatMetres(5e-6).EndsWith("um"));
    }
}
