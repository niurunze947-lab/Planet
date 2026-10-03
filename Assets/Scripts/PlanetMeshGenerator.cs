using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds a UV sphere whose vertices are displaced by a 16-bit height map, so the terrain is
/// real silhouette geometry rather than a shader trick.
///
/// The height maps keep their source encoding (they are NOT normalised 0-1), so the decode is
/// explicit:  metres = (raw / 65535) * heightSpanMetres + heightOffsetMetres
///   Earth (ETOPO / AWS terrain tiles): offset -11000, span 20000
///   Moon  (LOLA ldem_*_uint)         : offset -10000, span 32767.5   (= raw * 0.5 - 10000)
///
/// Displacement is computed in real metres and then scaled into scene units, so
/// verticalExaggeration is a true multiple of physical relief. Earth's real relief is only
/// ~0.3% of its radius, so without exaggeration the planet looks like a billiard ball.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class PlanetMeshGenerator : MonoBehaviour
{
    [Header("Height source (must have Read/Write Enabled)")]
    public Texture2D heightMap;

    [Tooltip("metres = (raw / 65535) * span + offset")]
    public float heightOffsetMetres = -11000f;
    public float heightSpanMetres = 20000f;

    [Header("Geometry")]
    [Range(32, 2048)] public int longitudeSegments = 768;
    [Range(16, 1024)] public int latitudeSegments = 384;

    [Tooltip("Scene-unit radius of the undisplaced sphere.")]
    public float radius = 1f;

    [Tooltip("Physical radius of the body, used to convert metres into scene units.")]
    public float realRadiusMetres = 6371000f;

    [Range(1f, 200f)]
    public float verticalExaggeration = 20f;

    [Tooltip("Heights below this are clamped. Set to 0 to flatten the ocean floor into a sea level.")]
    public float floorClampMetres = -11000f;

    [Header("Regeneration")]
    public bool generateOnEnable = true;

    Mesh _mesh;

    void OnEnable()
    {
        // Adding the component fires OnEnable before a height map has been assigned, and a
        // scene that never had one would log an error on every load. Only build when there is
        // actually something to build from; an explicit Generate() still reports the problem.
        if (generateOnEnable && heightMap != null) Generate();
    }

    void OnValidate()
    {
        if (!isActiveAndEnabled || !generateOnEnable) return;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () => { if (this != null) Generate(); };
#endif
    }

    [ContextMenu("Generate Mesh")]
    public void Generate()
    {
        if (heightMap == null)
        {
            Debug.LogError("[PlanetMesh] " + name + ": no height map assigned.", this);
            return;
        }
        if (!heightMap.isReadable)
        {
            Debug.LogError("[PlanetMesh] " + name + ": height map '" + heightMap.name +
                           "' needs Read/Write Enabled in its import settings.", this);
            return;
        }

        int lon = Mathf.Max(8, longitudeSegments);
        int lat = Mathf.Max(4, latitudeSegments);
        int vCount = (lon + 1) * (lat + 1);

        var verts = new Vector3[vCount];
        var uvs = new Vector2[vCount];

        float metresToUnits = radius / realRadiusMetres * verticalExaggeration;

        for (int j = 0; j <= lat; j++)
        {
            float v = (float)j / lat;                 // 0 at south pole, 1 at north
            float latRad = (v - 0.5f) * Mathf.PI;     // -pi/2 .. +pi/2
            float cosLat = Mathf.Cos(latRad);
            float sinLat = Mathf.Sin(latRad);

            for (int i = 0; i <= lon; i++)
            {
                float u = (float)i / lon;

                // GetPixelBilinear uses Unity's convention: (0,0) is bottom-left, so v maps
                // directly to latitude with no manual row flipping. R16 is single-channel,
                // so the height lives in .r.
                float s = heightMap.GetPixelBilinear(u, v).r;
                float metres = s * heightSpanMetres + heightOffsetMetres;
                if (metres < floorClampMetres) metres = floorClampMetres;

                float r = radius + metres * metresToUnits;
                float phi = u * 2f * Mathf.PI;

                int idx = j * (lon + 1) + i;
                verts[idx] = new Vector3(cosLat * Mathf.Cos(phi), sinLat, cosLat * Mathf.Sin(phi)) * r;
                uvs[idx] = new Vector2(u, v);
            }
        }

        var tris = new int[lon * lat * 6];
        int t = 0;
        for (int j = 0; j < lat; j++)
        {
            for (int i = 0; i < lon; i++)
            {
                int a = j * (lon + 1) + i;
                int b = a + 1;
                int c = a + (lon + 1);
                int d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
        }

        if (_mesh == null)
        {
            _mesh = new Mesh { name = name + "_PlanetMesh" };
            _mesh.hideFlags = HideFlags.DontSave;
        }
        _mesh.Clear();
        if (vCount > 65000) _mesh.indexFormat = IndexFormat.UInt32;
        _mesh.vertices = verts;
        _mesh.uv = uvs;
        _mesh.triangles = tris;
        _mesh.RecalculateNormals();
        _mesh.RecalculateTangents();
        _mesh.RecalculateBounds();

        var mf = GetComponent<MeshFilter>();
        mf.sharedMesh = _mesh;
    }
}
