using Planet.Procedural;
using UnityEditor;
using UnityEngine;

namespace Planet.EditorTools
{
    [CustomEditor(typeof(PlanetDefinition))]
    public sealed class PlanetDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Runtime terrain chunks regenerate when the definition changes. Visual Relief Scale: 1 = physical relief; 20–50 = globe inspection. Physical height queries stay unchanged.", MessageType.Info);
            if (AssetDatabase.GetAssetPath(target) == PlanetFrameworkBuilder.DefinitionPath)
                using (new EditorGUI.DisabledScope(Application.isPlaying || EarthTextureLodBuilder.IsBuilding))
                    if (GUILayout.Button("Refresh Earth Cloud and Overview Textures")) EarthTextureLodBuilder.Build();
        }
    }
}
