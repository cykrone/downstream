namespace Downstream.Core.AI
{
    public enum AILevel : byte
    {
        Easy,
        Normal,
        Hard,
        /// <summary>Unlocked by winning a Hard cup.</summary>
        Expert,
    }

    /// <summary>
    /// The design's difficulty table. Difficulty only changes how well the AI drives; it never changes
    /// boat speed, and nothing here is adjusted by race position during a race.
    /// </summary>
    public struct AIDifficulty
    {
        public AILevel Level;
        /// <summary>Delay before the AI reacts to hazards, rivals and line changes, seconds.</summary>
        public float ReactionDelay;
        /// <summary>Amplitude of the slow wander off the chosen line, metres.</summary>
        public float LineNoise;
        /// <summary>Highest drift tier the AI will hold a drift for (0 = never drifts).</summary>
        public int MaxDriftTier;
        /// <summary>Chance of taking each shortcut, 0-1, rolled once per shortcut at race start.</summary>
        public float ShortcutUse;
        public int MistakesMin;
        public int MistakesMax;
        /// <summary>Seconds the AI sits on a new item before using it (Easy uses items late).</summary>
        public float ItemPatience;
        /// <summary>Chance the AI uses an item at all (Easy sometimes never does).</summary>
        public float ItemUseChance;

        public int ReactionTicks => (int)(ReactionDelay * Boat.BoatSimulator.TickRate + 0.5f);

        public static AIDifficulty For(AILevel level)
        {
            switch (level)
            {
                case AILevel.Easy:
                    return new AIDifficulty { Level = level, ReactionDelay = 0.35f, LineNoise = 1.5f, MaxDriftTier = 1, ShortcutUse = 0f, MistakesMin = 6, MistakesMax = 8, ItemPatience = 2.5f, ItemUseChance = 0.6f };
                case AILevel.Normal:
                    return new AIDifficulty { Level = level, ReactionDelay = 0.22f, LineNoise = 0.8f, MaxDriftTier = 2, ShortcutUse = 0.3f, MistakesMin = 3, MistakesMax = 5, ItemPatience = 0.8f, ItemUseChance = 1f };
                case AILevel.Hard:
                    return new AIDifficulty { Level = level, ReactionDelay = 0.12f, LineNoise = 0.4f, MaxDriftTier = 3, ShortcutUse = 0.7f, MistakesMin = 1, MistakesMax = 2, ItemPatience = 0.4f, ItemUseChance = 1f };
                default:
                    return new AIDifficulty { Level = AILevel.Expert, ReactionDelay = 0.06f, LineNoise = 0.15f, MaxDriftTier = 3, ShortcutUse = 1f, MistakesMin = 0, MistakesMax = 1, ItemPatience = 0.25f, ItemUseChance = 1f };
            }
        }
    }

    /// <summary>
    /// Opt-in adaptive difficulty (design): moves the AI level between races only, never during one,
    /// and the results screen shows an icon when it is on.
    /// </summary>
    public static class AdaptiveDifficulty
    {
        /// <summary>
        /// The level for the next race given the best place any local player got in the last one (1-based).
        /// A win steps up, finishing in the bottom quarter steps down; Expert only once unlocked.
        /// </summary>
        public static AILevel Next(AILevel current, int bestHumanPlace, int boats, bool expertUnlocked)
        {
            if (bestHumanPlace <= 1)
            {
                var top = expertUnlocked ? AILevel.Expert : AILevel.Hard;
                return current < top ? current + 1 : current;
            }
            if (bestHumanPlace > boats - boats / 4 && current > AILevel.Easy) return current - 1;
            return current;
        }
    }
}
