using Downstream.Core.Boat;
using Downstream.Core.Math;
using Downstream.Core.Water;

namespace Downstream.Core.Sim
{
    /// <summary>
    /// Client-side prediction for the local boat, as the design specifies: remember the input and
    /// predicted state for the last <see cref="Capacity"/> ticks; when the host's authoritative state
    /// for an older tick arrives and differs, adopt it and replay the stored inputs up to now.
    /// Water is a pure function of position and tick, so the replay sees exactly the same river.
    /// </summary>
    public sealed class OwnBoatPredictor
    {
        /// <summary>Design: up to 32 sim ticks (about 270 ms at 120 Hz).</summary>
        public const int Capacity = 32;

        private readonly BoatInput[] _inputs = new BoatInput[Capacity];
        private readonly BoatState[] _states = new BoatState[Capacity];
        private readonly int[] _ticks = new int[Capacity];

        public BoatState Current;
        public int CurrentTick { get; private set; }

        /// <summary>Corrections smaller than this (metres) are ignored to avoid constant tiny replays.</summary>
        public float PositionTolerance { get; set; } = 0.05f;

        public int ReplayCount { get; private set; }

        public OwnBoatPredictor(BoatState start, int startTick)
        {
            Current = start;
            CurrentTick = startTick;
            for (int i = 0; i < Capacity; i++) _ticks[i] = int.MinValue;
        }

        /// <summary>Predicts one tick ahead with local input and records it.</summary>
        public void Predict(in BoatInput input, in BoatTuning tuning, IWaterQuery water, in BoatModifiers mods)
        {
            var q = input.Quantized();
            int slot = CurrentTick % Capacity;
            _inputs[slot] = q;
            _ticks[slot] = CurrentTick;
            BoatSimulator.Step(ref Current, q, tuning, water, CurrentTick * BoatSimulator.TickDelta, mods);
            CurrentTick++;
            _states[slot] = Current; // state after applying the input of tick CurrentTick-1
        }

        /// <summary>
        /// Applies the host's state at the end of <paramref name="tick"/> (after that tick's input).
        /// Returns false if the tick is too old to replay; the caller should then snap.
        /// </summary>
        public bool Reconcile(int tick, in BoatState authoritative, in BoatTuning tuning, IWaterQuery water, in BoatModifiers mods)
        {
            if (tick >= CurrentTick) return true;
            int slot = tick % Capacity;
            if (_ticks[slot] != tick)
            {
                if (CurrentTick - tick > Capacity) return false;
                return true;
            }

            float error = (_states[slot].Position - authoritative.Position).Magnitude;
            if (error <= PositionTolerance) return true;

            var s = authoritative;
            int replayed = 0;
            for (int k = tick + 1; k < CurrentTick; k++)
            {
                int ks = k % Capacity;
                if (_ticks[ks] != k) break;
                BoatSimulator.Step(ref s, _inputs[ks], tuning, water, k * BoatSimulator.TickDelta, mods);
                _states[ks] = s;
                replayed++;
            }
            _states[slot] = authoritative;
            Current = s;
            ReplayCount += replayed;
            return true;
        }

        /// <summary>Distance between the stored prediction for a tick and a given state, or -1 if the tick is not stored.</summary>
        public float PredictionError(int tick, in BoatState other)
        {
            int slot = ((tick % Capacity) + Capacity) % Capacity;
            if (_ticks[slot] != tick) return -1f;
            return (_states[slot].Position - other.Position).Magnitude;
        }
    }
}
