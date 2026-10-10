using System;
using Downstream.Core.Math;

namespace Downstream.Core.Boat
{
    public enum LandingResult : byte
    {
        None,
        Clean,
        Slap,
        Neutral,
    }

    /// <summary>Things that happened during one tick, for audio, VFX, HUD and telemetry. Never fed back into the sim.</summary>
    [Flags]
    public enum BoatEvents : uint
    {
        None = 0,
        Hopped = 1 << 0,
        BecameAirborne = 1 << 1,
        LandedClean = 1 << 2,
        LandedSlap = 1 << 3,
        LandedNeutral = 1 << 4,
        DriftStarted = 1 << 5,
        DriftTierUp = 1 << 6,
        DriftReleased = 1 << 7,
        BoostStarted = 1 << 8,
        SpinOutStarted = 1 << 9,
        Grounding = 1 << 10,
        HoleGrabbed = 1 << 11,
        HoleReleased = 1 << 12,
        WakeEdge = 1 << 13,
        BoatBump = 1 << 14,
        EnteredEddy = 1 << 15,
        /// <summary>Hit a bank or a rock hard enough to lose speed (glancing contact only slides).</summary>
        HitWall = 1 << 16,
        /// <summary>Crossed into the current lane from slower water (the surge that announces the fast line).</summary>
        EnteredLane = 1 << 17,
    }

    /// <summary>
    /// The complete simulated state of one boat. A plain struct with no references, so a whole
    /// race can be snapshotted by copying an array: that is what makes 32-tick rollback cheap.
    /// Orientation is yaw/pitch/roll in radians (pitch positive = nose up, roll positive = right side down,
    /// yaw positive = turning right seen from above).
    /// </summary>
    [Serializable]
    public struct BoatState
    {
        public SimVec3 Position;
        public SimVec3 Velocity;
        public float Yaw;
        public float Pitch;
        public float Roll;
        public float YawRate;
        public float PitchRate;
        public float RollRate;

        /// <summary>Seconds every pontoon has been out of the water.</summary>
        public float DryTime;
        /// <summary>Seconds until crossing into the current lane can surge again (eddies cut the lane; re-entering is not a new find).</summary>
        public float LaneSurgeCooldown;
        public bool Airborne;
        /// <summary>Fraction of pontoons in the water last tick, 0..1.</summary>
        public float WetFraction;
        public bool InCurrentLane;

        public sbyte DriftDirection;
        public float DriftTime;
        public byte DriftTier;

        public float BoostTime;
        public float SlapTime;
        public float SpinTime;
        public float ImmunityTime;
        public float DraftCharge;

        /// <summary>Seconds since a hop left the water; 0 when no hop is in progress. A drift starts only after the hop lands.</summary>
        public float HopTime;

        public bool PrevHopDrift;
        public LandingResult LastLanding;

        /// <summary>True while any wet pontoon is in an eddy (pivot assist active).</summary>
        public bool InEddy;
        /// <summary>True while any wet pontoon is on a crest's downslope face (surfing).</summary>
        public bool OnCrest;
        /// <summary>Seconds the current hydraulic hole has held the boat.</summary>
        public float HoleTime;
        /// <summary>Set once a hole has let go (time up or hopped out); cleared when the hull is clear of every hole.</summary>
        public bool HoleSpent;

        public bool IsDrifting => DriftDirection != 0;
        public bool IsSpinning => SpinTime > 0f;
        public bool InHole => HoleTime > 0f && !HoleSpent;

        public SimVec3 Forward => BoatFrame.Rotate(Yaw, Pitch, Roll, SimVec3.Forward);
        public SimVec3 Up => BoatFrame.Rotate(Yaw, Pitch, Roll, SimVec3.Up);
        public SimVec3 FlatForward => new SimVec3(SimMath.Sin(Yaw), 0f, SimMath.Cos(Yaw));
        public SimVec3 FlatRight => new SimVec3(SimMath.Cos(Yaw), 0f, -SimMath.Sin(Yaw));

        public static BoatState At(SimVec3 position, float yaw) => new BoatState { Position = position, Yaw = yaw };

        /// <summary>Linear blend for render interpolation between two ticks. Angles take the short way round.</summary>
        public static BoatState Interpolate(in BoatState a, in BoatState b, float t)
        {
            var r = b;
            r.Position = SimVec3.Lerp(a.Position, b.Position, t);
            r.Velocity = SimVec3.Lerp(a.Velocity, b.Velocity, t);
            r.Yaw = a.Yaw + SimMath.WrapAngle(b.Yaw - a.Yaw) * t;
            r.Pitch = SimMath.Lerp(a.Pitch, b.Pitch, t);
            r.Roll = SimMath.Lerp(a.Roll, b.Roll, t);
            return r;
        }
    }

    /// <summary>Rotation helpers for the boat's yaw/pitch/roll convention.</summary>
    public static class BoatFrame
    {
        /// <summary>
        /// Rotates a boat-local vector to world space: roll about local Z, then pitch about local X,
        /// then yaw about world Y. In Unity this equals Quaternion.Euler(-pitchDeg, yawDeg, -rollDeg).
        /// </summary>
        public static SimVec3 Rotate(float yaw, float pitch, float roll, SimVec3 v)
        {
            float cr = SimMath.Cos(roll), sr = SimMath.Sin(roll);
            float x1 = v.X * cr + v.Y * sr;
            float y1 = -v.X * sr + v.Y * cr;
            float z1 = v.Z;

            float cp = SimMath.Cos(pitch), sp = SimMath.Sin(pitch);
            float y2 = y1 * cp + z1 * sp;
            float z2 = -y1 * sp + z1 * cp;
            float x2 = x1;

            float cy = SimMath.Cos(yaw), sy = SimMath.Sin(yaw);
            return new SimVec3(x2 * cy + z2 * sy, y2, -x2 * sy + z2 * cy);
        }
    }
}
