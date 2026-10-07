using Unity.Mathematics;
using UnityEngine;

namespace Planet.Procedural
{
    /// <summary>
    /// The FFT flow layers <c>Planet/Ocean</c> consumes: how many, how large one tile is, and how a
    /// chunk converts its double-precision body-fixed origin into a float phase origin safely.
    /// <para>
    /// Ocean meshes are chunk-local metres, so the wave lookup must not be taken from a body-fixed
    /// position of radius ~6.4e6 m: float32 resolves that at half a metre, which quantises a
    /// metre-scale ripple into visible blocks. Each chunk instead carries its origin projected onto
    /// its cube-face frame and reduced **modulo one tile** in double, and the shader adds only the
    /// short chunk-local offset. Because the texture repeats, the remainder describes the same
    /// surface, and because the reduction is done in double, neighbouring chunks agree exactly.
    /// </para>
    /// <para>
    /// The bake menu writes the same tile sizes into the material; <see cref="PatchMetres"/> is the
    /// single source for both, and the runtime republishes it so the two can never diverge.
    /// </para>
    /// </summary>
    public static class OceanWaveLayers
    {
        public const int Count = 3;
        private static readonly float[] Patches = { 512f, 128f, 32f };
        private static readonly int FrameUId = Shader.PropertyToID("_WaveFrameU");
        private static readonly int FrameVId = Shader.PropertyToID("_WaveFrameV");
        private static readonly int[] OriginIds =
        {
            Shader.PropertyToID("_WaveOrigin0"), Shader.PropertyToID("_WaveOrigin1"), Shader.PropertyToID("_WaveOrigin2")
        };

        public static float PatchMetres(int layer)
        {
            return Patches[math.clamp(layer, 0, Patches.Length - 1)];
        }

        /// <summary>
        /// Right-handed tangent frame of a cube face in body axes, in metres. The positive frame of
        /// the opposite face is reused, which mirrors the (stochastic) water pattern and nothing else.
        /// </summary>
        public static void FaceFrame(PlanetCubeFace face, out float3 u, out float3 v)
        {
            switch (face)
            {
                case PlanetCubeFace.PositiveX: u = new float3(0, 1, 0); v = new float3(0, 0, 1); return;
                case PlanetCubeFace.NegativeX: u = new float3(0, 0, 1); v = new float3(0, 1, 0); return;
                case PlanetCubeFace.PositiveY: u = new float3(0, 0, 1); v = new float3(1, 0, 0); return;
                case PlanetCubeFace.NegativeY: u = new float3(1, 0, 0); v = new float3(0, 0, 1); return;
                case PlanetCubeFace.PositiveZ: u = new float3(1, 0, 0); v = new float3(0, 1, 0); return;
                default: u = new float3(0, 1, 0); v = new float3(1, 0, 0); return;
            }
        }

        /// <summary>Writes the shared frame plus one phase origin per layer into a chunk's block.</summary>
        public static void WriteBlock(MaterialPropertyBlock block, double3 oceanOriginMetres, PlanetCubeFace face)
        {
            FaceFrame(face, out float3 u, out float3 v);
            block.SetVector(FrameUId, new Vector4(u.x, u.y, u.z, 0f));
            block.SetVector(FrameVId, new Vector4(v.x, v.y, v.z, 0f));
            for (int layer = 0; layer < Count; layer++)
            {
                double patch = PatchMetres(layer);
                float x = Fraction(math.dot(oceanOriginMetres, (double3)u) / patch);
                float y = Fraction(math.dot(oceanOriginMetres, (double3)v) / patch);
                block.SetVector(OriginIds[layer], new Vector4(x, y, 0f, 0f));
            }
        }

        private static float Fraction(double value)
        {
            return (float)(value - math.floor(value));
        }
    }
}
