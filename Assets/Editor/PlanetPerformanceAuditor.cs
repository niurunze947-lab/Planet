using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class PlanetPerformanceAuditor
{
    public static string ReportPath => Path.GetFullPath("Library/PlanetRenderBenchmark.json");
    public static string PlayerPath => Path.GetFullPath("Build/PlanetBenchmarkPlayer/Planet.exe");

    [MenuItem("Tools/Planet/Capture Render Benchmark", false, 31)]
    public static void CaptureCurrentView()
    {
        if (!Application.isPlaying || Camera.main == null)
            throw new InvalidOperationException("Enter Play mode before capturing a render benchmark.");
        var camera = Camera.main;
        var capture = camera.GetComponent<PlanetPerformanceCapture>();
        if (capture == null) capture = camera.gameObject.AddComponent<PlanetPerformanceCapture>();
        capture.Begin(camera, "Current", ReportPath);
    }

    [MenuItem("Tools/Planet/Build Benchmark Player", false, 32)]
    public static void BuildPlayer()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play mode before building the benchmark player.");
        Directory.CreateDirectory(Path.GetDirectoryName(PlayerPath));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/SolarSystem.unity" },
            locationPathName = PlayerPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        if (report == null || report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Benchmark player build failed; see the Unity build log.");
        Debug.Log("[Planet benchmark] Player: " + PlayerPath);
    }
}
