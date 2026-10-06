using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Planet.Solar
{
    /// <summary>Metre projection for nearby terrain, composited over the astronomical camera.</summary>
    [DisallowMultipleComponent]
    public sealed class ObserverRenderRig : MonoBehaviour
    {
        public const int TerrainLayer = 31;
        public const float NearMetres = 0.05f;
        public const float FarMetres = 5000f;
        public Camera LocalCamera { get; private set; }
        private Camera background;
        private int originalMask;

        private void OnEnable()
        {
            background = GetComponent<Camera>();
            if (background == null) return;
            originalMask = background.cullingMask;
            background.cullingMask &= ~(1 << TerrainLayer);
            var go = new GameObject("Metre terrain camera") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            LocalCamera = go.AddComponent<Camera>();
            LocalCamera.CopyFrom(background);
            LocalCamera.cullingMask = 1 << TerrainLayer;
            LocalCamera.nearClipPlane = NearMetres;
            LocalCamera.farClipPlane = FarMetres;
            LocalCamera.clearFlags = CameraClearFlags.Nothing;
            var localData = LocalCamera.GetUniversalAdditionalCameraData();
            localData.renderType = CameraRenderType.Overlay;
            localData.renderPostProcessing = false;
            background.GetUniversalAdditionalCameraData().cameraStack.Add(LocalCamera);
        }

        public void Sync()
        {
            if (LocalCamera == null || background == null) return;
            LocalCamera.fieldOfView = background.fieldOfView;
            LocalCamera.aspect = background.aspect;
            LocalCamera.transform.SetPositionAndRotation(Vector3.zero, background.transform.rotation);
        }

        private void OnDisable()
        {
            if (background != null)
            {
                background.cullingMask = originalMask;
                background.GetUniversalAdditionalCameraData().cameraStack.Remove(LocalCamera);
            }
            if (LocalCamera != null)
            {
                if (Application.isPlaying) Destroy(LocalCamera.gameObject);
                else DestroyImmediate(LocalCamera.gameObject);
            }
            LocalCamera = null;
        }
    }
}
