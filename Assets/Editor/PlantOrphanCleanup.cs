using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Remove transient plant primitives left alive by the old DontSave lifecycle.
[InitializeOnLoad]
internal static class PlantOrphanCleanup
{
    static PlantOrphanCleanup()
    {
        EditorApplication.delayCall += Clean;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) Clean();
        };
    }

    private static void Clean()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Clean;
            return;
        }
        int removed = 0;
        foreach (Transform item in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (item == null || EditorUtility.IsPersistent(item) ||
                !item.name.StartsWith("Animal Placement ", StringComparison.Ordinal) ||
                (item.gameObject.hideFlags & HideFlags.DontSaveInEditor) == 0) continue;
            UnityEngine.Object.DestroyImmediate(item.gameObject);
            removed++;
        }
        foreach (var renderer in Resources.FindObjectsOfTypeAll<MeshRenderer>())
        {
            if (renderer == null || EditorUtility.IsPersistent(renderer)) continue;
            GameObject go = renderer.gameObject;
            var filter = go.GetComponent<MeshFilter>();
            bool plant = go.name.StartsWith("Plant Placeholder", StringComparison.Ordinal);
            if (!plant) continue;
            Debug.Log("[PlantOrphanCleanup] Removing " + go.name + " sceneValid=" +
                go.scene.IsValid() + " flags=" + go.hideFlags + " mesh=" +
                (filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : "none"));
            UnityEngine.Object.DestroyImmediate(go);
            removed++;
        }
        Debug.Log("[PlantOrphanCleanup] Removed " + removed + " plant primitives. Remaining=" +
            Resources.FindObjectsOfTypeAll<MeshRenderer>().Count(r => r != null &&
                !EditorUtility.IsPersistent(r) && r.name.StartsWith("Plant Placeholder", StringComparison.Ordinal)));
        SceneView.RepaintAll();
        EditorApplication.QueuePlayerLoopUpdate();
    }
}
