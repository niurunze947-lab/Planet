using System.Collections.Generic;
using Planet.Procedural;
using Planet.Procedural.Terrain;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Planet.Solar
{
    /// <summary>
    /// Debug highlight for the bodies whose derived surface is still loading.
    ///
    /// "Still loading" is not one flag in this project: a body's visible surface is a derived cache
    /// built by several independent producers, so this component polls every producer that already
    /// publishes its own state and reports the first one that has not finished. It never starts,
    /// stops or steers a load - it is a read-only consumer, like the HUD.
    ///
    /// | reason | published by | judged loading when |
    /// |---|---|---|
    /// | `photosphere mesh` | <see cref="StellarSurfaceView.Mesh"/> | the star has no photosphere mesh |
    /// | `globe mesh` | <see cref="PlanetSurfaceView.Mesh"/> | the far globe has not been built |
    /// | `texture tier` | <see cref="PlanetTextureLodController"/> | a tier is in flight, or the requested tier has not been applied |
    /// | `terrain xN` | <see cref="RuntimeTerrainRenderer.PendingWork"/> | chunk work is queued or running |
    /// | `terrain coverage` | <see cref="RuntimeTerrainRenderer.CoverageReady"/> | chunks exist but the six root faces are not covered |
    ///
    /// It walks every <see cref="CelestialBody"/> in the scene, so the star and the moons are
    /// covered by the same code path as the planets - there is no per-body list to keep in sync.
    ///
    /// The highlight is drawn in the Scene view as gizmos (with a label naming the reason) and in
    /// the Game view as GL rings, so a Play-mode screenshot shows it too.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BodyLoadingHighlight : MonoBehaviour
    {
        [Header("What is drawn")]
        [Tooltip("Master switch. Reading the body state has no side effect on the loaders.")]
        public bool show = true;
        [Tooltip("Also outline bodies that have finished loading, so the whole body set stays visible.")]
        public bool showLoaded = true;
        [Tooltip("Draw the highlight in the Game view as well as the Scene view.")]
        public bool showInGameView = true;
        [Tooltip("List the still-loading bodies in the top-left corner of the view.")]
        public bool showReadout = true;

        [Header("Look")]
        public Color loadingColor = new Color(1f, 0.7f, 0.12f, 0.95f);
        public Color loadedColor = new Color(0.35f, 1f, 0.55f, 0.16f);
        [Tooltip("Pulse rate of a loading highlight, in cycles per second.")]
        [Range(0f, 6f)] public float pulseHz = 1.5f;
        [Tooltip("A highlight never draws smaller than this fraction of the body's distance from the " +
                 "eye, so a moon stays visible at true scale where its real radius is sub-pixel.")]
        [Range(0.002f, 0.5f)] public float minimumAngularRadius = 0.035f;
        [Range(16, 256)] public int ringSegments = 96;

        [Header("Polling")]
        [Tooltip("How often the published load state is re-read, in seconds.")]
        [Range(0.02f, 2f)] public float refreshInterval = 0.2f;

        /// <summary>One body and the load state last read from its producers.</summary>
        public struct BodyLoadState
        {
            public CelestialBody body;
            public bool loading;
            public string reason;
        }

        private readonly List<BodyLoadState> states = new List<BodyLoadState>();
        private float nextRefresh;
        private int loadingCount;
        private Material lineMaterial;
        private static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
        private static readonly int ZTest = Shader.PropertyToID("_ZTest");
        private static readonly int Cull = Shader.PropertyToID("_Cull");

        /// <summary>Bodies whose derived surface is still loading, as of the last poll.</summary>
        public int LoadingCount { get { EnsureStates(false); return loadingCount; } }

        /// <summary>Bodies the last poll classified, loading or not.</summary>
        public int TrackedCount { get { EnsureStates(false); return states.Count; } }

        // ==================================================================================
        // State
        // ==================================================================================

        /// <summary>
        /// Classifies one body from the state its producers publish. Query-only: the loaders are
        /// never told to do anything, so this stays usable in edit mode and in a build.
        /// </summary>
        /// <param name="reason">Short human-readable cause; null when the body is ready.</param>
        /// <returns>True while the body's visible surface is not the one that has been requested.</returns>
        public static bool Evaluate(CelestialBody body, out string reason)
        {
            reason = null;
            if (body == null) return false;

            // The star is a separate framework: an emissive photosphere mesh and nothing else, so it
            // has no texture tiers and no runtime terrain to wait for.
            StellarSurfaceView star = body.GetComponent<StellarSurfaceView>();
            if (star != null && star.definition != null)
            {
                if (star.Mesh == null) { reason = "photosphere mesh"; return true; }
                return false;
            }

            PlanetSurfaceView view = body.GetComponent<PlanetSurfaceView>();
            if (view != null && view.Mesh == null) { reason = "globe mesh"; return true; }

            PlanetTextureLodController textures = body.GetComponent<PlanetTextureLodController>();
            if (textures != null && textures.lodSet != null && textures.lodSet.tiers != null &&
                textures.lodSet.tiers.Length > 0)
            {
                // ActiveTier -1 means "no tier applied yet". In edit mode the LOD contract is only
                // driven while playing, so an unapplied tier there is by design and not a load.
                bool tierPending = textures.RequestedTier >= 0 && textures.ActiveTier != textures.RequestedTier;
                if (textures.IsLoading || (tierPending && Application.isPlaying))
                {
                    reason = "texture tier " + textures.ActiveTier + "->" + textures.RequestedTier;
                    return true;
                }
            }

            RuntimeTerrainRenderer terrain = body.GetComponent<RuntimeTerrainRenderer>();
            if (terrain != null && terrain.enabled)
            {
                if (terrain.PendingWork > 0) { reason = "terrain x" + terrain.PendingWork; return true; }
                if (terrain.ResidentCount > 0 && !terrain.CoverageReady) { reason = "terrain coverage"; return true; }
            }
            return false;
        }

        private void EnsureStates(bool force)
        {
            float now = Time.realtimeSinceStartup;
            if (!force && now < nextRefresh) return;
            nextRefresh = now + Mathf.Max(refreshInterval, 0.02f);

            states.Clear();
            loadingCount = 0;
            CelestialBody[] bodies = FindObjectsOfType<CelestialBody>();
            for (int i = 0; i < bodies.Length; i++)
            {
                CelestialBody body = bodies[i];
                if (body == null || !body.isActiveAndEnabled) continue;
                string reason;
                bool loading = Evaluate(body, out reason);
                if (loading) loadingCount++;
                states.Add(new BodyLoadState { body = body, loading = loading, reason = reason });
            }
        }

        // ==================================================================================
        // Drawing
        // ==================================================================================

        private void OnDrawGizmos()
        {
            if (!show) return;
            EnsureStates(false);
            Camera eye = Camera.current != null ? Camera.current : Camera.main;
            Vector3 eyePosition = eye != null ? eye.transform.position : Vector3.zero;

            for (int i = 0; i < states.Count; i++)
            {
                BodyLoadState state = states[i];
                if (state.body == null || (!state.loading && !showLoaded)) continue;
                Vector3 center = state.body.transform.position;
                float radius = DrawRadius(state.body, center, eyePosition);
                Gizmos.color = state.loading ? Pulsed(loadingColor) : loadedColor;
                Gizmos.DrawWireSphere(center, radius);
                if (!state.loading) continue;
#if UNITY_EDITOR
                Handles.Label(center + Vector3.up * radius,
                    state.body.name + " loading: " + state.reason);
#endif
            }
        }

        private void OnRenderObject()
        {
            if (!show || !showInGameView) return;
            Camera camera = Camera.current;
            if (camera == null || camera.cameraType == CameraType.Preview) return;
            if (!EnsureLineMaterial()) return;
            EnsureStates(false);

            Vector3 eyePosition = camera.transform.position;
            Vector3 axisA = camera.transform.right;
            Vector3 axisB = camera.transform.up;

            lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            GL.Begin(GL.LINES);
            for (int i = 0; i < states.Count; i++)
            {
                BodyLoadState state = states[i];
                if (state.body == null || (!state.loading && !showLoaded)) continue;
                Vector3 center = state.body.transform.position;
                float radius = DrawRadius(state.body, center, eyePosition);
                // A body the eye sits next to covers the whole frame; keep the Game-view ring on
                // screen instead of drawing a circle that is larger than the frustum.
                radius = Mathf.Min(radius, Vector3.Distance(eyePosition, center) * 0.45f);
                GL.Color(state.loading ? Pulsed(loadingColor) : loadedColor);
                DrawRing(center, axisA, axisB, radius);
            }
            GL.End();
            GL.PopMatrix();
        }

        /// <summary>
        /// Largest of the body's own radius and the minimum angular size, so a highlight is always
        /// visible: at true scale the moons are far below one scene unit across.
        /// </summary>
        private float DrawRadius(CelestialBody body, Vector3 center, Vector3 eyePosition)
        {
            float radius = Mathf.Max(body.RadiusUnits, 0f);
            float floor = Vector3.Distance(eyePosition, center) * minimumAngularRadius;
            return Mathf.Max(radius * 1.02f, floor);
        }

        private void DrawRing(Vector3 center, Vector3 axisA, Vector3 axisB, float radius)
        {
            int segments = Mathf.Max(16, ringSegments);
            Vector3 previous = center + axisA * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 next = center + (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * radius;
                GL.Vertex(previous);
                GL.Vertex(next);
                previous = next;
            }
        }

        private Color Pulsed(Color color)
        {
            if (pulseHz <= 0f) return color;
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * pulseHz * Mathf.PI * 2f);
            color.a *= Mathf.Lerp(0.45f, 1f, wave);
            return color;
        }

        private bool EnsureLineMaterial()
        {
            if (lineMaterial != null) return true;
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return false;
            lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            lineMaterial.SetInt(SrcBlend, (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lineMaterial.SetInt(DstBlend, (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lineMaterial.SetInt(ZWrite, 0);
            lineMaterial.SetInt(ZTest, (int)UnityEngine.Rendering.CompareFunction.Always);
            lineMaterial.SetInt(Cull, (int)UnityEngine.Rendering.CullMode.Off);
            return true;
        }

        private void OnGUI()
        {
            if (!show || !showReadout) return;
            EnsureStates(false);
            // Anchored to the bottom-left so it never covers the solar-system HUD panel.
            int lines = Mathf.Max(loadingCount, 1);
            var rect = new Rect(12, Screen.height - 40 - lines * 18, 340, 34 + lines * 18);
            GUI.Box(rect, "Body Loading Highlight");
            GUI.Label(new Rect(20, rect.y + 20, 324, 18), loadingCount == 0
                ? "all " + states.Count + " bodies ready"
                : loadingCount + " of " + states.Count + " bodies still loading");
            int row = 0;
            for (int i = 0; i < states.Count; i++)
            {
                if (!states[i].loading || states[i].body == null) continue;
                GUI.Label(new Rect(28, rect.y + 40 + row * 18, 316, 18),
                    states[i].body.name + "  -  " + states[i].reason);
                row++;
            }
        }

        private void OnDestroy()
        {
            if (lineMaterial != null) DestroyImmediate(lineMaterial);
            lineMaterial = null;
            states.Clear();
        }
    }
}
