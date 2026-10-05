using System.Collections;
using UnityEngine;

namespace Planet.Procedural
{
    /// <summary>Loads only selected derived textures; callers supply the published view-height ratio.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(PlanetSurfaceView))]
    public sealed class PlanetTextureLodController : MonoBehaviour
    {
        public PlanetTextureLodSet lodSet;
        public int ActiveTier { get; private set; } = -1;
        public int RequestedTier { get; private set; }
        public bool IsLoading { get { return loading != null; } }
        public Cubemap ActiveAlbedo { get { return albedo; } }
        private PlanetSurfaceView view;
        private PlanetCloudLayer clouds;
        private Cubemap albedo, normal, density, baselineCloud;
        private Coroutine loading;
        private ResourceRequest pendingAlbedo, pendingNormal, pendingCloud;

        private void OnEnable()
        {
            view = GetComponent<PlanetSurfaceView>(); clouds = GetComponent<PlanetCloudLayer>();
            baselineCloud = clouds != null ? clouds.densityCubemap : null;
            ActiveTier = -1;
        }

        public void UpdateView(float clearanceRatio, bool focused)
        {
            if (lodSet == null || lodSet.tiers == null || lodSet.tiers.Length == 0) return;
            RequestedTier = lodSet.SelectTier(clearanceRatio, focused, RequestedTier);
            if (loading != null || ActiveTier == RequestedTier) return;
            if (Application.isPlaying) loading = StartCoroutine(LoadAsync(RequestedTier));
            else
            {
                PlanetTextureTier tier = lodSet.tiers[RequestedTier];
                Apply(RequestedTier, Load(tier.albedoResource), Load(tier.normalResource), Load(tier.cloudResource));
            }
        }

        private static Cubemap Load(string path) { return string.IsNullOrEmpty(path) ? null : Resources.Load<Cubemap>(path); }

        private IEnumerator LoadAsync(int tierIndex)
        {
            PlanetTextureTier tier = lodSet.tiers[tierIndex];
            ResourceRequest a = pendingAlbedo = Resources.LoadAsync<Cubemap>(tier.albedoResource);
            ResourceRequest n = pendingNormal = string.IsNullOrEmpty(tier.normalResource) ? null : Resources.LoadAsync<Cubemap>(tier.normalResource);
            ResourceRequest c = pendingCloud = string.IsNullOrEmpty(tier.cloudResource) ? null : Resources.LoadAsync<Cubemap>(tier.cloudResource);
            yield return a;
            if (n != null) yield return n;
            if (c != null) yield return c;
            Cubemap nextA = a.asset as Cubemap, nextN = n != null ? n.asset as Cubemap : null, nextC = c != null ? c.asset as Cubemap : null;
            // Focus can change while a higher tier is loading. Discard it instead of briefly applying it.
            if (tierIndex == RequestedTier) Apply(tierIndex, nextA, nextN, nextC);
            else { Release(nextA); Release(nextN); Release(nextC); }
            loading = null; pendingAlbedo = pendingNormal = pendingCloud = null;
        }

        private void Apply(int tier, Cubemap nextAlbedo, Cubemap nextNormal, Cubemap nextCloud)
        {
            if (nextAlbedo == null) { Debug.LogError("[Planet LOD] Missing baked tier " + tier, this); return; }
            Cubemap oldA = albedo, oldN = normal, oldC = density;
            albedo = nextAlbedo; normal = nextNormal; density = nextCloud;
            view.SetVisualTextures(albedo, normal, density);
            if (clouds != null) { clouds.densityCubemap = density != null ? density : baselineCloud; clouds.Refresh(); }
            ActiveTier = tier;
            if (oldA != albedo) Release(oldA);
            if (oldN != normal) Release(oldN);
            if (oldC != density) Release(oldC);
        }

        private void Release(Cubemap texture)
        {
            if (!Application.isPlaying || texture == null || (view != null && (texture == view.albedoCubemap || texture == view.cloudDensityCubemap)) || texture == baselineCloud) return;
            Resources.UnloadAsset(texture);
        }

        private void OnDisable()
        {
            if (loading != null) StopCoroutine(loading);
            loading = null;
            if (view != null) view.ClearVisualTextures();
            // Restoring the reference is enough: a surviving cloud layer applies it in LateUpdate.
            // Refresh here could recreate a shell while the parent GameObject is deactivating.
            if (clouds != null) clouds.densityCubemap = baselineCloud;
            ReleaseWhenComplete(pendingAlbedo); ReleaseWhenComplete(pendingNormal); ReleaseWhenComplete(pendingCloud);
            pendingAlbedo = pendingNormal = pendingCloud = null;
            Release(albedo); Release(normal); Release(density);
            albedo = normal = density = null; ActiveTier = -1;
        }

        private void ReleaseWhenComplete(ResourceRequest request)
        {
            if (request == null || !Application.isPlaying) return;
            if (request.isDone) Release(request.asset as Cubemap);
            else request.completed += finished => Release(((ResourceRequest)finished).asset as Cubemap);
        }
    }
}
