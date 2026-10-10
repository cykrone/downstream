using Downstream.Core.Math;
using Downstream.Core.Water;

namespace Downstream.Core.Boat
{
    /// <summary>Per-tick influences that come from other boats or items rather than the water.</summary>
    public struct BoatModifiers
    {
        /// <summary>True while sitting in a rival's wake slot inside a current lane (design: +8%).</summary>
        public bool InWakeSlot;

        /// <summary>Multiplier on the current felt by this boat (Dam Burst: 1.2 for places 5-8).</summary>
        public float FlowMultiplier;

        /// <summary>True while clipping the rough edge of a rival's wake (design: grip loss within 0.1 s).</summary>
        public bool OnWakeEdge;

        /// <summary>Multiplier on sideways grip from wakes (wake edge, or a Wake Blaster swamping the slot). 0 means 1.</summary>
        public float GripScale;

        public static BoatModifiers None => new BoatModifiers { FlowMultiplier = 1f, GripScale = 1f };
    }

    /// <summary>
    /// Optional world collision. The Unity runtime implements this with Physics.ComputePenetration
    /// against static colliders; the deterministic time-trial path will use a baked bank representation.
    /// Implementations must only read static geometry so rollback re-simulation stays valid.
    /// </summary>
    public interface IBoatCollider
    {
        /// <summary>
        /// Pushes the boat out of static geometry and removes (and partly reflects, by WallRestitution) velocity
        /// into the contact. Returns true on contact; <paramref name="impactSpeed"/> is the fastest closing speed
        /// into any contact this tick, so the sim can charge for a hard hit.
        /// </summary>
        bool Resolve(ref SimVec3 position, ref SimVec3 velocity, float yaw, in BoatTuning tuning, out float impactSpeed);
    }

    /// <summary>
    /// The arcade boat model from the design doc's "Boat handling model" section, as one pure step
    /// function: next state = f(state, input, water, race time). No allocation, no engine calls,
    /// no hidden state, so it can be rolled back and re-run for netcode and leaderboard checks.
    /// </summary>
    public static class BoatSimulator
    {
        public const int PontoonCount = 8;
        public const float TickRate = 120f;
        public const float TickDelta = 1f / TickRate;

        private const float HopIgnoreAirSeconds = 0.05f;
        private const float MaxHopSeconds = 1f;

        /// <summary>Boat-local pontoon offset i (0..7): two rows of four along the hull.</summary>
        public static SimVec3 PontoonLocal(in BoatTuning t, int i)
        {
            float x = (i & 1) == 0 ? -t.HullBeam * 0.5f : t.HullBeam * 0.5f;
            int row = i >> 1; // 0 = bow ... 3 = stern
            float z = t.HullLength * (0.5f - row / 3f);
            return new SimVec3(x, t.PontoonY, z);
        }

        public static BoatEvents Step(ref BoatState s, in BoatInput rawInput, in BoatTuning t, IWaterQuery water,
            float raceTime, in BoatModifiers mods, IBoatCollider collider = null, float dt = TickDelta)
        {
            var events = BoatEvents.None;
            var input = rawInput;
            bool spinning = s.SpinTime > 0f;
            if (spinning)
            {
                input.Throttle = 0f;
                input.Steer = 0f;
                input.HopDrift = false;
            }
            bool hopPressed = input.HopDrift && !s.PrevHopDrift;

            // ---- Sample the water under each pontoon --------------------------------------------
            float buoyAccelY = 0f, pitchTorque = 0f, rollTorque = 0f;
            int wetCount = 0;
            SimVec3 flowSum = SimVec3.Zero, normalSum = SimVec3.Zero;
            float depthSum = 0f;
            bool inLane = false, inEddy = false, onCrest = false;
            int holeCount = 0;
            float pitchRateEff = s.PitchRate, rollRateEff = s.RollRate;
            for (int i = 0; i < PontoonCount; i++)
            {
                var local = PontoonLocal(t, i);
                var offset = BoatFrame.Rotate(s.Yaw, s.Pitch, s.Roll, local);
                var p = s.Position + offset;
                var w = water.Sample(p.X, p.Z, raceTime);
                if (!w.IsWet) continue;
                float submersion = w.SurfaceHeight - p.Y;
                if (submersion <= 0f) continue;

                wetCount++;
                flowSum += w.Flow;
                normalSum += w.Normal;
                depthSum += w.Depth;
                if ((w.Features & WaterFeature.CurrentLane) != 0) inLane = true;
                if ((w.Features & WaterFeature.Eddy) != 0) inEddy = true;
                if ((w.Features & WaterFeature.Crest) != 0) onCrest = true;
                if ((w.Features & WaterFeature.HydraulicHole) != 0) holeCount++;

                float d = SimMath.Min(submersion, t.MaxSubmersion);
                // Vertical speed of this pontoon from linear and angular motion (small-angle).
                float vy = s.Velocity.Y + pitchRateEff * local.Z - rollRateEff * local.X;
                float a = (t.BuoyancyStiffness * d - t.BuoyancyDamping * vy) / PontoonCount;
                if (a < 0f) a *= 0.5f; // water resists pulling out less than pushing in
                buoyAccelY += a;
                pitchTorque += a * local.Z;
                rollTorque -= a * local.X;
            }

            float wetFraction = wetCount / (float)PontoonCount;
            bool wet = wetCount > 0;
            SimVec3 waterFlow = wet ? flowSum / wetCount : SimVec3.Zero;
            waterFlow *= mods.FlowMultiplier <= 0f ? 1f : mods.FlowMultiplier;
            SimVec3 waterNormal = wet ? normalSum.Normalized : SimVec3.Up;
            float waterDepth = wet ? depthSum / wetCount : 0f;

            // ---- Airborne state and landing --------------------------------------------------------
            bool wasDry = s.DryTime > 0f;
            if (wet)
            {
                if (wasDry) s.HopTime = 0f; // back on the water after a hop
                if (s.Airborne)
                {
                    var landing = ClassifyLanding(s, waterNormal, t);
                    s.LastLanding = landing;
                    switch (landing)
                    {
                        case LandingResult.Clean:
                            s.BoostTime = SimMath.Max(s.BoostTime, t.CleanLandBoost);
                            events |= BoatEvents.LandedClean | BoatEvents.BoostStarted;
                            break;
                        case LandingResult.Slap:
                            s.SlapTime = t.SlapDuration;
                            s.Velocity = new SimVec3(s.Velocity.X * t.SlapSpeedFactor, s.Velocity.Y, s.Velocity.Z * t.SlapSpeedFactor);
                            events |= BoatEvents.LandedSlap;
                            break;
                        default:
                            events |= BoatEvents.LandedNeutral;
                            break;
                    }
                }
                s.Airborne = false;
                s.DryTime = 0f;
            }
            else
            {
                s.DryTime += dt;
                if (!s.Airborne && s.DryTime >= t.AirborneDelay)
                {
                    s.Airborne = true;
                    events |= BoatEvents.BecameAirborne;
                }
            }

            // ---- Timers ------------------------------------------------------------------------------
            if (s.BoostTime > 0f) s.BoostTime = SimMath.Max(0f, s.BoostTime - dt);
            if (s.SlapTime > 0f) s.SlapTime = SimMath.Max(0f, s.SlapTime - dt);
            if (s.SpinTime > 0f)
            {
                s.SpinTime = SimMath.Max(0f, s.SpinTime - dt);
                if (s.SpinTime == 0f) s.ImmunityTime = t.HitImmunity;
            }
            else if (s.ImmunityTime > 0f)
            {
                s.ImmunityTime = SimMath.Max(0f, s.ImmunityTime - dt);
            }

            // ---- Hydraulic hole: grabs the boat for up to HoleMaxHold, a hop breaks out early -------
            // The clock runs from the grab while any part of the hull is still in the boil, so
            // rocking at the hole's edge never extends the hold past HoleMaxHold.
            bool held = false;
            if (holeCount == 0)
            {
                s.HoleTime = 0f;
                s.HoleSpent = false;
            }
            else if (!s.HoleSpent && (s.HoleTime > 0f || holeCount * 2 >= PontoonCount))
            {
                if (s.HoleTime <= 0f) events |= BoatEvents.HoleGrabbed;
                s.HoleTime += dt;
                held = holeCount * 2 >= PontoonCount;
                if (s.HoleTime >= t.HoleMaxHold - 1e-4f || (hopPressed && held))
                {
                    s.HoleSpent = true;
                    held = false;
                    events |= BoatEvents.HoleReleased;
                }
            }

            if (inEddy && !s.InEddy) events |= BoatEvents.EnteredEddy;
            if (mods.OnWakeEdge && wet) events |= BoatEvents.WakeEdge;

            // ---- Hop and drift -------------------------------------------------------------------
            if (s.HopTime > 0f)
            {
                s.HopTime += dt;
                if (s.HopTime > MaxHopSeconds) s.HopTime = 0f;
            }
            if (hopPressed && wet && s.DryTime < HopIgnoreAirSeconds)
            {
                s.HopTime = dt;
                // Hopping off a crest launches higher: air, a trick and a landing boost.
                float hop = onCrest ? t.HopSpeed * t.CrestHopScale : t.HopSpeed;
                s.Velocity = new SimVec3(s.Velocity.X, SimMath.Max(s.Velocity.Y, 0f) + hop, s.Velocity.Z);
                events |= BoatEvents.Hopped;
            }

            var fwd = s.FlatForward;
            var right = s.FlatRight;
            var relative = (s.Velocity - waterFlow).Flat;
            float vf = SimVec3.Dot(relative, fwd);
            float vl = SimVec3.Dot(relative, right);

            if (s.DriftDirection == 0)
            {
                // Kart-style: the press hops, and the drift (and its charge) begins once the hull is back on the water.
                if (input.HopDrift && wet && s.HopTime <= 0f && !spinning && vf >= t.DriftMinSpeed && SimMath.Abs(input.Steer) >= t.DriftSteerThreshold)
                {
                    s.DriftDirection = (sbyte)(input.Steer > 0f ? 1 : -1);
                    s.DriftTime = 0f;
                    s.DriftTier = 0;
                    events |= BoatEvents.DriftStarted;
                }
            }
            else
            {
                bool release = !input.HopDrift;
                bool cancel = spinning || vf < t.DriftMinSpeed * 0.75f;
                if (release || cancel)
                {
                    if (release && !cancel && s.DriftTier > 0)
                    {
                        float boost = s.DriftTier == 1 ? t.DriftBoost1 : (s.DriftTier == 2 ? t.DriftBoost2 : t.DriftBoost3);
                        s.BoostTime = SimMath.Max(s.BoostTime, boost);
                        events |= BoatEvents.BoostStarted;
                    }
                    s.DriftDirection = 0;
                    s.DriftTime = 0f;
                    s.DriftTier = 0;
                    events |= BoatEvents.DriftReleased;
                }
                else if (wet)
                {
                    s.DriftTime += dt * (inLane ? t.DriftLaneChargeScale : 1f);
                    byte tier = s.DriftTime >= t.DriftTier3 ? (byte)3 : s.DriftTime >= t.DriftTier2 ? (byte)2 : s.DriftTime >= t.DriftTier1 ? (byte)1 : (byte)0;
                    if (tier > s.DriftTier)
                    {
                        s.DriftTier = tier;
                        events |= BoatEvents.DriftTierUp;
                    }
                }
            }

            // ---- Draft (wake slot) ---------------------------------------------------------------
            if (mods.InWakeSlot && wet)
            {
                s.DraftCharge += dt;
                if (s.DraftCharge >= t.DraftChargeSeconds)
                {
                    s.DraftCharge = 0f;
                    s.BoostTime = SimMath.Max(s.BoostTime, t.DraftBoost);
                    events |= BoatEvents.BoostStarted;
                }
            }
            else
            {
                s.DraftCharge = SimMath.Max(0f, s.DraftCharge - dt);
            }

            // ---- Horizontal forces (relative to the water) -----------------------------------------
            SimVec3 accel = SimVec3.Zero;
            if (wet)
            {
                bool boosting = s.BoostTime > 0f;
                float top = boosting ? t.BoostTopSpeed : t.TopSpeed;
                if (mods.InWakeSlot) top *= t.DraftSpeedFactor;
                if (s.SlapTime > 0f) top *= t.SlapSpeedFactor;

                float target = input.Throttle >= 0f ? input.Throttle * top : input.Throttle * t.ReverseSpeed;
                if (boosting) target = top;
                float rate;
                if (boosting) rate = t.BoostAccelRate;
                else if (vf < target) rate = t.AccelRate;
                else if (onCrest && input.Throttle >= 0f) rate = 0f; // surfing the face holds speed without throttle
                else rate = input.Throttle < 0f ? t.BrakeRate : t.CoastRate;
                float af = (target - vf) * rate;

                if (waterDepth < t.Draft)
                {
                    af -= vf * t.ShallowDrag * (1f - waterDepth / t.Draft);
                    events |= BoatEvents.Grounding;
                }

                // Keel turning scrubs speed: the faster you turn without drifting, the more you give up.
                if (s.DriftDirection == 0 && !spinning) af -= SimMath.Abs(s.YawRate) * SimMath.Max(vf, 0f) * t.TurnScrub;
                // Eddies are slack water: a hull that wanders in loses way.
                if (inEddy) af -= vf * t.EddyDrag;

                float grip = s.DriftDirection != 0 || spinning ? t.DriftGrip : t.KeelGrip;
                if (mods.GripScale > 0f) grip *= mods.GripScale;
                float al = -vl * grip * t.GripRate;

                accel += (fwd * af + right * al) * wetFraction;

                if (held)
                {
                    // The recirculation stalls the hull in place: no thrust, ground speed pulled to zero.
                    var ground = s.Velocity.Flat;
                    accel = ground * -t.HoleGrip;
                }

                // Gravity pulls boats down sloped water (rapids, the face of a standing wave).
                accel += new SimVec3(waterNormal.X, 0f, waterNormal.Z) * (t.Gravity * t.SlopeAssist);
            }

            // ---- Vertical --------------------------------------------------------------------------
            float gravity = t.Gravity * (s.Airborne ? t.AirGravityScale : 1f);
            accel += new SimVec3(0f, buoyAccelY - gravity, 0f);

            // ---- Yaw -----------------------------------------------------------------------------
            float speedFactor = SimMath.Clamp01(SimMath.Abs(vf) / 6f);
            float targetYawRate;
            if (spinning)
            {
                targetYawRate = 2f * SimMath.Pi / t.SpinOutDuration;
                s.YawRate = targetYawRate;
            }
            else
            {
                if (s.DriftDirection != 0)
                    targetYawRate = s.DriftDirection * t.TurnRate * (0.8f + 0.45f * input.Steer * s.DriftDirection);
                else
                {
                    // Keel turning tightens at low speed and loosens at high speed; the drift is the fast way round.
                    float falloff = t.TurnSpeedFalloff > 0f ? 1f / (1f + SimMath.Max(vf, 0f) / t.TurnSpeedFalloff) : 1f;
                    targetYawRate = input.Steer * t.TurnRate * speedFactor * falloff * (vf < 0f ? -1f : 1f);
                }
                if (!wet) targetYawRate *= t.AirSteerFactor;
                else if (inEddy) targetYawRate *= t.EddyTurnScale; // the eddy line swings the hull round
                s.YawRate = SimMath.MoveTowards(s.YawRate, targetYawRate, t.YawResponse * t.TurnRate * dt);
            }

            // ---- Pitch and roll ------------------------------------------------------------------
            float pitchAccel = pitchTorque / t.PitchInertia - s.PitchRate * t.AngularDamping;
            float rollAccel = rollTorque / t.RollInertia - s.RollRate * t.AngularDamping;
            if (s.Airborne)
            {
                float targetPitchRate = input.Pitch * t.AirPitchRate;
                s.PitchRate = SimMath.MoveTowards(s.PitchRate, targetPitchRate, 12f * dt);
                rollAccel = -s.Roll * 6f - s.RollRate * 3f;
                pitchAccel = 0f;
            }
            if (wet && s.DriftDirection != 0)
                rollAccel += s.DriftDirection * 2.5f; // lean into the drift (right side down when drifting right)

            // ---- Integrate (semi-implicit Euler) --------------------------------------------------
            s.Velocity += accel * dt;
            s.PitchRate += pitchAccel * dt;
            s.RollRate += rollAccel * dt;
            s.Position += s.Velocity * dt;
            s.Yaw = SimMath.WrapAngle(s.Yaw + s.YawRate * dt);
            s.Pitch += s.PitchRate * dt;
            s.Roll += s.RollRate * dt;

            float limit = s.Airborne ? t.MaxPitchRollAir : t.MaxPitchRollWater;
            ClampAngle(ref s.Pitch, ref s.PitchRate, limit);
            ClampAngle(ref s.Roll, ref s.RollRate, limit);

            if (collider != null && collider.Resolve(ref s.Position, ref s.Velocity, s.Yaw, t, out float impact) && impact > t.WallHitSpeed)
            {
                // A hard hit on a bank or rock: speed comes off, the hull wallows for a moment, and the
                // nose is knocked away from the contact so a head-on stop never pins the boat.
                float k = SimMath.Clamp01((impact - t.WallHitSpeed) / 10f);
                float keep = 1f - t.WallHitSpeedLoss * (0.4f + 0.6f * k);
                s.Velocity = new SimVec3(s.Velocity.X * keep, s.Velocity.Y, s.Velocity.Z * keep);
                s.SlapTime = SimMath.Max(s.SlapTime, t.WallHitSlowSeconds * (0.5f + 0.5f * k));
                if (s.DriftDirection != 0) { s.DriftDirection = 0; s.DriftTier = 0; s.DriftTime = 0f; events |= BoatEvents.DriftReleased; }
                events |= BoatEvents.HitWall;
            }

            s.WetFraction = wetFraction;
            s.InCurrentLane = inLane;
            s.InEddy = inEddy && wet;
            s.OnCrest = onCrest && wet;
            s.PrevHopDrift = rawInput.HopDrift;
            return events;
        }

        /// <summary>Starts a spin-out from an item hit unless the boat is immune. Returns true if the hit landed.</summary>
        public static bool ApplyHit(ref BoatState s, in BoatTuning t)
        {
            if (s.SpinTime > 0f || s.ImmunityTime > 0f) return false;
            s.SpinTime = t.SpinOutDuration;
            s.DriftDirection = 0;
            s.DriftTier = 0;
            s.DriftTime = 0f;
            s.BoostTime = 0f;
            return true;
        }

        /// <summary>
        /// Design rule: angle between the hull and the water it lands on. Bow first at 10-40 degrees
        /// nose-down is clean; flatter than 5 degrees or stern first is a slap; anything else is neutral.
        /// </summary>
        public static LandingResult ClassifyLanding(in BoatState s, SimVec3 waterNormal, in BoatTuning t)
        {
            var fwd = s.FlatForward;
            // Pitch of the water surface along the heading (positive when the surface rises ahead).
            float surfacePitch = SimMath.Atan2(-(waterNormal.X * fwd.X + waterNormal.Z * fwd.Z), waterNormal.Y);
            float noseDownDeg = -(s.Pitch - surfacePitch) * SimMath.Rad2Deg;
            if (noseDownDeg >= t.CleanLandMinDeg && noseDownDeg <= t.CleanLandMaxDeg) return LandingResult.Clean;
            if (noseDownDeg < t.SlapBelowDeg) return LandingResult.Slap;
            return LandingResult.Neutral;
        }

        private static void ClampAngle(ref float angle, ref float rate, float limit)
        {
            if (angle > limit)
            {
                angle = limit;
                if (rate > 0f) rate = 0f;
            }
            else if (angle < -limit)
            {
                angle = -limit;
                if (rate < 0f) rate = 0f;
            }
        }
    }
}
