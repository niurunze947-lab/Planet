using System;
using Unity.Mathematics;

namespace Planet.Procedural.Terrain
{
    /// <summary>
    /// C1 — per-frame view state pushed by the visual coordinator.
    ///
    /// Positions and rotations are body-fixed (BF) doubles in metres so chunk placement stays
    /// exact near the surface. The terrain renderer converts to camera-relative floats on the CPU
    /// each frame before writing chunk transforms.
    /// </summary>
    [Serializable]
    public struct PlanetViewState
    {
        /// <summary>Camera position in body-fixed metres.</summary>
        public double3 positionBfMetres;

        /// <summary>Camera rotation in the body-fixed frame.</summary>
        public quaternion rotationBf;

        /// <summary>Vertical field of view in degrees.</summary>
        public float verticalFovDegrees;

        /// <summary>Camera aspect (width / height).</summary>
        public float aspect;

        /// <summary>Pixel height of the target viewport (drives LOD selection).</summary>
        public int pixelHeight;

        /// <summary>Body-fixed to world rotation (includes spin). Optional for pure BF work.</summary>
        public quaternion bodyRotationBfToW;

        /// <summary>True when a local (metre-scale) surface camera is active.</summary>
        public bool localCameraActive;
        /// <summary>Flight velocity for terrain requests independent of the view cone.</summary>
        public double3 velocityBfMetresPerSecond;
    }
}
