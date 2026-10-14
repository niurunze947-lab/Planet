using Planet.Procedural;
using Planet.Solar;
using Unity.Mathematics;
using UnityEngine;

/// <summary>One-way adapter from the published solar camera/time contract to rendering consumers.</summary>
[ExecuteAlways, DefaultExecutionOrder(1000)]
public sealed class SolarSystemVisualCoordinator : MonoBehaviour
{
    public SolarSystemSimulation simulation;
    public SolarSystemCamera cameraRig;
    public PlanetTextureLodController earthTextures;
    public StellarSurfaceView star;
    public PlanetMeshLodController[] planetMeshes;
    private float nextMeshDiscovery;
    private PlanetSurfaceView[] terrainViews;

    private void LateUpdate() { Refresh(); }
    public void Refresh()
    {
        if (simulation == null) simulation = FindObjectOfType<SolarSystemSimulation>();
        if (simulation == null || simulation.ViewCamera == null) return;
        if (cameraRig == null) cameraRig = simulation.ViewCamera.GetComponent<SolarSystemCamera>();
        if (earthTextures == null) earthTextures = FindObjectOfType<PlanetTextureLodController>();
        if (simulation.BodyCount == 0) return;
        int observedIndex = simulation.HasObserver && simulation.ProximityIndex >= 0
            ? simulation.ProximityIndex : simulation.FocusIndex;
        CelestialBody observedBody = simulation.ViewAt(observedIndex);
        float observedRatio = cameraRig != null ? cameraRig.SurfaceClearanceRatio : 0f;
        if (simulation.HasObserver && observedBody != null && cameraRig != null)
        {
            double distance = simulation.ObserverRelativeToBody(observedIndex).Length;
            double radius = observedBody.Definition.RadiusKm * 1000.0;
            observedRatio = (float)((distance - radius) / System.Math.Max(radius, 1.0));
        }
        if (earthTextures != null && cameraRig != null && Application.isPlaying)
            earthTextures.UpdateView(observedRatio, observedBody != null && observedBody.bodyName == "Earth");
        if (star == null) star = FindObjectOfType<StellarSurfaceView>();
        if (star != null)
            star.UpdateObservation(simulation.focusBody == star.GetComponent<CelestialBody>().bodyName,
                simulation.julianDate - OrbitalMechanics.J2000, Time.realtimeSinceStartupAsDouble);
        if (planetMeshes == null || Time.realtimeSinceStartup >= nextMeshDiscovery)
        {
            planetMeshes = FindObjectsOfType<PlanetMeshLodController>();
            terrainViews = FindObjectsOfType<PlanetSurfaceView>();
            nextMeshDiscovery = Time.realtimeSinceStartup + 1f;
        }
        if (cameraRig != null && planetMeshes != null)
        {
            for (int i = 0; i < planetMeshes.Length; i++)
            {
                PlanetMeshLodController controller = planetMeshes[i];
                if (controller == null) continue;
                CelestialBody body = controller.GetComponent<CelestialBody>();
                controller.UpdateView(observedRatio, body == observedBody);
            }
        }
        if (terrainViews != null)
            foreach (PlanetSurfaceView view in terrainViews)
            {
                if (view == null || !view.useRuntimeChunks || view.TerrainRenderer == null) continue;
                CelestialBody body = view.GetComponent<CelestialBody>();
                var terrain = view.TerrainRenderer;
                if (terrain.ExternalCollisionQuery != null)
                {
                    view.SetTerrainState(false, false);
                    continue; // The sculpt tool supplies the displayed mesh and its collision together.
                }
                bool nearby = body == observedBody && simulation.HasObserver && simulation.ProximityIndex >= 0;
                bool prewarm = nearby && observedRatio <= 0.25f;
                bool active = nearby && observedRatio <= 0.12f;
                view.SetTerrainState(prewarm, active);
                var state = terrain.ViewFromCamera(simulation.ViewCamera);
                int index = -1;
                for (int i = 0; i < simulation.BodyCount; i++)
                    if (simulation.ViewAt(i) == body) { index = i; break; }
                if (simulation.HasObserver && index >= 0)
                {
                    Vec3d p = simulation.ObserverRelativeToBody(index);
                    state.positionBfMetres = new double3(p.x, p.y, p.z);
                    state.localCameraActive = true;
                    Vec3d v = simulation.ObserverVelocityInBody(index);
                    state.velocityBfMetresPerSecond = new double3(v.x, v.y, v.z);
                    terrain.SetCollisionObserver(state.positionBfMetres, new double3(v.x, v.y, v.z),
                        nearby && observedRatio <= 0.09f);
                }
                terrain.UpdateView(state, simulation.ViewCamera, body == observedBody);
                var rig = simulation.ViewCamera.GetComponent<ObserverRenderRig>();
                terrain.SetLocalCamera(rig != null ? rig.LocalCamera : null, ObserverRenderRig.TerrainLayer);
                if (body == observedBody && cameraRig != null && cameraRig.HasPreciseSurfaceDistance && !simulation.HasObserver)
                {
                    Vec3d p = cameraRig.CameraBodyFixedMetres;
                    terrain.SetPreciseCameraPosition(simulation.ViewCamera, p.x, p.y, p.z);
                }
            }
    }

#if UNITY_EDITOR
    public static SolarSystemVisualCoordinator EnsureInScene()
    {
        SolarSystemVisualCoordinator existing = FindObjectOfType<SolarSystemVisualCoordinator>();
        if (existing != null) return existing;
        SolarSystemSimulation simulation = FindObjectOfType<SolarSystemSimulation>();
        if (simulation == null) throw new System.InvalidOperationException("SolarSystem simulation is missing.");
        return simulation.gameObject.AddComponent<SolarSystemVisualCoordinator>();
    }
#endif
}
