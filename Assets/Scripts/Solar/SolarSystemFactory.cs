using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Planet.Solar
{
    /// <summary>
    /// Turns the catalog into a scene: one white-model sphere per body, an orbit line per
    /// heliocentric orbit, two instanced debris belts, the Sun's light, and the camera.
    ///
    /// Everything is derived from <see cref="SolarSystemCatalog"/>, so the scene and the numbers
    /// in the HUD can never drift apart. The one value that is *copied* rather than read live is
    /// each body's scene-unit radius: <see cref="CelestialBody.BakeScale"/> converts it once at
    /// scene-build time and stores it on the body, which is what makes the runtime scale a
    /// serialized per-body property instead of a per-frame derivation. Changing the catalog or
    /// <c>kmPerUnit</c> therefore requires re-running Tools ▸ Planet ▸ Bake Body Scales (or the
    /// scene rebuild, which does it for you).
    /// </summary>
    public static class SolarSystemFactory
    {
        public const string RootName = "Solar System";

        /// <summary>Scene units per kilometre at the default scale: 1 unit = 1,000,000 km.</summary>
        public const float DefaultKmPerUnit = 1000000f;

        public static GameObject BuildAll(Material skybox, VolumeProfile postProfile,
                                          float kmPerUnit = DefaultKmPerUnit, string focus = "Earth")
        {
            var root = new GameObject(RootName);
            var sim = root.AddComponent<SolarSystemSimulation>();
            sim.kmPerUnit = kmPerUnit;
            sim.focusBody = focus;

            // ---- containers -----------------------------------------------------------------
            var bodies = new GameObject("Bodies");
            bodies.transform.SetParent(root.transform, false);
            sim.bodiesRoot = bodies.transform;

            var orbits = new GameObject("Orbits");
            orbits.transform.SetParent(root.transform, false);
            sim.orbitsRoot = orbits.transform;

            // ---- space frames ---------------------------------------------------------------
            // Added explicitly rather than left to the simulation's play-mode auto-creation, so a
            // built scene carries the component in its serialized form and it can be inspected,
            // tuned and audited without entering play mode.
            root.AddComponent<SolarSpaceContext>();

            // Debug view of which bodies are still streaming (mesh / texture tier / terrain chunks).
            // Added here for the same reason as the space context: the scene is generated, so a
            // hand-placed component would disappear on the next rebuild.
            root.AddComponent<BodyLoadingHighlight>();

            // ---- bodies ---------------------------------------------------------------------
            Body[] defs = SolarSystemCatalog.Bodies;
            for (int i = 0; i < defs.Length; i++)
            {
                Body def = defs[i];

                var go = new GameObject(def.Name);
                go.transform.SetParent(bodies.transform, false);

                var cb = go.AddComponent<CelestialBody>();
                cb.bodyName = def.Name;
                ChooseResolution(def, out cb.longitudeSegments, out cb.latitudeSegments);
                // The catalog's physical radius becomes the body's own serialized scene scale here,
                // once, at scene-build time - not re-derived per frame and not re-derived per Play.
                cb.BakeScale(kmPerUnit);
                cb.BuildVisuals();
            }

            // ---- orbit lines -----------------------------------------------------------------
            for (int i = 0; i < defs.Length; i++)
            {
                Body def = defs[i];
                if (!def.HasOrbit) continue;
                if (!string.IsNullOrEmpty(def.Parent)) continue;   // heliocentric only

                var go = new GameObject(def.Name + " Orbit");
                go.transform.SetParent(orbits.transform, false);

                var op = go.AddComponent<OrbitPath>();
                op.bodyName = def.Name;
                op.kmPerUnit = kmPerUnit;
                op.samples = def.Orbit.PeriodDays > 4000.0 ? 1024 : 720;
                op.color = OrbitColor(def);
                op.Rebuild();
            }

            // ---- Kuiper belt boundary rings ----------------------------------------------------
            AddCircle(orbits.transform, "Kuiper Belt Inner (30 AU)", 30f, new Color(0.34f, 0.42f, 0.62f, 0.22f), kmPerUnit);
            AddCircle(orbits.transform, "Kuiper Belt Outer (50 AU)", 50f, new Color(0.34f, 0.42f, 0.62f, 0.22f), kmPerUnit);

            // ---- debris belts ------------------------------------------------------------------
            var asteroidBelt = new GameObject("Asteroid Belt 2.1-3.3 AU");
            asteroidBelt.transform.SetParent(root.transform, false);
            var ab = asteroidBelt.AddComponent<BeltField>();
            ab.fieldName = "Asteroid Belt";
            ab.innerAu = 2.06;
            ab.outerAu = 3.27;
            ab.count = 2200;
            ab.maxEccentricity = 0.22;
            ab.coldFraction = 0.68f;
            ab.coldMaxInclination = 4.0;
            ab.hotMaxInclination = 22.0;
            ab.markerAngularSize = 0.0012f;

            var kuiperBelt = new GameObject("Kuiper Belt 30-50 AU");
            kuiperBelt.transform.SetParent(root.transform, false);
            var kb = kuiperBelt.AddComponent<BeltField>();
            kb.fieldName = "Kuiper Belt";
            kb.innerAu = 30.0;
            kb.outerAu = 50.0;
            kb.count = 5000;
            kb.maxEccentricity = 0.20;
            kb.coldFraction = 0.62f;
            kb.coldMaxInclination = 5.0;
            kb.hotMaxInclination = 26.0;
            kb.radialBias = 0.85;
            kb.markerAngularSize = 0.0012f;

            // ---- light ---------------------------------------------------------------------------
            var lightGo = new GameObject("Sun Light");
            lightGo.transform.SetParent(root.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.972f, 0.925f);
            light.intensity = 1.35f;
            light.shadows = LightShadows.None;     // cascades over 1e10 km are meaningless
            lightGo.AddComponent<UniversalAdditionalLightData>();
            sim.sunLight = light;

            // ---- camera ---------------------------------------------------------------------------
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 1e-4f;
            cam.farClipPlane = 40000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<SolarSystemCamera>();
            camGo.AddComponent<SolarSystemHud>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;
            sim.viewCamera = cam;

            // ---- global volume ---------------------------------------------------------------------
            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0f;
            vol.sharedProfile = postProfile;

            // ---- environment -------------------------------------------------------------------------
            RenderSettings.skybox = skybox;
            RenderSettings.ambientMode = AmbientMode.Flat;
            // Deep-space fill light. Real space has no ambient at all, but a pure black night side
            // makes a white model unreadable; this is just enough to show the silhouette.
            RenderSettings.ambientLight = new Color(0.055f, 0.062f, 0.082f);
            RenderSettings.fog = false;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.35f;

            sim.SnapToEpoch();
            return root;
        }

        private static void AddCircle(Transform parent, string name, float radiusAu, Color color, float kmPerUnit)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<CircleLine>();
            c.radiusAu = radiusAu;
            c.color = color;
            c.kmPerUnit = kmPerUnit;
            c.Rebuild();
        }

        private static Color OrbitColor(Body def)
        {
            switch (def.Kind)
            {
                case BodyKind.Planet: return new Color(0.42f, 0.56f, 0.80f, 0.55f);
                case BodyKind.DwarfPlanet: return new Color(0.62f, 0.50f, 0.34f, 0.45f);
            }
            return new Color(0.5f, 0.5f, 0.5f, 0.4f);
        }

        private static void ChooseResolution(Body def, out int longitudeSegments, out int latitudeSegments)
        {
            if (def.IsSun) { longitudeSegments = 160; latitudeSegments = 80; return; }
            if (def.RadiusKm >= 20000.0) { longitudeSegments = 128; latitudeSegments = 64; return; }
            if (def.RadiusKm >= 3000.0) { longitudeSegments = 96; latitudeSegments = 48; return; }
            longitudeSegments = 64;
            latitudeSegments = 32;
        }
    }
}
