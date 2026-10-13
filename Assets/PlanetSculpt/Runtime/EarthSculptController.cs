using System.Collections;
using System.Collections.Generic;
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
        readonly List<Behaviour> suspended = new List<Behaviour>();
        SolarSystemSimulation simulation;
        PlanetSurfaceView surface;
        Camera view;
        Transform target;
        Mesh originalMesh;
        bool previousPaused, previousInput, previousVisible;
        bool previousOrbitsActive;
        Vector3 cameraPosition;
        Quaternion cameraRotation;
        float cameraNear, cameraFar;
        static Rect Toolbar => new Rect(PlanetBrush.PanelRect.x, 156f, PlanetBrush.PanelRect.width, 32f);

        public static bool ContainsToolbar(Vector3 mouse) => Toolbar.Contains(new Vector2(mouse.x, Screen.height - mouse.y));

        IEnumerator Start()
        {
            // Allow the solar bootstrap and the flight observer to build their runtime state first.
            yield return null;
            if (startInSculptMode) EnterSculptMode();
        }

        void Update()
        {
            if (BrushInput.GetKeyDown(KeyCode.F8)) Toggle();
        }

        void Suspend(Behaviour component)
        {
            if (component == null || !component.enabled || suspended.Contains(component)) return;
            suspended.Add(component);
            component.enabled = false;
        }

        public void EnterSculptMode()
        {
            if (IsSculpting) return;
            simulation = SolarSystemSimulation.Instance;
            surface = GetComponent<PlanetSurfaceView>();
            target = surface != null ? surface.SurfaceTransform : null;
            view = simulation != null ? simulation.ViewCamera : null;
            if (target == null || view == null)
            {
                Debug.LogError("[EarthBrush] Earth surface or solar camera is missing.", this);
                return;
            }
            Terrain = target.GetComponent<PlanetTerrain>();
            Brush = target.GetComponent<PlanetBrush>();
            if (Terrain == null || Brush == null)
            {
                Debug.LogError("[EarthBrush] Run Tools > Planet > Install Earth Brush in the editor.", this);
                return;
            }
            cameraPosition = view.transform.position;
            cameraRotation = view.transform.rotation;
            cameraNear = view.nearClipPlane;
            cameraFar = view.farClipPlane;
            previousPaused = simulation.paused;
            previousInput = simulation.handleInput;
            previousOrbitsActive = simulation.orbitsRoot != null && simulation.orbitsRoot.gameObject.activeSelf;
            previousVisible = target.GetComponent<MeshRenderer>().enabled;
            originalMesh = target.GetComponent<MeshFilter>().sharedMesh;
            simulation.paused = true;
            simulation.handleInput = false;
            Suspend(simulation);
            if (simulation.orbitsRoot != null) simulation.orbitsRoot.gameObject.SetActive(false);
            foreach (var component in FindObjectsOfType<MonoBehaviour>())
                if (component is UnifiedFlightController || component is SolarSystemCamera ||
                    component is ObserverRenderRig || component is SolarSystemVisualCoordinator ||
                    component is BodyLoadingHighlight)
                    Suspend(component);
            surface.SetTerrainState(false, false);
            Suspend(surface.TerrainRenderer);
            Terrain.Initialize();
            target.GetComponent<MeshFilter>().sharedMesh = Terrain.SurfaceMesh;
            target.GetComponent<MeshRenderer>().enabled = true;
            Terrain.SurfaceCollider.enabled = true;
            Brush.viewCamera = view;
            Brush.frameOnStart = false;
            Brush.orbitCamera = true;
            Brush.enabled = true;
            IsSculpting = true;
            FrameEarth();
        }

        public void FrameEarth()
        {
            if (!IsSculpting || target == null || view == null) return;
            float radius = Mathf.Max(GetComponent<CelestialBody>().RadiusUnits, 1e-6f);
            Vector3 direction = simulation.sunLight != null ? -simulation.sunLight.transform.forward : Vector3.back;
            view.transform.position = target.position + direction.normalized * radius * 3.5f;
            view.transform.LookAt(target.position, Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up);
            view.nearClipPlane = radius * 0.005f;
            view.farClipPlane = radius * 50f;
        }

        public void ExitSculptMode()
        {
            if (!IsSculpting) return;
            IsSculpting = false;
            if (Brush != null) Brush.enabled = false;
            if (Terrain != null && Terrain.SurfaceCollider != null) Terrain.SurfaceCollider.enabled = false;
            if (target != null)
            {
                target.GetComponent<MeshFilter>().sharedMesh = originalMesh;
                target.GetComponent<MeshRenderer>().enabled = previousVisible;
            }
            if (view != null)
            {
                view.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                view.nearClipPlane = cameraNear;
                view.farClipPlane = cameraFar;
            }
            if (simulation != null)
            {
                simulation.paused = previousPaused;
                simulation.handleInput = previousInput;
                if (simulation.orbitsRoot != null) simulation.orbitsRoot.gameObject.SetActive(previousOrbitsActive);
            }
            foreach (var component in suspended) if (component != null) component.enabled = true;
            suspended.Clear();
        }

        void Toggle() { if (IsSculpting) ExitSculptMode(); else EnterSculptMode(); }
        void OnDisable() { ExitSculptMode(); }
        void OnGUI()
        {
            GUILayout.BeginArea(Toolbar, GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(IsSculpting ? "退出笔刷（F8）" : "进入笔刷（F8）")) Toggle();
            if (IsSculpting && GUILayout.Button("查看球体")) FrameEarth();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
