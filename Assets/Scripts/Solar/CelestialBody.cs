using UnityEngine;
using Planet.Procedural;

namespace Planet.Solar
{
    /// <summary>
    /// One white-model body: an untextured sphere sized to the real radius, plus an optional
    /// ring system. This component owns the body's *shape* and its *spin axis*; the orbital
    /// position and the spin angle are pushed in every frame by <see cref="SolarSystemSimulation"/>.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class CelestialBody : MonoBehaviour, IPlanetRadiusProvider
    {
        [Tooltip("Name used to look the body up in SolarSystemCatalog.")]
        public string bodyName = "Earth";

        [Header("Mesh resolution")]
        public int longitudeSegments = 96;
        public int latitudeSegments = 48;

        [Header("Rings")]
        public int ringSegments = 192;

        [System.NonSerialized] public Body Definition;

        private Transform _surface;
        private Transform _rings;
        private MeshFilter _surfaceFilter;
        private MeshFilter _ringFilter;
        private PlanetSurfaceView _proceduralSurface;

        /// <summary>Unit vector (Unity world axes) of the body's north pole.</summary>
        public Vector3 PoleUnity { get; private set; } = Vector3.up;

        /// <summary>Radius of the body in scene units, set by the simulation.</summary>
        public float RadiusUnits { get; private set; }

        private void OnEnable()
        {
            EnsureDefinition();
            BuildVisuals();
        }

        private void OnValidate()
        {
            // bodyName can change in the inspector; re-resolve and mark for rebuild. The rebuild
            // itself is deferred to the next Update: assigning sharedMesh from inside OnValidate
            // makes Unity log "SendMessage cannot be called during Awake, CheckConsistency, or
            // OnValidate" for every mesh change.
            if (Definition == null || Definition.Name != bodyName)
            {
                Definition = null;
                EnsureDefinition();
                _rebuildPending = true;
            }
        }

        private void Update()
        {
            if (_rebuildPending)
            {
                _rebuildPending = false;
                BuildVisuals();
            }
        }

        /// <summary>
        /// Resolves the catalog entry for <see cref="bodyName"/>. Always re-resolves rather than
        /// caching: the component is added before its name is assigned, so caching the first lookup
        /// would silently give every body the default (Earth) definition.
        /// </summary>
        private void EnsureDefinition()
        {
            Definition = SolarSystemCatalog.Find(bodyName);
        }

        /// <summary>Rebuilds the mesh hierarchy. Safe to call repeatedly.</summary>
        private bool _rebuildPending;

        public void BuildVisuals()
        {
            EnsureDefinition();
            if (Definition == null) return;

            // ---- pole, in Unity world axes -------------------------------------------------
            Vec3d poleEcliptic = OrbitalMechanics.PoleFromRaDec(Definition.PoleRaDeg, Definition.PoleDecDeg);
            Vector3 pole = OrbitalMechanics.ToUnity(poleEcliptic, 1.0).normalized;

            // Satellites do not carry their own IAU pole; they sit in the parent's equatorial
            // plane and spin about the parent's axis, so borrow it.
            if (Definition.Kind == BodyKind.Moon && !string.IsNullOrEmpty(Definition.Parent))
            {
                Body parent = SolarSystemCatalog.Find(Definition.Parent);
                if (parent != null)
                {
                    pole = OrbitalMechanics.ToUnity(
                        OrbitalMechanics.PoleFromRaDec(parent.PoleRaDeg, parent.PoleDecDeg), 1.0).normalized;
                }
            }
            PoleUnity = pole;

            // The render scale is needed in edit mode and during OnEnable, before the first
            // simulation tick. Never build a unit-radius Earth in a million-km-per-unit scene.
            SolarSystemSimulation simulation = GetComponentInParent<SolarSystemSimulation>();
            if (simulation != null)
                RadiusUnits = (float)(Definition.RadiusKm / simulation.KmPerUnit *
                    Mathf.Max(simulation.bodySizeExaggeration, 1e-6f));
            else if (RadiusUnits <= 0f) RadiusUnits = 1f;

            _proceduralSurface = GetComponent<PlanetSurfaceView>();
            if (_proceduralSurface != null && _proceduralSurface.definition != null)
            {
                _proceduralSurface.BuildSurface(RadiusUnits);
                _surface = _proceduralSurface.SurfaceTransform;
                _surfaceFilter = _surface != null ? _surface.GetComponent<MeshFilter>() : null;
                return;
            }

            // ---- surface -------------------------------------------------------------------
            if (_surface == null)
            {
                _surface = GetOrCreateChild("Surface");
                _surfaceFilter = _surface.GetComponent<MeshFilter>();
                if (_surfaceFilter == null) _surfaceFilter = _surface.gameObject.AddComponent<MeshFilter>();
                if (_surface.GetComponent<MeshRenderer>() == null) _surface.gameObject.AddComponent<MeshRenderer>();
            }

            _surfaceFilter.sharedMesh = SolarMeshFactory.Sphere(
                longitudeSegments, latitudeSegments, (float)Definition.Flattening, Definition.ShapeScale);

            var surfaceRenderer = _surface.GetComponent<MeshRenderer>();
            surfaceRenderer.sharedMaterial = Definition.IsSun ? SolarMaterials.Sun : SolarMaterials.Clay;
            surfaceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            surfaceRenderer.receiveShadows = false;
            surfaceRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            surfaceRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            surfaceRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            // The child carries the radius; the parent transform is free for orbit placement.
            float radiusUnits = 1.0f;
            _surface.localScale = Vector3.one * radiusUnits;
            _surface.localPosition = Vector3.zero;
            _surface.localRotation = Quaternion.identity;

            // ---- rings ---------------------------------------------------------------------
            bool wantsRings = Definition.Rings.HasValue;
            if (wantsRings)
            {
                if (_rings == null)
                {
                    _rings = GetOrCreateChild("Rings");
                    _ringFilter = _rings.GetComponent<MeshFilter>();
                    if (_ringFilter == null) _ringFilter = _rings.gameObject.AddComponent<MeshFilter>();
                    if (_rings.GetComponent<MeshRenderer>() == null) _rings.gameObject.AddComponent<MeshRenderer>();
                }

                RingSet rs = Definition.Rings.Value;
                _ringFilter.sharedMesh = SolarMeshFactory.Rings(rs.BandFractions(), ringSegments);

                var rr = _rings.GetComponent<MeshRenderer>();
                rr.sharedMaterial = SolarMaterials.Ring;
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rr.receiveShadows = false;
                rr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                rr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                rr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

                _rings.localRotation = Quaternion.identity;   // rings lie in the body's equator
                _rings.localPosition = Vector3.zero;
                _rings.gameObject.SetActive(true);
            }
            else if (_rings != null)
            {
                _rings.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Applies the body's absolute state for this frame.
        /// </summary>
        /// <param name="worldPosition">Position in scene units.</param>
        /// <param name="radiusUnits">Real radius converted to scene units.</param>
        /// <param name="spinDegrees">Absolute sidereal rotation angle since J2000.</param>
        public void ApplyState(Vector3 worldPosition, float radiusUnits, double spinDegrees)
        {
            RadiusUnits = radiusUnits;

            transform.position = worldPosition;

            // Tilt the spin axis first, then spin about it. Composing in this order means the
            // rotation happens about the body's own (already tilted) pole, which is exactly the
            // real geometry: the axis keeps a fixed direction in inertial space while the body
            // turns underneath it.
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, PoleUnity);
            float angle = (float)(spinDegrees - System.Math.Floor(spinDegrees / 360.0) * 360.0);
            transform.rotation = tilt * Quaternion.AngleAxis(angle, Vector3.up);

            if (_proceduralSurface != null && _proceduralSurface.definition != null)
                _proceduralSurface.SetRadius(radiusUnits);
            else if (_surface != null) _surface.localScale = Vector3.one * radiusUnits;

            PlanetAtmosphere atmosphere = GetComponent<PlanetAtmosphere>();
            if (atmosphere != null)
            {
                atmosphere.planetRadius = radiusUnits;
                atmosphere.Refresh();
            }
            PlanetCloudLayer clouds = GetComponent<PlanetCloudLayer>();
            if (clouds != null)
            {
                clouds.planetRadius = radiusUnits;
                clouds.Refresh();
            }

            if (_rings != null && _rings.gameObject.activeSelf && Definition != null && Definition.Rings.HasValue)
            {
                // The ring mesh has unit outer radius; scale it to the real outer edge.
                float outerUnits = (float)(Definition.Rings.Value.outerKm * (radiusUnits / Definition.RadiusKm));
                _rings.localScale = new Vector3(outerUnits, 1f, outerUnits);
            }
        }

        private Transform GetOrCreateChild(string childName)
        {
            Transform existing = transform.Find(childName);
            if (existing != null) return existing;
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            return go.transform;
        }
    }
}
