using System;
using Planet.Procedural;
using Planet.Solar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Planet.EditorTools
{
    public static class StellarFrameworkBuilder
    {
        public const string Folder = "Assets/Resources/Solar/BodyMaterials/Stellar";
        public const string DefinitionPath = Folder + "/SunStellar.asset";
        public const string LutPath = Folder + "/Blackbody_Planck_CIE.asset";

        [MenuItem("Tools/Planet/Build Stellar Framework", false, 24)]
        public static void BuildAndSave()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Build the stellar framework outside Play mode.");
            ConfigureSun();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
        }

        public static void ConfigureSun()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(SolarSystemMaterialBuilder.Folder, "Stellar");
            CelestialBody sun = null;
            foreach (CelestialBody body in UnityEngine.Object.FindObjectsOfType<CelestialBody>()) if (body.bodyName == "Sun") sun = body;
            if (sun == null) throw new InvalidOperationException("SolarSystem Sun is missing.");
            PlanetDefinition previous = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(SolarSystemMaterialBuilder.DefinitionPath("Sun"));
            StellarDefinition definition = AssetDatabase.LoadAssetAtPath<StellarDefinition>(DefinitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<StellarDefinition>(); definition.displayName = "Sun Photosphere";
                if (previous != null) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(previous.physical), definition.physical);
                AssetDatabase.CreateAsset(definition, DefinitionPath);
            }
            Body catalog = SolarSystemCatalog.Find("Sun");
            definition.physical.radiusMetres = catalog.RadiusKm * 1000;
            definition.physical.northPoleRightAscensionDegrees = catalog.PoleRaDeg;
            definition.physical.northPoleDeclinationDegrees = catalog.PoleDecDeg;
            definition.ValidateParameters(); EditorUtility.SetDirty(definition);

            Texture2D lut = AssetDatabase.LoadAssetAtPath<Texture2D>(LutPath);
            string lutKey = "StellarSpectrum:" + StellarRadiation.SpectrumVersion;
            AssetImporter lutImporter = AssetImporter.GetAtPath(LutPath);
            if (lut == null || lutImporter == null || lutImporter.userData != lutKey)
            {
                Texture2D next = new Texture2D(512,1,TextureFormat.RGBAHalf,false,true)
                    {name = "Planck_CIE_2500_9000K", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear};
                Color[] pixels = new Color[512];
                for (int i=0;i<pixels.Length;i++) pixels[i]=StellarRadiation.LinearRgb(Mathf.Lerp(2500,9000,(float)i/(pixels.Length-1)));
                next.SetPixels(pixels); next.Apply(false,true);
                if (lut == null) { AssetDatabase.CreateAsset(next,LutPath); lut = next; }
                else { EditorUtility.CopySerialized(next,lut); UnityEngine.Object.DestroyImmediate(next); EditorUtility.SetDirty(lut); }
                AssetDatabase.SaveAssets(); lutImporter = AssetImporter.GetAtPath(LutPath); lutImporter.userData = lutKey;
                AssetDatabase.WriteImportSettingsIfDirty(LutPath); AssetDatabase.ImportAsset(LutPath,ImportAssetOptions.ForceUpdate);
            }
            Material detailed = EnsureMaterial(Folder+"/SunPhotosphere.mat","Planet/StellarPhotosphere");
            detailed.SetTexture("_BlackbodyLut",lut);
            detailed.SetFloat("_Temperature",definition.effectiveTemperatureKelvin);
            detailed.SetFloat("_TemperatureContrast",definition.granuleTemperatureContrastKelvin);
            detailed.SetFloat("_LimbCooling",definition.limbCoolingKelvin);
            detailed.SetVector("_LimbCoefficients",new Vector4(definition.limbLinear,definition.limbQuadratic,0,0));
            detailed.SetFloat("_DisplayScale",definition.displayRadianceScale);
            detailed.SetFloat("_GranuleFrequency",(float)(definition.physical.radiusMetres/1000/definition.granuleDiameterKilometres));
            detailed.SetFloat("_SuperFrequency",(float)(definition.physical.radiusMetres/1000/definition.supergranuleDiameterKilometres));
            detailed.SetFloat("_NormalRelief",(float)(definition.normalReliefMetres/definition.physical.radiusMetres));
            detailed.SetFloat("_Seed",definition.seed); detailed.SetFloat("_SpotCount",definition.spotCount);
            detailed.SetFloat("_SpotLifetime",definition.spotLifetimeDays);
            detailed.SetFloat("_SpotRadius",definition.spotAngularRadiusDegrees*Mathf.Deg2Rad);
            detailed.SetVector("_SpotTemperatures",new Vector4(definition.umbraTemperatureKelvin,definition.penumbraTemperatureKelvin,0,0));
            detailed.SetFloat("_FocusExposureEV",definition.focusedExposureEV);
            detailed.SetColor("_FocusSpectralResponse",definition.focusedSpectralResponse);
            detailed.SetFloat("_FocusSpectralMix",definition.focusedSpectralMix);
            detailed.SetFloat("_FocusedGranulationBoost",definition.focusedGranulationBoost);
            detailed.SetFloat("_FocusLimbGlow",definition.focusedLimbGlow);
            EditorUtility.SetDirty(detailed);
            Material distant = EnsureMaterial(Folder+"/SunDistant.mat","Planet/StellarDistant");
            Color color = StellarRadiation.LinearRgb(definition.effectiveTemperatureKelvin)*definition.displayRadianceScale;
            distant.SetColor("_Radiance",color); EditorUtility.SetDirty(distant);

            // Keep the legacy assets/GUIDs for rollback, but remove the planet renderer from Sun.
            PlanetSurfaceView oldView = sun.GetComponent<PlanetSurfaceView>();
            if (oldView != null) UnityEngine.Object.DestroyImmediate(oldView);
            StellarSurfaceView view = sun.GetComponent<StellarSurfaceView>();
            if (view == null) view = sun.gameObject.AddComponent<StellarSurfaceView>();
            view.definition = definition; view.blackbodyLut = lut; view.photosphereMaterial = detailed; view.distantMaterial = distant;
            sun.BuildVisuals(); EditorUtility.SetDirty(view); EditorUtility.SetDirty(sun);
            SolarSystemVisualCoordinator coordinator = SolarSystemVisualCoordinator.EnsureInScene(); coordinator.star = view; coordinator.Refresh();
            EditorUtility.SetDirty(coordinator); AssetDatabase.SaveAssets();
        }
        private static Material EnsureMaterial(string path,string shaderName)
        {
            Shader shader=Shader.Find(shaderName); if(shader==null) throw new InvalidOperationException("Missing shader: "+shaderName);
            Material material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null) {material=new Material(shader);AssetDatabase.CreateAsset(material,path);} else material.shader=shader;
            return material;
        }
    }
}
