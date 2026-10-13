using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yongxian.PlanetSculpt
{
    // A radial height field on a welded icosphere. No Unity Terrain APIs are used.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class PlanetTerrain : MonoBehaviour
    {
        [Min(0.01f)] public float radius = 0.5f;
        [Range(2, 6)] public int subdivisions = 6;
        [Header("Game height (visually exaggerated)")]
        [Range(0.05f, 0.35f)] public float maxMountainHeightRatio = 0.18f;
        [Min(1f)] public float maxElevationMeters = 9000f;
        [Min(1f)] public float maxDepthMeters = 2000f;
        [Min(0f)] public float snowLineMeters = 4000f;
        public float maxHeightRatio => maxMountainHeightRatio;
        public float maxDepthRatio => maxMountainHeightRatio * maxDepthMeters / Mathf.Max(1f, maxElevationMeters);
        public int UndoCount => undo.Count;
        public float MetersToLocalHeight(float meters) => radius * maxMountainHeightRatio * meters / Mathf.Max(1f, maxElevationMeters);
        public float LocalHeightToMeters(float height) => height / (radius * maxMountainHeightRatio) * maxElevationMeters;
        [Header("Mountain shape and surface")]
        public bool mountainBrush = true;
        [Range(0f, 1f)] public float ridgeStrength = 0.55f;
        [Range(4f, 32f)] public float ridgeFrequency = 10f;
        public bool useNaturalSurface = true;
        public bool preserveSourceMesh;
        public bool initializeOnAwake = true;
        Vector3[] baseVertices;

        public Mesh SurfaceMesh { get; private set; }
        public MeshCollider SurfaceCollider { get; private set; }
        public int VertexCount => heights == null ? 0 : heights.Length;
        public float[] CopyHeights() => (float[])heights.Clone();

        Vector3[] directions;
        Vector3[] vertices;
        Vector2[] elevationUV;
        int[] triangleIndices;
        float[] heights;
        float[] smoothingSource;
        int[][] neighbors;
        Mesh originalMesh;
        Mesh originalColliderMesh;
        Material originalMaterial;
        Material naturalMaterial;
        MaterialPropertyBlock originalProperties;
        readonly List<Collider> disabledColliders = new List<Collider>();
        readonly Stack<float[]> undo = new Stack<float[]>();

        void Awake() { if (initializeOnAwake) Initialize(); }

        public void Initialize()
        {
            if (SurfaceMesh != null) return;
            radius = Mathf.Max(0.01f, radius);
            maxMountainHeightRatio = Mathf.Clamp(maxMountainHeightRatio, 0.05f, 0.35f);
            subdivisions = Mathf.Clamp(subdivisions, 2, 6);
            var body = GetComponentInParent<Rigidbody>();
            if (body != null && !body.isKinematic)
            {
                Debug.LogError("PlanetTerrain requires a static or kinematic planet (non-convex collider).", this);
                enabled = false;
                return;
            }
            var points = new List<Vector3>();
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            points.AddRange(new[] {
                new Vector3(-1,t,0), new Vector3(1,t,0), new Vector3(-1,-t,0), new Vector3(1,-t,0),
                new Vector3(0,-1,t), new Vector3(0,1,t), new Vector3(0,-1,-t), new Vector3(0,1,-t),
                new Vector3(t,0,-1), new Vector3(t,0,1), new Vector3(-t,0,-1), new Vector3(-t,0,1)
            });
            for (int i = 0; i < points.Count; i++) points[i] = points[i].normalized;
            var faces = new List<int> {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };
            for (int level = 0; level < subdivisions; level++)
            {
                var cache = new Dictionary<long, int>();
                var next = new List<int>(faces.Count * 4);
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                    int ab = Midpoint(a, b, points, cache);
                    int bc = Midpoint(b, c, points, cache);
                    int ca = Midpoint(c, a, points, cache);
                    next.AddRange(new[] { a,ab,ca, b,bc,ab, c,ca,bc, ab,bc,ca });
                }
                faces = next;
            }
            var sourceMesh = GetComponent<MeshFilter>().sharedMesh;
            if (preserveSourceMesh)
            {
                if (sourceMesh == null || !sourceMesh.isReadable)
                    throw new InvalidOperationException("Earth brush needs a readable source mesh.");
                points = new List<Vector3>(sourceMesh.vertices);
                faces = new List<int>(sourceMesh.triangles);
                baseVertices = sourceMesh.vertices;
                for (int i = 0; i < points.Count; i++)
                {
                    points[i] = points[i].normalized;
                }
            }
            directions = points.ToArray();
            vertices = new Vector3[directions.Length];
            elevationUV = new Vector2[directions.Length];
            triangleIndices = faces.ToArray();
            heights = new float[directions.Length];
            smoothingSource = new float[directions.Length];
            var adjacency = new HashSet<int>[directions.Length];
            for (int i = 0; i < adjacency.Length; i++) adjacency[i] = new HashSet<int>();
            for (int i = 0; i < faces.Count; i += 3)
            {
                int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                adjacency[a].Add(b); adjacency[a].Add(c);
                adjacency[b].Add(a); adjacency[b].Add(c);
                adjacency[c].Add(a); adjacency[c].Add(b);
            }
            if (preserveSourceMesh)
            {
                var welded = new Dictionary<Vector3Int, HashSet<int>>();
                for (int i = 0; i < directions.Length; i++)
                {
                    Vector3 d = directions[i] * 100000f;
                    var key = new Vector3Int(Mathf.RoundToInt(d.x), Mathf.RoundToInt(d.y), Mathf.RoundToInt(d.z));
                    if (!welded.TryGetValue(key, out var shared)) welded[key] = shared = new HashSet<int>();
                    shared.UnionWith(adjacency[i]);
                }
                for (int i = 0; i < directions.Length; i++)
                {
                    Vector3 d = directions[i] * 100000f;
                    adjacency[i] = welded[new Vector3Int(Mathf.RoundToInt(d.x), Mathf.RoundToInt(d.y), Mathf.RoundToInt(d.z))];
                }
            }
            neighbors = new int[directions.Length][];
            for (int i = 0; i < neighbors.Length; i++)
            {
                neighbors[i] = new int[adjacency[i].Count];
                adjacency[i].CopyTo(neighbors[i]);
                vertices[i] = directions[i] * radius;
            }
            SurfaceMesh = preserveSourceMesh ? Instantiate(sourceMesh) : new Mesh();
            SurfaceMesh.name = "Runtime sculptable planet";
            SurfaceMesh.MarkDynamic();
            SurfaceMesh.vertices = vertices;
            SurfaceMesh.SetTriangles(faces, 0);
            var filter = GetComponent<MeshFilter>();
            originalMesh = filter.sharedMesh;
            filter.sharedMesh = SurfaceMesh;
            SurfaceCollider = GetComponent<MeshCollider>();
            originalColliderMesh = SurfaceCollider.sharedMesh;
            SurfaceCollider.convex = false;
            if (useNaturalSurface)
            {
                naturalMaterial = Resources.Load<Material>("PlanetSurface");
                if (naturalMaterial != null)
                {
                    var surfaceRenderer = GetComponent<MeshRenderer>();
                    originalMaterial = surfaceRenderer.sharedMaterial;
                    originalProperties = new MaterialPropertyBlock();
                    surfaceRenderer.GetPropertyBlock(originalProperties);
                    surfaceRenderer.sharedMaterial = naturalMaterial;
                    var properties = new MaterialPropertyBlock();
                    surfaceRenderer.GetPropertyBlock(properties);
                    properties.SetFloat("_PlanetRadius", radius);
                    properties.SetFloat("_SnowLineMeters", snowLineMeters);
                    surfaceRenderer.SetPropertyBlock(properties);
                }
            }
            // A primitive SphereCollider would otherwise mask the deformed surface.
            foreach (var collider in GetComponents<Collider>())
                if (collider != SurfaceCollider && collider.enabled)
                {
                    disabledColliders.Add(collider);
                    collider.enabled = false;
                }
            RefreshSurface();
        }

        static int Midpoint(int a, int b, List<Vector3> points, Dictionary<long, int> cache)
        {
            long key = ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
            if (cache.TryGetValue(key, out int index)) return index;
            index = points.Count;
            points.Add((points[a] + points[b]).normalized);
            cache.Add(key, index);
            return index;
        }

        public void BeginStroke()
        {
            // Bound memory to 20 full height snapshots.
            if (undo.Count == 20)
            {
                var recent = undo.ToArray();
                undo.Clear();
                for (int i = 18; i >= 0; i--) undo.Push(recent[i]);
            }
            undo.Push(CopyHeights());
        }

        public bool UndoStroke()
        {
            if (undo.Count == 0) return false;
            heights = undo.Pop();
            RefreshSurface();
            return true;
        }

        public void ResetTerrain()
        {
            BeginStroke();
            Array.Clear(heights, 0, heights.Length);
            RefreshSurface();
        }

        // A no-op stamp must not consume either an undo entry or a drawing allowance.
        public void DiscardUndoStroke() { if (undo.Count > 0) undo.Pop(); }

        // brushRadians measures surface distance / base radius, not world-space distance.
        // amount is local-space height for raise/lower, or interpolation [0..1] for smooth.
        public bool ApplyBrush(Vector3 localDirection, float brushRadians, float amount, bool smooth)
            => ApplyBrushInternal(localDirection, brushRadians, amount, smooth, true);

        // Resample a drag so quick mouse movement does not leave disconnected dots.
        // All stamps share one mesh/collider update and one undo entry at the caller.
        public bool ApplyBrushSegment(Vector3 from, Vector3 to, float brushRadians, float amount, bool smooth)
        {
            float angle = Vector3.Angle(from, to) * Mathf.Deg2Rad;
            int count = Mathf.Clamp(Mathf.CeilToInt(angle / Mathf.Max(0.005f, brushRadians * 0.35f)), 1, 128);
            float perStamp = smooth ? 1f - Mathf.Pow(1f - Mathf.Clamp01(amount), 1f / count) : amount / count;
            bool changed = false;
            for (int i = 0; i < count; i++)
                changed |= ApplyBrushInternal(Vector3.Slerp(from, to, (i + 0.5f) / count), brushRadians, perStamp, smooth, false);
            if (changed) RefreshSurface();
            return changed;
        }

        bool ApplyBrushInternal(Vector3 localDirection, float brushRadians, float amount, bool smooth, bool refresh)
        {
            if (SurfaceMesh == null || localDirection.sqrMagnitude < 0.0001f) return false;
            localDirection.Normalize();
            brushRadians = Mathf.Clamp(brushRadians, 1f * Mathf.Deg2Rad, 6f * Mathf.Deg2Rad);
            float minDot = Mathf.Cos(brushRadians);
            if (smooth) Array.Copy(heights, smoothingSource, heights.Length);
            bool changed = false;
            for (int i = 0; i < directions.Length; i++)
            {
                float dot = Vector3.Dot(directions[i], localDirection);
                if (dot < minDot) continue;
                float distance = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
                float x = 1f - distance / brushRadians;
                float weight = x * x * (3f - 2f * x);
                float value;
                if (smooth)
                {
                    float average = 0f;
                    foreach (int neighbor in neighbors[i]) average += smoothingSource[neighbor];
                    average /= neighbors[i].Length;
                    value = Mathf.Lerp(smoothingSource[i], average, Mathf.Clamp01(amount * weight));
                }
                else
                {
                    // Coherent object-space ridges: repeated strokes build the same mountain
                    // structure instead of introducing a new random pattern every frame.
                    float shape = mountainBrush && amount > 0f
                        ? Mathf.Lerp(1f, 0.55f + 0.95f * MountainRidge(directions[i] * ridgeFrequency), ridgeStrength)
                        : 1f;
                    value = heights[i] + amount * weight * shape;
                }
                value = Mathf.Clamp(value, -radius * maxDepthRatio, radius * maxHeightRatio);
                if (Mathf.Abs(value - heights[i]) < 1e-12f) continue;
                heights[i] = value;
                changed = true;
            }
            if (changed && refresh) RefreshSurface();
            return changed;
        }

        void RefreshSurface()
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = baseVertices != null ? baseVertices[i] + directions[i] * heights[i] : directions[i] * (radius + heights[i]);
                // Color/snow use game altitude independently of exaggerated display geometry.
                elevationUV[i] = new Vector2(LocalHeightToMeters(heights[i]), 0f);
            }
            SurfaceMesh.vertices = vertices;
            if (!preserveSourceMesh) SurfaceMesh.uv2 = elevationUV;
            SurfaceMesh.RecalculateNormals();
            SurfaceMesh.RecalculateBounds();
            SurfaceCollider.sharedMesh = null;
            SurfaceCollider.sharedMesh = SurfaceMesh;
        }

        public float GetHeightMeters(RaycastHit hit)
        {
            int index = hit.triangleIndex * 3;
            if (hit.collider != SurfaceCollider || index < 0 || index + 2 >= triangleIndices.Length) return 0f;
            Vector3 bary = hit.barycentricCoordinate;
            return LocalHeightToMeters(heights[triangleIndices[index]] * bary.x +
                heights[triangleIndices[index + 1]] * bary.y + heights[triangleIndices[index + 2]] * bary.z);
        }

        static float MountainRidge(Vector3 point)
        {
            float a = Mathf.PerlinNoise(point.x + point.z * 0.37f + 31.7f, point.y - point.z * 0.61f + 19.2f);
            float b = Mathf.PerlinNoise(point.y * 2.03f + point.x * 0.42f + 7.1f, point.z * 2.03f - point.x * 0.71f + 53.4f);
            float ridge = 1f - Mathf.Abs(2f * a - 1f);
            return Mathf.Clamp01(ridge * ridge * 0.9f + b * 0.1f);
        }

        void OnDestroy()
        {
            if (SurfaceMesh == null) return;
            var filter = GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh == SurfaceMesh) filter.sharedMesh = originalMesh;
            if (SurfaceCollider != null) SurfaceCollider.sharedMesh = originalColliderMesh;
            var surfaceRenderer = GetComponent<MeshRenderer>();
            if (naturalMaterial != null && surfaceRenderer != null && surfaceRenderer.sharedMaterial == naturalMaterial)
            {
                surfaceRenderer.sharedMaterial = originalMaterial;
                surfaceRenderer.SetPropertyBlock(originalProperties);
            }
            foreach (var collider in disabledColliders) if (collider != null) collider.enabled = true;
            if (Application.isPlaying) Destroy(SurfaceMesh);
            else DestroyImmediate(SurfaceMesh);
        }
    }
}
