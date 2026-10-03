using UnityEngine;

/// <summary>
/// Keeps the procedural atmosphere and the physical starfield aligned with the scene's sun.
/// A directional light points along its forward vector, so the incoming sunlight direction is
/// the negative of that vector.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class PlanetLightingController : MonoBehaviour
{
    public Light sun;
    public Material starfieldMaterial;
    [Range(0.02f, 1f)] public float updateInterval = 0.12f;

    private float _nextUpdate;
    private Vector3 _lastDirection;

    private void OnEnable()
    {
        SyncLighting(true);
    }

    private void Update()
    {
        if (sun == null) sun = GetComponent<Light>();
        if (Time.realtimeSinceStartup < _nextUpdate) return;
        Vector3 direction = sun != null ? -sun.transform.forward : new Vector3(-0.45f, 0.30f, -0.84f);
        if ((direction - _lastDirection).sqrMagnitude > 0.000001f)
            SyncLighting(false);
        _nextUpdate = Time.realtimeSinceStartup + Mathf.Max(0.02f, updateInterval);
    }

    private void SyncLighting(bool force)
    {
        if (sun == null) sun = GetComponent<Light>();
        Vector3 direction = sun != null ? -sun.transform.forward : new Vector3(-0.45f, 0.30f, -0.84f);
        if (!force && (direction - _lastDirection).sqrMagnitude < 0.000001f) return;
        _lastDirection = direction;

        Material sky = RenderSettings.skybox;
        if (sky != null && sky.HasProperty("_SunDirection"))
            sky.SetVector("_SunDirection", new Vector4(direction.x, direction.y, direction.z, 0f));
        if (starfieldMaterial != null && starfieldMaterial.HasProperty("_SunDirection"))
            starfieldMaterial.SetVector("_SunDirection", new Vector4(direction.x, direction.y, direction.z, 0f));

        PlanetAtmosphere[] atmospheres = FindObjectsOfType<PlanetAtmosphere>();
        for (int i = 0; i < atmospheres.Length; i++)
            atmospheres[i].SetSunDirection(direction);
    }
}
