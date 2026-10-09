using System;
using Downstream.Core.Math;

namespace Downstream.Core.Items
{
    public enum ItemType : byte
    {
        None,
        LilyMine,
        LogJam,
        ReedShield,
        Pike,
        TurbineX1,
        TriplePike,
        WakeBlaster,
        Whirl,
        TurbineX3,
        Surge,
        Kingfisher,
        DamBurst,
    }

    /// <summary>
    /// The design doc's position-weighted item table (8 boats, Standard ruleset). Leaders get
    /// defence, the middle gets attacks, the back gets speed. Values are percentages per pickup
    /// and are starting points to tune from telemetry.
    /// </summary>
    public static class ItemDistribution
    {
        /// <summary>Position bands: 0 = 1st, 1 = 2nd-3rd, 2 = 4th-5th, 3 = 6th-8th.</summary>
        public const int BandCount = 4;

        private static readonly ItemType[] Items =
        {
            ItemType.LilyMine, ItemType.LogJam, ItemType.ReedShield, ItemType.Pike, ItemType.TurbineX1,
            ItemType.TriplePike, ItemType.WakeBlaster, ItemType.Whirl, ItemType.TurbineX3, ItemType.Surge,
            ItemType.Kingfisher, ItemType.DamBurst,
        };

        // Rows follow Items; columns are the four position bands.
        private static readonly int[,] Weights =
        {
            { 30, 20, 10, 0 },  // Lily Mine
            { 25, 15, 5, 0 },   // Log Jam
            { 25, 10, 5, 0 },   // Reed Shield
            { 15, 25, 15, 5 },  // Pike
            { 5, 15, 15, 5 },   // Turbine x1
            { 0, 10, 15, 10 },  // Triple Pike
            { 0, 5, 10, 5 },    // Wake Blaster
            { 0, 0, 10, 10 },   // Whirl
            { 0, 0, 10, 20 },   // Turbine x3
            { 0, 0, 5, 20 },    // Surge
            { 0, 0, 0, 10 },    // Kingfisher
            { 0, 0, 0, 15 },    // Dam Burst
        };

        public static int ItemCount => Items.Length;
        public static ItemType ItemAt(int row) => Items[row];

        /// <summary>Maps a 1-based race place to its band.</summary>
        public static int BandForPlace(int place)
        {
            if (place <= 1) return 0;
            if (place <= 3) return 1;
            if (place <= 5) return 2;
            return 3;
        }

        public static int Weight(ItemType item, int band)
        {
            int row = Array.IndexOf(Items, item);
            return row < 0 ? 0 : Weights[row, band];
        }

        public static int BandTotal(int band)
        {
            int sum = 0;
            for (int r = 0; r < Items.Length; r++) sum += Weights[r, band];
            return sum;
        }

        /// <summary>Draws an item for a boat in <paramref name="place"/> using the race's seeded generator.</summary>
        public static ItemType Roll(int place, ref SimRandom rng)
        {
            int band = BandForPlace(place);
            int total = BandTotal(band);
            int pick = rng.NextInt(total);
            for (int r = 0; r < Items.Length; r++)
            {
                pick -= Weights[r, band];
                if (pick < 0) return Items[r];
            }
            return Items[Items.Length - 1];
        }
    }
}
