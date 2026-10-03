using UnityEngine;

namespace Planet.Solar
{
    /// <summary>
    /// Named camera poses, used by the documentation captures and by the editor tooling. Keeping
    /// them in code (rather than as a pile of hand-placed scene cameras) means the tour stays in
    /// sync with the catalog: every shot is derived from a body's real radius and orbit.
    /// </summary>
    public static class SolarSystemPoses
    {
        public struct Pose
        {
            public string Name;
            public string Body;
            /// <summary>Camera distance as a multiple of the body's radius.</summary>
            public float FrameMultiplier;
            public float Pitch;
            public float Yaw;
            /// <summary>Julian date to freeze at; 0 means "leave the clock alone".</summary>
            public double JulianDate;

            public Pose(string name, string body, float frameMultiplier, float pitch, float yaw, double jd)
            {
                Name = name; Body = body; FrameMultiplier = frameMultiplier;
                Pitch = pitch; Yaw = yaw; JulianDate = jd;
            }
        }

        private const double J2000 = 2451545.0;

        public static readonly Pose[] All =
        {
            //                                                     frameMult  pitch  yaw    date
            new Pose("01-earth",         "Earth",      4.5f,      15f,   35f,  J2000),
            new Pose("02-earth-moon",    "Earth",      90f,       20f,   35f,  J2000),
            new Pose("03-sun",           "Sun",        4.5f,      12f,   30f,  J2000),
            new Pose("04-jupiter",       "Jupiter",    40f,       18f,   30f,  J2000),
            new Pose("05-saturn",        "Saturn",     12f,       22f,   30f,  J2000),
            new Pose("06-uranus",        "Uranus",     5f,        10f,   30f,  J2000),
            new Pose("07-pluto-charon",  "Pluto",      14f,       16f,   30f,  J2000),
            new Pose("08-inner-system",  "Sun",        420f,      22f,   30f,  J2000),
            new Pose("09-full-system",   "Sun",        12000f,    26f,   30f,  J2000),
            new Pose("10-system-top",    "Sun",        12000f,    78f,    0f,  J2000),
            new Pose("11-inner-halved",  "Sun",        420f,      22f,   30f,  J2000 + 182.628),
        };

        public static Pose Find(string name)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Name == name) return All[i];
            return All[0];
        }

        /// <summary>
        /// Puts the simulation and the camera into the pose immediately and freezes input, so a
        /// capture can be taken without racing the keyboard handler.
        /// </summary>
        public static string Apply(string poseName)
        {
            Pose p = Find(poseName);
            var sim = SolarSystemSimulation.Instance;
            if (sim == null) return "no simulation";

            var rig = sim.viewCamera != null ? sim.viewCamera.GetComponent<SolarSystemCamera>() : null;
            if (rig == null) return "no camera rig";

            sim.handleInput = false;
            rig.handleInput = false;
            sim.paused = true;

            if (p.JulianDate != 0.0) sim.julianDate = p.JulianDate;

            rig.pitch = p.Pitch;
            rig.yaw = p.Yaw;
            rig.frameMultiplier = Mathf.Max(p.FrameMultiplier, 1.2f);

            sim.SetFocusByName(p.Body, true);
            sim.SnapToEpoch();            // rebuild positions and snap the floating origin right now
            rig.SnapToFocus();            // frame the new body with no easing
            sim.Tick();                   // push transforms, light and line widths
            rig.ApplyTransform();

            return string.Format("{0}: body={1} distance={2:G6} units ({3}) pitch={4} yaw={5}",
                p.Name, sim.focusBody, rig.distance,
                SolarSystemSimulation.FormatKm(rig.distance * sim.KmPerUnit), p.Pitch, p.Yaw);
        }
    }
}
