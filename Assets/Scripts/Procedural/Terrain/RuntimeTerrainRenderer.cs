using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Planet.Procedural.Terrain
{
    /// <summary>On-demand bounded mesh cache. At most two worker jobs globally, one per body, no Unity calls on workers.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class RuntimeTerrainRenderer : MonoBehaviour, IPlanetSurfaceQuery
    {
        [Range(6, 512)] public int maxActiveChunks = 384;
        [Range(16, 1024)] public int maxResidentChunks = 640;
        [Range(0, TerrainLattice.MaxLevel)] public int maxLevel = TerrainLattice.MaxLevel;
        [Range(0.5f, 12f)] public float pixelTolerance = 3;
        [Min(0.1f)] public float uploadBudgetMilliseconds = 2;

        public int ResidentCount { get { return nodes.Count; } }
        public int VisibleCount { get; private set; }
        public int PendingWork { get { return pendingCount; } }
        public int FinestVisibleLevel { get; private set; }
        public int BuiltCount { get; private set; }
        public string LastError { get; private set; }
        public RuntimeTerrainSource Source { get { return source; } }
        public double RadiusMetres { get { return ready ? source.radius : 0; } }
        public int PendingDetailWork { get { return pendingCount; } }
        public double SampleHeightMetres(double3 directionBf, double sampleSpacingMetres)
        {
            if (!ready) return 0;
            double3 d = math.normalizesafe(directionBf, new double3(0, 1, 0));
            return source.ReferenceRadius(d) - source.radius + source.Height(d, sampleSpacingMetres);
        }
        public double SampleSurfaceRadiusMetres(double3 directionBf, double sampleSpacingMetres)
        { return RadiusMetres + SampleHeightMetres(directionBf, sampleSpacingMetres); }
        public double DisplayRadiusMetres(double x, double y, double z)
        {
            if (!ready) return 0;
            var d = math.normalizesafe(new double3(x, y, z), new double3(0, 1, 0));
            return math.length(source.Position(d, 0.005));
        }
        public void SetPreciseCameraPosition(Camera camera, double x, double y, double z)
        {
            var state = ViewFromCamera(camera);
            state.positionBfMetres = new double3(x, y, z); state.localCameraActive = true;
            UpdateView(state, camera, true);
        }

        private sealed class Node
        {
            public RuntimeTerrainChunk data;
            public Mesh mesh;
            public GameObject go;
            public MeshRenderer renderer;
            public MaterialPropertyBlock block = new MaterialPropertyBlock();
            public int lastUsed;
        }
        private static readonly SemaphoreSlim Workers = new SemaphoreSlim(2, 2);
        private readonly Dictionary<TerrainNodeKey, Node> nodes = new Dictionary<TerrainNodeKey, Node>();
        private readonly List<Node> visible = new List<Node>();
        private HashSet<TerrainNodeKey> desired = new HashSet<TerrainNodeKey>();
        private HashSet<TerrainNodeKey> wanted = new HashSet<TerrainNodeKey>();
        private readonly HashSet<TerrainNodeKey> splits = new HashSet<TerrainNodeKey>();
        private readonly List<TerrainNodeKey> queue = new List<TerrainNodeKey>();
        private PlanetSurfaceView owner;
        private RuntimeTerrainSource source;
        private string identity;
        private double unitsPerMetre;
        private Material material;
        private Transform root;
        private Camera viewCamera;
        private PlanetViewState view;
        private bool focused, receivedView, ready, shown = true;
        private float nextSelection, nextIdentity;
        private Task<RuntimeTerrainChunk> job;
        private Task<HashSet<TerrainNodeKey>> selectionJob;
        private CancellationTokenSource cancellation = new CancellationTokenSource();
        private int pendingCount;

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += EditorTick;
#endif
        }
        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
#endif
            Clear();
        }
#if UNITY_EDITOR
        private void EditorTick()
        {
            if (!Application.isPlaying && !UnityEditor.EditorApplication.isCompiling)
            {
                LateUpdate();
                if (pendingCount > 0) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            }
        }
#endif

        public void Configure(PlanetSurfaceView viewOwner, Transform surface, float radiusUnits, Material sharedMaterial)
        {
            owner = viewOwner; root = surface; material = sharedMaterial;
            unitsPerMetre = radiusUnits / owner.definition.physical.radiusMetres;
            RefreshSource();
        }

        public void SetRadius(float radiusUnits)
        {
            if (owner != null && owner.definition != null) unitsPerMetre = radiusUnits / owner.definition.physical.radiusMetres;
        }

        public void UpdateView(PlanetViewState state, Camera camera, bool isFocused)
        {
            view = state; viewCamera = camera; focused = isFocused; receivedView = true;
        }

        public void SetVisible(bool value)
        {
            shown = value;
            if (!value) foreach (var node in visible) if (node.renderer != null) node.renderer.enabled = false;
        }

        private void RefreshSource()
        {
            if (owner == null || owner.definition == null) return;
            string fingerprint = owner.definition.TerrainContentFingerprint();
            if (identity == fingerprint && ready) return;
            Clear(); source = new RuntimeTerrainSource(owner.definition);
            identity = fingerprint; ready = true; nextSelection = 0; LastError = null;
        }

        private void LateUpdate()
        {
            if (owner == null || owner.definition == null || root == null) return;
            if (Time.realtimeSinceStartup >= nextIdentity)
            { nextIdentity = Time.realtimeSinceStartup + 0.5f; RefreshSource(); }
            if (!ready || !shown) return;
            if (!receivedView)
            {
                Camera camera = Camera.main;
                if (camera != null) UpdateView(ViewFromCamera(camera), camera, true);
            }
            if (desired.Count == 0)
            {
                for (int face = 0; face < 6; face++) desired.Add(TerrainNodeKey.Root((PlanetCubeFace)face));
                wanted = RuntimeTerrainSelection.WithAncestors(desired);
            }
            if (selectionJob != null && selectionJob.IsCompleted)
            {
                var completed = selectionJob; selectionJob = null;
                if (completed.IsFaulted) LastError = completed.Exception.GetBaseException().Message;
                else
                {
                    desired = completed.Result; wanted = RuntimeTerrainSelection.WithAncestors(desired);
                    splits.Clear(); foreach (var key in wanted) if (!desired.Contains(key)) splits.Add(key);
                }
            }
            if (!focused && desired.Count != 6)
            {
                desired.Clear();
                for (int face = 0; face < 6; face++) desired.Add(TerrainNodeKey.Root((PlanetCubeFace)face));
                wanted = RuntimeTerrainSelection.WithAncestors(desired); splits.Clear();
            }
            if (focused && selectionJob == null && Time.realtimeSinceStartup >= nextSelection && Workers.Wait(0))
            {
                nextSelection = Time.realtimeSinceStartup + 0.3f;
                int leafBudget = math.clamp(maxActiveChunks, 6, math.max(6, (maxResidentChunks - 12) * 3 / 4));
                var snapshot = source; var cameraSnapshot = view; bool focusedSnapshot = focused;
                int level = maxLevel; double tolerance = pixelTolerance; var previous = new HashSet<TerrainNodeKey>(splits);
                selectionJob = Task.Run(() =>
                {
                    try { return RuntimeTerrainSelection.Select(snapshot, cameraSnapshot, leafBudget, level, tolerance, focusedSnapshot, previous); }
                    finally { Workers.Release(); }
                });
            }
            var timer = Stopwatch.StartNew();
            CompleteJob();
            SelectCoverage();
            Evict();
            pendingCount = 0; queue.Clear();
            foreach (var key in wanted) if (!nodes.ContainsKey(key)) { pendingCount++; queue.Add(key); }
            queue.Sort((a, b) => a.level.CompareTo(b.level));
            if (job == null && queue.Count > 0 && timer.Elapsed.TotalMilliseconds < uploadBudgetMilliseconds && Workers.Wait(0))
            {
                var key = queue[0]; var snapshot = source; var token = cancellation.Token;
                job = Task.Run(() => { try { return RuntimeTerrainChunk.Build(snapshot, key, token); } finally { Workers.Release(); } });
            }
        }

        private void CompleteJob()
        {
            if (job == null || !job.IsCompleted) return;
            var completed = job; job = null;
            if (completed.IsCanceled) return;
            if (completed.IsFaulted)
            {
                LastError = completed.Exception.GetBaseException().Message;
                UnityEngine.Debug.LogError("[Planet terrain] " + LastError, this); return;
            }
            RuntimeTerrainChunk data = completed.Result;
            if (!wanted.Contains(data.key) || nodes.ContainsKey(data.key)) return;
            var go = new GameObject("Chunk " + data.key) { hideFlags = HideFlags.HideAndDontSave, layer = gameObject.layer };
            go.transform.SetParent(root, false);
            Mesh mesh = data.Upload();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false; renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = false;
            nodes.Add(data.key, new Node { data = data, mesh = mesh, go = go, renderer = renderer });
            BuiltCount++;
        }

        private void SelectCoverage()
        {
            foreach (var node in visible) if (node.renderer != null) node.renderer.enabled = false;
            visible.Clear(); FinestVisibleLevel = 0;
            for (int face = 0; face < 6; face++) Cover(TerrainNodeKey.Root((PlanetCubeFace)face));
            foreach (var node in visible)
            {
                node.lastUsed = Time.frameCount; node.renderer.enabled = shown;
                FinestVisibleLevel = math.max(FinestVisibleLevel, node.data.key.level);
                // Surface root carries radius units; each chunk stores local metre offsets.
                node.go.transform.localPosition = (float3)(node.data.origin / source.radius);
                node.go.transform.localRotation = Quaternion.identity;
                node.go.transform.localScale = Vector3.one / (float)source.radius;
            }
            VisibleCount = visible.Count;
        }

        private void Cover(TerrainNodeKey key)
        {
            if (!nodes.TryGetValue(key, out Node node)) return;
            bool split = splits.Contains(key);
            if (split) for (int i = 0; i < 4; i++) if (!nodes.ContainsKey(key.Child(i))) { split = false; break; }
            if (split) for (int i = 0; i < 4; i++) Cover(key.Child(i));
            else visible.Add(node);
        }

        private void Evict()
        {
            int limit = math.max(16, maxResidentChunks);
            while (nodes.Count >= limit)
            {
                TerrainNodeKey oldest = default; bool found = false; int age = int.MaxValue;
                foreach (var pair in nodes)
                    if (!wanted.Contains(pair.Key) && !visible.Contains(pair.Value) && pair.Value.lastUsed <= age)
                    { oldest = pair.Key; age = pair.Value.lastUsed; found = true; }
                if (!found) break;
                DestroyNode(nodes[oldest]); nodes.Remove(oldest);
            }
        }

        public PlanetViewState ViewFromCamera(Camera camera)
        {
            Quaternion rotation = Quaternion.Inverse(transform.rotation);
            double3 delta = ((double3)(float3)camera.transform.position - (double3)(float3)transform.position) / unitsPerMetre;
            return new PlanetViewState
            {
                positionBfMetres = Rotate(rotation, delta), rotationBf = rotation * camera.transform.rotation,
                bodyRotationBfToW = transform.rotation, verticalFovDegrees = camera.fieldOfView,
                aspect = camera.aspect, pixelHeight = camera.pixelHeight
            };
        }

        private static double3 Rotate(Quaternion rotation, double3 value)
        {
            var m = new float3x3((quaternion)rotation);
            return (double3)m.c0 * value.x + (double3)m.c1 * value.y + (double3)m.c2 * value.z;
        }

        private void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            PrepareCamera(camera);
        }

        /// <summary>Also used by explicit Camera.Render captures, which do not invoke SRP camera callbacks.</summary>
        public void PrepareCamera(Camera camera)
        {
            if (!ready || !shown || owner == null) return;
            PlanetViewState cameraView = camera == viewCamera && receivedView && view.localCameraActive ? view : ViewFromCamera(camera);
            foreach (var node in visible)
            {
                if (node.renderer == null) continue;
                owner.CopyVisualProperties(node.block);
                double3 offset = Rotate(transform.rotation, node.data.origin - cameraView.positionBfMetres) * unitsPerMetre;
                node.block.SetVector("_ChunkOriginCR", new Vector4((float)offset.x, (float)offset.y, (float)offset.z, 1));
                node.block.SetFloat("_ChunkEnabled", 1);
                node.block.SetMatrix("_ChunkBodyToWorld", Matrix4x4.Rotate(transform.rotation));
                node.block.SetFloat("_ChunkUnitsPerMetre", (float)unitsPerMetre);
                node.block.SetFloat("_UseVertexAlbedo", node.data.spacing < 20000 ? 1 : 0);
                // Chunk normals already contain resolved relief; a global normal cubemap would erase it.
                node.block.SetFloat("_NormalEnabled", 0);
                node.renderer.SetPropertyBlock(node.block);
            }
        }

        private static void DestroyNode(Node node)
        {
            if (Application.isPlaying) { Destroy(node.go); Destroy(node.mesh); }
            else { DestroyImmediate(node.go); DestroyImmediate(node.mesh); }
        }

        private void Clear()
        {
            cancellation.Cancel(); cancellation.Dispose(); cancellation = new CancellationTokenSource();
            // Old tasks own no Unity objects; cancellation is checked every row and releases the global worker slot.
            if (job != null) job.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            job = null;
            if (selectionJob != null) selectionJob.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            selectionJob = null;
            foreach (var node in nodes.Values) DestroyNode(node);
            nodes.Clear(); visible.Clear(); wanted.Clear(); desired.Clear(); splits.Clear();
            pendingCount = 0; VisibleCount = 0; ready = false;
        }
    }
}
