using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Planet.Procedural.Terrain
{
    /// <summary>On-demand bounded mesh cache. At most two worker jobs globally, one per body, no Unity calls on workers.</summary>
    [ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(1100)]
    public sealed class RuntimeTerrainRenderer : MonoBehaviour, IPlanetSurfaceQuery, IPlanetCollisionSurfaceQuery
    {
        [Range(6, 512)] public int maxActiveChunks = 384;
        [Range(16, 1024)] public int maxResidentChunks = 640;
        [Range(0, TerrainLattice.MaxLevel)] public int maxLevel = TerrainLattice.MaxLevel;
        [Range(0.5f, 12f)] public float pixelTolerance = 3;
        [Min(0.1f)] public float uploadBudgetMilliseconds = 2;
        [Header("Ground neighbourhood")]
        public TerrainNeighbourhoodPolicy neighbourhood = TerrainNeighbourhoodPolicy.Default;
        [Range(192, 2048)] public int nearActiveChunks = 1024;
        [Range(256, 4096)] public int nearResidentChunks = 1536;
        public int ResidentBudget { get; private set; }

        public int ResidentCount { get { return nodes.Count; } }
        public int VisibleCount { get; private set; }
        public int PendingWork { get { return pendingCount; } }
        public int FinestVisibleLevel { get; private set; }
        public int BuiltCount { get; private set; }
        public int CollisionChunkCount { get { return collisionNodes.Count; } }
        public string LastError { get; private set; }
        public RuntimeTerrainSource Source { get { return source; } }
        public double RadiusMetres { get { return ready ? source.radius : 0; } }
        public int PendingDetailWork { get { return pendingCount; } }
        public bool CoverageReady
        {
            get
            {
                for (int face = 0; face < 6; face++)
                    if (!nodes.ContainsKey(TerrainNodeKey.Root((PlanetCubeFace)face))) return false;
                return true;
            }
        }
        public bool CollisionCoverageReady
        {
            get
            {
                if (!collisionRequested || collisionNodes.Count == 0) return false;
                double length = math.length(collisionPlayerBf);
                if (!(length > 0)) return false;
                return HasCollisionAt(collisionPlayerBf);
            }
        }
        public double SampleCollisionRadiusMetres(double3 directionBf)
        {
            if (!ready) return 0;
            double3 direction = math.normalizesafe(directionBf, new double3(0, 1, 0));
            double spacing = 0.005;
            foreach (Node node in visible)
                if (node.data.TrySurfaceRadius(direction, out double displayedRadius)) return displayedRadius;
            return math.length(source.Position(direction, spacing));
        }

        public bool SweepSphere(double3 fromBfMetres, double3 toBfMetres, double radiusMetres,
            out double3 resolvedBfMetres, out double3 normalBf)
        {
            resolvedBfMetres = toBfMetres;
            normalBf = double3.zero;
            if (!ready || !source.solid) return false;
            double3 delta = toBfMetres - fromBfMetres;
            double distance = math.length(delta);
            if (!(distance > 1e-7)) return false;
            double envelope = source.radius * math.cmax(source.axes) + source.ReliefBound + radiusMetres;
            double closestT = math.clamp(-math.dot(fromBfMetres, delta) / math.lengthsq(delta), 0, 1);
            if (math.length(fromBfMetres + delta * closestT) > envelope) return false;
            if (!CollisionCoverageReady || !collisionScene.IsValid())
            {
                // Until collision data exists, the same source's relief bound is a conservative
                // loading barrier. Sweep the whole segment so a high-speed transit cannot tunnel.
                double a = math.dot(delta, delta), b = math.dot(fromBfMetres, delta);
                double c = math.dot(fromBfMetres, fromBfMetres) - envelope * envelope;
                double discriminant = b * b - a * c;
                if (c > 0 && b < 0 && discriminant >= 0)
                {
                    double t = (-b - math.sqrt(discriminant)) / a;
                    if (t >= 0 && t <= 1)
                    {
                        resolvedBfMetres = math.lerp(fromBfMetres, toBfMetres, math.max(0, t - 0.02 / distance));
                        normalBf = math.normalizesafe(resolvedBfMetres);
                        return true;
                    }
                }
                if (c <= 0 && closestT > 0)
                {
                    resolvedBfMetres = fromBfMetres;
                    normalBf = -delta / distance;
                    return true;
                }
                double3 directionBf = math.normalizesafe(toBfMetres, math.normalizesafe(fromBfMetres));
                double ground = SampleCollisionRadiusMetres(directionBf) + radiusMetres;
                if (math.length(toBfMetres) < ground)
                {
                    resolvedBfMetres = directionBf * ground;
                    normalBf = directionBf;
                    return true;
                }
                return false;
            }
            Vector3 direction = (Vector3)(float3)(delta / distance);
            Vector3 start = (Vector3)(float3)(fromBfMetres - collisionOriginBf);
            if (!collisionScene.GetPhysicsScene().SphereCast(start, (float)radiusMetres, direction,
                out RaycastHit hit, (float)distance, ~0, QueryTriggerInteraction.Ignore))
            {
                // Keep the whole movement corridor inside committed collision coverage while
                // streaming. Test at half the smallest resident patch width, not just endpoints.
                double spacing = double.MaxValue;
                foreach (Node node in collisionNodes.Values)
                    if (visible.Contains(node)) spacing = math.min(spacing,
                        TerrainLattice.NodeSize(source.radius, node.data.key.level) * 0.25);
                int steps = (int)math.min(512, math.max(1, math.ceil(distance / math.max(0.05, spacing))));
                double checkedDistance = math.min(distance, steps * math.max(0.05, spacing));
                for (int i = 1; i <= steps; i++)
                {
                    double t = checkedDistance / distance * i / steps;
                    if (HasCollisionAt(math.lerp(fromBfMetres, toBfMetres, t))) continue;
                    resolvedBfMetres = math.lerp(fromBfMetres, toBfMetres,
                        checkedDistance / distance * (i - 1) / steps);
                    normalBf = -delta / distance;
                    return true;
                }
                if (checkedDistance < distance)
                {
                    resolvedBfMetres = fromBfMetres + delta / distance * checkedDistance;
                    normalBf = -delta / distance;
                    return true;
                }
                return false;
            }
            resolvedBfMetres = fromBfMetres + delta / distance * math.max(0.0, hit.distance - 0.02);
            normalBf = (double3)(float3)hit.normal;
            return true;
        }

        private bool HasCollisionAt(double3 position)
        {
            double3 direction = math.normalizesafe(position, new double3(0, 1, 0));
            foreach (var pair in collisionNodes)
                if (visible.Contains(pair.Value) && TerrainLattice.NodeAt(direction, pair.Key.level).Equals(pair.Key))
                    return true;
            return false;
        }
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
            public GameObject colliderGo;
            public Mesh colliderMesh;
            public GameObject localGo;
            public MeshRenderer localRenderer;
            public MaterialPropertyBlock localBlock = new MaterialPropertyBlock();
            public Mesh oceanMesh;
            public GameObject oceanGo, localOceanGo;
            public MeshRenderer oceanRenderer, localOceanRenderer;
            public MaterialPropertyBlock oceanBlock = new MaterialPropertyBlock();
            public MaterialPropertyBlock localOceanBlock = new MaterialPropertyBlock();
        }
        private static readonly SemaphoreSlim Workers = new SemaphoreSlim(2, 2);
        private readonly Dictionary<TerrainNodeKey, Node> nodes = new Dictionary<TerrainNodeKey, Node>();
        private readonly List<Node> visible = new List<Node>();
        private HashSet<TerrainNodeKey> desired = new HashSet<TerrainNodeKey>();
        private HashSet<TerrainNodeKey> wanted = new HashSet<TerrainNodeKey>();
        private readonly HashSet<TerrainNodeKey> splits = new HashSet<TerrainNodeKey>();
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
        private int activeBudget;
        private double observerClearance;
        private Camera localCamera;
        private int localTerrainLayer = 31;
        private bool streamingRequested, collisionRequested;
        private double3 collisionPlayerBf, collisionVelocityBf, collisionOriginBf;
        private Scene collisionScene;
        private readonly Dictionary<TerrainNodeKey, Node> collisionNodes = new Dictionary<TerrainNodeKey, Node>();

        public void SetStreamingRequested(bool requested)
        {
            if (streamingRequested == requested) return;
            streamingRequested = requested;
            if (!requested)
            {
                bool sourceWasReady = ready;
                Clear();
                ready = sourceWasReady;
            }
        }

        public void SetLocalCamera(Camera camera, int layer)
        {
            localCamera = camera;
            localTerrainLayer = layer;
        }

        public void SetCollisionObserver(double3 playerBfMetres, double3 velocityBfMetresPerSecond, bool active)
        {
            collisionRequested = active && ready && source.solid;
            collisionPlayerBf = playerBfMetres;
            collisionVelocityBf = velocityBfMetresPerSecond;
            if (!collisionRequested && collisionNodes.Count > 0) ClearColliders();
        }

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
            if (!value) foreach (var node in nodes.Values)
            {
                if (node.localRenderer != null) node.localRenderer.enabled = false;
                if (node.oceanRenderer != null) node.oceanRenderer.enabled = false;
                if (node.localOceanRenderer != null) node.localOceanRenderer.enabled = false;
            }
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
            if (!ready || !streamingRequested) return;
            double3 cameraDirection = math.normalizesafe(view.positionBfMetres, new double3(0, 1, 0));
            observerClearance = math.abs(math.length(view.positionBfMetres) - math.length(source.Position(cameraDirection, 1)));
            bool groundNeighbourhood = view.localCameraActive && source.solid && observerClearance <= neighbourhood.overviewAltitudeMetres * 1.2;
            activeBudget = groundNeighbourhood ? math.max(maxActiveChunks, nearActiveChunks) : maxActiveChunks;
            ResidentBudget = groundNeighbourhood ? math.max(maxResidentChunks, nearResidentChunks) : maxResidentChunks;
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
                int leafBudget = math.clamp(activeBudget, 6, math.max(6, (ResidentBudget - 12) * 3 / 4));
                var snapshot = source; var cameraSnapshot = view; bool focusedSnapshot = focused;
                int level = maxLevel; double tolerance = pixelTolerance; var previous = new HashSet<TerrainNodeKey>(splits);
                var policy = neighbourhood;
                selectionJob = Task.Run(() =>
                {
                    try { return RuntimeTerrainSelection.Select(snapshot, cameraSnapshot, leafBudget, level, tolerance, focusedSnapshot, previous, policy); }
                    finally { Workers.Release(); }
                });
            }
            var timer = Stopwatch.StartNew();
            CompleteJob();
            SelectCoverage();
            Evict();
            SyncColliders(timer);
            // Only one chunk can start per frame. Sorting all pending chunks recomputed surface
            // distances O(n log n) times and could consume the entire upload budget forever.
            pendingCount = 0;
            TerrainNodeKey next = default;
            int bestPriority = -1;
            double bestDistance = double.PositiveInfinity;
            foreach (var key in wanted)
            {
                if (nodes.ContainsKey(key)) continue;
                pendingCount++;
                if (job != null) continue;
                int priority = RequestPriority(key);
                if (priority < bestPriority || (priority == bestPriority && key.level > next.level)) continue;
                double distance = NodeDistanceSquared(key);
                if (priority > bestPriority || key.level < next.level || distance < bestDistance)
                { next = key; bestPriority = priority; bestDistance = distance; }
            }
            if (job == null && pendingCount > 0 && Workers.Wait(0))
            {
                var key = next; var snapshot = source; var token = cancellation.Token;
                job = Task.Run(() => { try { return RuntimeTerrainChunk.Build(snapshot, key, token); } finally { Workers.Release(); } });
            }
        }

        private int RequestPriority(TerrainNodeKey key)
        {
            if (key.level == 0) return 3;
            double3 direction = math.normalizesafe(view.positionBfMetres, new double3(0, 1, 0));
            // Refinement is an atomic group of four. Prioritizing only the intersecting child
            // leaves its siblings behind every finer request, pinning the parent for minutes.
            TerrainNodeKey parent = key.Parent;
            double distance = TerrainNeighbourhoodPolicy.SurfaceDistanceToNode(direction, parent, source.radius);
            int required = neighbourhood.RequiredLevel(source.radius, observerClearance, distance);
            return parent.level < required ? 2 : TerrainLattice.NodeAt(direction, parent.level).Equals(parent) ? 1 : 0;
        }

        private double NodeDistanceSquared(TerrainNodeKey key)
        {
            TerrainLattice.NodeUv(key, out double2 min, out double2 max);
            double3 center = TerrainLattice.Direction(key.face, (min + max) * 0.5) * source.radius;
            return math.distancesq(center, view.positionBfMetres);
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
            var node = new Node { data = data, mesh = mesh, go = go, renderer = renderer };
            if (data.oceanVertices != null && owner.OceanMaterial != null)
            {
                node.oceanMesh = data.UploadOceanMesh();
                node.oceanGo = new GameObject("Ocean " + data.key) { hideFlags = HideFlags.HideAndDontSave, layer = gameObject.layer };
                node.oceanGo.transform.SetParent(root, false);
                node.oceanRenderer = AddOceanRenderer(node.oceanGo, node.oceanMesh);
            }
            nodes.Add(data.key, node);
            BuiltCount++;
        }

        private void SelectCoverage()
        {
            foreach (var node in visible) if (node.renderer != null) node.renderer.enabled = false;
            foreach (var node in nodes.Values)
            {
                if (node.localRenderer != null) node.localRenderer.enabled = false;
                if (node.oceanRenderer != null) node.oceanRenderer.enabled = false;
                if (node.localOceanRenderer != null) node.localOceanRenderer.enabled = false;
                if (node.colliderGo != null) node.colliderGo.GetComponent<MeshCollider>().enabled = false;
            }
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
                if (node.oceanGo != null)
                {
                    node.oceanGo.transform.localPosition = (float3)(node.data.oceanOrigin / source.radius);
                    node.oceanGo.transform.localScale = Vector3.one / (float)source.radius;
                    node.oceanRenderer.enabled = shown;
                }
                if (node.colliderGo != null) node.colliderGo.GetComponent<MeshCollider>().enabled = true;
            }
            VisibleCount = visible.Count;
        }

        private void Cover(TerrainNodeKey key)
        {
            if (!nodes.TryGetValue(key, out Node node)) return;
            bool split = splits.Contains(key);
            if (split) for (int i = 0; i < 4; i++) if (!nodes.ContainsKey(key.Child(i))) { split = false; break; }
            if (split && collisionRequested && collisionNodes.ContainsKey(key))
                for (int i = 0; i < 4; i++)
                    if (!collisionNodes.ContainsKey(key.Child(i))) { split = false; break; }
            if (split) for (int i = 0; i < 4; i++) Cover(key.Child(i));
            else visible.Add(node);
        }

        private void Evict()
        {
            int limit = math.max(16, ResidentBudget);
            while (nodes.Count >= limit)
            {
                TerrainNodeKey oldest = default; bool found = false; int age = int.MaxValue;
                foreach (var pair in nodes)
                    if (!wanted.Contains(pair.Key) && !visible.Contains(pair.Value) &&
                        !collisionNodes.ContainsKey(pair.Key) && pair.Value.lastUsed <= age)
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
            double3 q = new double3(rotation.x, rotation.y, rotation.z);
            double w = rotation.w;
            double norm = math.dot(q, q) + w * w;
            if (norm < 1e-20) return value;
            double3 t = 2 * math.cross(q, value);
            return value + (w * t + math.cross(q, t)) / norm;
        }

        private void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            PrepareCamera(camera);
        }

        /// <summary>Also used by explicit Camera.Render captures, which do not invoke SRP camera callbacks.</summary>
        public void PrepareCamera(Camera camera)
        {
            if (!ready || !shown || owner == null) return;
            if (camera == localCamera) return;
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
                PrepareOcean(node, cameraView, false);
                PrepareLocalNode(node, cameraView);
            }
        }

        private void PrepareLocalNode(Node node, PlanetViewState cameraView)
        {
            if (localCamera == null) return;
            double3 offsetMetres = Rotate(transform.rotation, node.data.origin - cameraView.positionBfMetres);
            bool close = math.length(offsetMetres) < 10000.0 + TerrainLattice.NodeSize(source.radius, node.data.key.level);
            if (!close || node.data.key.level < 8)
            {
                if (node.localRenderer != null) node.localRenderer.enabled = false;
                return;
            }
            if (node.localGo == null)
            {
                node.localGo = new GameObject("Local " + node.data.key)
                    { hideFlags = HideFlags.HideAndDontSave, layer = localTerrainLayer };
                node.localGo.AddComponent<MeshFilter>().sharedMesh = node.mesh;
                node.localRenderer = node.localGo.AddComponent<MeshRenderer>();
                node.localRenderer.sharedMaterial = material;
                node.localRenderer.shadowCastingMode = ShadowCastingMode.Off;
                node.localRenderer.receiveShadows = false;
            }
            node.localGo.transform.SetPositionAndRotation((float3)offsetMetres, transform.rotation);
            node.localRenderer.enabled = shown;
            var localBlock = node.localBlock;
            owner.CopyVisualProperties(localBlock);
            localBlock.SetVector("_ChunkOriginCR", new Vector4((float)offsetMetres.x, (float)offsetMetres.y, (float)offsetMetres.z, 1));
            localBlock.SetFloat("_ChunkEnabled", 1);
            localBlock.SetMatrix("_ChunkBodyToWorld", Matrix4x4.Rotate(transform.rotation));
            localBlock.SetFloat("_ChunkUnitsPerMetre", 1);
            localBlock.SetFloat("_LocalClipDistance", 4500);
            localBlock.SetFloat("_UseVertexAlbedo", node.data.spacing < 20000 ? 1 : 0);
            localBlock.SetFloat("_NormalEnabled", 0);
            node.localRenderer.SetPropertyBlock(localBlock);
            PrepareOcean(node, cameraView, true);
        }

        private MeshRenderer AddOceanRenderer(GameObject go, Mesh mesh)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = owner.OceanMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false; renderer.enabled = false;
            return renderer;
        }

        private void PrepareOcean(Node node, PlanetViewState cameraView, bool local)
        {
            if (node.oceanMesh == null) return;
            double3 offset = Rotate(transform.rotation, node.data.oceanOrigin - cameraView.positionBfMetres);
            if (local && node.localOceanGo == null)
            {
                node.localOceanGo = new GameObject("Local Ocean " + node.data.key)
                    { hideFlags = HideFlags.HideAndDontSave, layer = localTerrainLayer };
                node.localOceanRenderer = AddOceanRenderer(node.localOceanGo, node.oceanMesh);
            }
            if (local) node.localOceanGo.transform.SetPositionAndRotation((float3)offset, transform.rotation);
            else offset *= unitsPerMetre;
            var block = local ? node.localOceanBlock : node.oceanBlock;
            block.SetVector("_ChunkOriginCR", new Vector4((float)offset.x, (float)offset.y, (float)offset.z, 1));
            block.SetFloat("_ChunkEnabled", 1);
            block.SetMatrix("_ChunkBodyToWorld", Matrix4x4.Rotate(transform.rotation));
            block.SetFloat("_ChunkUnitsPerMetre", local ? 1f : (float)unitsPerMetre);
            block.SetFloat("_OceanProjection", local ? 2 : 0);
            var renderer = local ? node.localOceanRenderer : node.oceanRenderer;
            renderer.enabled = shown; renderer.SetPropertyBlock(block);
            if (local)
            {
                node.oceanBlock.SetFloat("_OceanProjection", 1);
                node.oceanRenderer.SetPropertyBlock(node.oceanBlock);
            }
        }

        private void DestroyNode(Node node)
        {
            if (Application.isPlaying)
            { Destroy(node.localOceanGo); Destroy(node.oceanGo); Destroy(node.oceanMesh); }
            else
            { DestroyImmediate(node.localOceanGo); DestroyImmediate(node.oceanGo); DestroyImmediate(node.oceanMesh); }
            collisionNodes.Remove(node.data.key);
            if (node.localGo != null)
            {
                if (Application.isPlaying) Destroy(node.localGo); else DestroyImmediate(node.localGo);
            }
            if (node.colliderGo != null)
            {
                if (Application.isPlaying) { Destroy(node.colliderGo); Destroy(node.colliderMesh); }
                else { DestroyImmediate(node.colliderGo); DestroyImmediate(node.colliderMesh); }
            }
            if (Application.isPlaying) { Destroy(node.go); Destroy(node.mesh); }
            else { DestroyImmediate(node.go); DestroyImmediate(node.mesh); }
        }

        private void Clear()
        {
            ClearColliders();
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

        private void SyncColliders(Stopwatch timer)
        {
            if (!Application.isPlaying || !collisionRequested || visible.Count == 0) return;
            if (!collisionScene.IsValid())
            {
                collisionScene = SceneManager.CreateScene("PlanetCollision_" + Guid.NewGuid().ToString("N"),
                    new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                collisionOriginBf = collisionPlayerBf;
            }
            if (math.distance(collisionOriginBf, collisionPlayerBf) > 64.0)
                collisionOriginBf = collisionPlayerBf;

            double3 direction = math.normalizesafe(collisionPlayerBf, new double3(0, 1, 0));
            double3 ahead = math.normalizesafe(collisionPlayerBf + collisionVelocityBf * 2.0, collisionPlayerBf);
            double3 aheadDirection = math.normalizesafe(ahead, direction);
            int built = 0;
            var candidates = new List<Node>();
            foreach (Node parent in visible)
            {
                candidates.Add(parent);
                if (!splits.Contains(parent.data.key) || !collisionNodes.ContainsKey(parent.data.key)) continue;
                for (int i = 0; i < 4; i++)
                    if (nodes.TryGetValue(parent.data.key.Child(i), out Node child)) candidates.Add(child);
            }
            candidates.Sort((a, b) => math.distancesq(a.data.origin, collisionPlayerBf)
                .CompareTo(math.distancesq(b.data.origin, collisionPlayerBf)));
            foreach (Node node in candidates)
            {
                TerrainNodeKey key = node.data.key;
                bool underfoot = TerrainLattice.NodeAt(direction, key.level).Equals(key);
                bool forward = TerrainLattice.NodeAt(aheadDirection, key.level).Equals(key);
                bool replacingParent = key.level > 0 && collisionNodes.ContainsKey(key.Parent);
                double range = math.clamp(math.length(collisionVelocityBf) * 2.0, 128.0, 10000.0);
                if (!underfoot && !forward && !replacingParent &&
                    math.distance(node.data.origin, collisionPlayerBf) > range) continue;
                if (node.colliderGo == null && built < 1 && collisionNodes.Count < 192 &&
                    timer.Elapsed.TotalMilliseconds < uploadBudgetMilliseconds)
                {
                    CreateCollider(node);
                    built++;
                }
            }
            foreach (Node node in collisionNodes.Values)
                if (node.colliderGo != null)
                    node.colliderGo.transform.position = (Vector3)(float3)(node.data.origin - collisionOriginBf);
            Physics.SyncTransforms();

            var remove = new List<TerrainNodeKey>();
            foreach (var pair in collisionNodes)
            {
                bool current = visible.Contains(pair.Value);
                bool replacing = pair.Key.level > 0 && nodes.TryGetValue(pair.Key.Parent, out Node parent) &&
                    visible.Contains(parent) && collisionNodes.ContainsKey(pair.Key.Parent);
                if (replacing) continue;
                bool underfoot = TerrainLattice.NodeAt(direction, pair.Key.level).Equals(pair.Key);
                bool forward = TerrainLattice.NodeAt(aheadDirection, pair.Key.level).Equals(pair.Key);
                bool near = math.distance(pair.Value.data.origin, collisionPlayerBf) <
                    math.clamp(math.length(collisionVelocityBf) * 3.0, 256.0, 15000.0);
                if (!underfoot && !forward && !near) remove.Add(pair.Key);
                else if (!current && !near) remove.Add(pair.Key);
                else if (!current && HasChildrenColliders(pair.Key)) remove.Add(pair.Key);
            }
            foreach (TerrainNodeKey key in remove)
            {
                Node node = collisionNodes[key];
                DestroyCollider(node);
                collisionNodes.Remove(key);
            }
        }

        private bool HasChildrenColliders(TerrainNodeKey parent)
        {
            for (int i = 0; i < 4; i++) if (!collisionNodes.ContainsKey(parent.Child(i))) return false;
            return true;
        }

        private void CreateCollider(Node node)
        {
            Mesh mesh = node.data.UploadCollisionMesh();
            var go = new GameObject("Collision " + node.data.key) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(go, collisionScene);
            go.transform.position = (Vector3)(float3)(node.data.origin - collisionOriginBf);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            node.colliderGo = go;
            node.colliderMesh = mesh;
            collisionNodes[node.data.key] = node;
        }

        private static void DestroyCollider(Node node)
        {
            if (node.colliderGo == null) return;
            Destroy(node.colliderGo);
            Destroy(node.colliderMesh);
            node.colliderGo = null;
            node.colliderMesh = null;
        }

        private void ClearColliders()
        {
            foreach (Node node in collisionNodes.Values) DestroyCollider(node);
            collisionNodes.Clear();
            if (collisionScene.IsValid() && Application.isPlaying)
            {
                SceneManager.UnloadSceneAsync(collisionScene);
                collisionScene = default(Scene);
            }
        }
    }
}
