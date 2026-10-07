using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Planet.EditorTools
{
    /// <summary>
    /// Bakes deep-water ocean spectra into tiling flow maps for <c>Planet/Ocean</c>.
    /// <para>
    /// Each layer is one Phillips/JONSWAP-shaped spectrum on a periodic patch, so the result is a
    /// **tileable** patch of a real sea surface rather than hand-painted noise: the height field is
    /// the inverse FFT of the spectrum with Hermitian symmetry, normals are the central differences
    /// of that field, and whitecaps come from the field's own steepness. The wind speed is not a
    /// free knob — it is derived from the requested significant wave height through the
    /// fully-developed-sea relation, which also sets the flow speed (peak phase speed = wind speed).
    /// </para>
    /// <para>
    /// Motion is produced by scrolling these patches in the shader, so a linear scroll never pops.
    /// Channels: R/G tangent normal xy, B signed height over ±3σ, A crest whitecaps.
    /// This file is the single owner of the bake parameters; the material only receives the result.
    /// </para>
    /// </summary>
    public static class OceanWaveTextureBuilder
    {
        public const string Folder = "Assets/Resources/Solar/Ocean";
        public const string MaterialPath = "Assets/Resources/Solar/OceanSurface.mat";
        public const int Resolution = 256;
        /// <summary>Bump to force every layer to be baked again.</summary>
        private const string KeyPrefix = "PlanetWave:2:";
        private const double Gravity = 9.80665;
        /// <summary>Fully developed sea: significant height = 0.21 V² / g.</summary>
        private const double SignificantHeightCoefficient = 0.21;
        private const int SeedBase = 20260707;

        private sealed class Layer
        {
            public string file;
            /// <summary>Metres covered by one tile. Sets the smallest resolved wavelength.</summary>
            public float patchMetres;
            public float significantHeightMetres;
            /// <summary>Isotropic floor of |k̂·ŵ|²; 0.5 is nearly omnidirectional.</summary>
            public float directionalBias;
            /// <summary>Steepness (in RMS slopes) where whitecaps start.</summary>
            public float crestSteepness;
        }

        private static readonly Layer[] Layers =
        {
            new Layer { file = "OceanWaveSwell", patchMetres = 512f, significantHeightMetres = 1.6f, directionalBias = 0.22f, crestSteepness = 1.7f },
            new Layer { file = "OceanWaveWindSea", patchMetres = 128f, significantHeightMetres = 0.7f, directionalBias = 0.32f, crestSteepness = 1.6f },
            new Layer { file = "OceanWaveChop", patchMetres = 32f, significantHeightMetres = 0.25f, directionalBias = 0.45f, crestSteepness = 1.5f }
        };

        [MenuItem("Tools/Planet/Build Ocean Wave Textures", false, 26)]
        public static void Build()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Bake ocean waves outside Play mode.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) throw new InvalidOperationException("Missing material " + MaterialPath);
            if (!material.HasProperty("_WaveNormal0"))
                throw new InvalidOperationException("Planet/Ocean has no FFT wave properties. Reimport the shader first.");
            Directory.CreateDirectory(Folder);
            for (int i = 0; i < Layers.Length; i++) BakeLayer(i, material);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Planet waves] Baked " + Layers.Length + " FFT ocean layers into " + Folder + " and bound them to " + MaterialPath + ".");
        }

        private static void BakeLayer(int index, Material material)
        {
            Layer layer = Layers[index];
            // Native-format texture assets keep their own wrap/filter/linear settings; a real image
            // extension would hand them to a TextureImporter that overwrites them on every reimport.
            string path = Folder + "/" + layer.file + ".asset";
            string key = KeyPrefix + index.ToString(CultureInfo.InvariantCulture) + ":" +
                layer.patchMetres.ToString(CultureInfo.InvariantCulture) + ":" +
                layer.significantHeightMetres.ToString(CultureInfo.InvariantCulture) + ":" +
                layer.directionalBias.ToString(CultureInfo.InvariantCulture) + ":" +
                layer.crestSteepness.ToString(CultureInfo.InvariantCulture) + ":" + Resolution;

            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (existing != null && existing.width == Resolution && importer != null && importer.userData == key)
            {
                Bind(material, index, layer, existing);
                return;
            }

            float[] height = HeightField(layer, index, out double sigma);
            Color32[] pixels = Encode(layer, height, sigma, out float slopeDegrees, out float foamCoverage);
            double windSpeed = Math.Sqrt(layer.significantHeightMetres * Gravity / SignificantHeightCoefficient);

            Texture2D next = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, true, true)
            {
                name = layer.file, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4
            };
            next.SetPixels32(pixels);
            next.Apply(true, true); // Derived cache: no readable CPU copy is kept.
            if (existing != null)
            {
                EditorUtility.CopySerialized(next, existing);
                Object.DestroyImmediate(next);
                EditorUtility.SetDirty(existing);
                next = existing;
            }
            else AssetDatabase.CreateAsset(next, path);
            AssetDatabase.SaveAssets();

            TextureImporter textureImporter = AssetImporter.GetAtPath(path) as TextureImporter;
            if (textureImporter != null)
            {
                textureImporter.textureType = TextureImporterType.Default;
                textureImporter.sRGBTexture = false; // Wave data, not colour.
                textureImporter.mipmapEnabled = true;
                textureImporter.filterMode = FilterMode.Trilinear;
                textureImporter.anisoLevel = 4;
                // Per-axis wrap: the single wrapMode field is overwritten by the next reimport.
                textureImporter.wrapModeU = TextureWrapMode.Repeat;
                textureImporter.wrapModeV = TextureWrapMode.Repeat;
                textureImporter.wrapModeW = TextureWrapMode.Repeat;
                textureImporter.userData = key;
                textureImporter.SaveAndReimport();
            }
            Bind(material, index, layer, AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[Planet waves] {0}: patch {1} m, Hs {2} m, wind {3:F1} m/s, 3\u03c3 {4:F3} m, {5} px/tile, " +
                "slope {6:F1}\u00b0 rms, foam cover {7:P2}.",
                layer.file, layer.patchMetres, layer.significantHeightMetres, windSpeed, 3.0 * sigma, Resolution,
                slopeDegrees, foamCoverage));
        }

        private static void Bind(Material material, int index, Layer layer, Texture2D texture)
        {
            if (texture == null) return;
            double windSpeed = Math.Sqrt(layer.significantHeightMetres * Gravity / SignificantHeightCoefficient);
            material.SetTexture("_WaveNormal" + index, texture);
            material.SetFloat("_WavePatch" + index, layer.patchMetres);
            material.SetFloat("_WaveFlow" + index, (float)windSpeed);
            // Height channel is stored as +/- 3 sigma, and the bake normalises 4 sigma to the
            // significant height, so this is exactly 3/4 of it.
            material.SetFloat("_WaveAmplitude" + index, layer.significantHeightMetres * 0.75f);
        }

        /// <summary>Periodic inverse FFT of a Phillips spectrum sampled at t = 0.</summary>
        private static float[] HeightField(Layer layer, int index, out double sigma)
        {
            int n = Resolution;
            double patch = layer.patchMetres;
            double wind = Math.Sqrt(layer.significantHeightMetres * Gravity / SignificantHeightCoefficient);
            double largest = wind * wind / Gravity;   // Phillips' largest wave L = V²/g
            double suppression = 2.0 * patch / n;     // Attenuate what the grid cannot resolve.
            double bias = layer.directionalBias;
            var re = new double[n * n];
            var im = new double[n * n];
            var random = new System.Random(SeedBase + index * 7919);

            for (int y = 0; y < n; y++)
            {
                double kz = WaveNumber(y, n, patch);
                for (int x = 0; x <= n / 2; x++)
                {
                    double kx = WaveNumber(x, n, patch);
                    int i = y * n + x;
                    double k2 = kx * kx + kz * kz;
                    if (k2 > 0.0)
                    {
                        double k = Math.Sqrt(k2);
                        double facing = kx / k;                       // Wind runs along +u.
                        double directional = facing * facing * (1.0 - bias) + bias;
                        double spectrum = Math.Exp(-1.0 / (k2 * largest * largest)) / (k2 * k2) *
                            directional * Math.Exp(-k2 * suppression * suppression);
                        double amplitude = Math.Sqrt(Math.Max(spectrum, 0.0) * 0.5);
                        re[i] = Gaussian(random) * amplitude;
                        im[i] = Gaussian(random) * amplitude;
                    }
                    // The four self-conjugate bins must be real for a real-valued field.
                    if ((x == 0 || x == n / 2) && (y == 0 || y == n / 2)) im[i] = 0.0;
                    int mx = (n - x) % n, my = (n - y) % n;
                    if (mx != x || my != y) { re[my * n + mx] = re[i]; im[my * n + mx] = -im[i]; }
                }
            }

            Fft2D(re, im, n, true);
            var height = new float[n * n];
            double mean = 0.0;
            for (int i = 0; i < height.Length; i++) { height[i] = (float)re[i]; mean += height[i]; }
            mean /= height.Length;
            double variance = 0.0;
            for (int i = 0; i < height.Length; i++)
            {
                height[i] -= (float)mean;
                variance += height[i] * (double)height[i];
            }
            sigma = Math.Sqrt(variance / height.Length);
            // The Phillips shape carries an arbitrary amplitude, so normalise it to the requested
            // significant height (4 sigma). Everything downstream is in metres of a fully developed
            // sea: the geometric displacement, the shoaling, and the height the shore train breaks at.
            // Slopes scale with it, so this is also what makes the baked normals carry real tilt.
            if (sigma > 1e-9)
            {
                double target = layer.significantHeightMetres * 0.25;
                float gain = (float)(target / sigma);
                for (int i = 0; i < height.Length; i++) height[i] *= gain;
                sigma = target;
            }
            return height;
        }

        private static Color32[] Encode(Layer layer, float[] height, double sigma, out float slopeDegrees, out float foamCoverage)
        {
            int n = Resolution;
            double texel = layer.patchMetres / n;
            double scale = Math.Max(3.0 * sigma, 1e-6);
            var normalX = new float[n * n];
            var normalY = new float[n * n];
            var steepness = new float[n * n];
            double sumSquares = 0.0;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                double dhdx = (height[y * n + (x + 1) % n] - height[y * n + (x + n - 1) % n]) / (2.0 * texel);
                double dhdz = (height[((y + 1) % n) * n + x] - height[((y + n - 1) % n) * n + x]) / (2.0 * texel);
                double inv = 1.0 / Math.Sqrt(dhdx * dhdx + dhdz * dhdz + 1.0);
                normalX[i] = (float)(-dhdx * inv);
                normalY[i] = (float)(-dhdz * inv);
                steepness[i] = (float)Math.Sqrt(dhdx * dhdx + dhdz * dhdz);
                sumSquares += steepness[i] * (double)steepness[i];
            }
            float rms = (float)Math.Sqrt(sumSquares / steepness.Length);
            float low = Math.Max(rms * layer.crestSteepness, 1e-5f);
            var foam = new float[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                // Whitecaps live on steep crest faces, not in the troughs.
                float crest = Smooth(0.0f, (float)(1.2 * sigma), height[i]);
                foam[i] = Smooth(low, low * 2.0f, steepness[i]) * crest;
            }
            Blur(foam, n);

            var pixels = new Color32[n * n];
            double coverage = 0.0;
            for (int i = 0; i < pixels.Length; i++)
            {
                float stored = Mathf.Clamp((float)(height[i] / scale), -1f, 1f) * 0.5f + 0.5f;
                pixels[i] = new Color32(
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(normalX[i] * 0.5f + 0.5f) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(normalY[i] * 0.5f + 0.5f) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(stored) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(foam[i]) * 255f));
                coverage += foam[i];
            }
            slopeDegrees = (float)(Math.Atan(rms) * 180.0 / Math.PI);
            foamCoverage = (float)(coverage / foam.Length);
            return pixels;
        }

        private static void Blur(float[] values, int n)
        {
            var source = (float[])values.Clone();
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float sum = 0f;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    sum += source[((y + dy + n) % n) * n + (x + dx + n) % n];
                values[y * n + x] = sum / 9f;
            }
        }

        private static double WaveNumber(int index, int n, double patch)
        {
            int signed = index <= n / 2 ? index : index - n;
            return 2.0 * Math.PI * signed / patch;
        }

        private static double Gaussian(System.Random random)
        {
            double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        private static float Smooth(float edge0, float edge1, float value)
        {
            float t = Mathf.Clamp01((value - edge0) / Mathf.Max(edge1 - edge0, 1e-6f));
            return t * t * (3f - 2f * t);
        }

        private static void Fft2D(double[] re, double[] im, int n, bool inverse)
        {
            var lineRe = new double[n];
            var lineIm = new double[n];
            for (int y = 0; y < n; y++)
            {
                Array.Copy(re, y * n, lineRe, 0, n);
                Array.Copy(im, y * n, lineIm, 0, n);
                Fft1D(lineRe, lineIm, inverse);
                Array.Copy(lineRe, 0, re, y * n, n);
                Array.Copy(lineIm, 0, im, y * n, n);
            }
            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < n; y++) { lineRe[y] = re[y * n + x]; lineIm[y] = im[y * n + x]; }
                Fft1D(lineRe, lineIm, inverse);
                for (int y = 0; y < n; y++) { re[y * n + x] = lineRe[y]; im[y * n + x] = lineIm[y]; }
            }
        }

        /// <summary>Iterative radix-2 Cooley-Tukey. <paramref name="inverse"/> uses the +2πi kernel.</summary>
        private static void Fft1D(double[] re, double[] im, bool inverse)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i >= j) continue;
                double swap = re[i]; re[i] = re[j]; re[j] = swap;
                swap = im[i]; im[i] = im[j]; im[j] = swap;
            }
            for (int length = 2; length <= n; length <<= 1)
            {
                double angle = (inverse ? 2.0 : -2.0) * Math.PI / length;
                double wr = Math.Cos(angle), wi = Math.Sin(angle);
                for (int start = 0; start < n; start += length)
                {
                    double cr = 1.0, ci = 0.0;
                    for (int j = 0; j < length / 2; j++)
                    {
                        int a = start + j, b = a + length / 2;
                        double vr = re[b] * cr - im[b] * ci;
                        double vi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - vr; im[b] = im[a] - vi;
                        re[a] += vr; im[a] += vi;
                        double next = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr; cr = next;
                    }
                }
            }
            if (!inverse) return;
            double norm = 1.0 / n;
            for (int i = 0; i < n; i++) { re[i] *= norm; im[i] *= norm; }
        }
    }
}
