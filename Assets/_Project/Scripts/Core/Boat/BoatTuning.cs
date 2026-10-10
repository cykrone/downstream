using System;
using Downstream.Core.Math;

namespace Downstream.Core.Boat
{
    /// <summary>
    /// Every handling number in one place. Values marked "design" come straight from the design doc;
    /// the rest are starting points to tune in the handling prototype. Built from hull stats and a
    /// speed class by <see cref="Create"/>; designers can override any field afterwards.
    /// </summary>
    [Serializable]
    public struct BoatTuning
    {
        // Hull geometry (pontoons sit at the corners and along the sides of this box).
        public float HullLength;
        public float HullBeam;
        public float PontoonY;
        public float Draft;

        // Speed.
        public float TopSpeed;
        public float BoostTopSpeed;
        public float ReverseSpeed;
        public float AccelRate;
        public float BoostAccelRate;
        public float CoastRate;
        public float BrakeRate;

        // Grip and steering.
        public float KeelGrip;          // design: 1.0
        public float DriftGrip;         // design: 0.35
        public float GripRate;
        public float TurnRate;
        public float YawResponse;
        public float AirSteerFactor;    // design: 0.4
        public float TurnSpeedFalloff;  // m/s at which keel turning has halved: fast boats need the drift
        public float TurnScrub;         // forward speed lost per radian of keel turn, as a fraction per second

        // Buoyancy (accelerations per unit mass).
        public float BuoyancyStiffness;
        public float BuoyancyDamping;
        public float MaxSubmersion;
        public float PitchInertia;
        public float RollInertia;
        public float AngularDamping;
        public float SlopeAssist;

        // Air.
        public float Gravity;
        public float AirGravityScale;   // design: 1.4
        public float AirborneDelay;     // design: 0.15 s
        public float HopSpeed;
        public float AirPitchRate;
        public float MaxPitchRollWater; // design: 35 degrees
        public float MaxPitchRollAir;   // design: 60 degrees

        // Drift tiers (design: charge at 0.7/1.4/2.2 s, boost 0.5/1.0/1.6 s, 25% faster in a lane).
        public float DriftTier1, DriftTier2, DriftTier3;
        public float DriftBoost1, DriftBoost2, DriftBoost3;
        public float DriftLaneChargeScale;
        public float DriftMinSpeed;
        public float DriftSteerThreshold;

        // Landing (design: clean = 10-40 degrees nose down, 0.6 s boost; slap = under 5 degrees or stern first, -20% for 0.4 s).
        public float CleanLandMinDeg, CleanLandMaxDeg, SlapBelowDeg;
        public float CleanLandBoost;
        public float SlapSpeedFactor;
        public float SlapDuration;

        // Drafting (design: +8% in the wake slot).
        public float DraftSpeedFactor;
        public float DraftChargeSeconds;
        public float DraftBoost;

        // Hits (design: 0.8 s spin-out, then 1.5 s immunity).
        public float SpinOutDuration;
        public float HitImmunity;

        // Shallows.
        public float ShallowDrag;

        // Banks and rocks: contact into the surface faster than WallHitSpeed costs speed and a slap; glancing hits slide.
        public float WallHitSpeed;
        public float WallHitSpeedLoss;
        public float WallHitSlowSeconds;
        public float WallRestitution;
        public float EddyDrag;          // extra forward drag inside an eddy: slack water is slow water

        // River features (design: eddy pivot of 120 degrees in 1.0 s or less on Rapid; holes grab for up to 1.5 s).
        public float EddyTurnScale;
        public float HoleGrip;
        public float HoleMaxHold;
        public float CrestHopScale;
        public float WakeEdgeGripFactor;

        // Boat-to-boat contact. Mass comes from the Weight stat, so a Tug shoves a Skiff.
        public float Mass;
        public float BumpRestitution;

        public static BoatTuning Create(HullStats hull, SpeedClass speedClass)
        {
            SpeedClasses.TopSpeeds(speedClass, out float top, out float boostTop);
            float speedFactor = 1f + 0.025f * (hull.Speed - 4);
            return new BoatTuning
            {
                HullLength = 4f,
                HullBeam = 2f,
                PontoonY = -0.2f,
                Draft = hull.DraftMetres,

                TopSpeed = top * speedFactor,
                BoostTopSpeed = boostTop * speedFactor,
                ReverseSpeed = 6f,
                AccelRate = 0.35f + 0.08f * hull.Acceleration,
                BoostAccelRate = 3f,
                CoastRate = 0.35f,
                BrakeRate = 1.6f,

                KeelGrip = 1f,
                DriftGrip = 0.35f,
                GripRate = 10f,
                TurnRate = 1.05f + 0.15f * hull.Handling,
                YawResponse = 10f,
                AirSteerFactor = 0.4f,
                TurnSpeedFalloff = 28f,
                TurnScrub = 0.07f,

                BuoyancyStiffness = 220f,
                BuoyancyDamping = 24f,
                MaxSubmersion = 0.7f,
                PitchInertia = 1.5f,
                RollInertia = 0.5f,
                AngularDamping = 4f,
                SlopeAssist = 0.5f,

                Gravity = 9.81f,
                AirGravityScale = 1.4f,
                AirborneDelay = 0.15f,
                HopSpeed = 3.2f,
                AirPitchRate = 2.5f,
                MaxPitchRollWater = 35f * SimMath.Deg2Rad,
                MaxPitchRollAir = 60f * SimMath.Deg2Rad,

                DriftTier1 = 0.7f, DriftTier2 = 1.4f, DriftTier3 = 2.2f,
                DriftBoost1 = 0.5f, DriftBoost2 = 1.0f, DriftBoost3 = 1.6f,
                DriftLaneChargeScale = 1.25f,
                DriftMinSpeed = 8f,
                DriftSteerThreshold = 0.3f,

                CleanLandMinDeg = 10f, CleanLandMaxDeg = 40f, SlapBelowDeg = 5f,
                CleanLandBoost = 0.6f,
                SlapSpeedFactor = 0.8f,
                SlapDuration = 0.4f,

                DraftSpeedFactor = 1.08f,
                DraftChargeSeconds = 1.5f,
                DraftBoost = 0.5f,

                SpinOutDuration = 0.8f,
                HitImmunity = 1.5f,

                ShallowDrag = 3f,

                WallHitSpeed = 3.5f,
                WallHitSpeedLoss = 0.35f,
                WallHitSlowSeconds = 0.6f,
                WallRestitution = 0.25f,
                EddyDrag = 0.6f,

                EddyTurnScale = 2.1f, // design: 120 degrees in 1.0 s on Rapid, with the slower keel turning above
                HoleGrip = 14f,
                HoleMaxHold = 1.5f,
                CrestHopScale = 1.8f,
                WakeEdgeGripFactor = 0.4f,

                Mass = 1f + 0.3f * (hull.Weight - 1),
                BumpRestitution = 0.35f,
            };
        }
    }
}
