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
        [Range(0, TerrainLattice.MaxLevel)] public int visualMaxLevel = 20;
        [Min(0.05f)] public float collisionSpacingMetres = 1f;
        public bool proceduralDetailEnabled = true;
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
        public int FinestCollisionLevel { get; private set; }
        public int BuiltCount { get; private set; }
        public int CollisionChunkCount { get { return collisionNodes.Count; } }
        public int CoverageRevision { get; private set; }
        public double LastUpdateMilliseconds { get; private set; }
        public int FallbackSweepCount { get; private set; }
        public string LastError { get; private set; }
        public RuntimeTerrainSource Source { get { return source; } }
        public double RadiusMetres { get { return ready ? source.radius : 0; } }
        public int PendingDetailWork { get { return pendingCount; } }
        public int VisualMaxLevel { get { return EffectiveVisualMaxLevel; } }
        public double CollisionSpacingMetres { get { return math.max(0.05, collisionSpacingMetres); } }
        public int CollisionLevel
        {
            get { return ready ? TerrainLattice.LevelForSpacing(source.radius, CollisionSpacingMetres) : 0; }
        }
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
            Node node = CollisionNodeAt(direction);
            if (node != null && node.data.key.level >= CollisionLevel &&
                node.data.TrySurfaceRadius(direction, out double displayedRadius)) return displayedRadius;
            return math.length(source.Position(direction, CollisionSpacingMetres));
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
            Vector3 direction = (Vector3)(float3)(delta / distance);
            Vector3 start = (Vector3)(float3)(fromBfMetres - collisionOriginBf);
            RaycastHit hit = default;
            bool hitMesh = collisionScene.IsValid() && collisionScene.GetPhysicsScene().SphereCast(start,
                (float)radiusMetres, direction, out hit, (float)distance, ~0, QueryTriggerInteraction.Ignore);
            if (hitMesh)
            {
                resolvedBfMetres = fromBfMetres + delta / distance * math.max(0, hit.distance - 0.02);
                normalBf = (double3)(float3)hit.normal;
            }
            // A cooked collider of one committed convex-UV patch covers this short corridor.
            // Missing patches use the same displayed height field instead of fabricating a wall.
            Node fromNode = CollisionNodeAt(math.normalizesafe(fromBfMetres));
            bool covered = fromNode != null && fromNode.collider != null && fromNode.collider.enabled &&
                ReferenceEquals(fromNode, CollisionNodeAt(math.normalizesafe(toBfMetres))) &&
                ReferenceEquals(fromNode, CollisionNodeAt(math.normalizesafe((fromBfMetres + toBfMetres) * 0.5))) &&
                distance < TerrainLattice.NodeSize(source.radius, fromNode.data.key.level);
            if (covered) return hitMesh;
            FallbackSweepCount++;
            if (TerrainSurfaceSweep.Sweep(fromBfMetres, resolvedBfMetres, radiusMetres, envelope,
                SampleCollisionRadiusMetres, out double3 fallback, out double3 fallbackNormal))
            { resolvedBfMetres = fallback; normalBf = fallbackNormal; return true; }
            return hitMesh;
        }

        private bool HasCollisionAt(double3 position)
        {
            double3 direction = math.normalizesafe(position, new double3(0, 1, 0));
            Node node = CollisionNodeAt(direction);
            return node != null && node.data.key.level >= CollisionLevel;
        }

        private Node CollisionNodeAt(double3 direction)
        {
            var key = TerrainLattice.NodeAt(direction, TerrainLattice.MaxLevel);
            for (int level = TerrainLattice.MaxLevel; level >= 0; level--)
            {
                TerrainNodeKey ancestor = key.Ancestor(level);
                if (collisionNodes.TryGetValue(ancestor, out Node node) && node.collider != null && node.collider.enabled)
                    return node;
            }
            return null;
        }

        private int EffectiveVisualMaxLevel
        {
            get { return math.clamp(math.min(maxLevel, visualMaxLevel), 0, TerrainLattice.MaxLevel); }
        }

        private Node VisibleNodeAt(double3 direction)
        {
            var key = TerrainLattice.NodeAt(direction, TerrainLattice.MaxLevel);
            for (int level = TerrainLattice.MaxLevel; level >= 0; level--)
                if (visibleKeys.Contains(key.Ancestor(level)) && nodes.TryGetValue(key.Ancestor(level), out Node node)) return node;
            return null;
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
            public MeshCollider collider;
            public GameObject localGo;
            public MeshRenderer localRenderer;
            public MaterialPropertyBlock localBlock = new MaterialPropertyBlock();
            public Mesh oceanMesh;
            public GameObject oceanGo, localOceanGo;
            public MeshRenderer oceanRenderer, localOceanRenderer;
            public MaterialPropertyBlock oceanBlock = new MaterialPropertyBlock();
            public MaterialPropertyBlock localOceanBlock = new MaterialPropertyBlock();
        }

        public struct DebugChunk
        {
            public RuntimeTerrainChunk data;
            public MeshCollider collider;
            public bool visible;
            public bool collisionRelevant;
        }
        private static readonly SemaphoreSlim Workers = new SemaphoreSlim(2, 2);
        private readonly Dictionary<TerrainNodeKey, Node> nodes = new Dictionary<TerrainNodeKey, Node>();
        private readonly List<Node> visible = new List<Node>();
        private readonly HashSet<TerrainNodeKey> visibleKeys = new HashSet<TerrainNodeKey>();
        private readonly HashSet<TerrainNodeKey> nextKeys = new HashSet<TerrainNodeKey>();
        private readonly List<Node> nextVisible = new List<Node>();
        private bool coverageDirty = true, physicsDirty;
        private readonly List<Node> collisionCandidates = new List<Node>();
        private readonly List<TerrainNodeKey> retiredColliders = new List<TerrainNodeKey>();
        private double3 lastCollisionObserver;
        private float nextCollisionPlan;
        private int plannedCoverageRevision = -1;
        private readonly Stopwatch updateTimer = new Stopwatch();
        private PlanetViewState selectedView;
        private bool hasSelectedView;
        private int selectedBudget, selectedMaxLevel;
        private float selectedTolerance;
        private TerrainNeighbourhoodPolicy selectedPolicy;
        private double buildStarted, meanBuildSeconds = 0.1;
        private double PrefetchSeconds => math.clamp(2 + meanBuildSeconds * 4, 2, 6);
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

        public void CopyDebugChunks(List<DebugChunk> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            if (!ready) return;
            double3 direction = math.normalizesafe(collisionPlayerBf, new double3(0, 1, 0));
            double3 ahead = math.normalizesafe(collisionPlayerBf + collisionVelocityBf * 2.0, collisionPlayerBf);
            foreach (Node node in nodes.Values)
            {
                bool isVisible = visibleKeys.Contains(node.data.key);
                bool isCollision = collisionNodes.ContainsKey(node.data.key);
                if (!isVisible && !isCollision) continue;
                bool relevant = collisionRequested && node.data.key.level <= CollisionLevel &&
                    (TerrainLattice.NodeAt(direction, node.data.key.level).Equals(node.data.key) ||
                     TerrainLattice.NodeAt(ahead, node.data.key.level).Equals(node.data.key) ||
                     math.distance(node.data.origin, collisionPlayerBf) < math.clamp(math.length(collisionVelocityBf) * 2.0, 128.0, 10000.0));
                destination.Add(new DebugChunk
                {
                    data = node.data,
                    collider = node.collider,
                    visible = isVisible,
                    collisionRelevant = relevant
                });
            }
        }

        public Vector3 DebugProjectPoint(Camera camera, double3 bodyFixedMetres)
        {
            if (camera == null) return Vector3.zero;
            PlanetViewState cameraView = camera == localCamera && receivedView
                ? view : (camera == viewCamera && receivedView && view.localCameraActive ? view : ViewFromCamera(camera));
            double3 offset = Rotate(transform.rotation, bodyFixedMetres - cameraView.positionBfMetres);
            Vector3 worldOffset = (Vector3)(float3)(offset * (camera == localCamera ? 1.0 : unitsPerMetre));
            return Quaternion.Inverse(camera.transform.rotation) * worldOffset;
        }

        public double DebugRangeScale(Camera camera) { return camera == localCamera ? 1.0 : unitsPerMetre; }

        public void SetCollisionObserver(double3 playerBfMetres, double3 velocityBfMetresPerSecond, bool active)
        {
            bool previouslyRequested = collisionRequested;
            collisionRequested = active && ready && source.solid;
            if (previouslyRequested != collisionRequested) coverageDirty = true;
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
            if (owner != null && owner.definition != null)
            {
                double next = radiusUnits / owner.definition.physical.radiusMetres;
                if (next != unitsPerMetre) coverageDirty = true;
                unitsPerMetre = next;
            }
        }

        public void UpdateView(PlanetViewState state, Camera camera, bool isFocused)
        {
            view = state; viewCamera = camera; focused = isFocused; receivedView = true;
        }

        public void SetVisible(bool value)
        {
            if (shown == value) return;
            shown = value;
            coverageDirty = true;
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
            updateTimer.Restart();
            double3 cameraDirection = math.normalizesafe(view.positionBfMetres, new double3(0, 1, 0));
            observerClearance = math.abs(math.length(view.positionBfMetres) - math.length(source.Position(cameraDirection, 1)));
            bool groundNeighbourhood = view.localCameraActive && source.solid &&
                TerrainNeighbourhoodPolicy.PredictedClearance(view, observerClearance, PrefetchSeconds) <= neighbourhood.overviewAltitudeMetres * 2.3;
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
                coverageDirty = true;
            }
            if (selectionJob != null && selectionJob.IsCompleted)
            {
                var completed = selectionJob; selectionJob = null;
                if (completed.IsFaulted) LastError = completed.Exception.GetBaseException().Message;
                else
                {
                    if (!desired.SetEquals(completed.Result))
                    {
                        desired = completed.Result; wanted = RuntimeTerrainSelection.WithAncestors(desired);
                        splits.Clear(); foreach (var key in wanted) if (!desired.Contains(key)) splits.Add(key);
                        coverageDirty = true;
                    }
                }
            }
            if (!focused && desired.Count != 6)
            {
                desired.Clear();
                for (int face = 0; face < 6; face++) desired.Add(TerrainNodeKey.Root((PlanetCubeFace)face));
                wanted = RuntimeTerrainSelection.WithAncestors(desired); splits.Clear();
                coverageDirty = true; hasSelectedView = false;
            }
            if (focused && selectionJob == null && Time.realtimeSinceStartup >= nextSelection &&
                NeedsSelection() && Workers.Wait(0))
            {
                nextSelection = Time.realtimeSinceStartup + 0.3f;
                int leafBudget = math.clamp(activeBudget, 6, math.max(6, (ResidentBudget - 12) * 3 / 4));
                var snapshot = source; var cameraSnapshot = view; bool focusedSnapshot = focused;
                int level = EffectiveVisualMaxLevel; double tolerance = pixelTolerance; var previous = new HashSet<TerrainNodeKey>(splits);
                var policy = neighbourhood;
                double lookAheadSeconds = PrefetchSeconds;
                selectedView = view; selectedBudget = activeBudget; selectedMaxLevel = level;
                selectedTolerance = pixelTolerance; selectedPolicy = policy; hasSelectedView = true;
                selectionJob = Task.Run(() =>
                {
                    try { return RuntimeTerrainSelection.Select(snapshot, cameraSnapshot, leafBudget, level, tolerance, focusedSnapshot, previous, policy, lookAheadSeconds); }
                    finally { Workers.Release(); }
                });
            }
            var timer = updateTimer;
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
            if (job == null && pendingCount > 0 && nodes.Count < ResidentBudget && Workers.Wait(0))
            {
                buildStarted = Time.realtimeSinceStartupAsDouble;
                var key = next; var snapshot = source; var token = cancellation.Token;
                job = Task.Run(() => { try { return RuntimeTerrainChunk.Build(snapshot, key, token); } finally { Workers.Release(); } });
            }
            LastUpdateMilliseconds = updateTimer.Elapsed.TotalMilliseconds;
        }

        private bool NeedsSelection()
        {
            if (!hasSelectedView || selectedBudget != activeBudget || selectedMaxLevel != EffectiveVisualMaxLevel ||
                selectedTolerance != pixelTolerance || !selectedPolicy.Equals(neighbourhood)) return true;
            double moveThreshold = math.clamp(observerClearance * 0.02, 0.25, 128);
            return math.distance(selectedView.positionBfMetres, view.positionBfMetres) > moveThreshold ||
                math.distance(selectedView.velocityBfMetresPerSecond, view.velocityBfMetresPerSecond) > 1 ||
                math.abs(math.dot(selectedView.rotationBf.value, view.rotationBf.value)) < 0.999f ||
                selectedView.pixelHeight != view.pixelHeight || selectedView.aspect != view.aspect ||
                selectedView.verticalFovDegrees != view.verticalFovDegrees;
        }

        private int RequestPriority(TerrainNodeKey key)
        {
            if (key.level == 0) return 5;
            double3 direction = math.normalizesafe(view.positionBfMetres, new double3(0, 1, 0));
            // Refinement is an atomic group of four. Prioritizing only the intersecting child
            // leaves its siblings behind every finer request, pinning the parent for minutes.
            TerrainNodeKey parent = key.Parent;
            bool underfoot = TerrainLattice.NodeAt(direction, parent.level).Equals(parent);
            if (underfoot && TerrainLattice.Spacing(source.radius, parent.level) > math.max(0.01, observerClearance * 0.02)) return 4;
            double distance = TerrainNeighbourhoodPolicy.SurfaceDistanceToNode(direction, parent, source.radius);
            double height = TerrainNeighbourhoodPolicy.PredictedClearance(view, observerClearance, PrefetchSeconds);
            int required = neighbourhood.RequiredLevel(source.radius, height, distance, true);
            if (parent.level < required) return 3;
            double3 ahead = math.normalizesafe(TerrainNeighbourhoodPolicy.LookAhead(view, PrefetchSeconds), direction);
            distance = TerrainNeighbourhoodPolicy.SurfaceDistanceToNode(ahead, parent, source.radius);
            return parent.level < neighbourhood.RequiredLevel(source.radius, height, distance, true) ? 2 : underfoot ? 1 : 0;
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
            meanBuildSeconds = math.lerp(meanBuildSeconds, Time.realtimeSinceStartupAsDouble - buildStarted, 0.15);
            if (!wanted.Contains(data.key) || nodes.ContainsKey(data.key)) return;
            Evict();
            if (nodes.Count >= ResidentBudget) return;
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
            coverageDirty = true;
            BuiltCount++;
        }

        private void SelectCoverage()
        {
            if (!coverageDirty) return;
            coverageDirty = false;
            nextVisible.Clear();
            for (int face = 0; face < 6; face++) Cover(TerrainNodeKey.Root((PlanetCubeFace)face));
            nextKeys.Clear();
            foreach (Node node in nextVisible) nextKeys.Add(node.data.key);
            foreach (var node in visible)
            {
                if (nextKeys.Contains(node.data.key)) continue;
                node.renderer.enabled = false;
                if (node.localRenderer != null) node.localRenderer.enabled = false;
                if (node.oceanRenderer != null) node.oceanRenderer.enabled = false;
                if (node.localOceanRenderer != null) node.localOceanRenderer.enabled = false;
            }
            visible.Clear(); visible.AddRange(nextVisible);
            visibleKeys.Clear(); visibleKeys.UnionWith(nextKeys);
            FinestVisibleLevel = 0;
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
            }
            VisibleCount = visible.Count;
            CoverageRevision++;
        }

        private void Cover(TerrainNodeKey key)
        {
            if (!nodes.TryGetValue(key, out Node node)) return;
            bool split = splits.Contains(key);
            if (split) for (int i = 0; i < 4; i++) if (!nodes.ContainsKey(key.Child(i))) { split = false; break; }
            if (split) for (int i = 0; i < 4; i++) Cover(key.Child(i));
            else nextVisible.Add(node);
        }

        private void Evict()
        {
            int limit = math.max(16, ResidentBudget);
            while (nodes.Count >= limit)
            {
                TerrainNodeKey oldest = default; bool found = false; int age = int.MaxValue;
                foreach (var pair in nodes)
                    if (!wanted.Contains(pair.Key) && !visibleKeys.Contains(pair.Key) &&
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
                SetDetailProperties(node.block, node.data, false);
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
            SetDetailProperties(localBlock, node.data, true);
            node.localRenderer.SetPropertyBlock(localBlock);
            PrepareOcean(node, cameraView, true);
        }

        private void SetDetailProperties(MaterialPropertyBlock block, RuntimeTerrainChunk data, bool local)
        {
            block.SetFloat("_ProceduralDetailEnabled", proceduralDetailEnabled && local && source.solid ? 1f : 0f);
            block.SetFloat("_ProceduralDetailAmplitude", (float)(source.detail.amplitudeMetres * source.reliefScale));
            block.SetFloat("_ProceduralDetailBaseWavelength", (float)PlanetDetailSampler.BaseWavelengthMetres);
            block.SetFloat("_ProceduralDetailGain", (float)source.detail.gain);
            block.SetFloat("_ProceduralDetailSeed", (float)(source.detail.seed & 0x00ffffffUL));
            block.SetFloat("_ProceduralDetailOctaves", source.detail.octaveCount);
            block.SetFloat("_ChunkSpacing", (float)data.spacing);
            SplitDetailOrigin(data.origin, out float3 high, out float3 low);
            block.SetVector("_DetailOriginHigh", new Vector4(high.x, high.y, high.z, 0));
            block.SetVector("_DetailOriginLow", new Vector4(low.x, low.y, low.z, 0));
        }

        private static void SplitDetailOrigin(double3 value, out float3 high, out float3 low)
        {
            high = new float3(Split(value.x), Split(value.y), Split(value.z));
            low = (float3)(value - (double3)high);
        }

        private static float Split(double value)
        {
            return (float)(math.floor(value / 1024.0) * 1024.0);
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
            nodes.Clear(); visible.Clear(); visibleKeys.Clear(); nextVisible.Clear(); nextKeys.Clear();
            wanted.Clear(); desired.Clear(); splits.Clear(); collisionCandidates.Clear();
            coverageDirty = true; hasSelectedView = false; plannedCoverageRevision = -1;
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
            {
                collisionOriginBf = collisionPlayerBf;
                foreach (Node node in collisionNodes.Values)
                    node.colliderGo.transform.position = (Vector3)(float3)(node.data.origin - collisionOriginBf);
                physicsDirty = true;
            }

            double3 direction = math.normalizesafe(collisionPlayerBf, new double3(0, 1, 0));
            double3 ahead = math.normalizesafe(collisionPlayerBf + collisionVelocityBf * 2.0, collisionPlayerBf);
            double3 aheadDirection = math.normalizesafe(ahead, direction);
            int targetLevel = CollisionLevel;
            int built = 0;
            bool replan = plannedCoverageRevision != CoverageRevision ||
                (Time.realtimeSinceStartup >= nextCollisionPlan &&
                 math.distance(lastCollisionObserver, collisionPlayerBf) > 1);
            if (replan)
            {
                collisionCandidates.Clear();
                foreach (Node node in nodes.Values)
                    if (node.data.key.level <= targetLevel) collisionCandidates.Add(node);
                collisionCandidates.Sort((a, b) => CollisionPriority(b, direction, aheadDirection)
                    .CompareTo(CollisionPriority(a, direction, aheadDirection)));
                plannedCoverageRevision = CoverageRevision;
                lastCollisionObserver = collisionPlayerBf;
                nextCollisionPlan = Time.realtimeSinceStartup + 0.1f;
            }
            // Retire collision patches independently from visual coverage. A parent remains while
            // it is the best cooked patch under the player; a finer collision patch may replace it
            // before the visual quadtree is allowed to split again.
            retiredColliders.Clear();
            foreach (var pair in collisionNodes)
            {
                bool underfoot = TerrainLattice.NodeAt(direction, pair.Key.level).Equals(pair.Key);
                bool forward = TerrainLattice.NodeAt(aheadDirection, pair.Key.level).Equals(pair.Key);
                bool coveredByFiner = CollisionNodeAt(direction) != pair.Value || CollisionNodeAt(aheadDirection) != pair.Value;
                bool keep = math.distance(pair.Value.data.origin, collisionPlayerBf) <
                    math.clamp(math.length(collisionVelocityBf) * 3.0, 256.0, 15000.0) ||
                    underfoot || forward;
                if (coveredByFiner || !keep) retiredColliders.Add(pair.Key);
            }
            foreach (var key in retiredColliders)
            {
                DestroyCollider(collisionNodes[key]); collisionNodes.Remove(key);
                physicsDirty = true; plannedCoverageRevision = -1;
            }
            foreach (Node node in collisionCandidates)
            {
                TerrainNodeKey key = node.data.key;
                if (!nodes.ContainsKey(key)) continue;
                bool underfoot = TerrainLattice.NodeAt(direction, key.level).Equals(key);
                bool forward = TerrainLattice.NodeAt(aheadDirection, key.level).Equals(key);
                double range = math.clamp(math.length(collisionVelocityBf) * 2.0, 128.0, 10000.0);
                if (!underfoot && !forward &&
                    math.distance(node.data.origin, collisionPlayerBf) > range) continue;
                if (node.colliderGo == null && built < 1 && collisionNodes.Count < 192 &&
                    timer.Elapsed.TotalMilliseconds < uploadBudgetMilliseconds)
                {
                    CreateCollider(node);
                    built++;
                }
            }
            FinestCollisionLevel = 0;
            foreach (var pair in collisionNodes)
                FinestCollisionLevel = math.max(FinestCollisionLevel, pair.Key.level);
            if (physicsDirty) { Physics.SyncTransforms(); physicsDirty = false; }
        }

        private double CollisionPriority(Node node, double3 direction, double3 ahead)
        {
            TerrainNodeKey key = node.data.key;
            double priority = TerrainLattice.NodeAt(direction, key.level).Equals(key) ? 1e18 :
                TerrainLattice.NodeAt(ahead, key.level).Equals(key) ? 1e16 : 0;
            priority += key.level * 1e10;
            return priority - math.distance(node.data.origin, collisionPlayerBf);
        }

        private void CreateCollider(Node node)
        {
            Mesh mesh = node.data.UploadCollisionMesh();
            var go = new GameObject("Collision " + node.data.key) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(go, collisionScene);
            go.transform.position = (Vector3)(float3)(node.data.origin - collisionOriginBf);
            node.collider = go.AddComponent<MeshCollider>();
            node.collider.sharedMesh = mesh;
            node.collider.enabled = true;
            node.colliderGo = go;
            node.colliderMesh = mesh;
            collisionNodes[node.data.key] = node;
            coverageDirty = true; physicsDirty = true; plannedCoverageRevision = -1;
        }

        private static void DestroyCollider(Node node)
        {
            if (node.colliderGo == null) return;
            if (node.collider != null) node.collider.enabled = false;
            Destroy(node.colliderGo);
            Destroy(node.colliderMesh);
            node.colliderGo = null;
            node.colliderMesh = null;
            node.collider = null;
        }

        private void ClearColliders()
        {
            foreach (Node node in collisionNodes.Values) DestroyCollider(node);
            collisionNodes.Clear();
            FinestCollisionLevel = 0;
            collisionCandidates.Clear(); coverageDirty = true; physicsDirty = true; plannedCoverageRevision = -1;
            if (collisionScene.IsValid() && Application.isPlaying)
            {
                SceneManager.UnloadSceneAsync(collisionScene);
                collisionScene = default(Scene);
            }
        }
    }
}
