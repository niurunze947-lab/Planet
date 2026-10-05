using UnityEngine;
using Planet.Procedural;

namespace Planet.Solar
{
    /// <summary>
    /// One catalog body: a definition-driven surface sized to the real radius, plus an optional
    /// ring system. This component owns the body's *shape*, its *scene scale* and its *spin axis*;
    /// the orbital position and the spin angle are pushed in every frame by
    /// <see cref="SolarSystemSimulation"/>.
    ///
    /// The scale is owned here rather than derived by the simulation: <see cref="sceneRadiusUnits"/>
    /// is a serialized, Inspector-editable property of the body, filled in once by the editor bake
    /// (<see cref="BakeScale"/>) from SolarSystemCatalog and the simulation's kmPerUnit. See
    /// DEVELOPMENT-WORKFLOW.md §4.1 for why the catalog stays the single source of truth.
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
        public Material ringMaterial;

        [Header("Scale")]
        [Tooltip("Radius of this body in scene units at true scale: 1 scene unit = the simulation's " +
                 "kmPerUnit kilometres. This is the body's own render scale - the surface is sized to " +
                 "it, the ring system derives from it, and the atmosphere / cloud shells read it back " +
                 "through IPlanetRadiusProvider. Baked from SolarSystemCatalog by " +
                 "Tools > Planet > Bake Body Scales. 0 means 'not baked yet' and falls back to the " +
                 "catalog until the menu has been run once.")]
        public float sceneRadiusUnits;

        [Tooltip("The kmPerUnit this radius was derived from. Never used to compute anything at " +
                 "runtime; it only lets the body warn when the scene scale has drifted away from the " +
                 "baked value.")]
        public float bakedKmPerUnit;

        [System.NonSerialized] public Body Definition;

        private Transform _surface;
        private Transform _rings;
        private MeshFilter _surfaceFilter;
        private MeshFilter _ringFilter;
        private PlanetSurfaceView _proceduralSurface;
        private StellarSurfaceView _stellarSurface;
        private SolarSystemSimulation _simulation;
        private float _resolvedSceneRadius;
        private bool _scaleResolved;
        private float _appliedRadius = -1f;
        private bool _warnedScaleDrift;
        private float _inspectedSceneRadius = float.NaN;
        private bool _renderersVisible = true;

        /// <summary>
        /// Turns this body's own surface and ring renderers on or off without touching its
        /// simulation state, its transform or its published radius.
        ///
        /// This is the far-field half of the focus policy: at true scale a non-focused body covers
        /// a fraction of a pixel, so submitting its 50k-triangle procedural mesh and its cubemaps
        /// buys nothing. Turning the renderer off rather than deactivating the GameObject keeps the
        /// radius published, so the atmosphere and cloud shells that subscribe to it keep working
        /// and the camera's framing maths does not change.
        ///
        /// Deliberately limited to what this assembly owns: the shells live in another assembly and
        /// manage their own visibility.
        /// </summary>
        public void SetRenderersVisible(bool visible)
        {
            if (_renderersVisible == visible) return;
            _renderersVisible = visible;
            if (_proceduralSurface != null) _proceduralSurface.SetRenderersVisible(visible);

            if (_surface != null)
            {
                MeshRenderer r = _surface.GetComponent<MeshRenderer>();
                if (r != null) r.enabled = visible;
            }
            if (_rings != null)
            {
                MeshRenderer r = _rings.GetComponent<MeshRenderer>();
                if (r != null) r.enabled = visible;
            }
        }

        /// <summary>True when this body's own renderers are currently enabled.</summary>
        public bool RenderersVisible { get { return _renderersVisible; } }

        /// <summary>Unit vector (Unity world axes) of the body's north pole.</summary>
        public Vector3 PoleUnity { get; private set; } = Vector3.up;

        /// <summary>
        /// Radius of the body in scene units, including the simulation's size exaggeration. This is
        /// the value published to the atmosphere and cloud shells through IPlanetRadiusProvider.
        /// It reads the body's own <see cref="sceneRadiusUnits"/>; the catalog is only consulted
        /// when the body has never been baked.
        /// </summary>
        public float RadiusUnits
        {
            get
            {
                if (!_scaleResolved) ResolveScale();
                return _resolvedSceneRadius * SizeExaggeration;
            }
        }

        /// <summary>Runtime size multiplier owned by the simulation (the 'G' key toggles it).</summary>
        public float SizeExaggeration
        {
            get
            {
                SolarSystemSimulation sim = Simulation;
                return sim != null ? Mathf.Max(sim.bodySizeExaggeration, 1e-6f) : 1f;
            }
        }

        private SolarSystemSimulation Simulation
        {
            get
            {
                if (_simulation == null) _simulation = GetComponentInParent<SolarSystemSimulation>();
                return _simulation;
            }
        }

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
                return;
            }

            // sceneRadiusUnits is this body's own, hand-editable scale. Nothing else writes the
            // transforms, so an Inspector edit has to be picked up here rather than by rebuilding
            // the mesh: the globe mesh is generated at unit radius, so a new scale only needs the
            // surface and ring transforms rewritten.
            if (!Mathf.Approximately(_inspectedSceneRadius, sceneRadiusUnits))
            {
                _inspectedSceneRadius = sceneRadiusUnits;
                _scaleResolved = false;
                _appliedRadius = -1f;
                if (isActiveAndEnabled) ApplyRadius(RadiusUnits);
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
            // The body owns its scale now, so this reads the baked value rather than re-deriving
            // it from the catalog on every rebuild.
            _simulation = null;
            _scaleResolved = false;
            _appliedRadius = -1f;
            float radiusUnits = RadiusUnits;

            _proceduralSurface = GetComponent<PlanetSurfaceView>();
            _stellarSurface = GetComponent<StellarSurfaceView>();
            if (_stellarSurface != null && _stellarSurface.definition != null)
            {
                _stellarSurface.BuildSurface(RadiusUnits);
                _surface = _stellarSurface.SurfaceTransform;
            }
            else if (_proceduralSurface != null && _proceduralSurface.definition != null)
            {
                _proceduralSurface.BuildSurface(radiusUnits);
                _surface = _proceduralSurface.SurfaceTransform;
                _surfaceFilter = _surface != null ? _surface.GetComponent<MeshFilter>() : null;
            }
            else
            {
                // ---- fallback surface -----------------------------------------------------
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
                _surface.localScale = Vector3.one * radiusUnits;
                _surface.localPosition = Vector3.zero;
                _surface.localRotation = Quaternion.identity;
            }

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
                rr.sharedMaterial = ringMaterial != null ? ringMaterial : SolarMaterials.Ring;
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rr.receiveShadows = false;
                rr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                rr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                rr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

                _rings.localRotation = Quaternion.identity;   // rings lie in the body's equator
                _rings.localPosition = Vector3.zero;
                float outerUnits = (float)(rs.outerKm * (radiusUnits / Definition.RadiusKm));
                _rings.localScale = Vector3.one * outerUnits;
                _rings.gameObject.SetActive(true);
            }
            else if (_rings != null)
            {
                _rings.gameObject.SetActive(false);
            }

            // BuildVisuals has just written the radius into both transforms, so record it: the
            // per-frame path only has work to do when the value actually changes (the 'G' toggle
            // or a hand edit of sceneRadiusUnits in the Inspector).
            _appliedRadius = radiusUnits;
            _inspectedSceneRadius = sceneRadiusUnits;

            // A rebuild may have recreated the renderers, so the far-field visibility has to be
            // written back or a body that was culled would pop back in on the next rebake.
            ApplyRendererVisibility();
        }

        private void ApplyRendererVisibility()
        {
            if (_proceduralSurface != null) _proceduralSurface.SetRenderersVisible(_renderersVisible);
            if (_surface != null)
            {
                MeshRenderer r = _surface.GetComponent<MeshRenderer>();
                if (r != null) r.enabled = _renderersVisible;
            }
            if (_rings != null)
            {
                MeshRenderer r = _rings.GetComponent<MeshRenderer>();
                if (r != null) r.enabled = _renderersVisible;
            }
        }

        /// <summary>
        /// Applies the body's absolute state for this frame.
        ///
        /// The radius is deliberately not a parameter: the body owns its own scale in
        /// <see cref="sceneRadiusUnits"/>, so the simulation only places and spins it.
        /// </summary>
        /// <param name="worldPosition">Position in scene units.</param>
        /// <param name="spinDegrees">Absolute sidereal rotation angle since J2000.</param>
        public void ApplyState(Vector3 worldPosition, double spinDegrees)
        {
            transform.position = worldPosition;

            // Tilt the spin axis first, then spin about it. Composing in this order means the
            // rotation happens about the body's own (already tilted) pole, which is exactly the
            // real geometry: the axis keeps a fixed direction in inertial space while the body
            // turns underneath it.
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, PoleUnity);
            float angle = (float)(spinDegrees - System.Math.Floor(spinDegrees / 360.0) * 360.0);
            transform.rotation = tilt * Quaternion.AngleAxis(angle, Vector3.up);

            ApplyRadius(RadiusUnits);

            // Spin changes every frame, so it must remain outside ApplyRadius's cached path.
            if (_stellarSurface != null && _stellarSurface.definition != null)
                _stellarSurface.SetRigidSpin(angle);

            // The atmosphere and cloud shells are deliberately NOT pushed from here. Both live
            // outside this assembly and already pull RadiusUnits from the IPlanetRadiusProvider on
            // this component in their own ApplyProperties, which runs every frame from LateUpdate.
            // Radius travels one way only - the body publishes it, the shells subscribe - so this
            // assembly never needs a compile-time reference to the shell components.
        }

        /// <summary>
        /// Writes the radius into the surface and ring transforms.
        ///
        /// This is the whole per-frame cost of scale, and it is skipped whenever the value is
        /// unchanged. The globe mesh is generated at unit radius, so a different scale is a
        /// transform edit and never a mesh rebuild.
        /// </summary>
        private void ApplyRadius(float radiusUnits)
        {
            if (Mathf.Approximately(radiusUnits, _appliedRadius)) return;
            _appliedRadius = radiusUnits;

            if (_stellarSurface != null && _stellarSurface.definition != null)
                _stellarSurface.SetRadius(radiusUnits);
            else if (_proceduralSurface != null && _proceduralSurface.definition != null)
                _proceduralSurface.SetRadius(radiusUnits);
            else if (_surface != null) _surface.localScale = Vector3.one * radiusUnits;

            if (_rings != null && _rings.gameObject.activeSelf && Definition != null && Definition.Rings.HasValue)
            {
                // The ring mesh has unit outer radius; scale it to the real outer edge.
                float outerUnits = (float)(Definition.Rings.Value.outerKm * (radiusUnits / Definition.RadiusKm));
                _rings.localScale = Vector3.one * outerUnits;
            }
        }

        // ==================================================================================
        // Scale
        // ==================================================================================

        /// <summary>
        /// Resolves the scene-unit radius at true scale. The body's own baked value wins; the
        /// catalog is only consulted when the body has never been baked, so a component that was
        /// just added still renders correctly before the editor menu persists its scale.
        /// </summary>
        private void ResolveScale()
        {
            _scaleResolved = true;

            if (sceneRadiusUnits > 0f)
            {
                _resolvedSceneRadius = sceneRadiusUnits;
                WarnIfScaleDrifted();
                return;
            }

            SolarSystemSimulation sim = Simulation;
            double kmPerUnit = sim != null ? sim.KmPerUnit : SolarSystemFactory.DefaultKmPerUnit;
            // Unbaked body with no catalog entry: publish 0, not a fake 1-unit radius.
            // A 1-unit fallback makes every close camera look "inside" the body and silently
            // disables the near-surface gate (SurfaceModeController).
            _resolvedSceneRadius = Definition != null && kmPerUnit > 0.0
                ? (float)(Definition.RadiusKm / kmPerUnit)
                : 0f;
        }

        private void WarnIfScaleDrifted()
        {
            if (_warnedScaleDrift || bakedKmPerUnit <= 0f) return;
            SolarSystemSimulation sim = Simulation;
            if (sim == null || Mathf.Approximately(bakedKmPerUnit, sim.KmPerUnit)) return;

            _warnedScaleDrift = true;
            Debug.LogWarning(string.Format(
                "[CelestialBody] '{0}' scale was baked for kmPerUnit={1:N0} but the simulation now uses " +
                "{2:N0}. Run Tools > Planet > Bake Body Scales to re-derive every scene radius.",
                name, bakedKmPerUnit, sim.KmPerUnit), this);
        }

        /// <summary>
        /// Derives this body's scene-unit radius from <see cref="SolarSystemCatalog"/> and writes it
        /// into the serialized scale. Editor and scene-build only: this is the single place where the
        /// catalog's physical radius becomes the body's own persistent scale, which is why the
        /// catalogue stays the one source of truth for physical size (DEVELOPMENT-WORKFLOW §4).
        /// </summary>
        /// <returns>True when the stored scale actually changed.</returns>
        public bool BakeScale(float kmPerUnit)
        {
            EnsureDefinition();
            if (Definition == null) return false;

            float unit = kmPerUnit > 0f ? kmPerUnit : SolarSystemFactory.DefaultKmPerUnit;
            float next = (float)(Definition.RadiusKm / unit);
            bool changed = !Mathf.Approximately(sceneRadiusUnits, next) ||
                           !Mathf.Approximately(bakedKmPerUnit, unit);

            sceneRadiusUnits = next;
            bakedKmPerUnit = unit;
            _scaleResolved = false;
            _appliedRadius = -1f;
            _warnedScaleDrift = false;
            return changed;
        }

        [ContextMenu("Rebake Scale From Catalog")]
        private void RebakeScaleFromCatalog()
        {
            SolarSystemSimulation sim = Simulation;
            BakeScale(sim != null ? sim.KmPerUnit : SolarSystemFactory.DefaultKmPerUnit);
            BuildVisuals();
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
