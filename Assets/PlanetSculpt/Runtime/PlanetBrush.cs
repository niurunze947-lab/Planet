using UnityEngine;
using Input = Yongxian.PlanetSculpt.BrushInput;
using UnityEngine.EventSystems;

namespace Yongxian.PlanetSculpt
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlanetTerrain))]
    public sealed class PlanetBrush : MonoBehaviour
    {
        public Camera viewCamera;
        public const float MinBrushAngle = 1f, MaxBrushAngle = 6f;
        public const float MinStrength = 0.2f, MaxStrength = 1f;
        [Range(MinBrushAngle, MaxBrushAngle)] public float brushAngle = 2.5f;
        [Range(MinStrength, MaxStrength)] public float strength = 0.6f;
        public bool orbitCamera = true;
        public bool frameOnStart = true;
        public bool showHelp = true;
        public bool LowerMode { get; private set; }
        public const int StrokeLimit = 15;
        [SerializeField, HideInInspector] int strokesUsed;
        public int StrokesUsed => strokesUsed;
        public int RemainingStrokes => Mathf.Max(0, StrokeLimit - strokesUsed);

        PlanetTerrain terrain;
        LineRenderer outline;
        Material outlineMaterial;
        bool strokeActive;
        bool hasLastDirection;
        Vector3 lastDirection;
        bool waitForMouseRelease;
        bool hoveringPlanet;
        float hoveredHeightMeters;
        float nextDabTime;
        const float DabInterval = 1f / 20f;
        public static Rect PanelRect
        {
            get
            {
                float width = Mathf.Clamp(Screen.width - 408f, 140f, 340f);
                return new Rect(Screen.width - width - 12f, 196f, width, Mathf.Max(100f, Screen.height - 208f));
            }
        }
        Vector2 panelScroll;
        Rect helpRect => PanelRect;

        void OnValidate()
        {
            brushAngle = Mathf.Clamp(brushAngle, MinBrushAngle, MaxBrushAngle);
            strength = Mathf.Clamp(strength, MinStrength, MaxStrength);
        }

        void Start()
        {
            terrain = GetComponent<PlanetTerrain>();
            OnValidate();
            if (terrain.SurfaceMesh == null) { enabled = false; return; }
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera == null)
            {
                Debug.LogError("Assign a camera to PlanetBrush.", this);
                enabled = false;
                return;
            }
            if (frameOnStart)
            {
                viewCamera.transform.position = transform.position + new Vector3(0f, 0.15f, -1f).normalized * WorldRadius * 2.8f;
                viewCamera.transform.LookAt(transform.position);
                viewCamera.nearClipPlane = Mathf.Max(0.001f, WorldRadius * 0.01f);
            }
            var child = new GameObject("Brush footprint");
            child.transform.SetParent(transform, false);
            outline = child.AddComponent<LineRenderer>();
            outline.useWorldSpace = true;
            outline.loop = true;
            outline.positionCount = 64;
            outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outline.receiveShadows = false;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            outlineMaterial = new Material(shader);
            outline.sharedMaterial = outlineMaterial;
            outline.enabled = false;
        }

        float WorldRadius
        {
            get
            {
                Vector3 scale = transform.lossyScale;
                return terrain.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            }
        }

        void Update()
        {
            if (viewCamera == null) return;
            bool overUI = (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) ||
                          (showHelp && helpRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)));
            overUI |= EarthSculptController.ContainsToolbar(Input.mousePosition);
            Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            overUI |= new Rect(12, 12, 372, 500).Contains(guiMouse) ||
                      new Rect(Screen.width - 250, 12, 238, 132).Contains(guiMouse);
            if (!Input.GetMouseButton(0)) { EndStroke(); waitForMouseRelease = false; }
            if (Input.GetKeyDown(KeyCode.Z) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                UndoLastStroke();
                return;
            }
            if (Input.GetKeyDown(KeyCode.E)) SetLowerMode(!LowerMode);
            if (Input.GetKey(KeyCode.LeftBracket)) brushAngle = Mathf.Max(MinBrushAngle, brushAngle - 2f * Time.deltaTime);
            if (Input.GetKey(KeyCode.RightBracket)) brushAngle = Mathf.Min(MaxBrushAngle, brushAngle + 2f * Time.deltaTime);
            if (!overUI && orbitCamera) UpdateCamera();
            bool smooth = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool lower = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            var ray = viewCamera.ScreenPointToRay(Input.mousePosition);
            // The closest physics hit must be this planet, so UI/other objects cannot paint through it.
            bool hitPlanet = terrain.SurfaceCollider.Raycast(ray, out RaycastHit hit, viewCamera.farClipPlane);
            hoveringPlanet = hitPlanet && !overUI;
            if (hoveringPlanet) hoveredHeightMeters = terrain.GetHeightMeters(hit);
            if (overUI || !hitPlanet || Input.GetMouseButton(1))
            {
                outline.enabled = false;
                hasLastDirection = false;
                return;
            }
            Vector3 direction = transform.InverseTransformPoint(hit.point).normalized;
            if (Input.GetMouseButton(0) && !waitForMouseRelease && (!strokeActive || Time.time >= nextDabTime))
            {
                float elapsed = strokeActive ? Mathf.Clamp(Time.time - (nextDabTime - DabInterval), 0f, 0.1f) : DabInterval;
                PaintAt(direction, elapsed, lower, smooth);
                nextDabTime = Time.time + DabInterval;
            }
            DrawOutline(direction, smooth ? Color.cyan : LowerMode ? new Color(1f, 0.45f, 0.75f) : lower ? new Color(1f, 0.4f, 0.2f) : Color.yellow);
        }

        // Shared by mouse input and editor regression previews: even one click gets a
        // visible initial stamp. Holding grows it; dragging lays down a continuous range.
        public void PaintAt(Vector3 direction, float elapsed, bool lower = false, bool smooth = false)
        {
            if (terrain == null) terrain = GetComponent<PlanetTerrain>();
            if (terrain == null || terrain.SurfaceMesh == null || direction.sqrMagnitude < 0.001f) return;
            direction.Normalize();
            bool startingStroke = !strokeActive;
            if (startingStroke && RemainingStrokes == 0) return;
            if (startingStroke) terrain.BeginStroke();
            float brushRadians = Mathf.Clamp(brushAngle, MinBrushAngle, MaxBrushAngle) * Mathf.Deg2Rad;
            float stampMeters = 300f * Mathf.Clamp(strength, MinStrength, MaxStrength);
            elapsed = Mathf.Clamp(elapsed, 0f, 0.1f);
            float amount = smooth ? 1f - Mathf.Exp(-12f * elapsed) : terrain.MetersToLocalHeight(stampMeters) * (lower || LowerMode ? -1f : 1f);
            bool changed;
            if (!hasLastDirection)
                changed = terrain.ApplyBrush(direction, brushRadians, amount, smooth);
            else
            {
                float travel = Vector3.Angle(lastDirection, direction) * Mathf.Deg2Rad;
                // Distance deposits keep a fast stroke visible. Elapsed-time deposits
                // keep a stationary hold useful, without stamping full strength every frame.
                if (!smooth) amount *= travel / (brushRadians * 0.35f) + elapsed;
                changed = terrain.ApplyBrushSegment(lastDirection, direction, brushRadians, amount, smooth);
            }
            if (startingStroke)
            {
                if (!changed) { terrain.DiscardUndoStroke(); return; }
                strokesUsed++;
                strokeActive = true;
            }
            lastDirection = direction;
            hasLastDirection = true;
        }

        public void EndStroke() { strokeActive = false; hasLastDirection = false; }

        public void SetLowerMode(bool enabled)
        {
            if (LowerMode == enabled) return;
            EndStroke();
            LowerMode = enabled;
            waitForMouseRelease = true;
        }

        void UpdateCamera()
        {
            Vector3 center = transform.position;
            if (Input.GetMouseButton(1))
            {
                viewCamera.transform.RotateAround(center, viewCamera.transform.up, Input.GetAxis("Mouse X") * 4f);
                viewCamera.transform.RotateAround(center, viewCamera.transform.right, -Input.GetAxis("Mouse Y") * 4f);
                viewCamera.transform.LookAt(center, viewCamera.transform.up);
            }
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                Vector3 offset = viewCamera.transform.position - center;
                float distance = Mathf.Clamp(offset.magnitude * Mathf.Exp(-scroll * 0.12f),
                    WorldRadius * (1f + terrain.maxHeightRatio + 0.2f), WorldRadius * 12f);
                viewCamera.transform.position = center + offset.normalized * distance;
            }
        }

        void DrawOutline(Vector3 direction, Color color)
        {
            Vector3 tangent = Vector3.Cross(direction, Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 bitangent = Vector3.Cross(direction, tangent);
            float angle = brushAngle * Mathf.Deg2Rad;
            for (int i = 0; i < outline.positionCount; i++)
            {
                float phase = i * Mathf.PI * 2f / outline.positionCount;
                Vector3 radial = direction * Mathf.Cos(angle) + (tangent * Mathf.Cos(phase) + bitangent * Mathf.Sin(phase)) * Mathf.Sin(angle);
                Vector3 origin = transform.TransformPoint(radial * terrain.radius * (2f + terrain.maxHeightRatio));
                Vector3 inward = (transform.position - origin).normalized;
                var ray = new Ray(origin, inward);
                Vector3 point = transform.TransformPoint(radial * terrain.radius);
                if (terrain.SurfaceCollider.Raycast(ray, out RaycastHit hit, WorldRadius * 6f)) point = hit.point - inward * WorldRadius * 0.003f;
                outline.SetPosition(i, point);
            }
            outlineMaterial.SetColor("_BaseColor", color);
            outlineMaterial.color = color;
            outline.widthMultiplier = WorldRadius * 0.0015f;
            outline.enabled = true;
        }

        void OnGUI()
        {
            if (!showHelp || terrain == null) return;
            GUILayout.BeginArea(helpRect, GUI.skin.box);
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            GUILayout.Label("裸岩球体笔刷", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 17 });
            GUILayout.Label("剩余绘画次数：" + RemainingStrokes + " / " + StrokeLimit,
                new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 17 });
            GUILayout.Label("按下到松开算一笔；撤销不返还次数。");
            if (RemainingStrokes == 0) GUILayout.Label(strokeActive ? "最后一笔绘画中，松开后次数用尽。" : "15 次已用完，可继续查看或撤销。");
            GUILayout.Space(6);
            GUILayout.Label("左键抬高 · Shift + 左键降低");
            GUILayout.Label("Ctrl + 左键平滑 · Ctrl + Z 撤销");
            GUILayout.Label("右键环绕 · 滚轮缩放");
            if (GUILayout.Button(LowerMode ? "当前：降低 / 下挖（E 切换）" : "当前：抬高（E 切换）"))
                SetLowerMode(!LowerMode);
            GUILayout.Label("笔刷半径：" + brushAngle.ToString("F1") + "°（[ / ] 调整）");
            brushAngle = GUILayout.HorizontalSlider(brushAngle, MinBrushAngle, MaxBrushAngle);
            GUILayout.Label("绘画力度：" + (strength * 100f).ToString("F0") + "%");
            strength = GUILayout.HorizontalSlider(strength, MinStrength, MaxStrength);
            GUILayout.Label(hoveringPlanet ? "笔刷高度：" + hoveredHeightMeters.ToString("F0") + " m" : "在球体表面拖动即可绘画");
            terrain.mountainBrush = GUILayout.Toggle(terrain.mountainBrush, "自然山脊");
            GUI.enabled = terrain.UndoCount > 0;
            if (GUILayout.Button("撤销上一笔（Ctrl + Z） [" + terrain.UndoCount + "]")) UndoLastStroke();
            GUI.enabled = true;
            if (GUILayout.Button("重置球体（不恢复绘画次数）"))
            {
                terrain.ResetTerrain();
                EndStroke();
                waitForMouseRelease = true;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        public void UndoLastStroke()
        {
            if (terrain == null) terrain = GetComponent<PlanetTerrain>();
            if (terrain == null) return;
            terrain.UndoStroke();
            EndStroke();
            waitForMouseRelease = true;
            if (outline != null) outline.enabled = false;
        }

        void OnDisable() { if (outline != null) outline.enabled = false; EndStroke(); }
        void OnDestroy()
        {
            if (outline != null) Destroy(outline.gameObject);
            if (outlineMaterial != null) Destroy(outlineMaterial);
        }
    }
}
