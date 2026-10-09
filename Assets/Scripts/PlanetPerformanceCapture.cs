using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Planet.Interaction;
using Planet.Procedural.Terrain;
using Planet.Solar;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Opt-in diagnostics for the current view and deterministic standalone benchmark poses.</summary>
[DefaultExecutionOrder(32000)]
public sealed class PlanetPerformanceCapture : MonoBehaviour
{
    public const int TargetWidth = 1920;
    public const int TargetHeight = 1080;
    public const double TargetFrameMilliseconds = 1000.0 / 60.0;
    private const int SampleLimit = 32768;

    [Serializable] public struct Distribution
    {
        public int samples;
        public double median, p95, p99, maximum;
    }

    [Serializable] public sealed class Report
    {
        public string scenario, utc, environment, unityVersion, gpu, cpu, scene;
        public int width, height, qualityLevel, vSyncCount, targetFrameRate;
        public float renderScale;
        public double measuredSeconds, targetFrameMilliseconds;
        public bool resolutionMatchesTarget, meetsSampledFrameBudget, gpuTimingAvailable, sampleLimitReached;
        public bool renderingVerified, gcTimingAvailable;
        public int renderedFrames;
        public Distribution frameMilliseconds, cpuMilliseconds, gpuMilliseconds, gcBytes;
        public Distribution terrainUpdateMilliseconds, terrainSubmitMilliseconds, terrainUploadMilliseconds;
        public int maximumResidentChunks, maximumVisibleChunks, maximumPendingChunks;
        public double maximumBuildMilliseconds;
    }

    public bool Capturing { get; private set; }
    public string LastReportPath { get; private set; }
    private Camera captureCamera;
    private string scenario, reportPath, screenshotPath;
    private bool quitWhenFinished;
    private double startAt, finishAt, previousTime, firstSampleTime;
    private RuntimeTerrainRenderer[] terrains;
    private ProfilerRecorder cpuRecorder, gcRecorder;
    private readonly FrameTiming[] timings = new FrameTiming[1];
    private readonly List<double> frames = new List<double>(4096);
    private readonly List<double> cpuTimes = new List<double>(4096);
    private readonly List<double> gpuTimes = new List<double>(4096);
    private readonly List<double> allocations = new List<double>(4096);
    private readonly List<double> terrainUpdates = new List<double>(4096);
    private readonly List<double> terrainSubmits = new List<double>(4096);
    private readonly List<double> terrainUploads = new List<double>(4096);
    private int maximumResident, maximumVisible, maximumPending;
    private double maximumBuild;
    private int renderedFrames;

    public void Begin(Camera camera, string label, string outputPath, float warmupSeconds = 3,
        float durationSeconds = 10, string capturePath = null, bool quit = false)
    {
        if (Capturing) throw new InvalidOperationException("A render benchmark is already running.");
        if (camera == null) throw new ArgumentNullException(nameof(camera));
        captureCamera = camera;
        scenario = label;
        reportPath = Path.GetFullPath(outputPath);
        screenshotPath = capturePath;
        quitWhenFinished = quit;
        startAt = Time.realtimeSinceStartupAsDouble + math.max(0, warmupSeconds);
        finishAt = startAt + math.clamp(durationSeconds, 1, 60);
        previousTime = 0;
        firstSampleTime = 0;
        frames.Clear(); cpuTimes.Clear(); gpuTimes.Clear(); allocations.Clear();
        terrainUpdates.Clear(); terrainSubmits.Clear(); terrainUploads.Clear();
        maximumResident = maximumVisible = maximumPending = 0;
        maximumBuild = 0;
        renderedFrames = 0;
        terrains = FindObjectsOfType<RuntimeTerrainRenderer>();
        cpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
        gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        // Allocate the bounded measurement storage before sampling so the harness does not
        // introduce capacity-growth GC into a high frame-rate capture.
        frames.Capacity = cpuTimes.Capacity = gpuTimes.Capacity = allocations.Capacity = SampleLimit;
        terrainUpdates.Capacity = terrainSubmits.Capacity = terrainUploads.Capacity = SampleLimit;
        RenderPipelineManager.beginCameraRendering += TrackRenderedCamera;
        Capturing = true;
    }

    private void TrackRenderedCamera(ScriptableRenderContext context, Camera camera)
    {
        if (Capturing && camera == captureCamera && Time.realtimeSinceStartupAsDouble >= startAt)
            renderedFrames++;
    }

    private void Update()
    {
        if (!Capturing) return;
        FrameTimingManager.CaptureFrameTimings();
        double now = Time.realtimeSinceStartupAsDouble;
        if (now < startAt) { previousTime = 0; return; }
        if (previousTime == 0) { previousTime = firstSampleTime = now; return; }
        frames.Add((now - previousTime) * 1000);
        previousTime = now;
        if (cpuRecorder.Valid && cpuRecorder.LastValue > 0) cpuTimes.Add(cpuRecorder.LastValue * 1e-6);
        if (gcRecorder.Valid) allocations.Add(gcRecorder.LastValue);
        if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0)
            gpuTimes.Add(timings[0].gpuFrameTime);
        double update = 0, submit = 0, upload = 0;
        int resident = 0, visible = 0, pending = 0;
        foreach (RuntimeTerrainRenderer terrain in terrains)
        {
            if (terrain == null || terrain.ResidentCount == 0) continue;
            update += terrain.LastUpdateMilliseconds;
            submit += terrain.LastSubmitMilliseconds;
            upload += terrain.LastUploadMilliseconds;
            resident += terrain.ResidentCount;
            visible += terrain.VisibleCount;
            pending += terrain.PendingWork;
            maximumBuild = math.max(maximumBuild, terrain.LastBuildMilliseconds);
        }
        terrainUpdates.Add(update); terrainSubmits.Add(submit); terrainUploads.Add(upload);
        maximumResident = math.max(maximumResident, resident);
        maximumVisible = math.max(maximumVisible, visible);
        maximumPending = math.max(maximumPending, pending);
        if (now >= finishAt || frames.Count >= SampleLimit) Finish(now);
    }

    public static Distribution Summarize(List<double> values)
    {
        if (values.Count == 0) return default;
        double[] sorted = values.ToArray();
        Array.Sort(sorted);
        return new Distribution
        {
            samples = sorted.Length,
            median = sorted[(sorted.Length - 1) / 2],
            p95 = sorted[math.clamp((int)Math.Ceiling(sorted.Length * 0.95) - 1, 0, sorted.Length - 1)],
            p99 = sorted[math.clamp((int)Math.Ceiling(sorted.Length * 0.99) - 1, 0, sorted.Length - 1)],
            maximum = sorted[sorted.Length - 1]
        };
    }

    private void Finish(double now)
    {
        Capturing = false;
        RenderPipelineManager.beginCameraRendering -= TrackRenderedCamera;
        DisposeRecorders();
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        var report = new Report
        {
            scenario = scenario, utc = DateTime.UtcNow.ToString("O"),
            environment = Application.isEditor ? "Editor" : "Player",
            unityVersion = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName,
            cpu = SystemInfo.processorType, scene = captureCamera.gameObject.scene.path,
            width = captureCamera.pixelWidth, height = captureCamera.pixelHeight,
            renderScale = pipeline != null ? pipeline.renderScale : 1,
            qualityLevel = QualitySettings.GetQualityLevel(), vSyncCount = QualitySettings.vSyncCount,
            targetFrameRate = Application.targetFrameRate,
            measuredSeconds = now - firstSampleTime, targetFrameMilliseconds = TargetFrameMilliseconds,
            frameMilliseconds = Summarize(frames), cpuMilliseconds = Summarize(cpuTimes),
            gpuMilliseconds = Summarize(gpuTimes), gcBytes = Summarize(allocations),
            terrainUpdateMilliseconds = Summarize(terrainUpdates),
            terrainSubmitMilliseconds = Summarize(terrainSubmits),
            terrainUploadMilliseconds = Summarize(terrainUploads),
            gpuTimingAvailable = gpuTimes.Count > 0, gcTimingAvailable = allocations.Count > 0,
            sampleLimitReached = frames.Count >= SampleLimit, renderedFrames = renderedFrames,
            renderingVerified = renderedFrames >= frames.Count * 0.99 && renderedFrames >= 120,
            maximumResidentChunks = maximumResident, maximumVisibleChunks = maximumVisible,
            maximumPendingChunks = maximumPending, maximumBuildMilliseconds = maximumBuild
        };
        report.resolutionMatchesTarget = report.width == TargetWidth && report.height == TargetHeight && report.renderScale >= 1;
        report.meetsSampledFrameBudget = report.resolutionMatchesTarget && report.renderingVerified && frames.Count >= 120 &&
            report.frameMilliseconds.p95 <= TargetFrameMilliseconds;
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
        LastReportPath = reportPath;
        Debug.Log("[Planet benchmark] " + reportPath + " (frame p95 " + report.frameMilliseconds.p95.ToString("F2") + " ms)");
        if (!string.IsNullOrEmpty(screenshotPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshotPath)));
            ScreenCapture.CaptureScreenshot(screenshotPath);
        }
        if (quitWhenFinished && !Application.isEditor) StartCoroutine(QuitAfterCapture());
    }

    private IEnumerator QuitAfterCapture()
    {
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        Application.Quit();
    }

    private void OnDisable()
    {
        Capturing = false;
        RenderPipelineManager.beginCameraRendering -= TrackRenderedCamera;
        DisposeRecorders();
    }
    private void DisposeRecorders() { cpuRecorder.Dispose(); gcRecorder.Dispose(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CommandLineBenchmark()
    {
        if (Application.isEditor) return;
        string[] arguments = Environment.GetCommandLineArgs();
        string selected = Argument(arguments, "-planet-benchmark", null);
        if (selected == null) return;
        var go = new GameObject("Planet Render Benchmark") { hideFlags = HideFlags.DontSave };
        var capture = go.AddComponent<PlanetPerformanceCapture>();
        capture.StartCoroutine(capture.Boot(arguments, selected));
    }

    private IEnumerator Boot(string[] arguments, string selected)
    {
        yield return null;
        yield return null;
        string output = Argument(arguments, "-planet-benchmark-output",
            Path.Combine(Application.persistentDataPath, "PlanetRenderBenchmark.json"));
        string image = Argument(arguments, "-planet-benchmark-image", null);
        float warmup = ParseSeconds(arguments, "-planet-benchmark-warmup", 15);
        float seconds = ParseSeconds(arguments, "-planet-benchmark-seconds", 10);
        try
        {
            ConfigureScenario(selected);
            Begin(Camera.main, selected, output, warmup, seconds, image, true);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Application.Quit(1);
        }
    }

    public static void ConfigureScenario(string selected)
    {
        if (selected == "Current") return;
        bool surface = selected == "EarthSurface" || selected == "MoonSurface";
        if (!surface && selected != "EarthOrbit") throw new ArgumentException("Unknown benchmark scenario: " + selected);
        var simulation = SolarSystemSimulation.Instance;
        if (simulation == null || simulation.BodyCount == 0) throw new InvalidOperationException("SolarSystem must be running.");
        simulation.paused = true;
        simulation.handleInput = false;
        simulation.bodySizeExaggeration = 1;
        simulation.julianDate = OrbitalMechanics.J2000;
        simulation.SetFocusByName(selected == "MoonSurface" ? "Moon" : "Earth", false);
        simulation.Tick();
        var body = simulation.FocusView;
        RuntimeTerrainSource source = body.GetComponent<RuntimeTerrainRenderer>().Source;
        var flight = simulation.ViewCamera.GetComponent<UnifiedFlightController>();
        if (flight == null) throw new InvalidOperationException("Unified observer is unavailable.");
        flight.handleInput = false;
        Vec3d centre = simulation.PositionWorldMetresAt(simulation.FocusIndex);
        int sunIndex = 0;
        for (int i = 0; i < simulation.BodyCount; i++) if (simulation.DefinitionAt(i).IsSun) { sunIndex = i; break; }
        Vec3d sunlight = simulation.PositionWorldMetresAt(sunIndex) - centre;
        sunlight /= math.max(1, sunlight.Length);
        Vec3d localSun = SolarSystemSimulation.RotateMetres(Quaternion.Inverse(body.transform.rotation), sunlight);
        double3 direction = math.normalize(new double3(localSun.x, localSun.y, localSun.z));
        if (surface)
        {
            double best = double.NegativeInfinity;
            for (int i = 0; i < 256; i++)
            {
                double y = 1 - 2 * (i + 0.5) / 256;
                double r = math.sqrt(1 - y * y), angle = i * 2.399963229728653;
                double3 candidate = new double3(r * math.cos(angle), y, r * math.sin(angle));
                if (math.dot(candidate, new double3(localSun.x, localSun.y, localSun.z)) < 0.35) continue;
                double height = source.Height(candidate, 1);
                if (height <= best) continue;
                best = height; direction = candidate;
            }
        }
        double3 point = surface ? source.Position(direction, 0.005) + direction * 500 : direction * source.radius * 3;
        Vec3d offset = SolarSystemSimulation.RotateMetres(body.transform.rotation, new Vec3d(point.x, point.y, point.z));
        Vector3 up = new Vector3((float)offset.x, (float)offset.y, (float)offset.z).normalized;
        Vector3 look = surface
            ? (Vector3.ProjectOnPlane(new Vector3((float)sunlight.x, (float)sunlight.y, (float)sunlight.z), up) - up * 0.45f).normalized
            : -up;
        Vector3 cameraUp = surface ? up : Vector3.up;
        if (Mathf.Abs(Vector3.Dot(look, cameraUp)) > 0.95f) cameraUp = Vector3.forward;
        flight.SetPose(new double3(centre.x + offset.x, centre.y + offset.y, centre.z + offset.z),
            Quaternion.LookRotation(look, cameraUp), double3.zero);
    }

    private static string Argument(string[] values, string name, string fallback)
    {
        for (int i = 0; i + 1 < values.Length; i++) if (values[i] == name) return values[i + 1];
        return fallback;
    }

    private static float ParseSeconds(string[] values, string name, float fallback)
    {
        return float.TryParse(Argument(values, name, null), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            && math.isfinite(value) ? Mathf.Clamp(value, 0, 60) : fallback;
    }
}
