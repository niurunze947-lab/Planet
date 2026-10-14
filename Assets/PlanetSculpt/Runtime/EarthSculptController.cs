using System.Collections;
using Planet.Interaction;
using Planet.Procedural;
using Planet.Solar;
using UnityEngine;

namespace Yongxian.PlanetSculpt
{
    [DisallowMultipleComponent, DefaultExecutionOrder(1200)]
    public sealed class EarthSculptController : MonoBehaviour
    {
        public bool startInSculptMode = true;
        public bool IsSculpting { get; private set; }
        public PlanetTerrain Terrain { get; private set; }
        public PlanetBrush Brush { get; private set; }
        public SculptFlightBridge Bridge { get; private set; }
        SolarSystemSimulation simulation;
        PlanetSurfaceView surface;
        UnifiedFlightController flight;
        Mesh originalMesh;
        bool previousPaused, previousOrbit;
        System.Func<bool> previousPointer;
        static Rect Toolbar => new Rect(PlanetBrush.PanelRect.x, 156f, PlanetBrush.PanelRect.width, 32f);
        public static bool ContainsToolbar(Vector3 mouse) => Toolbar.Contains(new Vector2(mouse.x, Screen.height - mouse.y));
        IEnumerator Start()
        {
            yield return null;
            if (startInSculptMode) { EnterSculptMode(); FrameEarth(); }
        }
        void Update()
        {
            if (!IsSculpting) return;
            if (BrushInput.GetKeyDown(KeyCode.F8)) Brush.paintEnabled = !Brush.paintEnabled;
            flight.allowOrbitInput = !Brush.paintEnabled;
        }
        void LateUpdate() { if (IsSculpting && Bridge != null) Bridge.Sync(); }
        bool PointerOverUI()
        {
            Vector3 p = BrushInput.mousePosition;
            Vector2 gui = new Vector2(p.x, Screen.height - p.y);
            return ContainsToolbar(p) || PlanetBrush.PanelRect.Contains(gui) ||
                new Rect(12, 12, 372, 500).Contains(gui) || new Rect(Screen.width - 250, 12, 238, 132).Contains(gui);
        }
        public void EnterSculptMode()
        {
            if (IsSculpting) return;
            simulation = SolarSystemSimulation.Instance;
            surface = GetComponent<PlanetSurfaceView>();
            flight = simulation.ViewCamera.GetComponent<UnifiedFlightController>();
            if (flight == null) throw new System.InvalidOperationException("Original flight controller missing.");
            var target = surface.SurfaceTransform;
            Terrain = target.GetComponent<PlanetTerrain>();
            Brush = target.GetComponent<PlanetBrush>();
            originalMesh = target.GetComponent<MeshFilter>().sharedMesh;
            previousPaused = simulation.paused;
            simulation.paused = true;
            previousOrbit = flight.allowOrbitInput;
            previousPointer = flight.PointerOverTool;
            flight.PointerOverTool = PointerOverUI;
            surface.SetTerrainState(false, false);
            Terrain.Initialize();
            target.GetComponent<MeshFilter>().sharedMesh = Terrain.SurfaceMesh;
            target.GetComponent<MeshRenderer>().enabled = true;
            Terrain.SurfaceCollider.enabled = false; // Isolated unit collider provides precision at every distance.
            Bridge = gameObject.AddComponent<SculptFlightBridge>();
            Bridge.Initialize(Terrain, GetComponent<CelestialBody>());
            surface.TerrainRenderer.ExternalCollisionQuery = Bridge;
            Brush.flightBridge = Bridge;
            Brush.viewCamera = simulation.ViewCamera;
            Brush.orbitCamera = false;
            Brush.frameOnStart = false;
            Brush.enabled = true;
            IsSculpting = true;
        }
        public void FrameEarth()
        {
            if (!IsSculpting) return;
            simulation.SetFocusByName("Earth", false);
            flight.BeginFocus(simulation.FocusIndex);
        }
        public void ExitSculptMode()
        {
            if (!IsSculpting) return;
            IsSculpting = false;
            Brush.enabled = false;
            Brush.flightBridge = null;
            surface.TerrainRenderer.ExternalCollisionQuery = null;
            surface.SurfaceTransform.GetComponent<MeshFilter>().sharedMesh = originalMesh;
            flight.allowOrbitInput = previousOrbit;
            flight.PointerOverTool = previousPointer;
            simulation.paused = previousPaused;
            Bridge.Shutdown();
            Destroy(Bridge);
            Bridge = null;
        }
        void OnDisable() { ExitSculptMode(); }
        void OnGUI()
        {
            if (!IsSculpting || Brush == null) return;
            GUILayout.BeginArea(Toolbar, GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Brush.paintEnabled ? "切换观察（F8）" : "开始绘画（F8）"))
            { Brush.EndStroke(); Brush.paintEnabled = !Brush.paintEnabled; }
            if (GUILayout.Button("查看地球")) FrameEarth();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
