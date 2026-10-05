using Planet.Procedural;
using Planet.Solar;
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
        if (earthTextures != null && cameraRig != null && Application.isPlaying)
            earthTextures.UpdateView(cameraRig.SurfaceClearanceRatio, simulation.focusBody == "Earth");
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
                controller.UpdateView(cameraRig.SurfaceClearanceRatio,
                    body != null && string.Equals(body.bodyName, simulation.focusBody, System.StringComparison.OrdinalIgnoreCase));
            }
        }
        if (terrainViews != null)
            foreach (PlanetSurfaceView view in terrainViews)
            {
                if (view == null || !view.useRuntimeChunks || view.TerrainRenderer == null) continue;
                CelestialBody body = view.GetComponent<CelestialBody>();
                var terrain = view.TerrainRenderer;
                terrain.UpdateView(terrain.ViewFromCamera(simulation.ViewCamera), simulation.ViewCamera,
                    body != null && body.bodyName == simulation.focusBody);
                if (body == simulation.FocusView && cameraRig != null && cameraRig.HasPreciseSurfaceDistance)
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
