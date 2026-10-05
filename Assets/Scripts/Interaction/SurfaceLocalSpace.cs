using System;
using Unity.Mathematics;
using UnityEngine;

namespace Planet.Interaction
{
    /// <summary>
    /// C6 — surface-local player frame (scheme A).
    ///
    /// <para>While free-roam is active the player's truth is a <b>body-fixed</b> offset from a
    /// ground anchor, expressed in metres along east / up / north. Body-fixed means the numbers
    /// do not move when the planet spins: both the player and a surface chunk share the same
    /// rotating frame, so their relative offset is spin-invariant. That is the property the
    /// coordinate tests pin.</para>
    ///
    /// <para>The flatten rotation maps the local up direction onto Unity's world +Y, so the
    /// chunk under the player has its surface normal parallel to world up (scheme A) and the
    /// ground reads as a plane under a free camera.</para>
    /// </summary>
    public struct SurfaceLocalSpace
    {
        /// <summary>Ground-anchor position in body-fixed metres (on or above the reference surface).</summary>
        public double3 anchorBodyFixedMetres;

        /// <summary>Unit east axis, body-fixed.</summary>
        public double3 east;

        /// <summary>Unit up axis (surface normal), body-fixed.</summary>
        public double3 up;

        /// <summary>Unit north axis, body-fixed.</summary>
        public double3 north;

        /// <summary>Mean / reference sphere radius in metres.</summary>
        public double radiusMetres;

        /// <summary>Player position relative to the anchor, in local metres (x=east, y=up, z=north).</summary>
        public double3 playerLocalMetres;

        /// <summary>True once the frame has been built from a real surface point.</summary>
        public bool IsValid
        {
            get
            {
                return math.all(math.isfinite(anchorBodyFixedMetres))
                    && math.all(math.isfinite(east))
                    && math.all(math.isfinite(up))
                    && math.all(math.isfinite(north))
                    && math.abs(math.lengthsq(up) - 1.0) < 1e-6
                    && radiusMetres > 0.0;
            }
        }

        /// <summary>Player position in body-fixed metres. The roaming truth.</summary>
        public double3 PlayerBodyFixedMetres
        {
            get { return LocalToBodyFixed(playerLocalMetres); }
        }

        /// <summary>Height of the player above the anchor plane, in metres (the GUI figure).</summary>
        public double PlayerToSurfaceDistanceMetres
        {
            get { return playerLocalMetres.y; }
        }

        /// <summary>Converts local east/up/north metres to body-fixed metres.</summary>
        public double3 LocalToBodyFixed(double3 localMetres)
        {
            return anchorBodyFixedMetres
                 + east * localMetres.x
                 + up * localMetres.y
                 + north * localMetres.z;
        }

        /// <summary>Converts body-fixed metres to local east/up/north metres.</summary>
        public double3 BodyFixedToLocal(double3 bodyFixedMetres)
        {
            double3 offset = bodyFixedMetres - anchorBodyFixedMetres;
            return new double3(
                math.dot(offset, east),
                math.dot(offset, up),
                math.dot(offset, north));
        }

        /// <summary>
        /// Player minus a chunk origin, both body-fixed. Spin-invariant: a planet rotation is a
        /// change of world pose, not of body-fixed numbers, so this vector never moves.
        /// </summary>
        public double3 ChunkRelativeMetres(double3 chunkOriginBodyFixedMetres)
        {
            return PlayerBodyFixedMetres - chunkOriginBodyFixedMetres;
        }

        /// <summary>
        /// Scheme A: world rotation that takes the surface normal to Unity +Y.
        /// <paramref name="bodyWorldRotation"/> is the focused body's current world rotation
        /// (the simulation's tilt × spin), so the result is in the same space as the transforms.
        /// </summary>
        public Quaternion FlattenToWorldUp(Quaternion bodyWorldRotation)
        {
            Vector3 n = bodyWorldRotation * ToVector3(up);
            if (n.sqrMagnitude < 1e-12f) return Quaternion.identity;
            return Quaternion.FromToRotation(n, Vector3.up);
        }

        /// <summary>
        /// Builds a frame whose origin sits on the reference surface along
        /// <paramref name="directionBodyFixed"/>, with up along the (flattened) surface normal.
        /// </summary>
        public static SurfaceLocalSpace CreateFromDirection(
            double3 directionBodyFixed,
            double radiusMetres,
            double flattening = 0.0,
            double3 shapeScale = default(double3))
        {
            double3 dir = NormalizeOrUp(directionBodyFixed);
            double3 up = SurfaceNormal(dir, flattening, shapeScale);
            double3 east = EastFromUp(up, dir);
            double3 north = NormalizeOrUp(math.cross(up, east));
            // Re-orthogonalise east against the final north/up so the triple is tight.
            east = NormalizeOrUp(math.cross(north, up));
            north = NormalizeOrUp(math.cross(up, east));

            double surfaceRadius = ReferenceRadius(dir, radiusMetres, flattening, shapeScale);
            return new SurfaceLocalSpace
            {
                anchorBodyFixedMetres = dir * surfaceRadius,
                east = east,
                up = up,
                north = north,
                radiusMetres = radiusMetres,
                playerLocalMetres = new double3(0.0, 0.0, 0.0)
            };
        }

        /// <summary>
        /// Builds a frame at an explicit body-fixed anchor (player or chunk), keeping the same
        /// up / east / north rules as <see cref="CreateFromDirection"/>.
        /// </summary>
        public static SurfaceLocalSpace CreateAtAnchor(
            double3 anchorBodyFixedMetres,
            double radiusMetres,
            double flattening = 0.0,
            double3 shapeScale = default(double3))
        {
            double len = math.length(anchorBodyFixedMetres);
            if (!(len > 1e-6))
                return CreateFromDirection(new double3(0.0, 1.0, 0.0), radiusMetres, flattening, shapeScale);

            double3 dir = anchorBodyFixedMetres / len;
            SurfaceLocalSpace space = CreateFromDirection(dir, radiusMetres, flattening, shapeScale);
            space.anchorBodyFixedMetres = anchorBodyFixedMetres;
            return space;
        }

        /// <summary>Surface normal at a direction: radial for a sphere, ellipsoid gradient otherwise.</summary>
        public static double3 SurfaceNormal(double3 directionBodyFixed, double flattening, double3 shapeScale)
        {
            double3 dir = NormalizeOrUp(directionBodyFixed);
            double f = math.clamp(flattening, 0.0, 0.2);
            double polar = 1.0 - f;
            double sx = math.abs(shapeScale.x) > 1e-9 ? shapeScale.x : 1.0;
            double sy = math.abs(shapeScale.y) > 1e-9 ? shapeScale.y * polar : polar;
            double sz = math.abs(shapeScale.z) > 1e-9 ? shapeScale.z : 1.0;

            // Gradient of x²/sx² + y²/sy² + z²/sz² is the ellipsoid normal.
            double3 g = new double3(dir.x / (sx * sx), dir.y / (sy * sy), dir.z / (sz * sz));
            return NormalizeOrUp(g);
        }

        /// <summary>Distance from the body centre to the reference surface along a direction, metres.</summary>
        public static double ReferenceRadius(
            double3 directionBodyFixed,
            double radiusMetres,
            double flattening,
            double3 shapeScale)
        {
            double3 dir = NormalizeOrUp(directionBodyFixed);
            double f = math.clamp(flattening, 0.0, 0.2);
            double polar = 1.0 - f;
            double sx = math.abs(shapeScale.x) > 1e-9 ? shapeScale.x : 1.0;
            double sy = math.abs(shapeScale.y) > 1e-9 ? shapeScale.y * polar : polar;
            double sz = math.abs(shapeScale.z) > 1e-9 ? shapeScale.z : 1.0;

            double s = (dir.x * dir.x) / (sx * sx)
                     + (dir.y * dir.y) / (sy * sy)
                     + (dir.z * dir.z) / (sz * sz);
            return s > 0.0 ? radiusMetres / Math.Sqrt(s) : radiusMetres;
        }

        /// <summary>Metres → scene units for the current solar-system scale.</summary>
        public static double MetresToSceneUnits(double metres, float kmPerUnit)
        {
            double perUnit = (double)kmPerUnit * 1000.0;
            return perUnit > 0.0 ? metres / perUnit : 0.0;
        }

        /// <summary>Scene units → metres for the current solar-system scale.</summary>
        public static double SceneUnitsToMetres(double units, float kmPerUnit)
        {
            return units * (double)kmPerUnit * 1000.0;
        }

        public static Vector3 ToVector3(double3 v)
        {
            return new Vector3((float)v.x, (float)v.y, (float)v.z);
        }

        public static double3 FromVector3(Vector3 v)
        {
            return new double3(v.x, v.y, v.z);
        }

        private static double3 EastFromUp(double3 up, double3 dir)
        {
            // Prefer the geographic east of the sample direction; fall back to any perpendicular.
            double3 pole = new double3(0.0, 1.0, 0.0);
            double3 east = math.cross(pole, dir);
            if (math.lengthsq(east) < 1e-12)
                east = math.cross(new double3(1.0, 0.0, 0.0), up);
            return NormalizeOrUp(east);
        }

        private static double3 NormalizeOrUp(double3 v)
        {
            double lenSq = math.lengthsq(v);
            if (!(lenSq > 1e-30) || !math.all(math.isfinite(v)))
                return new double3(0.0, 1.0, 0.0);
            return v / Math.Sqrt(lenSq);
        }
    }
}
