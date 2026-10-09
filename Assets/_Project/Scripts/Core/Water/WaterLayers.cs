using System;
using Downstream.Core.Math;

namespace Downstream.Core.Water
{
    /// <summary>
    /// One vertical Gerstner-style wave. Gameplay uses height and slope only (no horizontal
    /// displacement) so a CPU sample is cheap and exact. The visual shader adds the horizontal part.
    /// </summary>
    [Serializable]
    public struct GerstnerWave
    {
        public float DirectionX;
        public float DirectionZ;
        public float Wavelength;
        public float Amplitude;
        public float Speed;

        public void Evaluate(float x, float z, float t, out float height, out float slopeX, out float slopeZ)
        {
            float len = SimMath.Sqrt(DirectionX * DirectionX + DirectionZ * DirectionZ);
            if (len < 1e-6f || Wavelength <= 0f)
            {
                height = slopeX = slopeZ = 0f;
                return;
            }
            float dx = DirectionX / len, dz = DirectionZ / len;
            float k = 2f * SimMath.Pi / Wavelength;
            float phase = k * (dx * x + dz * z - Speed * t);
            height = Amplitude * SimMath.Sin(phase);
            float d = Amplitude * k * SimMath.Cos(phase);
            slopeX = d * dx;
            slopeZ = d * dz;
        }
    }

    /// <summary>
    /// A flood pulse released at <see cref="StartTime"/> that travels down the river at
    /// <see cref="Speed"/> m/s along river distance. Boats behind the front meet it first,
    /// which is the design's visible, rule-based comeback.
    /// </summary>
    [Serializable]
    public struct FloodPulse
    {
        public float StartTime;
        public float StartDistance;
        public float Speed;
        public float Rise;
        public float Length;
        public float FlowMultiplier;

        /// <summary>0..1 strength of the pulse at a river distance and time.</summary>
        public float Strength(float riverDistance, float t)
        {
            if (t < StartTime || Length <= 0f) return 0f;
            float front = StartDistance + Speed * (t - StartTime);
            float behind = front - riverDistance;
            if (behind < 0f) return 0f;
            if (behind > Length) return 1f; // the river stays high once the pulse has passed
            float u = behind / Length;
            return u * u * (3f - 2f * u);
        }
    }

    /// <summary>Tide on estuary tracks: ebb (outflow helps) until the turn, then flood (inflow pushes back, level rises).</summary>
    [Serializable]
    public struct TideClock
    {
        public bool Enabled;
        public float TurnTime;
        public float TransitionSeconds;
        public float LowLevel;
        public float HighLevel;
        public float EbbFlowScale;
        public float FloodFlowScale;

        /// <summary>0 at full ebb, 1 at full flood.</summary>
        public float FloodFraction(float t)
        {
            if (!Enabled) return 0f;
            float half = SimMath.Max(TransitionSeconds, 0.001f) * 0.5f;
            float u = SimMath.Clamp01((t - (TurnTime - half)) / (2f * half));
            return u * u * (3f - 2f * u);
        }

        public float Level(float t) => Enabled ? SimMath.Lerp(LowLevel, HighLevel, FloodFraction(t)) : 0f;
        public float FlowScale(float t) => Enabled ? SimMath.Lerp(EbbFlowScale, FloodFlowScale, FloodFraction(t)) : 1f;
    }

    /// <summary>All time-dependent water for one track. Pure data plus pure functions of race time.</summary>
    [Serializable]
    public sealed class WaterLayers
    {
        public GerstnerWave[] Waves = Array.Empty<GerstnerWave>();
        public FloodPulse[] Floods = Array.Empty<FloodPulse>();
        public TideClock Tide;
    }
}
