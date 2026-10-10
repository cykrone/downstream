using System;
using Downstream.Core.Math;

namespace Downstream.Core.AI
{
    public enum MistakeKind : byte
    {
        /// <summary>Lets go of the next drift a tier early.</summary>
        MissedDriftTier,
        /// <summary>Lands the next jump flat.</summary>
        SlappedLanding,
        /// <summary>Takes the slowest line for a few seconds.</summary>
        WrongChannel,
        /// <summary>Lifts off the throttle for a moment.</summary>
        ThrottleLift,
        /// <summary>A sharp over-correction of the steering.</summary>
        Overcorrect,
    }

    public struct ScheduledMistake
    {
        /// <summary>Distance along the track where the mistake becomes due.</summary>
        public float Distance;
        public MistakeKind Kind;
    }

    /// <summary>
    /// The seeded mistake budget (design): how many mistakes an AI makes and where, fixed at race start
    /// from the seed and the difficulty, and never adjusted by race position.
    /// </summary>
    public static class MistakeBudget
    {
        /// <summary>Mistakes fall between these fractions of the track, so every one is made before the finish.</summary>
        public const float FirstFraction = 0.05f;
        public const float LastFraction = 0.8f;

        public static ScheduledMistake[] Create(ulong seed, in AIDifficulty difficulty, float trackLength)
        {
            var rng = new SimRandom(seed, 0x6D15u);
            int count = difficulty.MistakesMin + rng.NextInt(difficulty.MistakesMax - difficulty.MistakesMin + 1);
            var budget = new ScheduledMistake[count];
            for (int i = 0; i < count; i++)
            {
                budget[i] = new ScheduledMistake
                {
                    Distance = trackLength * rng.Range(FirstFraction, LastFraction),
                    Kind = (MistakeKind)rng.NextInt(5),
                };
            }
            Array.Sort(budget, (a, b) => a.Distance.CompareTo(b.Distance));
            return budget;
        }
    }
}
