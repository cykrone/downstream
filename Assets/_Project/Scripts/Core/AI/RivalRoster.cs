using System;
using Downstream.Core.Boat;

namespace Downstream.Core.AI
{
    /// <summary>Personality traits from the design. A rival can have more than one.</summary>
    [Flags]
    public enum RivalTraits : ushort
    {
        None = 0,
        /// <summary>Cuts into the lane of the boat just ahead.</summary>
        LineThief = 1 << 0,
        /// <summary>Takes shortcuts more often than its difficulty alone would.</summary>
        ShortcutHunter = 1 << 1,
        /// <summary>Sits in the wake slot of the boat ahead instead of avoiding it.</summary>
        Drafter = 1 << 2,
        /// <summary>Heavy hull; leans into boats alongside.</summary>
        Bully = 1 << 3,
        /// <summary>Prefers the safe line.</summary>
        Steady = 1 << 4,
        /// <summary>Prefers the current lane.</summary>
        CurrentReader = 1 << 5,
        /// <summary>Prefers the aggressive line.</summary>
        Daredevil = 1 << 6,
    }

    public struct RivalProfile
    {
        public string Name;
        public HullType Hull;
        public RivalTraits Traits;

        public bool Has(RivalTraits trait) => (Traits & trait) != 0;
    }

    /// <summary>
    /// The 8 named rivals (design: "8 named rivals, each with traits such as Line Thief, Shortcut Hunter,
    /// Drafter and Bully"). Names are placeholders until the writing pass.
    /// </summary>
    public static class RivalRoster
    {
        public const int Count = 8;

        private static readonly RivalProfile[] Rivals =
        {
            new RivalProfile { Name = "Marlo", Hull = HullType.Runabout, Traits = RivalTraits.LineThief },
            new RivalProfile { Name = "Pip", Hull = HullType.Skiff, Traits = RivalTraits.ShortcutHunter },
            new RivalProfile { Name = "Dash", Hull = HullType.Hydrofoil, Traits = RivalTraits.Drafter },
            new RivalProfile { Name = "Big Hank", Hull = HullType.Tug, Traits = RivalTraits.Bully },
            new RivalProfile { Name = "Juniper", Hull = HullType.Jetboat, Traits = RivalTraits.Steady },
            new RivalProfile { Name = "Tamsin", Hull = HullType.Runabout, Traits = RivalTraits.CurrentReader },
            new RivalProfile { Name = "Ozzie", Hull = HullType.Hydrofoil, Traits = RivalTraits.Daredevil | RivalTraits.ShortcutHunter },
            new RivalProfile { Name = "Wren", Hull = HullType.Skiff, Traits = RivalTraits.Drafter | RivalTraits.LineThief },
        };

        public static RivalProfile Get(int index) => Rivals[((index % Count) + Count) % Count];
    }
}
