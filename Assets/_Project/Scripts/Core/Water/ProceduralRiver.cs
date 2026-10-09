using System;
using Downstream.Core.Math;

namespace Downstream.Core.Water
{
    /// <summary>A rock in the channel with an eddy circling upstream in its lee.</summary>
    [Serializable]
    public struct RiverBoulder
    {
        /// <summary>Position along the river, metres.</summary>
        public float Distance;
        /// <summary>Signed offset from the centreline, metres (positive = river right looking downstream).</summary>
        public float Lateral;
        public float Radius;
        /// <summary>Length of the eddy behind the rock, metres.</summary>
        public float EddyLength;
        /// <summary>Speed of the upstream eddy current, m/s.</summary>
        public float EddyFlow;
    }

    /// <summary>A ledge: the surface steps down and a hydraulic hole (white boil) recirculates below it.</summary>
    [Serializable]
    public struct RiverLedge
    {
        public float Distance;
        public float Drop;
        public float HoleLength;
        /// <summary>Centre of the hole across the river, metres from the centreline.</summary>
        public float Lateral;
        /// <summary>Width of the hole; the rest of the ledge is a clean tongue.</summary>
        public float Width;
    }

    /// <summary>A train of standing waves in a rapid. Their downslope faces are surfable crests.</summary>
    [Serializable]
    public struct StandingWaveTrain
    {
        public float Distance;
        public int Count;
        public float Wavelength;
        public float Height;
    }

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

        public RiverBoulder[] Boulders;
        public RiverLedge[] Ledges;
        public StandingWaveTrain[] WaveTrains;

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
        /// <summary>Length of the ramp from the channel edge up to the floodable shelf, metres.</summary>
        public const float BankRampLength = 2.5f;
        /// <summary>Height of the floodable shelf above the base water level, metres.</summary>
        public const float BankShelfHeight = 0.5f;

        public static float CentreX(in ProceduralRiverSettings s, float z) =>
            s.MeanderAmplitude <= 0f ? 0f : s.MeanderAmplitude * SimMath.Sin(2f * SimMath.Pi * z / s.MeanderWavelength);

        public static float SurfaceAt(in ProceduralRiverSettings s, float distance)
        {
            float h = -s.Gradient * distance;
            if (s.WaterfallDistance >= 0f && distance > s.WaterfallDistance) h -= s.WaterfallDrop;
            if (s.Ledges != null)
                for (int i = 0; i < s.Ledges.Length; i++)
                    if (distance > s.Ledges[i].Distance) h -= s.Ledges[i].Drop;
            if (s.WaveTrains != null)
                for (int i = 0; i < s.WaveTrains.Length; i++)
                {
                    float u = WaveTrainPhase(s.WaveTrains[i], distance);
                    if (u >= 0f) h += s.WaveTrains[i].Height * 0.5f * (1f - SimMath.Cos(2f * SimMath.Pi * u));
                }
            return h;
        }

        /// <summary>Position inside a wave train in wavelengths (0..Count), or -1 outside it.</summary>
        private static float WaveTrainPhase(in StandingWaveTrain w, float distance)
        {
            if (w.Wavelength <= 0f || w.Count <= 0) return -1f;
            float u = (distance - w.Distance) / w.Wavelength;
            return u < 0f || u > w.Count ? -1f : u;
        }

        /// <summary>True where the surface falls away downstream inside a wave train: the face a boat surfs.</summary>
        private static bool OnCrestFace(in ProceduralRiverSettings s, float distance)
        {
            if (s.WaveTrains == null) return false;
            for (int i = 0; i < s.WaveTrains.Length; i++)
            {
                float u = WaveTrainPhase(s.WaveTrains[i], distance);
                if (u < 0f) continue;
                float frac = u - SimMath.FloorToInt(u);
                if (frac > 0.5f) return true;
            }
            return false;
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
                        if (OnCrestFace(s, distance))
                            features |= WaterFeature.Crest;
                        if (InHole(s, distance, lateral))
                        {
                            features |= WaterFeature.HydraulicHole;
                            flow *= 0.2f;
                        }
                        int rock = BoulderAt(s, distance, lateral, out bool eddy);
                        if (rock >= 0 && !eddy)
                        {
                            // Dry rock: the bed stands above the surface, so it reads as land to every query.
                            bed = surface + 1f;
                            flow = 0f;
                            features = WaterFeature.None;
                        }
                        else if (rock >= 0)
                        {
                            flow = -s.Boulders[rock].EddyFlow;
                            features = (features & ~WaterFeature.CurrentLane) | WaterFeature.Eddy;
                        }
                    }
                    else
                    {
                        // The floodable meadow: a short ramp up from the channel floor, then a flat shelf
                        // 0.5 m above the water that only a flood pulse wets. A vertical wall at the channel
                        // edge would read as a 0.5 m sawtooth at grazing angles and ground boats on a step.
                        float over = abs - halfWidth;
                        float t = SimMath.Clamp01(over / BankRampLength);
                        t = t * t * (3f - 2f * t);
                        bed = SimMath.Lerp(surface - s.Depth * 0.4f, surface + BankShelfHeight, t);
                        features |= WaterFeature.Floodable;
                    }

                    field.SetTexel(ix, iz, surface, bed, tX * flow, tZ * flow, distance, features);
                }
            }
            return field;
        }

        private static bool InHole(in ProceduralRiverSettings s, float distance, float lateral)
        {
            if (s.Ledges == null) return false;
            for (int i = 0; i < s.Ledges.Length; i++)
            {
                var l = s.Ledges[i];
                if (distance > l.Distance && distance <= l.Distance + l.HoleLength && SimMath.Abs(lateral - l.Lateral) <= l.Width * 0.5f)
                    return true;
            }
            return false;
        }

        /// <summary>Index of the boulder whose rock or eddy covers this point, or -1. <paramref name="eddy"/> is false on the rock itself.</summary>
        private static int BoulderAt(in ProceduralRiverSettings s, float distance, float lateral, out bool eddy)
        {
            eddy = false;
            if (s.Boulders == null) return -1;
            for (int i = 0; i < s.Boulders.Length; i++)
            {
                var b = s.Boulders[i];
                float dd = distance - b.Distance, dl = lateral - b.Lateral;
                if (dd * dd + dl * dl <= b.Radius * b.Radius) return i;
                if (dd > 0f && dd <= b.Radius + b.EddyLength && SimMath.Abs(dl) <= b.Radius * 1.2f)
                {
                    eddy = true;
                    return i;
                }
            }
            return -1;
        }
    }
}
