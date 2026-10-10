using System;
using Downstream.Core.Math;

namespace Downstream.Core.Water
{
    /// <summary>Bit flags baked into the River Field's feature mask channel.</summary>
    [Flags]
    public enum WaterFeature : byte
    {
        None = 0,
        CurrentLane = 1 << 0,
        Eddy = 1 << 1,
        HydraulicHole = 1 << 2,
        Crest = 1 << 3,
        WaterfallLip = 1 << 4,
        Floodable = 1 << 5,
        Shallows = 1 << 6,
    }

    /// <summary>What a point query against the gameplay water returns.</summary>
    public struct WaterSample
    {
        /// <summary>True when the point lies over water (or flooded land) at this moment.</summary>
        public bool IsWet;

        /// <summary>World-space height of the water surface, metres.</summary>
        public float SurfaceHeight;

        /// <summary>Water depth below the surface (surface minus river bed), metres.</summary>
        public float Depth;

        /// <summary>Horizontal water velocity in world space, m/s. Y is always zero.</summary>
        public SimVec3 Flow;

        /// <summary>Approximate surface normal, unit length.</summary>
        public SimVec3 Normal;

        /// <summary>Distance along the river centreline, metres. Drives flood pulses and race progress.</summary>
        public float RiverDistance;

        public WaterFeature Features;

        public static WaterSample Dry => new WaterSample { IsWet = false, Normal = SimVec3.Up, SurfaceHeight = float.NegativeInfinity };
    }

    /// <summary>
    /// The one interface boat physics, AI and items use to read the water. Every implementation
    /// must be a pure function of (position, race time): no hidden state, no GPU readback,
    /// so every client and every rollback re-simulation sees the same water.
    /// </summary>
    public interface IWaterQuery
    {
        WaterSample Sample(float x, float z, float raceTime);
    }
}
