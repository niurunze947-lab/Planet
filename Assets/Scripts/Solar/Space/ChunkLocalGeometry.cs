using System;
using UnityEngine;

namespace Planet.Solar
{
    /// <summary>
    /// A point stored the way surface geometry is stored: an address plus a <b>float offset in
    /// metres from that address's own origin</b>.
    ///
    /// The pair is the whole data model of the surface coordinate system. Neither half is usable
    /// alone: the key alone cannot be rendered, and the offset alone is meaningless without knowing
    /// what it is offset from. Keeping them in one value type means a caller cannot accidentally
    /// keep the float and throw the origin away - which is exactly how a chunk mesh ends up 6,000 km
    /// from where it belongs the moment the focus changes.
    /// </summary>
    public struct ChunkLocalPoint
    {
        public SurfaceChunkKey key;

        /// <summary>Metres from the chunk's origin. Float32, and that is the point of the design.</summary>
        public Vector3 localMetres;

        public ChunkLocalPoint(SurfaceChunkKey key, Vector3 localMetres)
        {
            this.key = key;
            this.localMetres = localMetres;
        }
    }

    /// <summary>Managed buffers for one chunk's mesh, in chunk-local coordinates.</summary>
    public sealed class ChunkGeometryBuffers
    {
        public SurfaceChunkKey key;

        /// <summary>The chunk's origin, in the body-fixed frame, double metres. The other half of the address.</summary>
        public Vec3d originInBodyMetres;

        public Vector3[] localMetres;
        public Vector3[] normals;
        /// <summary>Body-fixed unit direction per vertex; what the surface shader samples the cubemap with.</summary>
        public Vector3[] directions;
        public Vector2[] faceUV;
        /// <summary>(height m, slope deg, ocean mask, water content) - the globe's TEXCOORD2 layout.</summary>
        public Vector4[] samples;
        public int[] triangles;

        public int vertexCount;
        public int indexCount;

        /// <summary>Half the chunk's diagonal in metres: the largest offset any vertex can have.</summary>
        public double extentMetres;

        public void Allocate(int vertices, int indices)
        {
            if (localMetres == null || localMetres.Length < vertices) localMetres = new Vector3[vertices];
            if (normals == null || normals.Length < vertices) normals = new Vector3[vertices];
            if (directions == null || directions.Length < vertices) directions = new Vector3[vertices];
            if (faceUV == null || faceUV.Length < vertices) faceUV = new Vector2[vertices];
            if (samples == null || samples.Length < vertices) samples = new Vector4[vertices];
            if (triangles == null || triangles.Length < indices) triangles = new int[indices];
            vertexCount = vertices;
            indexCount = indices;
        }

        /// <summary>Far corner of the chunk's local bounding box, for culling.</summary>
        public Bounds LocalBounds
        {
            get
            {
                float e = (float)extentMetres;
                return new Bounds(Vector3.zero, new Vector3(e * 2f, e * 2f, e * 2f));
            }
        }
    }

    /// <summary>
    /// The conversion between the two coordinate systems, in one place.
    ///
    /// <para>Read the order of operations carefully, because it is the entire design:</para>
    /// <list type="number">
    /// <item>the terrain point is built in <b>double body-frame metres</b>;</item>
    /// <item>the chunk origin is subtracted <b>in double</b>, giving a small residual;</item>
    /// <item>only then is the residual narrowed to a <c>Vector3</c>.</item>
    /// </list>
    ///
    /// <para>Doing it the other way round - narrowing the body-frame position first, then
    /// subtracting - would put the 6.371e6 m radius through a float and destroy everything below
    /// 0.5 m before the chunk offset ever got a chance to help.</para>
    /// </summary>
    public static class ChunkLocalGeometry
    {
        /// <summary>
        /// Body-frame double metres to chunk-local float metres. The subtraction is in double; the
        /// narrowing happens last and only on the residual.
        /// </summary>
        public static Vector3 ToChunkLocal(Vec3d pointInBodyMetres, Vec3d chunkOriginInBodyMetres)
        {
            Vec3d residual = pointInBodyMetres - chunkOriginInBodyMetres;
            return new Vector3((float)residual.x, (float)residual.y, (float)residual.z);
        }

        /// <summary>
        /// Chunk-local float metres back to body-frame double metres. Lossless in the sense that
        /// matters: the float offset is exact, and the origin it is added to is a double.
        /// </summary>
        public static Vec3d ToBodyMetres(Vector3 localMetres, Vec3d chunkOriginInBodyMetres)
        {
            return new Vec3d(chunkOriginInBodyMetres.x + localMetres.x,
                             chunkOriginInBodyMetres.y + localMetres.y,
                             chunkOriginInBodyMetres.z + localMetres.z);
        }

        /// <summary>
        /// The full trip: a chunk-local vertex all the way to the renderer, crossing the body frame,
        /// the stellar frame and the render origin, with every subtraction in double.
        ///
        /// This is the surface half of the "two coordinate systems" promise - a vertex can be a
        /// millimetre from its neighbour on a planet 1.5e11 m from the Sun, and the renderer only
        /// ever sees the millimetres.
        /// </summary>
        public static Vector3 ToRenderPosition(Vector3 localMetres, Vec3d chunkOriginInBodyMetres,
                                               SpaceFrame bodyFrame, RenderOrigin origin)
        {
            Vec3d inBody = ToBodyMetres(localMetres, chunkOriginInBodyMetres);
            Vec3d inOriginFrame = bodyFrame.ConvertTo(origin.frame, inBody);
            return origin.ToRender(inOriginFrame);
        }

        /// <summary>
        /// Largest float32 step a chunk vertex can be off by, in metres, given the chunk's angular
        /// level. This is the number the audit tool prints and the tests assert.
        /// </summary>
        public static double VertexStepMetres(SurfaceChunkGrid grid, int level)
        {
            return SpacePrecision.ExactUlp(grid.ChunkLocalExtentMetres(level), NumericFormat.Float32);
        }

        /// <summary>
        /// The same quantity for the existing single-mesh globe: every vertex lives at magnitude
        /// ~1.0 radii, so the step is <c>radius x 1.19e-7</c> no matter how fine the mesh is.
        /// Quoted next to <see cref="VertexStepMetres"/> by the audit tool so the improvement is
        /// visible rather than asserted.
        /// </summary>
        public static double GlobeVertexStepMetres(double bodyRadiusMetres)
        {
            return SpacePrecision.ExactUlp(bodyRadiusMetres, NumericFormat.Float32);
        }
    }
}
