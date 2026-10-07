using Unity.Mathematics;
using System.Collections.Generic;

namespace Planet.Procedural.Terrain
{
    /// <summary>Immutable worker snapshot. Macro interpolation keeps float noise quantisation out of centimetre meshes.</summary>
    public readonly struct RuntimeTerrainSource
    {
        public readonly PlanetSurfaceSampler macro;
        public readonly PlanetDetailSampler detail;
        public readonly double radius;
        public readonly double3 axes;
        public readonly double reliefScale;
        public readonly double opticalHeight;
        public readonly bool solid;
        public const int MacroSegments = 16384;

        public RuntimeTerrainSource(PlanetDefinition definition)
        {
            macro = definition.CreateSampler();
            detail = PlanetDetailSampler.FromDefinition(definition);
            radius = definition.physical.radiusMetres;
            axes = new double3(definition.physical.shapeScale.x,
                definition.physical.shapeScale.y * (1.0 - definition.physical.flattening), definition.physical.shapeScale.z);
            reliefScale = definition.generation.visualReliefScale;
            opticalHeight = definition.appearance.opticalSurfaceHeightMetres;
            PlanetSurfaceKind kind = definition.appearance.surfaceKind;
            solid = kind != PlanetSurfaceKind.GasGiant && kind != PlanetSurfaceKind.IceGiant &&
                kind != PlanetSurfaceKind.OpaqueHaze && kind != PlanetSurfaceKind.StellarPhotosphere;
        }

        public double ReferenceRadius(double3 direction)
        {
            return radius / math.sqrt(math.csum(direction * direction / (axes * axes)));
        }

        public double MacroHeight(double3 direction, Dictionary<float3, float> cache = null)
        {
            PlanetCubeFace face = TerrainLattice.DominantFace(direction);
            double2 uv = math.clamp(TerrainLattice.FaceUv(face, direction), -1.0, 1.0);
            double2 grid = (uv + 1.0) * (0.5 * MacroSegments);
            int2 cell = math.clamp((int2)math.floor(grid), 0, MacroSegments - 1);
            double2 t = grid - cell;
            double h00 = MacroVertex(face, cell.x, cell.y, cache), h10 = MacroVertex(face, cell.x + 1, cell.y, cache);
            double h01 = MacroVertex(face, cell.x, cell.y + 1, cache), h11 = MacroVertex(face, cell.x + 1, cell.y + 1, cache);
            return math.lerp(math.lerp(h00, h10, t.x), math.lerp(h01, h11, t.x), t.y);
        }

        private double MacroVertex(PlanetCubeFace face, int x, int y, Dictionary<float3, float> cache)
        {
            // Exact integer addresses, including cube edges. Only these fixed macro samples narrow to float.
            const long step = TerrainLattice.LatticeSize / MacroSegments;
            float3 d = (float3)TerrainLattice.Direction(face, x * step, y * step);
            if (cache != null && cache.TryGetValue(d, out float height)) return height;
            float result = macro.EvaluateHeight(d);
            if (cache != null) cache[d] = result;
            return result;
        }

        public double Height(double3 direction, double spacing, Dictionary<float3, float> cache = null)
        {
            double h = MacroHeight(direction, cache);
            if (!solid || !detail.IsEnabled) return h;
            // Freeze coast topology across LOD. Fine relief fades smoothly to zero at the macro shoreline.
            double coast = macro.hasOcean ? math.saturate(math.abs(h - macro.seaLevelMetres) / math.max(1.0, detail.AmplitudeBound * 2)) : 1.0;
            coast = coast * coast * (3 - 2 * coast);
            return h + detail.Evaluate(direction * radius, spacing) * coast;
        }

        public void HeightBands(double3 direction, double ownSpacing, double parentSpacing,
            out double ownHeight, out double parentHeight, Dictionary<float3, float> cache = null)
        {
            double macroHeight = MacroHeight(direction, cache);
            ownHeight = macroHeight;
            parentHeight = macroHeight;
            if (!solid || !detail.IsEnabled) return;
            double coast = macro.hasOcean
                ? math.saturate(math.abs(macroHeight - macro.seaLevelMetres) / math.max(1.0, detail.AmplitudeBound * 2))
                : 1.0;
            coast = coast * coast * (3 - 2 * coast);
            detail.Evaluate2(direction * radius, ownSpacing, parentSpacing, out double own, out double parent);
            ownHeight += own * coast;
            parentHeight += parent * coast;
        }

        public float3 MacroAlbedo(double3 direction, double spacing, Dictionary<float3, float3> cache)
        {
            if (spacing >= 2 * radius / MacroSegments) return macro.EvaluateAlbedoOnly((float3)direction);
            var face = TerrainLattice.DominantFace(direction);
            double2 grid = (math.clamp(TerrainLattice.FaceUv(face, direction), -1.0, 1.0) + 1) * (0.5 * MacroSegments);
            int2 cell = math.clamp((int2)math.floor(grid), 0, MacroSegments - 1);
            float2 t = (float2)(grid - cell);
            return math.lerp(math.lerp(AlbedoVertex(face, cell.x, cell.y, cache), AlbedoVertex(face, cell.x + 1, cell.y, cache), t.x),
                math.lerp(AlbedoVertex(face, cell.x, cell.y + 1, cache), AlbedoVertex(face, cell.x + 1, cell.y + 1, cache), t.x), t.y);
        }

        private float3 AlbedoVertex(PlanetCubeFace face, int x, int y, Dictionary<float3, float3> cache)
        {
            const long step = TerrainLattice.LatticeSize / MacroSegments;
            float3 d = (float3)TerrainLattice.Direction(face, x * step, y * step);
            if (cache.TryGetValue(d, out float3 color)) return color;
            color = macro.EvaluateAlbedoOnly(d); cache[d] = color;
            return color;
        }

        public double DisplayDisplacement(double height)
        {
            return (macro.hasOcean ? macro.seaLevelMetres + (height - macro.seaLevelMetres) * reliefScale
                : height * reliefScale) + opticalHeight;
        }

        public double3 OceanPosition(double3 direction)
        {
            return direction * (ReferenceRadius(direction) + macro.seaLevelMetres);
        }

        public double3 Position(double3 direction, double spacing, Dictionary<float3, float> cache = null)
        {
            return direction * (ReferenceRadius(direction) + DisplayDisplacement(Height(direction, spacing, cache)));
        }

        public double ReliefBound
        {
            get { return (math.max(math.abs(macro.minHeightMetres), math.abs(macro.maxHeightMetres)) + detail.AmplitudeBound) * reliefScale + math.abs(opticalHeight); }
        }
    }
}
