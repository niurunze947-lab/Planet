using UnityEngine;

namespace Planet.Procedural
{
    /// <summary>Switches prebuilt meshes from the published view-height contract; never generates runtime geometry.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(PlanetSurfaceView))]
    public sealed class PlanetMeshLodController : MonoBehaviour
    {
        public PlanetMeshLodSet lodSet;
        public int ActiveTier { get; private set; } = -1;
        public int RequestedTier { get; private set; }

        private PlanetSurfaceView view;

        private void OnEnable()
        {
            view = GetComponent<PlanetSurfaceView>();
            ActiveTier = -1;
            Apply(0);
        }

        public void UpdateView(float clearanceRatio, bool focused)
        {
            if (lodSet == null || view == null) return;
            RequestedTier = lodSet.SelectTier(clearanceRatio, focused, RequestedTier);
            if (RequestedTier != ActiveTier) Apply(RequestedTier);
        }

        private void Apply(int tier)
        {
            if (lodSet == null || view == null) return;
            Mesh mesh = lodSet.MeshAt(tier);
            if (mesh == null)
            {
                Debug.LogError("[Planet Mesh LOD] Missing offline mesh for tier " + tier + ".", this);
                return;
            }
            view.SetMeshAsset(mesh, tier);
            ActiveTier = tier;
        }
    }
}
