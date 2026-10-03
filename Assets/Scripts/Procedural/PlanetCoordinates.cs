using System;
using Unity.Mathematics;
using UnityEngine;

namespace Planet.Procedural
{
    [Serializable]
    public struct PlanetLocation
    {
        public double latitudeDegrees;
        public double longitudeDegrees;
        public double altitudeMetres;

        public PlanetLocation(double latitude, double longitude, double altitude = 0.0)
        {
            latitudeDegrees = latitude;
            longitudeDegrees = longitude;
            altitudeMetres = altitude;
        }
    }

    /// <summary>A local tangent frame. Local x = east, y = up, z = north; distances are metres.</summary>
    public struct PlanetLocalFrame
    {
        public double3 origin;
        public double3 east;
        public double3 up;
        public double3 north;

        public double3 LocalToPlanet(double3 eastUpNorth)
        {
            return origin + east * eastUpNorth.x + up * eastUpNorth.y + north * eastUpNorth.z;
        }

        public double3 PlanetToLocal(double3 planetPosition)
        {
            double3 offset = planetPosition - origin;
            return new double3(math.dot(offset, east), math.dot(offset, up), math.dot(offset, north));
        }

        public double3 LocalDirectionToPlanet(double3 direction)
        {
            return east * direction.x + up * direction.y + north * direction.z;
        }
    }

    /// <summary>
    /// Body-fixed coordinates: +Y north, longitude 0 at +X, longitude +90 at +Z.
    /// Latitude is geocentric; altitude is radial from the reference spheroid, not geodetic altitude.
    /// </summary>
    public static class PlanetCoordinates
    {
        private const double Deg2Rad = Math.PI / 180.0;
        private const double Rad2Deg = 180.0 / Math.PI;

        public static double3 LatLonToDirection(double latitudeDegrees, double longitudeDegrees)
        {
            if (double.IsNaN(latitudeDegrees) || double.IsInfinity(latitudeDegrees) ||
                double.IsNaN(longitudeDegrees) || double.IsInfinity(longitudeDegrees))
                throw new ArgumentOutOfRangeException("Coordinates must be finite.");
            double lat = math.clamp(latitudeDegrees, -90.0, 90.0) * Deg2Rad;
            double lon = WrapLongitude(longitudeDegrees) * Deg2Rad;
            double c = Math.Cos(lat);
            return new double3(c * Math.Cos(lon), Math.Sin(lat), c * Math.Sin(lon));
        }

        public static PlanetLocation DirectionToLatLon(double3 direction)
        {
            direction = NormalizeDirection(direction);
            return new PlanetLocation(Math.Asin(math.clamp(direction.y, -1.0, 1.0)) * Rad2Deg,
                WrapLongitude(Math.Atan2(direction.z, direction.x) * Rad2Deg));
        }

        public static double ReferenceRadius(double3 direction, double radiusMetres, double flattening = 0.0)
        {
            direction = NormalizeDirection(direction);
            double polarRatio = 1.0 - math.clamp(flattening, 0.0, 0.2);
            return radiusMetres / Math.Sqrt(direction.x * direction.x + direction.z * direction.z +
                direction.y * direction.y / (polarRatio * polarRatio));
        }

        public static double3 ToPlanetPosition(PlanetLocation location, PlanetPhysicalParameters physical)
        {
            double3 direction = LatLonToDirection(location.latitudeDegrees, location.longitudeDegrees);
            return direction * (ReferenceRadius(direction, physical.radiusMetres, physical.flattening) +
                location.altitudeMetres);
        }

        public static PlanetLocation FromPlanetPosition(double3 position, PlanetPhysicalParameters physical)
        {
            double3 direction = NormalizeDirection(position);
            PlanetLocation location = DirectionToLatLon(direction);
            location.altitudeMetres = math.length(position) -
                ReferenceRadius(direction, physical.radiusMetres, physical.flattening);
            return location;
        }

        public static PlanetLocalFrame CreateLocalFrame(PlanetLocation anchor, PlanetPhysicalParameters physical)
        {
            double3 radial = LatLonToDirection(anchor.latitudeDegrees, anchor.longitudeDegrees);
            double lon = WrapLongitude(anchor.longitudeDegrees) * Deg2Rad;
            double polarRatio = 1.0 - physical.flattening;
            double3 up = math.normalize(new double3(radial.x, radial.y / (polarRatio * polarRatio), radial.z));
            double3 east = new double3(-Math.Sin(lon), 0.0, Math.Cos(lon));
            return new PlanetLocalFrame
            {
                origin = ToPlanetPosition(anchor, physical),
                east = east,
                up = up,
                north = math.normalize(math.cross(east, up))
            };
        }

        /// <summary>Subtract in double precision BEFORE converting to Unity's float rendering coordinates.</summary>
        public static Vector3 ToRenderPosition(double3 planetPosition, double3 renderOrigin, double metresPerUnit)
        {
            if (!(metresPerUnit > 0.0)) throw new ArgumentOutOfRangeException(nameof(metresPerUnit));
            double3 local = (planetPosition - renderOrigin) / metresPerUnit;
            return new Vector3((float)local.x, (float)local.y, (float)local.z);
        }

        public static double WrapLongitude(double degrees)
        {
            return degrees - Math.Floor((degrees + 180.0) / 360.0) * 360.0;
        }

        private static double3 NormalizeDirection(double3 direction)
        {
            double lengthSquared = math.lengthsq(direction);
            if (!(lengthSquared > 1e-30) || !math.all(math.isfinite(direction)))
                throw new ArgumentException("A planet direction/position must be finite and nonzero.", nameof(direction));
            return direction / Math.Sqrt(lengthSquared);
        }
    }
}
