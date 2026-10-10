using Downstream.Core.Math;

namespace Downstream.Core.Water
{
    /// <summary>
    /// Gameplay water for one track: the baked <see cref="RiverField"/> plus the analytic
    /// <see cref="WaterLayers"/>. This is the single source of truth for boats, AI and items.
    /// </summary>
    public sealed class RiverWater : IWaterQuery
    {
        public RiverField Field { get; }
        public WaterLayers Layers { get; }

        /// <summary>Offset used for the finite-difference surface normal, metres.</summary>
        private readonly float _normalStep;

        public RiverWater(RiverField field, WaterLayers layers = null)
        {
            Field = field;
            Layers = layers ?? new WaterLayers();
            _normalStep = field.CellSize;
        }

        public WaterSample Sample(float x, float z, float raceTime)
        {
            var s = Field.SampleStatic(x, z);
            if (!s.HasData) return WaterSample.Dry;

            float rise = 0f;
            float flowScale = 1f;
            var floods = Layers.Floods;
            for (int i = 0; i < floods.Length; i++)
            {
                float k = floods[i].Strength(s.RiverDistance, raceTime);
                if (k <= 0f) continue;
                rise += floods[i].Rise * k;
                flowScale *= SimMath.Lerp(1f, floods[i].FlowMultiplier, k);
            }
            rise += Layers.Tide.Level(raceTime);
            flowScale *= Layers.Tide.FlowScale(raceTime);

            float waveH = 0f, waveSx = 0f, waveSz = 0f;
            var waves = Layers.Waves;
            for (int i = 0; i < waves.Length; i++)
            {
                waves[i].Evaluate(x, z, raceTime, out float h, out float sx, out float sz);
                waveH += h;
                waveSx += sx;
                waveSz += sz;
            }

            float surface = s.SurfaceHeight + rise;
            float depth = surface - s.BedHeight;
            if (depth <= 0f) return WaterSample.Dry;

            // Static slope from the baked surface (waterfalls, rapids), plus the wave slope.
            float step = _normalStep;
            float hx0 = StaticSurfaceOr(x - step, z, s.SurfaceHeight);
            float hx1 = StaticSurfaceOr(x + step, z, s.SurfaceHeight);
            float hz0 = StaticSurfaceOr(x, z - step, s.SurfaceHeight);
            float hz1 = StaticSurfaceOr(x, z + step, s.SurfaceHeight);
            float slopeX = (hx1 - hx0) / (2f * step) + waveSx;
            float slopeZ = (hz1 - hz0) / (2f * step) + waveSz;

            return new WaterSample
            {
                IsWet = true,
                SurfaceHeight = surface + waveH,
                Depth = depth + waveH,
                Flow = new SimVec3(s.FlowX * flowScale, 0f, s.FlowZ * flowScale),
                Normal = new SimVec3(-slopeX, 1f, -slopeZ).Normalized,
                RiverDistance = s.RiverDistance,
                Features = s.Features,
            };
        }

        private float StaticSurfaceOr(float x, float z, float fallback)
        {
            var s = Field.SampleStatic(x, z);
            return s.HasData ? s.SurfaceHeight : fallback;
        }
    }
}
