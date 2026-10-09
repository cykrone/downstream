using System;
using Downstream.Core.Boat;
using Downstream.Core.Math;
using Downstream.Core.Sim;
using Downstream.Core.Water;

namespace Downstream.Core.Race
{
    public enum RacePhase : byte
    {
        Countdown,
        Racing,
        Finished,
    }

    /// <summary>Race-level things that happened to a boat during one tick, for HUD, audio and telemetry.</summary>
    [Flags]
    public enum RaceEvents : byte
    {
        None = 0,
        Go = 1 << 0,
        Finished = 1 << 1,
        RespawnStarted = 1 << 2,
        Respawned = 1 << 3,
        PlaceChanged = 1 << 4,
    }

    /// <summary>Why a boat was sent back to the river.</summary>
    public enum RespawnReason : byte
    {
        None,
        Stranded,
        Stuck,
        Sunk,
    }

    /// <summary>Timings for the race flow. Defaults follow the design doc where it gives a number.</summary>
    [Serializable]
    public struct RaceRules
    {
        public float CountdownSeconds;
        /// <summary>After the first boat finishes, the rest have this long before they are ranked by distance.</summary>
        public float FinishTimeoutSeconds;
        /// <summary>Time a respawning boat is parked off the river. With the run back up to speed, respawn costs about 2.5 s (design).</summary>
        public float RespawnHoldSeconds;
        /// <summary>A boat that gains less than <see cref="StuckDistance"/> in this long is respawned.</summary>
        public float StuckSeconds;
        public float StuckDistance;
        /// <summary>A boat over dry land for this long is respawned.</summary>
        public float StrandedSeconds;
        /// <summary>A boat this far under the surface has fallen through the world and is respawned.</summary>
        public float SunkDepth;
        /// <summary>Respawns land this far upstream of where the boat left the river.</summary>
        public float RespawnBackoff;
        /// <summary>A respawned boat is dropped moving downstream at this fraction of its top speed.</summary>
        public float RespawnSpeedFraction;

        public static RaceRules Default => new RaceRules
        {
            CountdownSeconds = 3f,
            FinishTimeoutSeconds = 30f,
            RespawnHoldSeconds = 1.5f,
            StuckSeconds = 4f,
            StuckDistance = 3f,
            StrandedSeconds = 1f,
            SunkDepth = 5f,
            RespawnBackoff = 0f,
            RespawnSpeedFraction = 0.5f,
        };
    }

    /// <summary>Where one boat stands in the race. A plain struct so the session can be snapshotted later.</summary>
    [Serializable]
    public struct BoatRaceStatus
    {
        public bool Finished;
        /// <summary>Race clock at the finish line, interpolated inside the tick, seconds.</summary>
        public float FinishTime;
        /// <summary>1-based finishing place, 0 until finished.</summary>
        public int FinishPlace;
        /// <summary>Current 1-based place: finishers by finish order, then everyone else by distance.</summary>
        public int Place;

        public float RespawnTimer;
        public RespawnReason LastRespawn;
        public int Respawns;
        public float BestProgress;
        public float StuckMark;
        public float StuckTimer;
        public float StrandedTimer;

        public bool IsRespawning => RespawnTimer > 0f;
    }

    /// <summary>One line of the results table.</summary>
    public struct RaceResult
    {
        public int Boat;
        public int Place;
        public bool Finished;
        public float Time;
        public float Progress;
    }

    /// <summary>
    /// The race loop around a <see cref="RaceSimulation"/>: countdown on the grid, the run from source
    /// to mouth, the finish line, respawns for boats that leave the river or stall, and the results.
    /// Positions come from distance along the river (design: "Race framework"). Pure C#, deterministic,
    /// and driven one tick at a time by the Unity director or by headless tests.
    /// </summary>
    public sealed class RaceSession
    {
        public RaceSimulation Sim { get; }
        public RaceRules Rules { get; }
        public RacePhase Phase { get; private set; }
        public BoatRaceStatus[] Status { get; }
        public RaceEvents[] LastEvents { get; }

        /// <summary>Ticks spent on the grid before the start; the race clock starts at zero on "Go".</summary>
        public int CountdownTicks { get; }
        public int FinishTimeoutTicks { get; }

        /// <summary>Tick of the first finish, or -1.</summary>
        public int FirstFinishTick { get; private set; } = -1;

        private readonly SimVec3[] _gridPositions;
        private readonly BoatInput[] _inputs;
        private readonly float[] _prevProgress;
        private readonly int[] _placeOrder;
        private int _finishedCount;

        public RaceSession(RaceSimulation sim, RaceRules? rules = null)
        {
            Sim = sim;
            Rules = rules ?? RaceRules.Default;
            int n = sim.BoatCount;
            Status = new BoatRaceStatus[n];
            LastEvents = new RaceEvents[n];
            _gridPositions = new SimVec3[n];
            _inputs = new BoatInput[n];
            _prevProgress = new float[n];
            _placeOrder = new int[n];
            CountdownTicks = SecondsToTicks(Rules.CountdownSeconds);
            FinishTimeoutTicks = SecondsToTicks(Rules.FinishTimeoutSeconds);
            for (int i = 0; i < n; i++)
            {
                _gridPositions[i] = sim.State.Boats[i].Position;
                Status[i].BestProgress = sim.State.Progress[i];
                Status[i].StuckMark = sim.State.Progress[i];
            }
            Phase = CountdownTicks > 0 ? RacePhase.Countdown : RacePhase.Racing;
            UpdatePlaces();
        }

        /// <summary>Race clock in seconds: negative during the countdown, zero at "Go".</summary>
        public float RaceClock => (Sim.Tick - CountdownTicks) * BoatSimulator.TickDelta;

        /// <summary>Whole seconds left on the countdown (3, 2, 1), or 0 once racing.</summary>
        public int CountdownNumber => Phase == RacePhase.Countdown ? (int)System.MathF.Ceiling(-RaceClock) : 0;

        public int FinishedCount => _finishedCount;

        /// <summary>Advances the race one tick. Inputs of boats on the grid or respawning are ignored.</summary>
        public void Step(BoatInput[] inputs)
        {
            int n = Sim.BoatCount;
            for (int i = 0; i < n; i++)
            {
                LastEvents[i] = RaceEvents.None;
                _prevProgress[i] = Sim.State.Progress[i];
                _inputs[i] = Phase == RacePhase.Countdown || Status[i].IsRespawning ? default : inputs[i];
            }

            Sim.Step(_inputs);

            if (Phase == RacePhase.Countdown)
            {
                // Hold the grid: boats bob on the water but stay on their marks.
                for (int i = 0; i < n; i++)
                {
                    ref var b = ref Sim.State.Boats[i];
                    b.Position = new SimVec3(_gridPositions[i].X, b.Position.Y, _gridPositions[i].Z);
                    b.Velocity = new SimVec3(0f, b.Velocity.Y, 0f);
                    b.YawRate = 0f;
                }
                if (Sim.Tick >= CountdownTicks)
                {
                    Phase = RacePhase.Racing;
                    for (int i = 0; i < n; i++) LastEvents[i] |= RaceEvents.Go;
                }
                return;
            }

            float clock = RaceClock;
            for (int i = 0; i < n; i++)
            {
                ref var st = ref Status[i];
                if (st.IsRespawning)
                {
                    st.RespawnTimer = SimMath.Max(0f, st.RespawnTimer - BoatSimulator.TickDelta);
                    if (!st.IsRespawning) Release(i);
                    continue;
                }

                float progress = Sim.State.Progress[i];
                if (!st.Finished && progress >= Sim.Track.Length)
                {
                    float before = _prevProgress[i];
                    float u = progress > before ? SimMath.Clamp01((Sim.Track.Length - before) / (progress - before)) : 1f;
                    st.Finished = true;
                    st.FinishTime = clock - BoatSimulator.TickDelta * (1f - u);
                    st.FinishPlace = ++_finishedCount;
                    LastEvents[i] |= RaceEvents.Finished;
                    if (FirstFinishTick < 0) FirstFinishTick = Sim.Tick;
                }

                if (progress > st.BestProgress) st.BestProgress = progress;
                if (Phase == RacePhase.Racing && !st.Finished) CheckRespawn(i);
            }

            UpdatePlaces();

            if (Phase == RacePhase.Racing &&
                (_finishedCount == n || (FirstFinishTick >= 0 && Sim.Tick - FirstFinishTick >= FinishTimeoutTicks)))
                Phase = RacePhase.Finished;
        }

        /// <summary>Starts a respawn now (also used by the runtime for a "reset boat" button).</summary>
        public void BeginRespawn(int boat, RespawnReason reason)
        {
            ref var st = ref Status[boat];
            if (st.IsRespawning || st.Finished) return;
            st.RespawnTimer = SimMath.Max(Rules.RespawnHoldSeconds, BoatSimulator.TickDelta);
            st.LastRespawn = reason;
            st.Respawns++;
            st.StuckTimer = 0f;
            st.StrandedTimer = 0f;
            Sim.Parked[boat] = true;
            LastEvents[boat] |= RaceEvents.RespawnStarted;
        }

        /// <summary>The finishing order: finishers by time, then the rest by distance down the river.</summary>
        public RaceResult[] Results()
        {
            int n = Sim.BoatCount;
            var results = new RaceResult[n];
            for (int k = 0; k < n; k++)
            {
                int i = _placeOrder[k];
                results[k] = new RaceResult
                {
                    Boat = i,
                    Place = k + 1,
                    Finished = Status[i].Finished,
                    Time = Status[i].Finished ? Status[i].FinishTime : 0f,
                    Progress = Status[i].Finished ? Sim.Track.Length : Status[i].BestProgress,
                };
            }
            return results;
        }

        private void CheckRespawn(int i)
        {
            ref var st = ref Status[i];
            ref var b = ref Sim.State.Boats[i];
            float dt = BoatSimulator.TickDelta;

            var w = Sim.Water.Sample(b.Position.X, b.Position.Z, Sim.RaceTime);
            if (w.IsWet && b.Position.Y < w.SurfaceHeight - Rules.SunkDepth)
            {
                BeginRespawn(i, RespawnReason.Sunk);
                return;
            }

            st.StrandedTimer = !w.IsWet && b.WetFraction <= 0f ? st.StrandedTimer + dt : 0f;
            if (st.StrandedTimer >= Rules.StrandedSeconds)
            {
                BeginRespawn(i, RespawnReason.Stranded);
                return;
            }

            float progress = Sim.State.Progress[i];
            if (progress >= st.StuckMark + Rules.StuckDistance)
            {
                st.StuckMark = progress;
                st.StuckTimer = 0f;
            }
            else
            {
                st.StuckTimer += dt;
                if (st.StuckTimer >= Rules.StuckSeconds) BeginRespawn(i, RespawnReason.Stuck);
            }
        }

        /// <summary>Puts a boat back on the river at the nearest safe water to where it left, facing downstream.</summary>
        private void Release(int i)
        {
            ref var st = ref Status[i];
            var track = Sim.Track;
            float distance = SimMath.Max(0f, st.BestProgress - Rules.RespawnBackoff);
            var centre = track.PointAt(distance);
            var ahead = track.PointAt(distance + 2f);
            var dir = (ahead - centre).Flat.Normalized;
            if (dir.SqrMagnitude < 1e-6f) dir = SimVec3.Forward;
            var side = new SimVec3(dir.Z, 0f, -dir.X);

            var spot = centre;
            var water = WaterSample.Dry;
            float t = Sim.RaceTime;
            for (int k = 0; k < 16; k++)
            {
                // 0, +2, -2, +4, -4 ... metres across the river: the first clear, deep, calm patch wins.
                float offset = ((k + 1) / 2) * 2f * ((k & 1) == 0 ? 1f : -1f);
                var p = centre + side * offset;
                var w = Sim.Water.Sample(p.X, p.Z, t);
                const WaterFeature avoid = WaterFeature.HydraulicHole | WaterFeature.WaterfallLip | WaterFeature.Shallows;
                if (!w.IsWet || (w.Features & avoid) != 0 || w.Depth < Sim.Tunings[i].Draft + 0.2f) continue;
                spot = p;
                water = w;
                break;
            }

            var tuning = Sim.Tunings[i];
            var state = BoatState.At(new SimVec3(spot.X, water.IsWet ? water.SurfaceHeight : centre.Y, spot.Z), SimMath.Atan2(dir.X, dir.Z));
            state.Velocity = dir * (tuning.TopSpeed * Rules.RespawnSpeedFraction) + (water.IsWet ? water.Flow : SimVec3.Zero);
            state.ImmunityTime = tuning.HitImmunity;
            Sim.State.Boats[i] = state;
            Sim.State.Progress[i] = track.Project(state.Position, ref Sim.State.TrackHints[i], track.PointCount);
            Sim.Parked[i] = false;
            st.StuckMark = Sim.State.Progress[i];
            st.StuckTimer = 0f;
            st.StrandedTimer = 0f;
            LastEvents[i] |= RaceEvents.Respawned;
        }

        private void UpdatePlaces()
        {
            int n = Sim.BoatCount;
            for (int i = 0; i < n; i++) _placeOrder[i] = i;
            // Insertion sort, stable and allocation-free: finishers by finishing place, then by distance.
            for (int i = 1; i < n; i++)
            {
                int b = _placeOrder[i];
                int j = i - 1;
                while (j >= 0 && Ahead(b, _placeOrder[j]))
                {
                    _placeOrder[j + 1] = _placeOrder[j];
                    j--;
                }
                _placeOrder[j + 1] = b;
            }
            for (int k = 0; k < n; k++)
            {
                int i = _placeOrder[k];
                if (Status[i].Place != 0 && Status[i].Place != k + 1) LastEvents[i] |= RaceEvents.PlaceChanged;
                Status[i].Place = k + 1;
            }
        }

        private bool Ahead(int a, int b)
        {
            var sa = Status[a];
            var sb = Status[b];
            if (sa.Finished != sb.Finished) return sa.Finished;
            if (sa.Finished) return sa.FinishPlace < sb.FinishPlace;
            float pa = sa.IsRespawning ? sa.BestProgress : Sim.State.Progress[a];
            float pb = sb.IsRespawning ? sb.BestProgress : Sim.State.Progress[b];
            return pa > pb;
        }

        private static int SecondsToTicks(float seconds) => (int)System.MathF.Round(seconds * BoatSimulator.TickRate);
    }
}
