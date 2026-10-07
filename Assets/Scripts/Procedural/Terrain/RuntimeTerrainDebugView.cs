using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Planet.Procedural.Terrain
{
    /// <summary>Optional Game/Scene view diagnostics for streamed terrain and collision coverage.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class RuntimeTerrainDebugView : MonoBehaviour
    {
        [Header("Draw")]
        public bool showMeshWireframe;
        public bool showCollisionBounds;
        public bool showUncookedCoverage;
        public bool showOnlyNearby = true;
        public bool depthTest = true;
        [Range(1, 16)] public int wireframeStride = 1;
        [Range(1, 256)] public int maxDrawChunks = 96;
        [Min(100f)] public float nearbyRangeMetres = 25000f;

        [Header("Colours")]
        public Color wireframeColor = new Color(0.25f, 1f, 0.45f, 0.85f);
        public Color collisionColor = new Color(1f, 0.55f, 0.1f, 0.9f);
        public Color uncookedColor = new Color(1f, 0.2f, 0.2f, 0.75f);

        private RuntimeTerrainRenderer terrain;
        private readonly List<RuntimeTerrainRenderer.DebugChunk> chunks =
            new List<RuntimeTerrainRenderer.DebugChunk>();
        private Material lineMaterial;
        private static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
        private static readonly int ZTest = Shader.PropertyToID("_ZTest");
        private static readonly int Cull = Shader.PropertyToID("_Cull");

        private void Awake()
        {
            ResolveTerrain();
        }

        private void ResolveTerrain()
        {
            if (terrain == null) terrain = GetComponent<RuntimeTerrainRenderer>();
        }

        private void OnEnable()
        {
            RenderPipelineManager.endCameraRendering += AfterCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= AfterCamera;
        }

        private void AfterCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!AnyDrawEnabled || camera == null || camera.cameraType == CameraType.Preview) return;
            ResolveTerrain();
            if (terrain == null || terrain.IsLocalRenderCamera(camera)) return;
            Draw(camera);
        }

        private void OnDrawGizmosSelected()
        {
            if (!AnyDrawEnabled) return;
            ResolveTerrain();
            if (terrain == null) return;
            Camera camera = Camera.current != null ? Camera.current : Camera.main;
            if (camera == null) return;
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(camera.transform.position, camera.transform.rotation, Vector3.one);
            terrain.CopyDebugChunks(chunks);
            try
            {
                int drawn = 0;
                for (int i = 0; i < chunks.Count && drawn < maxDrawChunks; i++)
                {
                    RuntimeTerrainRenderer.DebugChunk chunk = chunks[i];
                    if (!chunk.visible && !showUncookedCoverage) continue;
                    if (!IsNearby(chunk, camera)) continue;
                    if (showMeshWireframe && chunk.visible)
                    {
                        DrawWireframeGizmo(camera, chunk.data);
                    }
                    if (showCollisionBounds && chunk.collider != null)
                        DrawBoundsGizmo(camera, chunk.data, chunk.collider.sharedMesh != null
                            ? chunk.collider.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.zero), collisionColor);
                    if (showUncookedCoverage && chunk.collisionRelevant && chunk.collider == null)
                        DrawBoundsGizmo(camera, chunk.data, SurfaceBounds(chunk.data), uncookedColor);
                    drawn++;
                }
            }
            finally { Gizmos.matrix = oldMatrix; }
        }

        private void Draw(Camera camera)
        {
            if (!EnsureLineMaterial()) return;
            terrain.CopyDebugChunks(chunks);
            ConfigureLineMaterial();
            GL.PushMatrix();
            GL.LoadProjectionMatrix(camera.projectionMatrix);
            GL.modelview = Matrix4x4.identity;
            GL.Begin(GL.LINES);
            int drawn = 0;
            for (int i = 0; i < chunks.Count && drawn < maxDrawChunks; i++)
            {
                RuntimeTerrainRenderer.DebugChunk chunk = chunks[i];
                if (!chunk.visible && !showUncookedCoverage) continue;
                if (!IsNearby(chunk, camera)) continue;
                if (showMeshWireframe && chunk.visible)
                {
                    GL.Color(wireframeColor);
                    DrawWireframe(camera, chunk.data);
                }
                if (showCollisionBounds && chunk.collider != null)
                {
                    GL.Color(collisionColor);
                    DrawBounds(camera, chunk.data,
                        chunk.collider.sharedMesh != null ? chunk.collider.sharedMesh.bounds : new Bounds());
                }
                if (showUncookedCoverage && chunk.collisionRelevant && chunk.collider == null)
                {
                    GL.Color(uncookedColor);
                    DrawBounds(camera, chunk.data, SurfaceBounds(chunk.data));
                }
                drawn++;
            }
            GL.End();
            GL.PopMatrix();
        }

        private bool IsNearby(RuntimeTerrainRenderer.DebugChunk chunk, Camera camera)
        {
            if (!showOnlyNearby) return true;
            Vector3 point = terrain.DebugProjectPoint(camera, chunk.data.origin);
            float range = (float)(nearbyRangeMetres * terrain.DebugRangeScale(camera));
            return point.sqrMagnitude <= range * range;
        }

        private void DrawWireframe(Camera camera, RuntimeTerrainChunk data)
        {
            const int side = TerrainLattice.N + 1;
            int stride = Mathf.Max(1, wireframeStride);
            for (int y = 0; y < side; y += stride)
            for (int x = 0; x < TerrainLattice.N; x += stride)
            {
                int a = y * side + x;
                int b = y * side + Mathf.Min(x + stride, TerrainLattice.N);
                Line(camera, data.origin + (double3)(float3)data.vertices[a],
                    data.origin + (double3)(float3)data.vertices[b]);
            }
            for (int x = 0; x < side; x += stride)
            for (int y = 0; y < TerrainLattice.N; y += stride)
            {
                int a = y * side + x;
                int b = Mathf.Min(y + stride, TerrainLattice.N) * side + x;
                Line(camera, data.origin + (double3)(float3)data.vertices[a],
                    data.origin + (double3)(float3)data.vertices[b]);
            }
            const int gridCount = (TerrainLattice.N + 1) * (TerrainLattice.N + 1);
            for (int edge = 0; edge < 4; edge++)
            for (int v = 0; v < side; v += stride)
            {
                int surface = EdgeVertex(edge, v, TerrainLattice.N);
                int skirt = gridCount + edge * side + v;
                Line(camera, data.origin + (double3)(float3)data.vertices[surface],
                    data.origin + (double3)(float3)data.vertices[skirt]);
                if (v + stride < side)
                    Line(camera, data.origin + (double3)(float3)data.vertices[skirt],
                        data.origin + (double3)(float3)data.vertices[gridCount + edge * side + v + stride]);
            }
        }

        private void DrawWireframeGizmo(Camera camera, RuntimeTerrainChunk data)
        {
            const int side = TerrainLattice.N + 1;
            int stride = Mathf.Max(1, wireframeStride);
            Gizmos.color = wireframeColor;
            for (int y = 0; y < side; y += stride)
            for (int x = 0; x < TerrainLattice.N; x += stride)
            {
                int a = y * side + x;
                int b = y * side + Mathf.Min(x + stride, TerrainLattice.N);
                Gizmos.DrawLine(terrain.DebugProjectPoint(camera, data.origin + (double3)(float3)data.vertices[a]),
                    terrain.DebugProjectPoint(camera, data.origin + (double3)(float3)data.vertices[b]));
            }
            for (int x = 0; x < side; x += stride)
            for (int y = 0; y < TerrainLattice.N; y += stride)
            {
                int a = y * side + x;
                int b = Mathf.Min(y + stride, TerrainLattice.N) * side + x;
                Gizmos.DrawLine(terrain.DebugProjectPoint(camera, data.origin + (double3)(float3)data.vertices[a]),
                    terrain.DebugProjectPoint(camera, data.origin + (double3)(float3)data.vertices[b]));
            }
            const int gridCount = side * side;
            for (int edge = 0; edge < 4; edge++)
            for (int v = 0; v < side; v += stride)
            {
                int surface = EdgeVertex(edge, v, TerrainLattice.N);
                int skirt = gridCount + edge * side + v;
                Gizmos.DrawLine(terrain.DebugProjectPoint(camera, data.origin + (double3)(float3)data.vertices[surface]),
                    terrain.DebugProjectPoint(camera, data.origin + (double3)(float3)data.vertices[skirt]));
                if (v + stride < side)
                    Gizmos.DrawLine(terrain.DebugProjectPoint(camera, data.origin + (double3)(float3)data.vertices[skirt]),
                        terrain.DebugProjectPoint(camera, data.origin + (double3)(float3)data.vertices[gridCount + edge * side + v + stride]));
            }
        }

        private static int EdgeVertex(int edge, int v, int n)
        {
            int side = n + 1;
            switch (edge)
            {
                case 0: return v;
                case 1: return v * side + n;
                case 2: return n * side + n - v;
                default: return (n - v) * side;
            }
        }

        private void DrawBounds(Camera camera, RuntimeTerrainChunk data, Bounds bounds)
        {
            Vector3[] corners = BoundsCorners(bounds);
            int[,] edges = BoxEdges;
            for (int i = 0; i < edges.GetLength(0); i++)
                Line(camera, data.origin + (double3)(float3)corners[edges[i, 0]],
                    data.origin + (double3)(float3)corners[edges[i, 1]]);
        }

        private void DrawBoundsGizmo(Camera camera, RuntimeTerrainChunk data, Bounds bounds, Color color)
        {
            Gizmos.color = color;
            Vector3[] corners = BoundsCorners(bounds);
            for (int i = 0; i < BoxEdges.GetLength(0); i++)
                Gizmos.DrawLine(terrain.DebugProjectPoint(camera, data.origin + (double3)(float3)corners[BoxEdges[i, 0]]),
                    terrain.DebugProjectPoint(camera, data.origin + (double3)(float3)corners[BoxEdges[i, 1]]));
        }

        private void Line(Camera camera, double3 a, double3 b)
        {
            GL.Vertex(ToClipView(terrain.DebugProjectPoint(camera, a)));
            GL.Vertex(ToClipView(terrain.DebugProjectPoint(camera, b)));
        }

        private static Vector3 ToClipView(Vector3 cameraLocal)
        {
            cameraLocal.z = -cameraLocal.z;
            return cameraLocal;
        }

        private static Bounds SurfaceBounds(RuntimeTerrainChunk data)
        {
            Bounds result = new Bounds(data.vertices[0], Vector3.zero);
            int surfaceCount = (TerrainLattice.N + 1) * (TerrainLattice.N + 1);
            for (int i = 1; i < surfaceCount; i++) result.Encapsulate(data.vertices[i]);
            return result;
        }

        private static Vector3[] BoundsCorners(Bounds bounds)
        {
            Vector3 min = bounds.min, max = bounds.max;
            return new[]
            {
                new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z)
            };
        }

        private static readonly int[,] BoxEdges =
        {
            { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
            { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
            { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
        };

        private bool AnyDrawEnabled { get { return showMeshWireframe || showCollisionBounds || showUncookedCoverage; } }

        private bool EnsureLineMaterial()
        {
            if (lineMaterial != null) return true;
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return false;
            lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            lineMaterial.SetInt(SrcBlend, (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lineMaterial.SetInt(DstBlend, (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lineMaterial.SetInt(ZWrite, 0);
            lineMaterial.SetInt(Cull, (int)UnityEngine.Rendering.CullMode.Off);
            return true;
        }

        private void ConfigureLineMaterial()
        {
            lineMaterial.SetInt(ZTest, (int)(depthTest
                ? UnityEngine.Rendering.CompareFunction.LessEqual
                : UnityEngine.Rendering.CompareFunction.Always));
            lineMaterial.SetPass(0);
        }

        private void OnDestroy()
        {
            if (lineMaterial != null) DestroyImmediate(lineMaterial);
            lineMaterial = null;
        }
    }
}
