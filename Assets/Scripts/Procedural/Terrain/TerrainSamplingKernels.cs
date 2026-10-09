using Unity.Burst;
using Unity.Mathematics;

namespace Planet.Procedural.Terrain
{
    /// <summary>Pure Burst entry points; direct calls retain a managed fallback when Burst is disabled.</summary>
    [BurstCompile]
    public static class TerrainSamplingKernels
    {
        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        public static void MacroHeight(in PlanetSurfaceSampler sampler, in float3 direction, out float height)
        {
            height = sampler.EvaluateHeight(direction);
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        public static void MacroAlbedo(in PlanetSurfaceSampler sampler, in float3 direction, out float3 color)
        {
            color = sampler.EvaluateAlbedoOnly(direction);
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        public static void Surface(in PlanetSurfaceSampler sampler, in float3 direction, out PlanetSurfaceSample sample)
        {
            sample = sampler.Evaluate(direction);
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        public static void DetailHeight(in PlanetDetailSampler sampler, in double3 position,
            double spacing, out double height)
        {
            height = sampler.Evaluate(position, spacing);
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        public static void DetailBands(in PlanetDetailSampler sampler, in double3 position,
            double ownSpacing, double parentSpacing, out double own, out double parent)
        {
            sampler.Evaluate2(position, ownSpacing, parentSpacing, out own, out parent);
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        public static void NodeDetail(in PlanetDetailSampler sampler, in double3 position,
            in TerrainNodeKey key, double gridX, double gridY, double spacing, out double height)
        {
            height = 0;
            double remaining = 1;
            int nodeX = key.x, nodeY = key.y;
            // A parent may itself be morphed to its parent at this point. Reconstruct that
            // same field, including the halo, instead of substituting an unmorphed band.
            for (int level = key.level; level >= 0; level--)
            {
                double edge = math.min(math.min(gridX, TerrainLattice.N - gridX),
                    math.min(gridY, TerrainLattice.N - gridY));
                double blend = level == 0 ? 1 : math.saturate(edge * 0.5);
                double weight = remaining * blend;
                if (weight > 0) height += sampler.Evaluate(position, spacing) * weight;
                remaining *= 1 - blend;
                if (remaining == 0) break;
                gridX = (gridX + (nodeX & 1) * TerrainLattice.N) * 0.5;
                gridY = (gridY + (nodeY & 1) * TerrainLattice.N) * 0.5;
                nodeX >>= 1;
                nodeY >>= 1;
                spacing *= 2;
            }
        }

        public static void Warmup(in PlanetSurfaceSampler macro, in PlanetDetailSampler detail)
        {
            var direction = new float3(1, 0, 0);
            var position = new double3(macro.radiusMetres, 0, 0);
            var key = TerrainNodeKey.Root(PlanetCubeFace.PositiveX);
            MacroHeight(macro, direction, out _);
            MacroAlbedo(macro, direction, out _);
            Surface(macro, direction, out _);
            DetailHeight(detail, position, 1, out _);
            DetailBands(detail, position, 1, 2, out _, out _);
            NodeDetail(detail, position, key, 16, 16, 1, out _);
        }
    }
}
