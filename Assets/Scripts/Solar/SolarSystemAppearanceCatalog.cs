using Planet.Procedural;
using UnityEngine;

namespace Planet.Solar
{
    /// <summary>Initial scientific appearance presets. Existing definition assets remain editable authority.</summary>
    public static class SolarSystemAppearanceCatalog
    {
        public static void SynchronizePhysical(PlanetDefinition definition, Body body)
        {
            definition.physical.radiusMetres = body.RadiusKm * 1000.0;
            definition.physical.flattening = (float)body.Flattening;
            definition.physical.shapeScale = body.ShapeScale;
            definition.physical.rotationPeriodSeconds = body.RotationHours * 3600.0;
            Body pole = body.Kind == BodyKind.Moon ? SolarSystemCatalog.Find(body.Parent) : body;
            definition.physical.northPoleRightAscensionDegrees = pole.PoleRaDeg;
            definition.physical.northPoleDeclinationDegrees = pole.PoleDecDeg;
        }

        public static PlanetDefinition CreateDefault(Body body)
        {
            PlanetDefinition d = ScriptableObject.CreateInstance<PlanetDefinition>();
            d.displayName = body.Name;
            SynchronizePhysical(d, body);
            int seed = 1729;
            foreach (char c in body.Name) seed = unchecked(seed * 31 + c);
            d.generation.seed = seed;
            d.generation.visualReliefScale = 1f;
            d.generation.slopeSampleDistanceMetres = (float)body.RadiusKm * 0.35f;
            d.features.hasOcean = d.features.hasEcology = d.features.hasClouds = d.features.hasIceCaps = false;
            d.features.hasAtmosphere = false;
            d.physical.atmosphereHeightMetres = 0f;
            PlanetAppearanceParameters a = d.appearance;
            a.surfaceKind = PlanetSurfaceKind.CrateredRock;
            a.baseColor = C(0.38f, 0.36f, 0.33f); a.secondaryColor = C(0.58f, 0.56f, 0.51f);
            a.interpretation = "Procedural statistical morphology; no observed geographic registration.";
            switch (body.Name)
            {
                case "Sun":
                    d.physical.massKilograms = 1.98847e30;
                    a.surfaceKind = PlanetSurfaceKind.StellarPhotosphere;
                    a.baseColor = C(1f, 0.98f, 0.93f); a.emissionStrength = 0.95f; a.limbDarkening = 0.65f;
                    Feature(a, 25f, 12f, 4.5f, 3f, 0.9f, C(0.16f, 0.12f, 0.09f));
                    a.interpretation = "White warm photosphere, illustrative spot and unresolved statistical granulation. No corona, prominences, differential rotation or radiometric solar luminance.";
                    break;
                case "Mercury":
                    d.physical.massKilograms = 3.3011e23;
                    a.baseColor = C(0.43f, 0.42f, 0.40f); a.secondaryColor = C(0.52f, 0.50f, 0.47f);
                    a.craterStrength = 0.95f; a.craterDepthMetres = 2400f; a.reliefMetres = 1500f;
                    break;
                case "Venus":
                    d.physical.massKilograms = 4.8675e24;
                    Haze(d, C(0.91f, 0.84f, 0.66f), C(1f, 0.95f, 0.80f), 70000f, 110000f, 0.24f);
                    a.interpretation = "Opaque sulfuric-acid cloud top in visible light. Solid ground is hidden; UV band contrast and atmospheric superrotation are not simulated.";
                    break;
                case "Earth":
                    d.physical.massKilograms = 5.9722e24;
                    a.surfaceKind = PlanetSurfaceKind.Terrestrial;
                    d.features = new PlanetFeatureOptions(); d.physical.atmosphereHeightMetres = 80000f;
                    d.generation.seed = 271828; d.generation.visualReliefScale = 1f;
                    // Calibrated from the same terrain sampler: roughly 20 km trough-to-peak.
                    d.generation.oceanDepthMetres = 10235.143f;
                    d.generation.plateauHeightMetres = 2274.4762f;
                    d.generation.mountainHeightMetres = 8813.595f;
                    d.generation.detailHeightMetres = 312.74048f;
                    a.interpretation = "Existing procedural Terra with ocean, ecology, ice, clouds and atmosphere; not Earth's observed continental map.";
                    break;
                case "Moon":
                    d.physical.massKilograms = 7.342e22;
                    a.baseColor = C(0.29f, 0.28f, 0.27f); a.secondaryColor = C(0.62f, 0.60f, 0.56f);
                    a.craterStrength = 1f; a.craterFrequency = 22f; a.craterDepthMetres = 2300f;
                    a.regionFrequency = 2.4f; a.reliefMetres = 1900f;
                    a.interpretation = "Dark basaltic maria and cratered highlands. Statistical crater field, not an observed LRO map.";
                    break;
                case "Mars":
                    d.physical.massKilograms = 6.4171e23;
                    a.surfaceKind = PlanetSurfaceKind.DustyRock;
                    a.baseColor = C(0.40f, 0.27f, 0.19f); a.secondaryColor = C(0.62f, 0.42f, 0.27f);
                    a.accentColor = C(0.91f, 0.89f, 0.83f); a.polarCapStart = 0.965f;
                    a.reliefMetres = 7000f; a.craterDepthMetres = 1600f; a.craterStrength = 0.55f;
                    Atmosphere(d, 80000f, C(0.72f, 0.43f, 0.25f), 0.055f);
                    a.interpretation = "Iron-oxide dust, dark basalt and CO2/water frost caps. No mapped Olympus Mons/Valles Marineris, seasonal frost cycle or dust storms.";
                    break;
                case "Jupiter":
                    d.physical.massKilograms = 1.8982e27;
                    Gas(d, C(0.64f, 0.43f, 0.29f), C(0.94f, 0.88f, 0.75f), 25f, 0.90f, 0.75f);
                    Feature(a, 30f, -22f, 13f, 7f, 0.9f, C(0.76f, 0.34f, 0.19f));
                    a.ringColor = C(0.45f, 0.38f, 0.30f); a.ringOpacity = 0.055f;
                    a.interpretation = "Ammonia-cloud belts/zones and illustrative Great Red Spot at 22 degrees south. Storm longitude, shape and wind evolution are not ephemerides. Faint main dust ring.";
                    break;
                case "Io":
                    d.physical.massKilograms = 8.9319e22;
                    a.surfaceKind = PlanetSurfaceKind.SulfurVolcanic;
                    a.baseColor = C(0.83f, 0.71f, 0.40f); a.secondaryColor = C(0.91f, 0.87f, 0.68f);
                    a.accentColor = C(0.63f, 0.24f, 0.12f); a.craterFrequency = 14f;
                    a.craterStrength = 0f; a.reliefMetres = 1800f;
                    a.interpretation = "Sulfur/SO2 deposits and dark volcanic calderas. No impact-dominated crust, global opaque atmosphere, visible lava emission or erupting plumes.";
                    break;
                case "Europa":
                    d.physical.massKilograms = 4.7998e22;
                    Ice(d, C(0.71f, 0.66f, 0.53f), C(0.91f, 0.88f, 0.77f), C(0.43f, 0.24f, 0.13f), 0.85f);
                    a.craterStrength = 0.08f; a.reliefMetres = 250f; a.craterDepthMetres = 300f;
                    a.interpretation = "Young water-ice crust and reddish fracture network. No surface ocean: the inferred ocean is beneath the ice. Cracks are statistical, not mapped lineae.";
                    break;
                case "Ganymede":
                    d.physical.massKilograms = 1.4819e23;
                    Ice(d, C(0.32f, 0.29f, 0.24f), C(0.69f, 0.64f, 0.53f), C(0.38f, 0.31f, 0.24f), 0.48f);
                    a.craterStrength = 0.65f; a.regionFrequency = 4.3f; a.reliefMetres = 1400f;
                    a.interpretation = "Old dark terrains, brighter grooved ice and craters. No subsurface ocean rendering or mapped groove belts.";
                    break;
                case "Callisto":
                    d.physical.massKilograms = 1.0759e23;
                    a.baseColor = C(0.24f, 0.23f, 0.21f); a.secondaryColor = C(0.34f, 0.32f, 0.28f);
                    a.craterStrength = 1f; a.craterFrequency = 27f; a.craterDepthMetres = 1600f;
                    a.interpretation = "Dark heavily cratered ice/rock crust with bright rims. Multi-ring impact basins and named crater geography are not reconstructed.";
                    break;
                case "Saturn":
                    d.physical.massKilograms = 5.6834e26;
                    Gas(d, C(0.76f, 0.65f, 0.43f), C(0.97f, 0.90f, 0.69f), 22f, 0.46f, 0.30f);
                    a.ringColor = C(0.87f, 0.80f, 0.66f); a.ringOpacity = 0.90f;
                    a.interpretation = "Pale ammonia-cloud bands. Ice-particle rings with differentiated D/C/B/A/F optical density and Cassini/Encke/Keeler gaps. No polar hexagon or particulate multiple scattering.";
                    break;
                case "Titan":
                    d.physical.massKilograms = 1.3452e23;
                    Haze(d, C(0.67f, 0.43f, 0.17f), C(0.86f, 0.64f, 0.30f), 80000f, 200000f, 0.12f);
                    a.interpretation = "Opaque orange organic aerosol haze in visible light. Methane lakes and water-ice ground remain hidden; IR windows and the full extended atmosphere are not modeled.";
                    break;
                case "Uranus":
                    d.physical.massKilograms = 8.6810e25;
                    Gas(d, C(0.56f, 0.77f, 0.79f), C(0.73f, 0.87f, 0.86f), 14f, 0.12f, 0.12f);
                    a.surfaceKind = PlanetSurfaceKind.IceGiant;
                    a.accentColor = C(0.94f, 0.98f, 0.95f); a.atmosphereColor = C(0.53f, 0.78f, 0.81f);
                    a.ringColor = C(0.29f, 0.28f, 0.25f); a.ringOpacity = 0.38f;
                    a.interpretation = "Pale cyan methane-absorbing atmosphere and subtle cloud bands; ice giant has no exposed solid ice surface. Representative narrow dark rings, not all known dusty components.";
                    break;
                case "Neptune":
                    d.physical.massKilograms = 1.02413e26;
                    Gas(d, C(0.39f, 0.64f, 0.73f), C(0.57f, 0.77f, 0.82f), 18f, 0.32f, 0.65f);
                    a.surfaceKind = PlanetSurfaceKind.IceGiant; a.accentColor = C(0.96f, 0.98f, 1f);
                    Feature(a, 30f, -20f, 12f, 7f, 0.60f, C(0.23f, 0.42f, 0.52f));
                    a.atmosphereColor = C(0.35f, 0.63f, 0.80f); a.ringColor = C(0.32f, 0.31f, 0.28f); a.ringOpacity = 0.20f;
                    a.interpretation = "Moderate natural-light cyan-blue, cloud streaks and illustrative transient dark storm. Not Voyager's enhanced cobalt color or a present-day storm map. Ring arcs are not resolved.";
                    break;
                case "Triton":
                    d.physical.massKilograms = 2.1390e22;
                    Ice(d, C(0.63f, 0.51f, 0.45f), C(0.84f, 0.80f, 0.72f), C(0.93f, 0.89f, 0.82f), 0.26f);
                    a.polarCapStart = 0.68f; a.reliefMetres = 600f; a.craterStrength = 0.18f;
                    Atmosphere(d, 50000f, C(0.47f, 0.64f, 0.83f), 0.025f);
                    a.interpretation = "Nitrogen frost and mottled/cantaloupe-like ice terrain; no seasonal volatile transport or nitrogen geysers.";
                    break;
                case "Pluto":
                    d.physical.massKilograms = 1.303e22;
                    Ice(d, C(0.49f, 0.28f, 0.20f), C(0.80f, 0.66f, 0.51f), C(0.87f, 0.79f, 0.64f), 0.10f);
                    a.craterStrength = 0.22f; a.reliefMetres = 1800f;
                    Feature(a, 30f, 12f, 27f, 24f, 0.95f, C(0.93f, 0.86f, 0.72f));
                    Atmosphere(d, 100000f, C(0.29f, 0.53f, 0.85f), 0.035f);
                    a.interpretation = "Tholin-red terrain and illustrative bright nitrogen-ice basin inspired by Sputnik Planitia. No exact heart-shaped geography, polygonal convection or multilayer haze.";
                    break;
                case "Charon":
                    d.physical.massKilograms = 1.586e21;
                    Ice(d, C(0.40f, 0.40f, 0.38f), C(0.68f, 0.66f, 0.61f), C(0.34f, 0.20f, 0.15f), 0.46f);
                    a.craterStrength = 0.68f; a.reliefMetres = 1500f;
                    Feature(a, 0f, 90f, 37f, 37f, 0.9f, C(0.40f, 0.20f, 0.13f));
                    a.interpretation = "Water-ice/rock crust, fractures and red northern polar deposit inspired by Mordor Macula. No symmetric red south cap or exact New Horizons map.";
                    break;
                case "Haumea":
                    d.physical.massKilograms = 4.006e21;
                    Ice(d, C(0.77f, 0.78f, 0.77f), C(0.93f, 0.94f, 0.92f), C(0.56f, 0.52f, 0.45f), 0.08f);
                    a.craterStrength = 0.10f; a.reliefMetres = 400f;
                    Feature(a, 30f, 5f, 24f, 19f, 0.38f, C(0.58f, 0.39f, 0.31f));
                    a.ringColor = C(0.52f, 0.49f, 0.44f); a.ringOpacity = 0.30f;
                    a.interpretation = "Bright water-ice triaxial body, illustrative red spot and 2287 km ring. Detailed geography is unresolved; the spot's position is illustrative.";
                    break;
                case "Makemake":
                    d.physical.massKilograms = 3.1e21;
                    Ice(d, C(0.68f, 0.50f, 0.37f), C(0.78f, 0.65f, 0.52f), C(0.89f, 0.80f, 0.68f), 0f);
                    a.craterStrength = 0.07f; a.reliefMetres = 350f;
                    a.interpretation = "Reddish methane/ethane frost inferred from spectra. Unresolved surface: patches and craters are illustrative; mass is a nominal uncertain estimate, not a precise measurement.";
                    break;
                case "Ceres":
                    d.physical.massKilograms = 9.3835e20;
                    a.baseColor = C(0.24f, 0.24f, 0.23f); a.secondaryColor = C(0.32f, 0.31f, 0.28f);
                    a.craterStrength = 0.88f; a.craterDepthMetres = 2200f; a.reliefMetres = 2300f;
                    Feature(a, 30f, 20f, 4f, 3f, 0.94f, C(0.88f, 0.87f, 0.82f));
                    a.interpretation = "Dark hydrated minerals, craters and illustrative carbonate/salt bright deposit inspired by Occator. No exact Dawn terrain or cryovolcanic activity.";
                    break;
                default: throw new System.ArgumentException("No appearance preset for " + body.Name);
            }
            d.ValidateParameters();
            return d;
        }

        private static Color C(float r, float g, float b) { return new Color(r, g, b, 1f); }
        private static void Atmosphere(PlanetDefinition d, float height, Color color, float density)
        {
            d.features.hasAtmosphere = true; d.physical.atmosphereHeightMetres = height;
            d.appearance.atmosphereColor = color; d.appearance.atmosphereDensity = density;
        }
        private static void Haze(PlanetDefinition d, Color primary, Color secondary, float opticalHeight, float height, float contrast)
        {
            d.appearance.surfaceKind = PlanetSurfaceKind.OpaqueHaze;
            d.appearance.baseColor = primary; d.appearance.secondaryColor = secondary;
            d.appearance.bandContrast = contrast; d.appearance.opticalSurfaceHeightMetres = opticalHeight;
            Atmosphere(d, height, primary, 0.25f);
        }
        private static void Gas(PlanetDefinition d, Color primary, Color secondary, float bands, float contrast, float turbulence)
        {
            d.appearance.surfaceKind = PlanetSurfaceKind.GasGiant;
            d.appearance.baseColor = primary; d.appearance.secondaryColor = secondary;
            d.appearance.bandCount = bands; d.appearance.bandContrast = contrast; d.appearance.turbulence = turbulence;
            d.appearance.craterStrength = 0f; d.appearance.landSmoothness = 0f;
            Atmosphere(d, 500000f, secondary, 0.07f);
        }
        private static void Ice(PlanetDefinition d, Color primary, Color secondary, Color fracture, float strength)
        {
            d.appearance.surfaceKind = PlanetSurfaceKind.FracturedIce;
            d.appearance.baseColor = primary; d.appearance.secondaryColor = secondary;
            d.appearance.accentColor = fracture; d.appearance.fractureStrength = strength;
        }
        private static void Feature(PlanetAppearanceParameters a, float lon, float lat, float width, float height, float strength, Color color)
        {
            a.featureLongitudeLatitude = new Vector2(lon, lat); a.featureAngularSize = new Vector2(width, height);
            a.featureStrength = strength; a.featureColor = color;
        }
    }
}
