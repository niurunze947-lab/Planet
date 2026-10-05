using System;
using UnityEngine;

namespace Planet.Procedural
{
    /// <summary>SI Planck radiance and CIE 1931 XYZ -> linear sRGB. No hardcoded solar hue.</summary>
    public static class StellarRadiation
    {
        public const int SpectrumVersion = 1;
        public const double StefanBoltzmann = 5.670374419e-8;
        public const float LutMinimumKelvin = 2500f, LutMaximumKelvin = 9000f;

        public static double SpectralRadiance(double wavelengthMetres, double kelvin)
        {
            if (wavelengthMetres <= 0 || kelvin <= 0) throw new ArgumentOutOfRangeException();
            double exponent = 0.01438776877 / (wavelengthMetres * kelvin);
            return exponent > 700 ? 0 : 1.191042972e-16 / (Math.Pow(wavelengthMetres, 5) * (Math.Exp(exponent) - 1.0));
        }
        public static double SurfaceFlux(double kelvin) { return StefanBoltzmann * Math.Pow(kelvin, 4); }

        public static Color LinearRgb(double kelvin)
        {
            double x = 0, y = 0, z = 0;
            for (int nm = 380; nm <= 780; nm += 5)
            {
                double radiance = SpectralRadiance(nm * 1e-9, kelvin);
                // Wyman, Sloan & Shirley (2013), analytic fits to the CIE 1931 2-degree CMFs.
                double cx = 0.362 * Gaussian(nm, 442, 0.0624, 0.0374) + 1.056 * Gaussian(nm, 599.8, 0.0264, 0.0323)
                    - 0.065 * Gaussian(nm, 501.1, 0.0490, 0.0382);
                double cy = 0.821 * Gaussian(nm, 568.8, 0.0213, 0.0247) + 0.286 * Gaussian(nm, 530.9, 0.0613, 0.0322);
                double cz = 1.217 * Gaussian(nm, 437, 0.0845, 0.0278) + 0.681 * Gaussian(nm, 459, 0.0385, 0.0725);
                double weight = nm == 380 || nm == 780 ? 0.5 : 1;
                x += radiance * cx * weight; y += radiance * cy * weight; z += radiance * cz * weight;
            }
            x /= y; z /= y; // chromaticity at unit photopic luminance; bolometric T^4 is applied separately.
            return new Color((float)Math.Max(0, 3.2406 * x - 1.5372 - 0.4986 * z),
                (float)Math.Max(0, -0.9689 * x + 1.8758 + 0.0415 * z),
                (float)Math.Max(0, 0.0557 * x - 0.2040 + 1.0570 * z), 1f);
        }
        private static double Gaussian(double wavelength, double centre, double left, double right)
        {
            double t = (wavelength - centre) * (wavelength < centre ? left : right);
            return Math.Exp(-0.5 * t * t);
        }
        public static double RotationPeriodDays(StellarDefinition d, double latitudeRadians)
        {
            double latitudeFactor = Math.Sin(latitudeRadians); latitudeFactor *= latitudeFactor;
            double rate = (1 - latitudeFactor) / d.equatorialRotationDays + latitudeFactor / d.polarRotationDays;
            return 1.0 / rate;
        }
        public static float LimbIntensity(StellarDefinition d, float mu)
        {
            float edge = 1f - Mathf.Clamp01(mu);
            return 1f - d.limbLinear * edge - d.limbQuadratic * edge * edge;
        }
    }
}
