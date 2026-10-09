using System;
using Downstream.Core.Boat;
using Downstream.Core.Math;
using Downstream.Core.Race;
using Downstream.Core.Water;

namespace Downstream.Core.Sim
{
    /// <summary>
    /// Everything that is rewound together for rollback: the boats and the tick counter.
    /// Copy it with <see cref="CopyFrom"/>; it allocates only when created.
    /// </summary>
    public sealed class RaceSnapshot
    {
        public int Tick;
        public readonly BoatState[] Boats;
        public readonly float[] Progress;
        public readonly int[] TrackHints;

        public RaceSnapshot(int boatCount)
        {
            Boats = new BoatState[boatCount];
            Progress = new float[boatCount];
            TrackHints = new int[boatCount];
        }

        public void CopyFrom(RaceSnapshot other)
        {
            Tick = other.Tick;
            Array.Copy(other.Boats, Boats, Boats.Length);
            Array.Copy(other.Progress, Progress, Progress.Length);
            Array.Copy(other.TrackHints, TrackHints, TrackHints.Length);
        }
    }

    /// <summary>
    /// One race's gameplay simulation at a fixed 120 Hz. Race time is derived from an integer tick,
    /// never accumulated, so every client and every re-simulation agrees on it exactly.
    /// The Unity runtime owns one of these and calls <see cref="Step"/> from its accumulator.
    /// </summary>
    public sealed class RaceSimulation
    {
        public const float WakeSlotMinDistance = 3f;
        public const float WakeSlotMaxDistance = 14f;
        public const float WakeSlotHalfWidth = 1.5f;

        public IWaterQuery Water { get; }
        public RaceTrack Track { get; }
        public IBoatCollider Collider { get; set; }
        public int BoatCount { get; }
        public RaceSnapshot State { get; }
        public BoatTuning[] Tunings { get; }
        public BoatEvents[] LastEvents { get; }
        public BoatModifiers[] Modifiers { get; }

        public int Tick => State.Tick;
        public float RaceTime => State.Tick * BoatSimulator.TickDelta;

        private readonly int[] _order;

        public RaceSimulation(IWaterQuery water, RaceTrack track, BoatTuning[] tunings, BoatState[] startStates)
        {
            if (tunings.Length != startStates.Length) throw new ArgumentException("One tuning per boat.");
            Water = water;
            Track = track;
            BoatCount = tunings.Length;
            Tunings = (BoatTuning[])tunings.Clone();
            State = new RaceSnapshot(BoatCount);
            Array.Copy(startStates, State.Boats, BoatCount);
            LastEvents = new BoatEvents[BoatCount];
            Modifiers = new BoatModifiers[BoatCount];
            _order = new int[BoatCount];
            for (int i = 0; i < BoatCount; i++)
            {
                Modifiers[i] = BoatModifiers.None;
                State.Progress[i] = track.Project(State.Boats[i].Position, ref State.TrackHints[i], track.PointCount);
                _order[i] = i;
            }
        }

        /// <summary>Advances every boat by one tick. <paramref name="inputs"/> holds one input per boat.</summary>
        public void Step(BoatInput[] inputs)
        {
            if (inputs.Length < BoatCount) throw new ArgumentException("One input per boat.", nameof(inputs));
            float t = RaceTime;
            UpdateWakeSlots();
            for (int i = 0; i < BoatCount; i++)
            {
                var input = inputs[i].Quantized();
                LastEvents[i] = BoatSimulator.Step(ref State.Boats[i], input, Tunings[i], Water, t, Modifiers[i], Collider);
                State.Progress[i] = Track.Project(State.Boats[i].Position, ref State.TrackHints[i]);
            }
            State.Tick++;
        }

        /// <summary>Boats sorted by distance along the river, leader first. Returns the shared buffer.</summary>
        public int[] Standings()
        {
            for (int i = 0; i < BoatCount; i++) _order[i] = i;
            // Insertion sort: 8 boats, stable, allocation-free.
            for (int i = 1; i < BoatCount; i++)
            {
                int b = _order[i];
                int j = i - 1;
                while (j >= 0 && State.Progress[_order[j]] < State.Progress[b])
                {
                    _order[j + 1] = _order[j];
                    j--;
                }
                _order[j + 1] = b;
            }
            return _order;
        }

        /// <summary>1-based place of a boat.</summary>
        public int PlaceOf(int boat)
        {
            var order = Standings();
            for (int i = 0; i < BoatCount; i++)
                if (order[i] == boat) return i + 1;
            return BoatCount;
        }

        /// <summary>FNV-1a hash over every boat's position, velocity and orientation bits. Used by desync and determinism tests.</summary>
        public ulong StateHash()
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < BoatCount; i++)
            {
                ref var b = ref State.Boats[i];
                h = Mix(h, b.Position.X); h = Mix(h, b.Position.Y); h = Mix(h, b.Position.Z);
                h = Mix(h, b.Velocity.X); h = Mix(h, b.Velocity.Y); h = Mix(h, b.Velocity.Z);
                h = Mix(h, b.Yaw); h = Mix(h, b.Pitch); h = Mix(h, b.Roll);
                h = Mix(h, b.BoostTime); h = Mix(h, b.DriftTime);
            }
            return h;
        }

        private static ulong Mix(ulong h, float f)
        {
            uint bits = (uint)BitConverter.SingleToInt32Bits(f);
            for (int k = 0; k < 4; k++)
            {
                h ^= (bits >> (k * 8)) & 0xFF;
                h *= 1099511628211UL;
            }
            return h;
        }

        /// <summary>
        /// A boat is in a wake slot when a rival is 3-14 m directly ahead (within 1.5 m of its centreline)
        /// and both are in a current lane, matching the design's "wake drafting in current".
        /// </summary>
        private void UpdateWakeSlots()
        {
            for (int i = 0; i < BoatCount; i++)
            {
                ref var me = ref State.Boats[i];
                bool slot = false;
                if (me.InCurrentLane)
                {
                    for (int j = 0; j < BoatCount && !slot; j++)
                    {
                        if (j == i) continue;
                        ref var rival = ref State.Boats[j];
                        if (!rival.InCurrentLane) continue;
                        var rf = rival.FlatForward;
                        var d = (me.Position - rival.Position).Flat;
                        float behind = -SimVec3.Dot(d, rf);
                        float lateral = SimMath.Abs(SimVec3.Dot(d, rival.FlatRight));
                        slot = behind >= WakeSlotMinDistance && behind <= WakeSlotMaxDistance && lateral <= WakeSlotHalfWidth;
                    }
                }
                var m = Modifiers[i];
                m.InWakeSlot = slot;
                Modifiers[i] = m;
            }
        }
    }
}
