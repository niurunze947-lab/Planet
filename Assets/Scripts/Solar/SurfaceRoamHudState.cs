namespace Planet.Solar
{
    /// <summary>
    /// Published by the interaction layer's surface free-roam controller so the HUD (which lives
    /// in this assembly and must not reference Planet.Interaction) can show the surface figure.
    ///
    /// <para>Write-only from the interaction side; read-only for display. Values are only
    /// meaningful while <see cref="Active"/> is true.</para>
    /// </summary>
    public static class SurfaceRoamHudState
    {
        /// <summary>True while surface free-roam owns the camera.</summary>
        public static bool Active;

        /// <summary>Player-to-surface distance in metres.</summary>
        public static double PlayerToSurfaceDistanceMetres;

        public static void Publish(bool active, double playerToSurfaceDistanceMetres)
        {
            Active = active;
            PlayerToSurfaceDistanceMetres = playerToSurfaceDistanceMetres;
        }

        public static void Clear()
        {
            Active = false;
            PlayerToSurfaceDistanceMetres = 0.0;
        }
    }
}
