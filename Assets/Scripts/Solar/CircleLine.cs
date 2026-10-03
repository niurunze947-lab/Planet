using UnityEngine;

namespace Planet.Solar
{
    /// <summary>A faint reference circle in the ecliptic plane, used to mark the Kuiper belt edges.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class CircleLine : MonoBehaviour
    {
        public float radiusAu = 30f;
        public int segments = 512;
        public Color color = new Color(0.30f, 0.38f, 0.55f, 0.30f);

        [Tooltip("Kilometres per scene unit. Must match SolarSystemSimulation.kmPerUnit.")]
        public float kmPerUnit = 1000000f;

        [System.NonSerialized] public float widthMultiplier = 1f;

        private LineRenderer _line;

        private void OnEnable() { Rebuild(); }

        private void Update()
        {
            // Nothing to poll: the simulation pushes the width through SetWidth, which writes
            // straight to the LineRenderer. See the note on OrbitPath.SetWidth.
        }

        /// <summary>Line width, as a fraction of the camera's distance.</summary>
        public void SetWidth(float w)
        {
            widthMultiplier = w;
            if (_line == null) Rebuild();
            if (_line != null) _line.widthMultiplier = w;
        }

        public void Rebuild()
        {
            if (_line == null)
            {
                _line = GetComponent<LineRenderer>();
                if (_line == null) _line = gameObject.AddComponent<LineRenderer>();
            }

            double unitsPerKm = kmPerUnit > 0f ? 1.0 / kmPerUnit : 1e-6;
            float r = (float)(radiusAu * OrbitalMechanics.AuKm * unitsPerKm);

            _line.useWorldSpace = false;
            _line.loop = true;
            _line.alignment = LineAlignment.View;
            _line.textureMode = LineTextureMode.Stretch;
            _line.numCornerVertices = 0;
            _line.numCapVertices = 0;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _line.sharedMaterial = SolarMaterials.OrbitLine;
            _line.startColor = color;
            _line.endColor = color;
            _line.widthMultiplier = widthMultiplier;

            int n = Mathf.Clamp(segments, 32, 4096);
            _line.positionCount = n;
            for (int k = 0; k < n; k++)
            {
                float a = (float)k / n * Mathf.PI * 2f;
                _line.SetPosition(k, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
        }
    }
}
