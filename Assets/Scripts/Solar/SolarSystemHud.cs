using UnityEngine;

namespace Planet.Solar
{
    /// <summary>
    /// On-screen readout. At true scale almost every body is an invisible dot, so the numbers have
    /// to carry the demonstration: real radius, real distance from the Sun, real orbital period,
    /// real day length. Everything shown is read straight out of the catalog.
    /// </summary>
    [DisallowMultipleComponent]
    public class SolarSystemHud : MonoBehaviour
    {
        public bool visible = true;
        public int fontSize = 13;

        private SolarSystemSimulation _sim;
        private GUIStyle _panel;
        private GUIStyle _title;
        private GUIStyle _body;
        private GUIStyle _hint;
        private Texture2D _bg;

        private void Update()
        {
            if (_sim == null) _sim = SolarSystemSimulation.Instance;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.hKey.wasPressedThisFrame) visible = !visible;
        }

        private void EnsureStyles()
        {
            if (_panel != null && _bg != null) return;

            _bg = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _bg.SetPixel(0, 0, new Color(0.02f, 0.03f, 0.05f, 0.72f));
            _bg.Apply();
            _bg.hideFlags = HideFlags.DontSave;

            _panel = new GUIStyle(GUI.skin.box);
            _panel.normal.background = _bg;
            _panel.padding = new RectOffset(12, 12, 10, 10);
            _panel.alignment = TextAnchor.UpperLeft;

            _title = new GUIStyle(GUI.skin.label);
            _title.fontSize = fontSize + 3;
            _title.fontStyle = FontStyle.Bold;
            _title.normal.textColor = new Color(0.95f, 0.96f, 1f);

            _body = new GUIStyle(GUI.skin.label);
            _body.fontSize = fontSize;
            _body.normal.textColor = new Color(0.80f, 0.84f, 0.92f);
            _body.richText = false;

            _hint = new GUIStyle(_body);
            _hint.fontSize = fontSize - 1;
            _hint.normal.textColor = new Color(0.58f, 0.64f, 0.76f);
        }

        private void OnGUI()
        {
            if (!visible) return;
            if (_sim == null) _sim = SolarSystemSimulation.Instance;
            if (_sim == null || _sim.BodyCount == 0) return;

            EnsureStyles();

            Body def = _sim.FocusDefinition;
            if (def == null) return;

            GUILayout.BeginArea(new Rect(12, 12, 372, 500), _panel);
            GUILayout.Label(DisplayName(def), _title);
            GUILayout.Space(2);
            GUILayout.Label("模拟日期  " + _sim.DateString(), _body);
            GUILayout.Label(string.Format("时间流速  {0:0.###} 天/秒{1}",
                _sim.daysPerSecond, _sim.paused ? "   [已暂停]" : ""), _body);
            GUILayout.Space(6);

            GUILayout.Label(string.Format("半径          {0:N1} km   ({1})", def.RadiusKm, RadiusNote(def)), _body);
            GUILayout.Label(string.Format("自转周期      {0}", RotationNote(def)), _body);
            GUILayout.Label(string.Format("转轴倾角      {0}", AxialTiltNote(def)), _body);
            if (def.Kind == BodyKind.Star)
            {
                GUILayout.Label("质量          1.989e30 kg  (占太阳系 99.86%)", _body);
                GUILayout.Label("表面温度      5,772 K", _body);
            }
            else
            {
                double a = def.Parent == null || def.Parent.Length == 0
                    ? def.Orbit.a
                    : def.MoonOrbit.aKm / OrbitalMechanics.AuKm;
                double period = def.Parent == null || def.Parent.Length == 0
                    ? def.Orbit.PeriodDays
                    : def.MoonOrbit.PeriodDays;

                GUILayout.Label(string.Format("轨道半长轴    {0}", DistanceNote(def, a)), _body);
                GUILayout.Label(string.Format("偏心率        {0:0.0000}", Eccentricity(def)), _body);
                GUILayout.Label(string.Format("轨道倾角      {0:0.00}°", Inclination(def)), _body);
                GUILayout.Label(string.Format("公转周期      {0}", PeriodNote(period)), _body);
            }

            GUILayout.Space(6);
            bool surfaceRoam = SurfaceRoamHudState.Active;
            double surfaceAltitudeMetres = SurfaceRoamHudState.PlayerToSurfaceDistanceMetres;
            if (surfaceRoam)
            {
                GUILayout.Label("按住右键拖动 环视（左右扭头）  右键 + Q/W/E/A/S/D 漫游", _hint);
                GUILayout.Label("滚轮 调速   Esc 返回轨道视角   H 隐藏本面板", _hint);
                GUILayout.Space(4);
                GUILayout.Label(string.Format("到地表距离  {0}", FormatMetresAlways(surfaceAltitudeMetres)), _body);
                GUILayout.Label(string.Format("漫游速度    {0}", FormatSpeed(SurfaceRoamHudState.FlySpeedMetresPerSecond)), _body);
                GUILayout.Label("玩家坐标基准：地表局部系（body-fixed，不受自转影响）", _hint);
            }
            else
            {
                GUILayout.Label("1-9,0 切换天体   Tab 逐个   F 重新取景", _hint);
                GUILayout.Label("滚轮 / PgUp,PgDn 缩放   左键拖动 环绕", _hint);
                GUILayout.Label("空格 暂停   -/= 时间流速   \\ 复位   N 今天   R J2000", _hint);
                GUILayout.Label("G 天体放大 400×（仅为可见，非真实比例）  L 经纬网格  H 隐藏本面板", _hint);

                GUILayout.Space(6);
                GUILayout.Label(string.Format("比例尺  1 场景单位 = {0:N0} km", _sim.KmPerUnit), _hint);
                GUILayout.Label(string.Format("当前视距  {0}", FormatLength(CameraDistance())), _hint);
            }

            GUILayout.EndArea();

            // Kuiper belt marker on the right edge, so "we went all the way out" is unambiguous.
            var cam = Camera.main;
            GUILayout.BeginArea(new Rect(Screen.width - 250, 12, 238, 132), _panel);
            GUILayout.Label("柯伊伯带  Kuiper Belt", _title);
            GUILayout.Label("30 – 50 AU", _body);
            GUILayout.Label("4.49e9 – 7.48e9 km", _hint);
            GUILayout.Label("4,488 – 7,480 场景单位", _hint);
            // Surface roam replaces the orbit view-height figure with the player-to-surface
            // distance in metres (C6). Orbit mode keeps the original field-height readout.
            GUILayout.Label(surfaceRoam
                ? string.Format("到地表距离 {0}", FormatMetresAlways(surfaceAltitudeMetres))
                : (cam != null
                    ? string.Format("当前视场高 {0}", FormatLength(2f * CameraDistance() * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)))
                    : "-"), _hint);
            GUILayout.EndArea();
        }

        private static float CameraDistance()
        {
            var cam = Camera.main;
            return cam != null ? cam.transform.position.magnitude : 0f;
        }

        /// <summary>
        /// Surface-mode GUI figure is always metres, per the C6 display contract — the orbit
        /// view-height length units are not carried over into the free-roam readout.
        /// </summary>
        private static string FormatMetresAlways(double metres)
        {
            if (double.IsNaN(metres) || double.IsInfinity(metres)) return "-";
            return string.Format("{0:N2} m", metres);
        }

        private static string FormatSpeed(double metresPerSecond)
        {
            if (double.IsNaN(metresPerSecond) || double.IsInfinity(metresPerSecond)) return "-";
            if (metresPerSecond >= 1000.0) return string.Format("{0:N2} km/s", metresPerSecond / 1000.0);
            return string.Format("{0:N1} m/s", metresPerSecond);
        }

        private static string DisplayName(Body b)
        {
            switch (b.Name)
            {
                case "Sun": return "太阳  Sun";
                case "Mercury": return "水星  Mercury";
                case "Venus": return "金星  Venus";
                case "Earth": return "地球  Earth";
                case "Moon": return "月球  Moon";
                case "Mars": return "火星  Mars";
                case "Jupiter": return "木星  Jupiter";
                case "Saturn": return "土星  Saturn";
                case "Uranus": return "天王星  Uranus";
                case "Neptune": return "海王星  Neptune";
                case "Pluto": return "冥王星  Pluto";
                case "Ceres": return "谷神星  Ceres";
                case "Haumea": return "妊神星  Haumea";
                case "Makemake": return "鸟神星  Makemake";
                case "Io": return "木卫一  Io";
                case "Europa": return "木卫二  Europa";
                case "Ganymede": return "木卫三  Ganymede";
                case "Callisto": return "木卫四  Callisto";
                case "Titan": return "土卫六  Titan";
                case "Triton": return "海卫一  Triton";
                case "Charon": return "冥卫一  Charon";
            }
            return b.Name;
        }

        private static string RadiusNote(Body b)
        {
            if (b.Name == "Earth") return "赤道 6,378 km";
            if (b.Name == "Sun") return "IAU 标称 695,700 km";
            if (b.Flattening > 0.01) return string.Format("扁率 {0:0.0}%", b.Flattening * 100.0);
            return "近球体";
        }

        private static string RotationNote(Body b)
        {
            double h = b.RotationHours;
            double abs = System.Math.Abs(h);
            string s = abs >= 48.0 ? string.Format("{0:0.000} 天", abs / 24.0) : string.Format("{0:0.000} 小时", abs);
            return h < 0.0 ? s + "（逆行）" : s;
        }

        private static string AxialTiltNote(Body b)
        {
            if (b.Parent != null && b.Parent.Length > 0)
                return "随母星（潮汐锁定）";
            return string.Format("{0:0.00}°", AxialTilt(b));
        }

        /// <summary>
        /// Real obliquity: the angle between the body's spin axis and the normal of its *own*
        /// orbital plane, not the ecliptic. Measuring against the ecliptic would report 26.71 deg
        /// for Mars (whose orbit is tilted 1.85 deg) instead of the textbook 25.19 deg.
        ///
        /// The retrograde correction is the subtle part, and it is not "flip every retrograde
        /// rotator". For Venus and Uranus the published IAU pole sits on the north side of the
        /// ecliptic even though the body spins backwards, so the pole is the *negative* of the spin
        /// vector and has to be flipped (2.64 -> 177.36, 82.24 -> 97.76). Pluto's published pole
        /// already points south, so it is the spin vector as-is and flipping it would report an
        /// absurd 60 deg instead of the textbook 122.5 deg. Hence the second condition.
        /// </summary>
        private static double AxialTilt(Body b)
        {
            Vec3d pole = OrbitalMechanics.PoleFromRaDec(b.PoleRaDeg, b.PoleDecDeg);
            Vec3d normal = b.IsSun
                ? new Vec3d(0, 0, 1)                        // the Sun's reference is the ecliptic itself
                : OrbitalMechanics.OrbitNormal(b.Orbit.i, b.Orbit.node);

            double pl = System.Math.Max(pole.Length, 1e-12);
            double nl = System.Math.Max(normal.Length, 1e-12);
            double dot = (pole.x * normal.x + pole.y * normal.y + pole.z * normal.z) / (pl * nl);
            double deg = System.Math.Acos(System.Math.Max(-1.0, System.Math.Min(1.0, dot))) * OrbitalMechanics.Rad2Deg;

            if (b.RotationHours < 0.0 && pole.z > 0.0) deg = 180.0 - deg;
            return deg;
        }

        private static double Eccentricity(Body b)
        {
            return (b.Parent == null || b.Parent.Length == 0) ? b.Orbit.e : b.MoonOrbit.e;
        }

        private static double Inclination(Body b)
        {
            return (b.Parent == null || b.Parent.Length == 0) ? b.Orbit.i : b.MoonOrbit.i;
        }

        private static string DistanceNote(Body b, double au)
        {
            if (b.Parent != null && b.Parent.Length > 0)
                return string.Format("{0:N0} km  (绕 {1})", b.MoonOrbit.aKm, b.Parent);
            return string.Format("{0:0.0000} AU  =  {1:N0} km", au, au * OrbitalMechanics.AuKm);
        }

        private static string PeriodNote(double days)
        {
            if (days <= 0.0) return "-";
            double years = days / 365.2568983;
            if (years >= 1.0) return string.Format("{0:N2} 年  ({1:N1} 天)", years, days);
            return string.Format("{0:0.000} 天", days);
        }

        private static string FormatLength(float units)
        {
            double km = units * 1e6;
            if (km >= 1.496e8) return string.Format("{0:N2} AU", km / OrbitalMechanics.AuKm);
            if (km >= 1e6) return string.Format("{0:N2} million km", km / 1e6);
            if (km >= 1e3) return string.Format("{0:N0} km", km);
            return string.Format("{0:N1} km", km);
        }
    }
}
