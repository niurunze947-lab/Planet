using System;
using System.Collections.Generic;
using Planet.Procedural.Terrain;
using Planet.Solar;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Planet.Interaction
{
    /// <summary>
    /// Drag-to-place animals. Animals are represented by fixed cubes until art is
    /// available. All placement data stays in body-fixed metres and is projected into the local
    /// metre camera, so it remains stable on a rotating true-scale planet.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(200)]
    public sealed class VegetationPlacementController : MonoBehaviour
    {
        public const int MaximumDrops = 5;
        public const int CubesPerDrop = 50;

        [Header("动物模型与动作配置")]
        public AnimalPlacementDefinition animalDefinition;

        [Header("Placement")]
        // Deliberately oversized placeholders so a deployment remains easy to read from the
        // gameplay camera. Keep the original 14:1 range-to-cube ratio from the first prototype.
        [Min(1f)] public float placementRadiusMetres = 11200f;
        [Min(0.1f)] public float cubeSizeMetres = 800f;
        [Tooltip("Covers the complete near-surface entry band (Earth enters at about 335 km).")]
        [Min(100f)] public float maximumPlacementRayMetres = 500000f;

        [Header("Automatic framing")]
        [Tooltip("Camera distance as a multiple of the placement radius after a successful drop.")]
        [Min(1.1f)] public float framingDistanceMultiplier = 2.35f;
        [Min(0f)] public float placedCircleSeconds = 2f;

        public int UsedDrops { get { return usedDrops; } }
        public int RemainingDrops { get { return Mathf.Max(0, MaximumDrops - usedDrops); } }
        public bool IsDragging { get { return dragging; } }

        private sealed class DroppedCube
        {
            public GameObject gameObject;
            public int bodyIndex;
            public double3 targetBodyFixed;
            public double3 normalBodyFixed;
            public float size;
        }

        private static bool pointerCaptured;
        private readonly List<DroppedCube> cubes = new List<DroppedCube>(MaximumDrops * CubesPerDrop);
        private UnifiedFlightController flight;
        private SolarSystemSimulation simulation;
        private Camera viewCamera;
        private ObserverRenderRig renderRig;
        private LineRenderer rangeCircle;
        private Material rangeMaterial;
        private Material cubeMaterial;
        private Texture2D panelBackground;
        private Texture2D iconTexture;
        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle hintStyle;
        private GUIStyle countStyle;
        private bool dragging;
        private bool previewValid;
        private int previewBodyIndex = -1;
        private double3 previewPointBodyFixed;
        private double3 previewNormalBodyFixed;
        private int usedDrops;
        private float placedCircleUntil;
        private bool orphanCleanupComplete;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SolarSystemCamera rig = FindObjectOfType<SolarSystemCamera>();
            if (rig != null && rig.GetComponent<VegetationPlacementController>() == null)
                rig.gameObject.AddComponent<VegetationPlacementController>();
        }

        /// <summary>Used by the flight controller before it consumes left-drag orbit input.</summary>
        public static bool ConsumesPointer(Vector2 screenPosition)
        {
            if (pointerCaptured) return true;
            Vector2 guiPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            return PlantIconRect().Contains(guiPosition);
        }

        private static Rect PanelRect()
        {
            return new Rect(Mathf.Max(12f, Screen.width - 260f), 156f, 248f, 226f);
        }

        private static Rect PlantIconRect()
        {
            Rect panel = PanelRect();
            return new Rect(panel.x + 16f, panel.y + 78f, 72f, 72f);
        }

        private void Awake()
        {
            viewCamera = GetComponent<Camera>();
            flight = GetComponent<UnifiedFlightController>();
            renderRig = GetComponent<ObserverRenderRig>();
            if (animalDefinition == null)
                animalDefinition = Resources.Load<AnimalPlacementDefinition>("AnimalPlacementSettings");
            EnsureMaterials();
            EnsureRangeCircle();
            CleanupOrphanedPlaceholders();
        }

        private void OnDisable()
        {
            dragging = false;
            pointerCaptured = false;
            previewValid = false;
            if (rangeCircle != null) rangeCircle.enabled = false;
        }

        private void Update()
        {
            ResolveRuntimeReferences();
            EnsureMaterials();
            if (!orphanCleanupComplete) CleanupOrphanedPlaceholders();
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 screenPosition = mouse.position.ReadValue();
            Vector2 guiPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);

            if (!dragging && RemainingDrops > 0 && mouse.leftButton.wasPressedThisFrame &&
                PlantIconRect().Contains(guiPosition))
            {
                dragging = true;
                pointerCaptured = true;
                previewValid = false;
            }

            if (!dragging) return;

            if (mouse.leftButton.isPressed)
            {
                previewValid = !PlantIconRect().Contains(guiPosition) &&
                    TryFindSurfacePoint(screenPosition, out previewPointBodyFixed,
                        out previewNormalBodyFixed, out previewBodyIndex);
                if (previewValid)
                {
                    // Follow the current cursor hit continuously without changing view direction.
                    FramePlacement(previewBodyIndex, previewPointBodyFixed, previewNormalBodyFixed);
                }
            }

            if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (previewValid && RemainingDrops > 0)
                {
                    DeployCubes(previewBodyIndex, previewPointBodyFixed, previewNormalBodyFixed);
                    placedCircleUntil = Time.time + placedCircleSeconds;
                }
                dragging = false;
                pointerCaptured = false;
                previewValid = false;
            }
        }

        private void LateUpdate()
        {
            ResolveRuntimeReferences();
            UpdateRangeCircle();
            UpdateDroppedCubes();
        }

        private void ResolveRuntimeReferences()
        {
            if (viewCamera == null) viewCamera = GetComponent<Camera>();
            if (flight == null) flight = GetComponent<UnifiedFlightController>();
            if (renderRig == null) renderRig = GetComponent<ObserverRenderRig>();
            if (simulation == null) simulation = SolarSystemSimulation.Instance;
        }

        private bool TryFindSurfacePoint(Vector2 screenPosition, out double3 pointBodyFixed,
            out double3 normalBodyFixed, out int bodyIndex)
        {
            pointBodyFixed = double3.zero;
            normalBodyFixed = new double3(0, 1, 0);
            bodyIndex = -1;
            if (flight == null || simulation == null || viewCamera == null || !flight.InNearSurface)
                return false;

            bodyIndex = flight.NearbyBodyIndex;
            if (bodyIndex < 0 || bodyIndex >= simulation.BodyCount) return false;
            Body definition = simulation.DefinitionAt(bodyIndex);
            if (definition == null || definition.Kind == BodyKind.Star) return false;

            CelestialBody body = simulation.ViewAt(bodyIndex);
            IPlanetSurfaceQuery query = body.GetComponent<IPlanetSurfaceQuery>();
            double baseRadius = query != null && query.RadiusMetres > 0
                ? query.RadiusMetres : definition.RadiusKm * 1000.0;

            Ray screenRay = viewCamera.ScreenPointToRay(screenPosition);
            Vector3 directionWorld = screenRay.direction.normalized;
            Vector3 directionLocalFloat = Quaternion.Inverse(body.transform.rotation) * directionWorld;
            double3 directionLocal = math.normalizesafe((double3)(float3)directionLocalFloat,
                new double3(0, 0, 1));
            double3 origin = flight.PositionBodyFixedMetres;
            if (!TryRayTerrain(origin, directionLocal, baseRadius, query,
                maximumPlacementRayMetres, out double distance, out double surfaceRadius)) return false;

            double3 surfaceDirection = math.normalizesafe(origin + directionLocal * distance,
                new double3(0, 1, 0));
            pointBodyFixed = surfaceDirection * surfaceRadius;
            normalBodyFixed = SurfaceLocalSpace.SurfaceNormal(surfaceDirection,
                definition.Flattening, (double3)(float3)definition.ShapeScale);
            return math.all(math.isfinite(pointBodyFixed)) && math.all(math.isfinite(normalBodyFixed));
        }

        /// <summary>
        /// Sphere-trace the radial terrain height field. This remains valid when the camera is
        /// below a mountain's radius; repeatedly intersecting fitted spheres can otherwise select
        /// the far side of the planet and make every visible patch reject placement.
        /// </summary>
        private static bool TryRayTerrain(double3 origin, double3 rayDirection, double baseRadius,
            IPlanetSurfaceQuery query, double maximumDistance, out double distance,
            out double surfaceRadius)
        {
            distance = 0.0;
            surfaceRadius = baseRadius;
            double previousDistance = 0.0;
            double previousClearance = double.PositiveInfinity;

            for (int iteration = 0; iteration < 192 && distance <= maximumDistance; iteration++)
            {
                double3 sample = origin + rayDirection * distance;
                double sampleLength = math.length(sample);
                if (!(sampleLength > 0.0) || !math.isfinite(sampleLength)) return false;
                double3 sampleDirection = sample / sampleLength;
                surfaceRadius = query != null
                    ? query.SampleSurfaceRadiusMetres(sampleDirection, 1.0)
                    : baseRadius;
                double clearance = sampleLength - surfaceRadius;

                if (clearance <= 0.25)
                {
                    // Refine the first crossing so the range circle sits on the displayed terrain.
                    double low = previousDistance;
                    double high = distance;
                    if (!math.isfinite(previousClearance)) low = Math.Max(0.0, distance - 1.0);
                    for (int refine = 0; refine < 14 && high - low > 0.05; refine++)
                    {
                        double middle = (low + high) * 0.5;
                        double3 middlePoint = origin + rayDirection * middle;
                        double middleLength = math.length(middlePoint);
                        double3 middleDirection = middlePoint / middleLength;
                        double middleRadius = query != null
                            ? query.SampleSurfaceRadiusMetres(middleDirection, 0.5)
                            : baseRadius;
                        if (middleLength - middleRadius > 0.05) low = middle;
                        else { high = middle; surfaceRadius = middleRadius; }
                    }
                    distance = high;
                    return distance >= 0.0;
                }

                // A conservative half-clearance step cannot jump over radial terrain. The upper
                // cap keeps shallow rays sampling often enough near the horizon.
                previousDistance = distance;
                previousClearance = clearance;
                distance += Math.Max(0.5, Math.Min(clearance * 0.5, 10000.0));
            }
            return false;
        }

        private void DeployCubes(int bodyIndex, double3 centre, double3 centreNormal)
        {
            if (simulation == null || bodyIndex < 0 || bodyIndex >= simulation.BodyCount) return;
            EnsureMaterials();
            Body definition = simulation.DefinitionAt(bodyIndex);
            CelestialBody body = simulation.ViewAt(bodyIndex);
            IPlanetSurfaceQuery query = body.GetComponent<IPlanetSurfaceQuery>();
            double radius = query != null && query.RadiusMetres > 0
                ? query.RadiusMetres : definition.RadiusKm * 1000.0;
            SurfaceLocalSpace frame = SurfaceLocalSpace.CreateAtAnchor(centre, radius,
                definition.Flattening, (double3)(float3)definition.ShapeScale);

            int seed = unchecked((usedDrops + 1) * 73856093 ^ bodyIndex * 19349663 ^
                Mathf.RoundToInt((float)(centre.x + centre.y + centre.z)));
            System.Random random = new System.Random(seed);
            for (int i = 0; i < CubesPerDrop; i++)
            {
                // r = U^1.8 strongly favours the centre. Uniform angle avoids visible spokes.
                double radial = placementRadiusMetres * Math.Pow(random.NextDouble(), 1.8);
                double angle = random.NextDouble() * Math.PI * 2.0;
                double3 approximate = centre + frame.east * (Math.Cos(angle) * radial) +
                    frame.north * (Math.Sin(angle) * radial);
                double3 direction = math.normalizesafe(approximate, math.normalizesafe(centre));
                double surfaceRadius = query != null
                    ? query.SampleSurfaceRadiusMetres(direction, 0.5)
                    : radius;
                double3 target = direction * surfaceRadius;
                double3 normal = SurfaceLocalSpace.SurfaceNormal(direction, definition.Flattening,
                    (double3)(float3)definition.ShapeScale);

                float size = cubeSizeMetres * Mathf.Lerp(0.82f, 1.18f, (float)random.NextDouble());
                GameObject cube = CreateAnimal(size);

                cubes.Add(new DroppedCube
                {
                    gameObject = cube,
                    bodyIndex = bodyIndex,
                    targetBodyFixed = target,
                    normalBodyFixed = normal,
                    size = size
                });
            }

            usedDrops++;
        }

        private GameObject CreateAnimal(float size)
        {
            // Only this outer anchor is repositioned by floating-origin updates. Never write bones.
            GameObject anchor = new GameObject("Animal Placement " + (cubes.Count + 1));
            anchor.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            anchor.transform.SetParent(transform, false);
            anchor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            anchor.transform.localScale = Vector3.one;
            bool hasModel = animalDefinition != null && animalDefinition.modelPrefab != null;
            GameObject visual = hasModel
                ? Instantiate(animalDefinition.modelPrefab, anchor.transform, false)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.transform.SetParent(anchor.transform, false);
            visual.transform.localPosition = Vector3.zero;
            if (hasModel)
            {
                visual.transform.localRotation = Quaternion.Euler(animalDefinition.rotationOffset) *
                    visual.transform.localRotation;
                // Use renderer bounds, including skinned meshes, to normalize imported units.
                Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
                bool hasBounds = false;
                Bounds bounds = new Bounds();
                foreach (Renderer renderer in renderers)
                {
                    if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                float extent = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                float factor = animalDefinition.scaleMultiplier;
                if (animalDefinition.normalizeSize && hasBounds && extent > 0.0001f)
                    factor *= size / extent;
                visual.transform.localScale *= factor;
                Vector3 offset = hasBounds
                    ? new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * factor
                    : Vector3.zero;
                visual.transform.localPosition = offset + Vector3.up * animalDefinition.groundOffsetMetres;
                animalDefinition.Configure(visual);
            }
            else
            {
                visual.name = "Animal Placeholder";
                visual.transform.localScale = Vector3.one * size;
                visual.transform.localPosition = Vector3.up * (size * 0.5f);
                MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = cubeMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                Destroy(visual.GetComponent<Collider>());
            }
            foreach (Transform child in anchor.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = ObserverRenderRig.TerrainLayer;
            return anchor;
        }

        /// <summary>
        /// Script recompiles during Play Mode can leave native placeholder objects behind after
        /// this controller's managed list has reset. Resources.FindObjectsOfTypeAll is required:
        /// ordinary FindObjectsOfType can omit DontSave objects, which is exactly how the bright
        /// purple orphan cubes survived the earlier cleanup.
        /// </summary>
        private void CleanupOrphanedPlaceholders()
        {
            foreach (Transform item in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (item == null || !item.gameObject.scene.IsValid() ||
                    !item.name.StartsWith("Animal Placement ", StringComparison.Ordinal) ||
                    (item.gameObject.hideFlags & HideFlags.DontSaveInEditor) == 0) continue;
                if (cubes.Exists(entry => entry.gameObject == item.gameObject)) continue;
                item.gameObject.SetActive(false);
                Destroy(item.gameObject);
            }
            MeshRenderer[] renderers = Resources.FindObjectsOfTypeAll<MeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                GameObject candidate = renderer != null ? renderer.gameObject : null;
                if (candidate == null) continue;

                bool tracked = false;
                for (int cubeIndex = 0; cubeIndex < cubes.Count; cubeIndex++)
                {
                    if (cubes[cubeIndex].gameObject != candidate) continue;
                    tracked = true;
                    break;
                }
                if (tracked) continue;

                bool namedPlaceholder = candidate.name.StartsWith(
                    "Plant Placeholder", StringComparison.Ordinal);
                MeshFilter filter = candidate.GetComponent<MeshFilter>();
                Material material = renderer.sharedMaterial;
                bool brokenMaterial = material == null || material.shader == null ||
                    !material.shader.isSupported ||
                    material.shader.name.IndexOf("InternalError", StringComparison.OrdinalIgnoreCase) >= 0;
                bool brokenRuntimePrimitive = candidate.layer == ObserverRenderRig.TerrainLayer &&
                    (candidate.hideFlags & HideFlags.DontSave) != 0 && filter != null &&
                    filter.sharedMesh != null && filter.sharedMesh.name == "Cube" && brokenMaterial;

                if (!namedPlaceholder && !brokenRuntimePrimitive) continue;
                candidate.SetActive(false);
                Destroy(candidate);
            }
            orphanCleanupComplete = true;
        }

        private void UpdateDroppedCubes()
        {
            if (flight == null || simulation == null) return;
            bool hasBodyFrame = flight.InNearSurface;
            int activeIndex = flight.NearbyBodyIndex;
            double3 observer = flight.PositionBodyFixedMetres;

            for (int i = 0; i < cubes.Count; i++)
            {
                DroppedCube cube = cubes[i];
                if (cube.gameObject == null) continue;
                bool visible = hasBodyFrame && cube.bodyIndex == activeIndex;
                if (cube.gameObject.activeSelf != visible) cube.gameObject.SetActive(visible);
                if (!visible) continue;

                CelestialBody body = simulation.ViewAt(cube.bodyIndex);
                double3 anchored = cube.targetBodyFixed;
                Vector3 position = body.transform.rotation * ToVector3(anchored - observer);
                Vector3 normalWorld = body.transform.rotation * ToVector3(cube.normalBodyFixed);
                cube.gameObject.transform.SetPositionAndRotation(position,
                    Quaternion.FromToRotation(Vector3.up, normalWorld));
            }
        }

        private void FramePlacement(int bodyIndex, double3 centre, double3 normal)
        {
            if (flight == null) return;
            double distance = Math.Max(placementRadiusMetres * framingDistanceMultiplier,
                cubeSizeMetres * 4.0);
            if (viewCamera != null)
            {
                double halfFov = viewCamera.fieldOfView * Mathf.Deg2Rad * 0.5;
                double limitingTangent = Math.Tan(halfFov) * Math.Min(1.0, viewCamera.aspect);
                distance = Math.Max(distance, placementRadiusMetres * 1.25 / Math.Max(0.1, limitingTangent));
            }
            flight.ZoomTowardsSurfacePoint(bodyIndex, centre, normal, distance, Time.unscaledDeltaTime);
            if (renderRig != null && renderRig.LocalCamera != null)
                renderRig.LocalCamera.farClipPlane = Mathf.Max(renderRig.LocalCamera.farClipPlane,
                    (float)(distance + placementRadiusMetres * 3.0));
        }

        private void EnsureRangeCircle()
        {
            if (rangeCircle != null) return;
            GameObject go = new GameObject("Animal placement range")
            {
                hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild,
                layer = ObserverRenderRig.TerrainLayer
            };
            rangeCircle = go.AddComponent<LineRenderer>();
            rangeCircle.useWorldSpace = true;
            rangeCircle.loop = true;
            rangeCircle.positionCount = 48;
            rangeCircle.widthMultiplier = Mathf.Max(0.75f, cubeSizeMetres * 0.3f);
            rangeCircle.numCornerVertices = 3;
            rangeCircle.numCapVertices = 3;
            rangeCircle.sharedMaterial = rangeMaterial;
            rangeCircle.startColor = new Color(0.62f, 1f, 0.30f, 0.95f);
            rangeCircle.endColor = new Color(0.25f, 0.82f, 0.18f, 0.95f);
            rangeCircle.shadowCastingMode = ShadowCastingMode.Off;
            rangeCircle.receiveShadows = false;
            rangeCircle.enabled = false;
        }

        private void UpdateRangeCircle()
        {
            if (rangeCircle == null) EnsureRangeCircle();
            bool previewing = dragging && previewValid;
            bool showingPlacedRange = !dragging && Time.time < placedCircleUntil;
            bool visible = (previewing || showingPlacedRange) && flight != null &&
                flight.InNearSurface && previewBodyIndex == flight.NearbyBodyIndex && simulation != null;
            rangeCircle.enabled = visible;
            if (!visible) return;

            Body definition = simulation.DefinitionAt(previewBodyIndex);
            CelestialBody body = simulation.ViewAt(previewBodyIndex);
            IPlanetSurfaceQuery query = body.GetComponent<IPlanetSurfaceQuery>();
            double radius = query != null && query.RadiusMetres > 0
                ? query.RadiusMetres : definition.RadiusKm * 1000.0;
            SurfaceLocalSpace frame = SurfaceLocalSpace.CreateAtAnchor(previewPointBodyFixed, radius,
                definition.Flattening, (double3)(float3)definition.ShapeScale);
            double3 observer = flight.PositionBodyFixedMetres;

            for (int i = 0; i < rangeCircle.positionCount; i++)
            {
                double angle = Math.PI * 2.0 * i / rangeCircle.positionCount;
                double3 approximate = previewPointBodyFixed +
                    frame.east * (Math.Cos(angle) * placementRadiusMetres) +
                    frame.north * (Math.Sin(angle) * placementRadiusMetres);
                double3 direction = math.normalizesafe(approximate, math.normalizesafe(previewPointBodyFixed));
                double surfaceRadius = query != null
                    ? query.SampleSurfaceRadiusMetres(direction, 1.0)
                    : radius;
                double3 point = direction * surfaceRadius +
                    SurfaceLocalSpace.SurfaceNormal(direction, definition.Flattening,
                        (double3)(float3)definition.ShapeScale) * Math.Max(0.35, cubeSizeMetres * 0.14);
                rangeCircle.SetPosition(i, body.transform.rotation * ToVector3(point - observer));
            }
        }

        private void EnsureMaterials()
        {
            if (rangeMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                rangeMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                rangeMaterial.color = new Color(0.45f, 1f, 0.22f, 0.95f);
            }
            if (cubeMaterial == null)
            {
                // Unlit keeps the temporary plant blocks bright on both the day and night side.
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                cubeMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                cubeMaterial.color = new Color(0.20f, 0.72f, 0.28f, 1f);
            }
        }

        private void EnsureGuiStyles()
        {
            if (panelStyle != null) return;
            panelBackground = MakeTexture(new Color(0.025f, 0.045f, 0.035f, 0.91f));
            iconTexture = MakeTexture(new Color(0.20f, 0.72f, 0.28f, 1f));

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = panelBackground },
                padding = new RectOffset(14, 14, 12, 12),
                alignment = TextAnchor.UpperLeft
            };
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.91f, 1f, 0.90f) }
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.79f, 0.91f, 0.81f) },
                wordWrap = true
            };
            hintStyle = new GUIStyle(bodyStyle)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.62f, 0.73f, 0.65f) }
            };
            countStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
        }

        private static Texture2D MakeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void OnGUI()
        {
            EnsureGuiStyles();
            Rect panel = PanelRect();
            GUI.Box(panel, GUIContent.none, panelStyle);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 10f, panel.width - 28f, 24f),
                "动物投放", titleStyle);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 38f, panel.width - 28f, 24f),
                string.Format("剩余次数  {0} / {1}", RemainingDrops, MaximumDrops), bodyStyle);

            Rect icon = PlantIconRect();
            Color previous = GUI.color;
            GUI.color = RemainingDrops > 0 ? Color.white : new Color(1f, 1f, 1f, 0.3f);
            GUI.DrawTexture(icon, iconTexture, ScaleMode.StretchToFill);
            GUI.color = previous;
            GUI.Label(icon, "50", countStyle);

            Rect help = new Rect(icon.xMax + 12f, icon.y - 2f, panel.xMax - icon.xMax - 24f, 82f);
            string message;
            if (RemainingDrops <= 0) message = "投放次数已用完";
            else if (flight == null || !flight.InNearSurface) message = "持续滚轮靠近地表后，拖动右侧动物图标投放。";
            else if (dragging) message = previewValid ? "松开鼠标：在圈内投放动物" : "把动物图标拖到可见地表";
            else message = "拖动动物到地表\n每次投放 50 只";
            GUI.Label(help, message, hintStyle);

            GUI.Label(new Rect(panel.x + 14f, panel.y + 164f, panel.width - 28f, 48f),
                "中心密集 · 边缘稀疏\n绿色圆圈表示投放范围", hintStyle);

            if (dragging)
            {
                Vector2 mouse = Event.current.mousePosition;
                Rect ghost = new Rect(mouse.x - 16f, mouse.y - 16f, 32f, 32f);
                GUI.color = new Color(1f, 1f, 1f, 0.82f);
                GUI.DrawTexture(ghost, iconTexture, ScaleMode.StretchToFill);
                GUI.color = previous;
            }
        }

        private static Vector3 ToVector3(double3 value)
        {
            return new Vector3((float)value.x, (float)value.y, (float)value.z);
        }

        private void OnDestroy()
        {
            // Application.isPlaying may already be false while Unity tears down Play Mode.
            for (int i = 0; i < cubes.Count; i++)
            {
                if (cubes[i].gameObject == null) continue;
                if (Application.isPlaying) Destroy(cubes[i].gameObject);
                else DestroyImmediate(cubes[i].gameObject);
            }
            cubes.Clear();
            if (Application.isPlaying)
            {
                if (rangeCircle != null) Destroy(rangeCircle.gameObject);
                Destroy(rangeMaterial);
                Destroy(cubeMaterial);
                Destroy(panelBackground);
                Destroy(iconTexture);
            }
            else
            {
                if (rangeCircle != null) DestroyImmediate(rangeCircle.gameObject);
                DestroyImmediate(rangeMaterial);
                DestroyImmediate(cubeMaterial);
                DestroyImmediate(panelBackground);
                DestroyImmediate(iconTexture);
            }
        }
    }
}
