using System;

namespace Downstream.Core.Boat
{
    public enum HullType
    {
        Skiff,
        Jetboat,
        Runabout,
        Hydrofoil,
        Tug,
    }

    public enum SpeedClass
    {
        Ripple,
        Rapid,
        Torrent,
        FlashFlood,
    }

    /// <summary>
    /// The design's five hull stats on the 1-6 scale. Speed, Acceleration, Handling and Weight
    /// total 14 for every stock hull; Draft is a depth in metres. Parts shift stats by up to ±1.
    /// </summary>
    [Serializable]
    public struct HullStats
    {
        public HullType Type;
        public int Speed;
        public int Acceleration;
        public int Handling;
        public int Weight;
        public float DraftMetres;

        public int PointTotal => Speed + Acceleration + Handling + Weight;

        public static HullStats For(HullType type)
        {
            switch (type)
            {
                case HullType.Skiff: return new HullStats { Type = type, Speed = 3, Acceleration = 4, Handling = 6, Weight = 1, DraftMetres = 0.30f };
                case HullType.Jetboat: return new HullStats { Type = type, Speed = 2, Acceleration = 6, Handling = 4, Weight = 2, DraftMetres = 0.35f };
                case HullType.Runabout: return new HullStats { Type = type, Speed = 4, Acceleration = 3, Handling = 4, Weight = 3, DraftMetres = 0.50f };
                case HullType.Hydrofoil: return new HullStats { Type = type, Speed = 6, Acceleration = 3, Handling = 3, Weight = 2, DraftMetres = 0.55f };
                case HullType.Tug: return new HullStats { Type = type, Speed = 4, Acceleration = 2, Handling = 2, Weight = 6, DraftMetres = 0.80f };
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }
    }

    public static class SpeedClasses
    {
        /// <summary>Calm-water top speed and boost top speed for a class, m/s, from the design doc.</summary>
        public static void TopSpeeds(SpeedClass c, out float top, out float boostTop)
        {
            switch (c)
            {
                case SpeedClass.Ripple: top = 22f; boostTop = 28f; break;
                case SpeedClass.Rapid: top = 27f; boostTop = 34f; break;
                default: top = 32f; boostTop = 40f; break;
            }
        }
    }
}
