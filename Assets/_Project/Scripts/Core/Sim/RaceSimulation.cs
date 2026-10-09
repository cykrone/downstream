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
        /// <summary>The rough water either side of the slot, out to this distance from the wake's centreline.</summary>
        public const float WakeEdgeHalfWidth = 3f;
        /// <summary>A boat slower than this leaves no wake worth drafting or clipping.</summary>
        public const float WakeMinSpeed = 5f;

        public IWaterQuery Water { get; }
        public RaceTrack Track { get; }
        public IBoatCollider Collider { get; set; }
        public int BoatCount { get; }
        public RaceSnapshot State { get; }
        public BoatTuning[] Tunings { get; }
        public BoatEvents[] LastEvents { get; }
        public BoatModifiers[] Modifiers { get; }

        /// <summary>
        /// Parked boats (respawning, or out of the race) are not stepped and take no part in wakes or
        /// bumps. Whoever parked them owns their state until they are released.
        /// </summary>
        public bool[] Parked { get; }

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
            Parked = new bool[BoatCount];
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
            UpdateWakes();
            for (int i = 0; i < BoatCount; i++)
            {
                if (Parked[i])
                {
                    LastEvents[i] = BoatEvents.None;
                    continue;
                }
                var input = inputs[i].Quantized();
                LastEvents[i] = BoatSimulator.Step(ref State.Boats[i], input, Tunings[i], Water, t, Modifiers[i], Collider);
            }
            ResolveBoatContacts();
            for (int i = 0; i < BoatCount; i++)
                State.Progress[i] = Track.Project(State.Boats[i].Position, ref State.TrackHints[i]);
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
        /// and both are in a current lane, matching the design's "wake drafting in current". Just outside
        /// the slot, out to 3 m, is the wake's rough edge, which costs grip wherever it is.
        /// </summary>
        private void UpdateWakes()
        {
            for (int i = 0; i < BoatCount; i++)
            {
                ref var me = ref State.Boats[i];
                bool slot = false, edge = false;
                for (int j = 0; j < BoatCount && !Parked[i]; j++)
                {
                    if (j == i || Parked[j]) continue;
                    ref var rival = ref State.Boats[j];
                    if (rival.Velocity.Flat.SqrMagnitude < WakeMinSpeed * WakeMinSpeed) continue;
                    var rf = rival.FlatForward;
                    var d = (me.Position - rival.Position).Flat;
                    float behind = -SimVec3.Dot(d, rf);
                    if (behind < WakeSlotMinDistance || behind > WakeSlotMaxDistance) continue;
                    float lateral = SimMath.Abs(SimVec3.Dot(d, rival.FlatRight));
                    if (lateral <= WakeSlotHalfWidth)
                    {
                        if (me.InCurrentLane && rival.InCurrentLane) slot = true;
                    }
                    else if (lateral <= WakeEdgeHalfWidth)
                    {
                        edge = true;
                    }
                }
                var m = Modifiers[i];
                m.InWakeSlot = slot;
                m.OnWakeEdge = edge && !slot;
                m.GripScale = m.OnWakeEdge ? Tunings[i].WakeEdgeGripFactor : 1f;
                Modifiers[i] = m;
            }
        }

        /// <summary>
        /// Boat-to-boat bumps. Each hull is a flat capsule along its heading; overlapping pairs are pushed
        /// apart and exchange momentum along the contact normal, weighted by mass from the Weight stat.
        /// Pairs resolve in a fixed order so the result is deterministic.
        /// </summary>
        private void ResolveBoatContacts()
        {
            for (int i = 0; i < BoatCount; i++)
            for (int j = i + 1; j < BoatCount; j++)
            {
                if (Parked[i] || Parked[j]) continue;
                ref var a = ref State.Boats[i];
                ref var b = ref State.Boats[j];
                ref readonly var ta = ref Tunings[i];
                ref readonly var tb = ref Tunings[j];
                float ra = ta.HullBeam * 0.5f, rb = tb.HullBeam * 0.5f;
                var a0 = a.Position.Flat;
                var b0 = b.Position.Flat;
                // Cheap reject on bounding circles.
                float reach = ta.HullLength * 0.5f + tb.HullLength * 0.5f;
                if ((a0 - b0).SqrMagnitude > reach * reach) continue;

                var ha = a.FlatForward * SimMath.Max(0f, ta.HullLength * 0.5f - ra);
                var hb = b.FlatForward * SimMath.Max(0f, tb.HullLength * 0.5f - rb);
                ClosestPoints(a0 - ha, a0 + ha, b0 - hb, b0 + hb, out var pa, out var pb);
                var delta = pa - pb;
                float dist = delta.Magnitude;
                float overlap = ra + rb - dist;
                if (overlap <= 0f) continue;
                var n = dist > 1e-5f ? delta / dist : (a0 - b0).SqrMagnitude > 1e-10f ? (a0 - b0).Normalized : SimVec3.Right;

                float wa = 1f / SimMath.Max(ta.Mass, 0.1f), wb = 1f / SimMath.Max(tb.Mass, 0.1f);
                float share = 1f / (wa + wb);
                a.Position += n * (overlap * wa * share);
                b.Position -= n * (overlap * wb * share);

                float closing = SimVec3.Dot(a.Velocity - b.Velocity, n);
                if (closing < 0f)
                {
                    float e = 0.5f * (ta.BumpRestitution + tb.BumpRestitution);
                    float impulse = -(1f + e) * closing * share;
                    a.Velocity += n * (impulse * wa);
                    b.Velocity -= n * (impulse * wb);
                }
                LastEvents[i] |= BoatEvents.BoatBump;
                LastEvents[j] |= BoatEvents.BoatBump;
            }
        }

        /// <summary>Closest points between flat segments p1-q1 and p2-q2 (Ericson, Real-Time Collision Detection 5.1.9).</summary>
        private static void ClosestPoints(SimVec3 p1, SimVec3 q1, SimVec3 p2, SimVec3 q2, out SimVec3 c1, out SimVec3 c2)
        {
            var d1 = q1 - p1;
            var d2 = q2 - p2;
            var r = p1 - p2;
            float a = SimVec3.Dot(d1, d1), e = SimVec3.Dot(d2, d2), f = SimVec3.Dot(d2, r);
            float s, t;
            if (a <= 1e-8f && e <= 1e-8f)
            {
                s = t = 0f;
            }
            else if (a <= 1e-8f)
            {
                s = 0f;
                t = SimMath.Clamp01(f / e);
            }
            else
            {
                float c = SimVec3.Dot(d1, r);
                if (e <= 1e-8f)
                {
                    t = 0f;
                    s = SimMath.Clamp01(-c / a);
                }
                else
                {
                    float b = SimVec3.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom > 1e-8f ? SimMath.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f)
                    {
                        t = 0f;
                        s = SimMath.Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = SimMath.Clamp01((b - c) / a);
                    }
                }
            }
            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
        }
    }
}
