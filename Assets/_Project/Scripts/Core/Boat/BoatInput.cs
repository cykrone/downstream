using System;

namespace Downstream.Core.Boat
{
    /// <summary>
    /// One tick of player or AI intent. Humans and AI produce exactly this struct, so AI can never
    /// do anything a controller cannot. Kept small and quantizable for input replays and netcode.
    /// </summary>
    [Serializable]
    public struct BoatInput : IEquatable<BoatInput>
    {
        /// <summary>-1 (full brake/reverse) to 1 (full throttle).</summary>
        public float Throttle;

        /// <summary>-1 (left) to 1 (right).</summary>
        public float Steer;

        /// <summary>-1 (nose down) to 1 (nose up). Only used while airborne.</summary>
        public float Pitch;

        /// <summary>Hop on press, drift while held.</summary>
        public bool HopDrift;

        public bool UseItem;

        /// <summary>Packs the input into 4 bytes: three signed 8-bit axes plus button bits.</summary>
        public uint Pack()
        {
            uint t = (uint)(byte)(sbyte)System.MathF.Round(Clamp(Throttle) * 127f);
            uint s = (uint)(byte)(sbyte)System.MathF.Round(Clamp(Steer) * 127f);
            uint p = (uint)(byte)(sbyte)System.MathF.Round(Clamp(Pitch) * 127f);
            uint b = (HopDrift ? 1u : 0u) | (UseItem ? 2u : 0u);
            return t | (s << 8) | (p << 16) | (b << 24);
        }

        public static BoatInput Unpack(uint v) => new BoatInput
        {
            Throttle = (sbyte)(byte)(v & 0xFF) / 127f,
            Steer = (sbyte)(byte)((v >> 8) & 0xFF) / 127f,
            Pitch = (sbyte)(byte)((v >> 16) & 0xFF) / 127f,
            HopDrift = ((v >> 24) & 1u) != 0,
            UseItem = ((v >> 24) & 2u) != 0,
        };

        /// <summary>Quantizes through <see cref="Pack"/> so a live run and its replay see identical values.</summary>
        public BoatInput Quantized() => Unpack(Pack());

        private static float Clamp(float v) => v < -1f ? -1f : (v > 1f ? 1f : v);

        public bool Equals(BoatInput o) =>
            Throttle == o.Throttle && Steer == o.Steer && Pitch == o.Pitch && HopDrift == o.HopDrift && UseItem == o.UseItem;

        public override bool Equals(object obj) => obj is BoatInput o && Equals(o);
        public override int GetHashCode() => (int)Pack();
    }
}
