namespace Downstream.Core.Sim
{
    /// <summary>
    /// Turns variable frame times into a whole number of fixed simulation ticks, plus the blend
    /// factor the renderer uses to interpolate between the last two ticks.
    /// </summary>
    public sealed class FixedStepClock
    {
        public float StepSeconds { get; }

        /// <summary>Most ticks run in one frame; beyond this the game slows down instead of spiralling.</summary>
        public int MaxStepsPerFrame { get; set; } = 8;

        private double _accumulator;

        public FixedStepClock(float stepSeconds)
        {
            StepSeconds = stepSeconds;
        }

        /// <summary>Adds frame time and returns how many ticks to run now.</summary>
        public int Advance(float frameSeconds)
        {
            _accumulator += frameSeconds;
            int steps = (int)(_accumulator / StepSeconds);
            if (steps > MaxStepsPerFrame)
            {
                steps = MaxStepsPerFrame;
                _accumulator = 0d;
                return steps;
            }
            _accumulator -= steps * (double)StepSeconds;
            return steps;
        }

        /// <summary>0..1 position between the previous and current tick.</summary>
        public float Alpha => (float)(_accumulator / StepSeconds);

        public void Reset() => _accumulator = 0d;
    }
}
