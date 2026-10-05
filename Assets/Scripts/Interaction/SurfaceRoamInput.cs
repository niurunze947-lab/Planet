using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Planet.Interaction
{
    /// <summary>
    /// Pure input mapping for surface free-roam (Unity-editor style).
    ///
    /// <para>Unity-editor style: hold the right mouse button and move the mouse to rotate the
    /// view; keep the right button held and use Q/W/E/A/S/D to fly. A plain left drag stays
    /// free for other UI. Movement is gated on the same button as look so the free-look
    /// direction and the fly direction can never disagree.</para>
    ///
    /// <para>Everything here is a pure function of the input snapshot, so the tests can pin the
    /// mapping without a PlayerLoop.</para>
    /// </summary>
    public static class SurfaceRoamInput
    {
        /// <summary>Look sensitivity in degrees per pixel, matching the orbit drag roughly.</summary>
        public const float DefaultLookSensitivity = 0.12f;

        /// <summary>Fly speed in metres per second at speed level 1.</summary>
        public const float DefaultMoveSpeedMetres = 8f;

        /// <summary>Speed is scaled by 2^level so a few notches cover cm/s to flight.</summary>
        public const int MinSpeedLevel = -3;
        public const int MaxSpeedLevel = 4;

        /// <summary>
        /// Free-look rotation delta in degrees (x=yaw, y=pitch). Only while the right button is held.
        /// </summary>
        public static Vector2 LookDeltaDegrees(
            Vector2 mouseDeltaPixels,
            bool rightMouseButtonHeld,
            float sensitivity = DefaultLookSensitivity)
        {
            if (!rightMouseButtonHeld) return Vector2.zero;
            return new Vector2(mouseDeltaPixels.x * sensitivity, -mouseDeltaPixels.y * sensitivity);
        }

        /// <summary>
        /// Fly direction in the look-local frame (x=right, y=up, z=forward), unnormalised
        /// so callers can apply speed. Q/E are down/up; WASD are the look-horizontal plane.
        ///
        /// <para><b>Unity-editor gesture: the right mouse button must be held</b> for both look
        /// and QWEASD flight, so a bare key press never drifts the player and the movement
        /// direction is always the current free-look direction.</para>
        /// </summary>
        public static double3 FlyLocalAxes(
            bool w, bool a, bool s, bool d, bool q, bool e,
            bool rightMouseButtonHeld = true)
        {
            if (!rightMouseButtonHeld) return double3.zero;
            double3 move = double3.zero;
            if (w) move.z += 1.0;
            if (s) move.z -= 1.0;
            if (d) move.x += 1.0;
            if (a) move.x -= 1.0;
            if (e) move.y += 1.0;
            if (q) move.y -= 1.0;
            return move;
        }

        /// <summary>
        /// Applies look to yaw/pitch. Pitch is clamped to the usual free-look band.
        /// </summary>
        public static void ApplyLook(ref float yaw, ref float pitch, Vector2 deltaDegrees, float pitchLimit = 89.9f)
        {
            yaw += deltaDegrees.x;
            pitch = Mathf.Clamp(pitch - deltaDegrees.y, -pitchLimit, pitchLimit);
            // Keep yaw in a sane range so long sessions do not lose float precision.
            if (yaw > 180f) yaw -= 360f;
            if (yaw < -180f) yaw += 360f;
        }

        /// <summary>
        /// Integrates one frame of free-fly into the player's local metres.
        /// Movement is applied in the tangent frame (east/up/north), never toward the planet
        /// centre, so the ground under the player stays the XZ plane after flatten.
        /// </summary>
        public static double3 IntegrateMove(
            double3 playerLocalMetres,
            double3 flyLocalAxes,
            float yawDegrees,
            float speedMetresPerSecond,
            float deltaTime)
        {
            if (deltaTime <= 0f) return playerLocalMetres;

            // WASD is camera-relative in the horizontal (east/north) plane; Q/E are world up.
            // Rotate the local xz by yaw so W is "where the camera looks", not always north.
            double yawRad = yawDegrees * (System.Math.PI / 180.0);
            double cos = System.Math.Cos(yawRad);
            double sin = System.Math.Sin(yawRad);

            double east = flyLocalAxes.x * cos + flyLocalAxes.z * sin;
            double north = -flyLocalAxes.x * sin + flyLocalAxes.z * cos;
            double up = flyLocalAxes.y;

            double3 step = new double3(east, up, north);
            double len = math.length(step);
            if (len > 1e-12) step = step / len;

            return playerLocalMetres + step * (double)speedMetresPerSecond * (double)deltaTime;
        }

        /// <summary>Speed in m/s for a discrete wheel notch / key step.</summary>
        public static float SpeedForLevel(int speedLevel, float baseSpeed = DefaultMoveSpeedMetres)
        {
            int level = Mathf.Clamp(speedLevel, MinSpeedLevel, MaxSpeedLevel);
            return baseSpeed * Mathf.Pow(2f, level);
        }

        /// <summary>
        /// Wheel changes fly speed, not view height — the opposite of the orbit camera, and
        /// the reason the orbit zoom must be hard-disabled while this mode is on.
        /// </summary>
        public static int AdjustSpeedLevel(int speedLevel, float scrollNotches)
        {
            int step = scrollNotches >= 0f ? Mathf.CeilToInt(scrollNotches - 0.01f) : Mathf.FloorToInt(scrollNotches + 0.01f);
            return Mathf.Clamp(speedLevel + step, MinSpeedLevel, MaxSpeedLevel);
        }
    }
}
