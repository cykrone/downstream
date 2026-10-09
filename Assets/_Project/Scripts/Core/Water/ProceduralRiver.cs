using System;
using Downstream.Core.Math;

namespace Downstream.Core.Water
{
    /// <summary>Settings for a procedurally generated greybox river.</summary>
    [Serializable]
    public struct ProceduralRiverSettings
    {
        public float Length;
        public float Width;
        public float Depth;
        /// <summary>Surface drop per metre of river distance (0.01 = 1 m per 100 m).</summary>
        public float Gradient;
        public float BaseFlow;
        /// <summary>Extra flow inside the current lane (the thalweg), m/s.</summary>
        public float LaneExtraFlow;
        public float LaneWidth;
        /// <summary>Side-to-side meander amplitude, metres. 0 gives a straight channel along +Z.</summary>
        public float MeanderAmplitude;
        public float MeanderWavelength;
        /// <summary>Waterfall position along the river (metres); negative disables it.</summary>
        public float WaterfallDistance;
        public float WaterfallDrop;
        /// <summary>Width of floodable bank on each side, metres (sits 0.5 m above the surface).</summary>
        public float FloodableBank;

        public static ProceduralRiverSettings Default => new ProceduralRiverSettings
        {
            Length = 600f,
            Width = 30f,
            Depth = 2f,
            Gradient = 0.01f,
            BaseFlow = 2f,
            LaneExtraFlow = 2f,
            LaneWidth = 8f,
            MeanderAmplitude = 25f,
            MeanderWavelength = 300f,
            WaterfallDistance = -1f,
            WaterfallDrop = 0f,
            FloodableBank = 0f,
        };
    }

    /// <summary>
    /// Builds a River Field for greybox scenes and tests without the offline baker: a meandering
    /// channel flowing towards +Z, a current lane that swings to the outside of each bend, and an
    /// optional waterfall. The real baker replaces this with a shallow-water solve plus hand combing.
    /// </summary>
    public static class ProceduralRiver
    {
        public static float CentreX(in ProceduralRiverSettings s, float z) =>
            s.MeanderAmplitude <= 0f ? 0f : s.MeanderAmplitude * SimMath.Sin(2f * SimMath.Pi * z / s.MeanderWavelength);

        public static float SurfaceAt(in ProceduralRiverSettings s, float distance)
        {
            float h = -s.Gradient * distance;
            if (s.WaterfallDistance >= 0f && distance > s.WaterfallDistance) h -= s.WaterfallDrop;
            return h;
        }

        public static RiverField Build(ProceduralRiverSettings s, float cellSize = RiverField.DefaultCellSize)
        {
            float halfSpan = s.Width * 0.5f + s.FloodableBank + s.MeanderAmplitude + 4f;
            float originX = -halfSpan;
            float originZ = -8f;
            float tileSize = cellSize * RiverField.TileTexels;
            int tilesX = (int)System.MathF.Ceiling(2f * halfSpan / tileSize);
            int tilesZ = (int)System.MathF.Ceiling((s.Length + 16f) / tileSize);
            var field = new RiverField(originX, originZ, tilesX, tilesZ, cellSize);

            float halfWidth = s.Width * 0.5f;
            int nx = field.TexelCountX, nz = field.TexelCountZ;
            for (int iz = 0; iz < nz; iz++)
            {
                field.TexelCentre(0, iz, out _, out float z);
                float distance = SimMath.Clamp(z, 0f, s.Length);
                float cx = CentreX(s, z);
                float dcx = s.MeanderAmplitude <= 0f
                    ? 0f
                    : s.MeanderAmplitude * 2f * SimMath.Pi / s.MeanderWavelength * SimMath.Cos(2f * SimMath.Pi * z / s.MeanderWavelength);
                // Unit tangent of the centreline and the outward side of the current bend.
                float tl = SimMath.Sqrt(1f + dcx * dcx);
                float tX = dcx / tl, tZ = 1f / tl;
                float curvature = -s.MeanderAmplitude * SimMath.Sin(2f * SimMath.Pi * z / s.MeanderWavelength);
                float laneOffset = SimMath.Clamp(curvature / SimMath.Max(s.MeanderAmplitude, 1e-3f), -1f, 1f) * (halfWidth - s.LaneWidth * 0.5f - 1f);
                float surface = SurfaceAt(s, distance);

                for (int ix = 0; ix < nx; ix++)
                {
                    field.TexelCentre(ix, iz, out float x, out _);
                    float lateral = (x - cx) * tZ; // approximate signed distance across the channel
                    float abs = SimMath.Abs(lateral);
                    bool inChannel = abs <= halfWidth && z >= -4f && z <= s.Length + 4f;
                    bool onBank = !inChannel && abs <= halfWidth + s.FloodableBank && z >= 0f && z <= s.Length;
                    if (!inChannel && !onBank) continue;

                    var features = WaterFeature.None;
                    float flow = 0f;
                    float bed;
                    if (inChannel)
                    {
                        // Parabolic cross-section, deepest in the middle.
                        float u = abs / halfWidth;
                        bed = surface - s.Depth * (1f - 0.6f * u * u);
                        float bankFalloff = 1f - 0.5f * u * u;
                        flow = s.BaseFlow * bankFalloff;
                        if (SimMath.Abs(lateral - laneOffset) <= s.LaneWidth * 0.5f)
                        {
                            flow += s.LaneExtraFlow;
                            features |= WaterFeature.CurrentLane;
                        }
                        if (s.WaterfallDistance >= 0f && SimMath.Abs(distance - s.WaterfallDistance) < 1.5f)
                            features |= WaterFeature.WaterfallLip;
                    }
                    else
                    {
                        bed = surface + 0.5f;
                        features |= WaterFeature.Floodable;
                    }

                    field.SetTexel(ix, iz, surface, bed, tX * flow, tZ * flow, distance, features);
                }
            }
            return field;
        }
    }
}
