using Downstream.Core.Boat;
using Downstream.Core.Items;
using Downstream.Core.Math;
using Downstream.Core.Sim;
using Downstream.Core.Water;

namespace Downstream.Core.AI
{
    /// <summary>
    /// The race AI from the design. Every 0.5 s it scores the track's racing lines (line speed, lane
    /// occupancy, items held, personality), follows the chosen one with a look-ahead controller, reads
    /// the gameplay water at 3 points ahead to steer round rocks and boof holes, drifts bends up to its
    /// difficulty's tier, and spends a seeded mistake budget. Like every AI here it only produces a
    /// <see cref="BoatInput"/>, so it can do nothing a player cannot. One instance per AI boat per race.
    /// </summary>
    public sealed class RacerAI
    {
        public const int DecisionIntervalTicks = 60;
        public const float DecisionWindow = 150f;
        public const float NoiseWavelength = 40f;

        private const float LookAheadSeconds = 1.1f;
        private const float MinLookAhead = 10f;
        private const float SteerGain = 2.2f;
        private const float YawDamping = 0.35f;
        private const float TargetNoseDownDeg = 20f;

        private readonly RacingLineSet _lines;
        private readonly BoatTuning _tuning;
        private readonly int _boat;
        private readonly ulong _seed;
        private readonly ScheduledMistake[] _mistakes;
        private readonly bool[] _shortcutTaken;
        private readonly float[] _correctionDelay;
        private readonly bool[] _hopDelay;

        private int _hint;
        private RacingLine _line;
        private RacingLine _pending;
        private int _pendingTick;
        private int _nextMistake;
        private MistakeKind _active;
        private int _activeUntil = -1;
        private int _missDriftArmed;
        private int _slapArmed;
        private bool _slapping;
        private bool _hopHeld;
        private bool _driftCounted;
        private int _armedUntil;
        private float _overcorrectSign = 1f;
        private float _lateral;

        private int _driftHeldTicks;
        private int _driftTarget;
        private int _driftCooldownUntil;

        public RivalProfile Profile { get; }
        public AIDifficulty Difficulty { get; }
        public RacingLine Line => _line;
        public float Progress { get; private set; }
        public int MistakeBudgetCount => _mistakes.Length;
        public int MistakesMade { get; private set; }
        /// <summary>Mistakes not yet made: still ahead on the track, or waiting for a drift or a jump.</summary>
        public int MistakesPending => _mistakes.Length - _nextMistake + _missDriftArmed + _slapArmed;
        public int LineChanges { get; private set; }
        public int DeepestDriftTier { get; private set; }
        public int DriftAttempts { get; private set; }
        public int Drifts { get; private set; }

        public RacerAI(RacingLineSet lines, in BoatTuning tuning, RivalProfile profile, in AIDifficulty difficulty, int boat, ulong raceSeed)
        {
            _lines = lines;
            _tuning = tuning;
            _boat = boat;
            Profile = profile;
            Difficulty = difficulty;
            _seed = raceSeed * 0x9E3779B97F4A7C15ul + (ulong)(boat + 1) * 0xBF58476D1CE4E5B9ul;
            _mistakes = MistakeBudget.Create(_seed, difficulty, lines.Track.Length);

            // Shortcuts are decided once at race start, like the mistake budget.
            var rng = new SimRandom(_seed, 0x5C07u);
            var shortcut = lines.Get(RacingLineKind.Shortcut);
            int sections = shortcut != null ? shortcut.Shortcuts.Count : 0;
            _shortcutTaken = new bool[sections];
            float chance = SimMath.Min(1f, difficulty.ShortcutUse + (profile.Has(RivalTraits.ShortcutHunter) ? 0.3f : 0f));
            for (int s = 0; s < sections; s++) _shortcutTaken[s] = rng.NextFloat() < chance;

            int delay = SimMath.Max(0, difficulty.ReactionTicks);
            _correctionDelay = new float[delay + 1];
            _hopDelay = new bool[delay + 1];
            _line = lines.Get(RacingLineKind.Safe);
        }

        /// <summary>The input for this boat this tick, reading the race as it stands.</summary>
        public BoatInput Think(RaceSimulation sim, ItemType held = ItemType.None)
        {
            // Start the track search where the race last found this boat, so a respawn's teleport is followed.
            _hint = sim.State.TrackHints[_boat];
            return Think(sim.State.Boats[_boat], sim.Tick, sim.State.Boats, sim.State.Progress, sim.Parked, held);
        }

        /// <param name="boats">Every boat in the race (may be null for a lone boat).</param>
        /// <param name="progress">Distance along the track of every boat, matching <paramref name="boats"/>.</param>
        public BoatInput Think(in BoatState me, int tick, BoatState[] boats = null, float[] progress = null, bool[] parked = null, ItemType held = ItemType.None)
        {
            var track = _lines.Track;
            float p = track.Project(me.Position, ref _hint);
            Progress = p;
            _lateral = _lines.LateralOf(me.Position, p);
            float speed = me.Velocity.Flat.Magnitude;

            while (_nextMistake < _mistakes.Length && p >= _mistakes[_nextMistake].Distance)
                Begin(_mistakes[_nextMistake++].Kind, tick);
            ExpireArmed(tick);

            bool forcedWrong = _activeUntil >= tick && _active == MistakeKind.WrongChannel;
            if (forcedWrong) _line = Worst(p);
            else if ((tick + _boat * 7) % DecisionIntervalTicks == 0) Decide(me, p, tick, boats, progress, parked, held);
            if (_pending != null && tick >= _pendingTick)
            {
                if (_pending != _line) LineChanges++;
                _line = _pending;
                _pending = null;
            }

            // ---- Where to aim ----------------------------------------------------------------------
            float ahead = SimMath.Max(MinLookAhead, speed * LookAheadSeconds);
            ReadWater(p, ahead, speed, tick, out float correction, out bool hop);
            float aim = p + ahead;
            float lateral = _line.LateralAt(aim) + Noise(aim) + correction + Lean(me, p, boats, progress, parked);
            var target = _lines.PointAt(aim, lateral);
            var to = (target - me.Position).Flat;
            float error = SimMath.WrapAngle(SimMath.Atan2(to.X, to.Z) - me.Yaw);

            var input = new BoatInput
            {
                Throttle = 1f,
                Steer = SimMath.Clamp(error * SteerGain - me.YawRate * YawDamping, -1f, 1f),
            };

            Drift(me, p, ahead, speed, error, tick, ref input);
            // A boof is one press; holding the button would start a drift on landing.
            if (hop && !me.IsDrifting && !_hopHeld) input.HopDrift = true;
            _hopHeld = hop;

            // Aim for a clean, bow-first landing unless a slap is due.
            if (me.Airborne)
            {
                input.Pitch = SimMath.Clamp((-TargetNoseDownDeg * SimMath.Deg2Rad - me.Pitch) * 3f, -1f, 1f);
                if (!_slapping && _slapArmed > 0)
                {
                    _slapping = true;
                    _slapArmed--;
                    MistakesMade++;
                }
                if (_slapping) input.Pitch = 1f; // nose up: lands flat
            }
            else
            {
                _slapping = false;
            }

            if (_activeUntil >= tick)
            {
                if (_active == MistakeKind.ThrottleLift) input.Throttle = 0.3f;
                else if (_active == MistakeKind.Overcorrect) input.Steer = _overcorrectSign;
            }
            return input;
        }

        // ---- Line choice -----------------------------------------------------------------------------

        private void Decide(in BoatState me, float p, int tick, BoatState[] boats, float[] progress, bool[] parked, ItemType held)
        {
            float myLateral = _lines.LateralOf(me.Position, p);
            RacingLine best = _line;
            float bestScore = float.MinValue;
            foreach (var line in _lines.Lines)
            {
                if (line.Kind == RacingLineKind.Shortcut && !ShortcutAllowed(line, p)) continue;
                float score = Score(line, p, myLateral, boats, progress, parked, held);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = line;
                }
            }
            if (best == _line) _pending = null;
            else if (best != _pending)
            {
                _pending = best;
                _pendingTick = tick + Difficulty.ReactionTicks;
            }
        }

        private float Score(RacingLine line, float p, float myLateral, BoatState[] boats, float[] progress, bool[] parked, ItemType held)
        {
            // Seconds matter most: 0.1 s over the next 150 m is worth a point.
            float score = -line.EstimatedTime(p, p + DecisionWindow) * 10f;
            score -= SimMath.Abs(line.LateralAt(p + 15f) - myLateral) * 0.1f;
            if (line == _line) score += 0.4f;

            switch (line.Kind)
            {
                case RacingLineKind.Safe: if (Profile.Has(RivalTraits.Steady)) score += 0.8f; break;
                case RacingLineKind.CurrentLane: if (Profile.Has(RivalTraits.CurrentReader)) score += 0.8f; break;
                case RacingLineKind.Aggressive:
                case RacingLineKind.Shortcut: if (Profile.Has(RivalTraits.Daredevil)) score += 0.8f; break;
            }
            // A boost is worth most on the fastest line.
            if (ItemDistribution.IsBoost(held) && line.Kind != RacingLineKind.Safe) score += 0.4f;

            if (boats == null || progress == null) return score;
            for (int j = 0; j < boats.Length; j++)
            {
                if (j == _boat || (parked != null && parked[j])) continue;
                float gap = progress[j] - p;
                if (gap < 0f || gap > 30f) continue;
                float off = SimMath.Abs(_lines.LateralOf(boats[j].Position, progress[j]) - line.LateralAt(progress[j]));
                if (off > 3f) continue;
                if (Profile.Has(RivalTraits.Drafter) && gap > RaceSimulation.WakeSlotMinDistance && gap < RaceSimulation.WakeSlotMaxDistance) score += 1f;
                else if (Profile.Has(RivalTraits.LineThief) && gap > 5f) score += 1.2f;
                else if (attacking(held)) score += 0.5f;
                else score -= 1.5f;
            }
            return score;

            static bool attacking(ItemType item) => item == ItemType.Pike || item == ItemType.TriplePike || item == ItemType.Whirl;
        }

        private bool ShortcutAllowed(RacingLine shortcut, float p)
        {
            // The decision is about the next fork: allowed if this boat rolled to take it.
            for (int s = 0; s < shortcut.Shortcuts.Count; s++)
                if (shortcut.Shortcuts[s].End > p) return _shortcutTaken[s];
            return false;
        }

        private RacingLine Worst(float p)
        {
            RacingLine worst = _line;
            float slowest = float.MinValue;
            foreach (var line in _lines.Lines)
            {
                float t = line.EstimatedTime(p, p + DecisionWindow);
                if (t > slowest)
                {
                    slowest = t;
                    worst = line;
                }
            }
            return worst;
        }

        // ---- Reading the water --------------------------------------------------------------------------

        /// <summary>
        /// Samples the water on the line at 3 points ahead. A blocked point (rock, bank, shallows or a
        /// hydraulic hole) produces a sideways correction to the nearest clear water; a hole right ahead
        /// with no way round is boofed with a hop. Both reach the controls after the reaction delay.
        /// </summary>
        private void ReadWater(float p, float ahead, float speed, int tick, out float correction, out bool hop)
        {
            float found = 0f;
            bool hopNow = false;
            for (int k = 1; k <= 3; k++)
            {
                float d = p + ahead * 0.4f * k;
                float lat = _line.LateralAt(d) + Noise(d);
                if (Clear(d, lat, out bool hole)) continue;
                bool fixedIt = false;
                for (int step = 1; step <= 5 && !fixedIt; step++)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float o = side * 2f * step;
                        if (Clear(d, lat + o, out _))
                        {
                            found = o;
                            fixedIt = true;
                            break;
                        }
                    }
                }
                if (!fixedIt && hole && k == 1 && speed > 3f) hopNow = true;
                break; // the nearest blocked point decides
            }

            int n = _correctionDelay.Length;
            _correctionDelay[tick % n] = found;
            _hopDelay[tick % n] = hopNow;
            correction = _correctionDelay[(tick + 1) % n];
            hop = _hopDelay[(tick + 1) % n];
        }

        private bool Clear(float d, float lateral, out bool hole)
        {
            var pt = _lines.PointAt(d, lateral);
            var w = SampleWater(pt);
            hole = w.IsWet && (w.Features & WaterFeature.HydraulicHole) != 0;
            return w.IsWet && w.Depth >= _lines.Settings.MinDepth && !hole;
        }

        private WaterSample SampleWater(SimVec3 p) => _lines.Water.Sample(p.X, p.Z, _lines.Settings.SampleTime);

        // ---- Drifting -----------------------------------------------------------------------------------

        private void Drift(in BoatState me, float p, float ahead, float speed, float error, int tick, ref BoatInput input)
        {
            if (Difficulty.MaxDriftTier <= 0) return;
            if (me.DriftTier > DeepestDriftTier) DeepestDriftTier = me.DriftTier;

            if (_driftHeldTicks > 0)
            {
                _driftHeldTicks++;
                bool release;
                if (!me.IsDrifting) release = _driftHeldTicks > 72; // the hop landed without a drift starting: give up
                else
                {
                    if (!_driftCounted) { _driftCounted = true; Drifts++; }
                    float need = BendRate(p + ahead * 0.5f, speed) * me.DriftDirection;
                    bool bendOver = need < 0.25f * _tuning.TurnRate;
                    bool against = SimMath.Sign(error) == -me.DriftDirection && SimMath.Abs(error) > 10f * SimMath.Deg2Rad;
                    // Hold through the end of a bend for a tier if the boat is not swinging past the line.
                    release = against || (bendOver && me.DriftTier > 0) || me.DriftTier >= _driftTarget || me.DriftTime > _tuning.DriftTier3 + 0.5f;
                }
                if (release)
                {
                    _driftHeldTicks = 0;
                    _driftCounted = false;
                    _driftCooldownUntil = tick + 90;
                    return;
                }
                input.HopDrift = true;
                return;
            }

            if (tick < _driftCooldownUntil || me.Airborne || me.InHole || speed < _tuning.DriftMinSpeed * 1.15f) return;
            // A bend worth drifting: it asks for at least the drift's slowest turn rate for long enough to charge tier 1.
            float first = BendRate(p + 5f, speed);
            float dir = SimMath.Sign(first);
            if (dir == 0f || (SimMath.Sign(error) == -dir && SimMath.Abs(error) > 5f * SimMath.Deg2Rad)) return;
            if (first * dir < 0.45f * _tuning.TurnRate) return;
            float span = speed * (_tuning.DriftTier1 + 0.2f);
            for (float d = 5f; d <= span; d += 5f)
                if (BendRate(p + d, speed) * dir < 0.35f * _tuning.TurnRate) return;

            _driftTarget = Difficulty.MaxDriftTier;
            if (_missDriftArmed > 0)
            {
                _driftTarget = SimMath.Max(0, _driftTarget - 1);
                _missDriftArmed--;
                MistakesMade++;
            }
            _driftHeldTicks = 1;
            DriftAttempts++;
            input.HopDrift = true;
            input.Steer = dir * SimMath.Max(SimMath.Abs(input.Steer), _tuning.DriftSteerThreshold + 0.05f);
        }

        /// <summary>Signed yaw rate the river asks for at a track distance and speed, rad/s (positive = right).</summary>
        private float BendRate(float d, float speed) => _lines.CurvatureAt(d) * speed;

        // ---- Personality, noise and mistakes ----------------------------------------------------------

        /// <summary>A Bully leans into a boat alongside; nobody else does.</summary>
        private float Lean(in BoatState me, float p, BoatState[] boats, float[] progress, bool[] parked)
        {
            if (!Profile.Has(RivalTraits.Bully) || boats == null || progress == null) return 0f;
            float myLat = _lines.LateralOf(me.Position, p);
            for (int j = 0; j < boats.Length; j++)
            {
                if (j == _boat || (parked != null && parked[j])) continue;
                if (SimMath.Abs(progress[j] - p) > 5f) continue;
                float side = _lines.LateralOf(boats[j].Position, progress[j]) - myLat;
                if (SimMath.Abs(side) < 6f && SimMath.Abs(side) > 0.5f) return SimMath.Sign(side) * 1.5f;
            }
            return 0f;
        }

        /// <summary>Smooth, seeded wander off the line, within the difficulty's noise amplitude.</summary>
        private float Noise(float d)
        {
            if (Difficulty.LineNoise <= 0f) return 0f;
            float f = d / NoiseWavelength;
            int k = SimMath.FloorToInt(f);
            float u = f - k;
            u = u * u * (3f - 2f * u);
            return SimMath.Lerp(Knot(k), Knot(k + 1), u) * Difficulty.LineNoise;
        }

        private float Knot(int k)
        {
            var rng = new SimRandom(_seed ^ ((ulong)(uint)k * 0xD6E8FEB86659FD93ul), 0x401Eu);
            return rng.Range(-1f, 1f);
        }

        private void Begin(MistakeKind kind, int tick)
        {
            switch (kind)
            {
                case MistakeKind.MissedDriftTier when Difficulty.MaxDriftTier > 0:
                    _missDriftArmed++;
                    _armedUntil = tick + 5 * 120;
                    return;
                case MistakeKind.SlappedLanding:
                    _slapArmed++;
                    _armedUntil = tick + 5 * 120;
                    return;
                case MistakeKind.WrongChannel when _lines.Lines.Count > 1:
                    Act(kind, tick, 4f);
                    return;
                case MistakeKind.ThrottleLift:
                    Act(kind, tick, 0.6f);
                    return;
                default:
                    Act(MistakeKind.Overcorrect, tick, 0.3f);
                    return;
            }
        }

        private void Act(MistakeKind kind, int tick, float seconds)
        {
            _active = kind;
            _activeUntil = tick + (int)(seconds * BoatSimulator.TickRate);
            // The twitch goes towards the open water side: a mistake costs time, not a beached boat.
            _overcorrectSign = _lateral > 0.5f ? -0.6f : (_lateral < -0.5f ? 0.6f : (((tick + _boat) & 1) == 0 ? 0.6f : -0.6f));
            MistakesMade++;
        }

        /// <summary>A mistake that waits for its moment (a drift, a jump) falls back to an over-correction if the moment never comes.</summary>
        private void ExpireArmed(int tick)
        {
            if (tick <= _armedUntil) return;
            for (; _missDriftArmed > 0; _missDriftArmed--) Act(MistakeKind.Overcorrect, tick, 0.3f);
            for (; _slapArmed > 0; _slapArmed--) Act(MistakeKind.Overcorrect, tick, 0.3f);
        }
    }
}
