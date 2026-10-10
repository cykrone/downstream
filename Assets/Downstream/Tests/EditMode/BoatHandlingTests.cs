using Downstream.Core.Boat;
using Downstream.Core.Math;
using Downstream.Core.Water;
using NUnit.Framework;

namespace Downstream.Core.Tests
{
    /// <summary>
    /// Automated versions of the design doc's handling acceptance criteria. Numbers in the
    /// assertions come straight from the "Acceptance criteria" and "Boat handling model" sections.
    /// </summary>
    public class BoatHandlingTests
    {
        [Test]
        public void EveryStockHullTotals14Points()
        {
            foreach (HullType h in System.Enum.GetValues(typeof(HullType)))
                Assert.AreEqual(14, HullStats.For(h).PointTotal, h.ToString());
        }

        [Test]
        public void BoatSettlesWithin0Point4SecondsAfterA6MetreDrop()
        {
            var water = TestRivers.Uniform(0f);
            var t = TestRivers.Runabout();
            int tick = 0;

            // Find the resting height first.
            var rest = TestRivers.Run(TestRivers.Afloat(), default, t, water, 600, ref tick);
            float restY = rest.Position.Y;

            var s = rest;
            s.Position = new SimVec3(0f, restY + 6f, 20f);
            s.Velocity = SimVec3.Zero;
            int contactTick = -1;
            int lastUnsettledTick = -1;
            for (int i = 0; i < 2 * 120; i++, tick++)
            {
                BoatSimulator.Step(ref s, default, t, water, tick * BoatSimulator.TickDelta, BoatModifiers.None);
                if (contactTick < 0 && s.WetFraction > 0f) contactTick = i;
                if (contactTick < 0) continue;
                bool settled = SimMath.Abs(s.Position.Y - restY) < 0.05f && SimMath.Abs(s.Velocity.Y) < 0.25f;
                if (!settled) lastUnsettledTick = i;
            }

            Assert.GreaterOrEqual(contactTick, 0, "boat never reached the water");
            float settleSeconds = (lastUnsettledTick + 1 - contactTick) * BoatSimulator.TickDelta;
            Assert.LessOrEqual(settleSeconds, 0.4f, $"settled after {settleSeconds:F3} s");
        }

        [Test]
        public void BoatBobsAtMost0Point15MetresOnCalmWater()
        {
            var water = TestRivers.Uniform(0f);
            var t = TestRivers.Runabout();
            int tick = 0;
            var s = TestRivers.Run(TestRivers.Afloat(), default, t, water, 600, ref tick);
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 600; i++, tick++)
            {
                BoatSimulator.Step(ref s, new BoatInput { Throttle = 1f }, t, water, tick * BoatSimulator.TickDelta, BoatModifiers.None);
                if (i < 120) continue;
                min = SimMath.Min(min, s.Position.Y);
                max = SimMath.Max(max, s.Position.Y);
            }
            Assert.LessOrEqual(max - min, 0.15f);
        }

        [Test]
        public void CurrentLaneAdds15PercentAtEqualThrottle()
        {
            var t = TestRivers.Runabout();
            float laneFlow = 0.15f * t.TopSpeed;
            float calm = SteadySpeed(TestRivers.Uniform(0f), t);
            float lane = SteadySpeed(TestRivers.Uniform(laneFlow, lane: true), t);
            Assert.AreEqual(1.15f, lane / calm, 0.01f, $"calm {calm:F2} m/s, lane {lane:F2} m/s");
        }

        [Test]
        public void WakeSlotAdds8Percent()
        {
            var t = TestRivers.Runabout();
            var water = TestRivers.Uniform(0f);
            float plain = SteadySpeed(water, t);
            var noCharge = t;
            noCharge.DraftChargeSeconds = float.MaxValue; // isolate the speed bonus from the boost it charges
            float draft = SteadySpeed(water, noCharge, new BoatModifiers { InWakeSlot = true, FlowMultiplier = 1f });
            Assert.AreEqual(1.08f, draft / plain, 0.01f);
        }

        [TestCase(false, 0.7f, 1.4f, 2.2f)]
        [TestCase(true, 0.7f / 1.25f, 1.4f / 1.25f, 2.2f / 1.25f)]
        public void DriftTiersTriggerWithinOneTick(bool inLane, float tier1, float tier2, float tier3)
        {
            var water = TestRivers.Uniform(inLane ? 0.01f : 0f, width: 600f, length: 600f, lane: inLane);
            var t = TestRivers.Runabout();
            var s = BoatState.At(new SimVec3(0f, 0f, 300f), 0f);
            int tick = 0;
            s = TestRivers.Run(s, new BoatInput { Throttle = 1f }, t, water, 5 * 120, ref tick);
            s.Position = new SimVec3(0f, s.Position.Y, 300f);

            var tierTicks = new int[4];
            int start = -1;
            var drift = new BoatInput { Throttle = 1f, Steer = 1f, HopDrift = true };
            for (int i = 0; i < 3 * 120; i++, tick++)
            {
                var e = BoatSimulator.Step(ref s, drift, t, water, tick * BoatSimulator.TickDelta, BoatModifiers.None);
                if ((e & BoatEvents.DriftStarted) != 0) start = i;
                if ((e & BoatEvents.DriftTierUp) != 0) tierTicks[s.DriftTier] = i;
            }

            Assert.GreaterOrEqual(start, 0, "drift never started");
            float tick1 = BoatSimulator.TickDelta;
            Assert.AreEqual(tier1, (tierTicks[1] - start) * tick1, tick1 + 1e-4f, "tier 1");
            Assert.AreEqual(tier2, (tierTicks[2] - start) * tick1, tick1 + 1e-4f, "tier 2");
            Assert.AreEqual(tier3, (tierTicks[3] - start) * tick1, tick1 + 1e-4f, "tier 3");
        }

        [Test]
        public void ReleasingATier3DriftGives1Point6SecondsOfBoost()
        {
            var water = TestRivers.Uniform(0f, width: 600f, length: 600f);
            var t = TestRivers.Runabout();
            var s = BoatState.At(new SimVec3(0f, 0f, 300f), 0f);
            int tick = 0;
            s = TestRivers.Run(s, new BoatInput { Throttle = 1f }, t, water, 5 * 120, ref tick);
            s = TestRivers.Run(s, new BoatInput { Throttle = 1f, Steer = 1f, HopDrift = true }, t, water, (int)(3.2f * 120), ref tick);
            Assert.AreEqual(3, s.DriftTier, "the hop takes about 0.5 s before charging starts");
            var e = BoatSimulator.Step(ref s, new BoatInput { Throttle = 1f }, t, water, tick * BoatSimulator.TickDelta, BoatModifiers.None);
            Assert.IsTrue((e & BoatEvents.BoostStarted) != 0);
            Assert.AreEqual(1.6f, s.BoostTime, BoatSimulator.TickDelta + 1e-4f);
        }

        [TestCase(-20f, LandingResult.Clean)]
        [TestCase(-10.5f, LandingResult.Clean)]
        [TestCase(-39.5f, LandingResult.Clean)]
        [TestCase(0f, LandingResult.Slap)]
        [TestCase(-4f, LandingResult.Slap)]
        [TestCase(15f, LandingResult.Slap)]
        [TestCase(-7f, LandingResult.Neutral)]
        [TestCase(-50f, LandingResult.Neutral)]
        public void LandingRuleMatchesTheDesign(float pitchDegrees, LandingResult expected)
        {
            var s = new BoatState { Pitch = pitchDegrees * SimMath.Deg2Rad };
            Assert.AreEqual(expected, BoatSimulator.ClassifyLanding(s, SimVec3.Up, TestRivers.Runabout()));
        }

        [Test]
        public void NoCapsizeUnderRandomInputs()
        {
            var s = ProceduralRiverSettings.Default;
            s.Length = 2000f;
            s.WaterfallDistance = 400f;
            s.WaterfallDrop = 8f;
            var water = new RiverWater(ProceduralRiver.Build(s), new WaterLayers
            {
                Waves = new[] { new GerstnerWave { DirectionX = 0.3f, DirectionZ = 1f, Wavelength = 12f, Amplitude = 0.4f, Speed = 4f } },
            });
            var t = BoatTuning.Create(HullStats.For(HullType.Skiff), SpeedClass.Torrent);
            var rng = new SimRandom(1234);
            var boat = BoatState.At(new SimVec3(0f, 0f, 10f), 0f);
            float limit = t.MaxPitchRollAir + 1e-4f;
            for (int i = 0; i < 120 * 60; i++)
            {
                var input = new BoatInput
                {
                    Throttle = rng.Range(-0.2f, 1f),
                    Steer = rng.Range(-1f, 1f),
                    Pitch = rng.Range(-1f, 1f),
                    HopDrift = rng.NextFloat() < 0.3f,
                };
                BoatSimulator.Step(ref boat, input.Quantized(), t, water, i * BoatSimulator.TickDelta, BoatModifiers.None);
                Assert.LessOrEqual(SimMath.Abs(boat.Pitch), limit, $"pitch at tick {i}");
                Assert.LessOrEqual(SimMath.Abs(boat.Roll), limit, $"roll at tick {i}");
                Assert.IsFalse(float.IsNaN(boat.Position.X) || float.IsNaN(boat.Position.Y), $"NaN at tick {i}");
            }
        }

        [Test]
        public void HitSpinsOutFor0Point8SecondsThenGrants1Point5SecondsImmunity()
        {
            var water = TestRivers.Uniform(0f);
            var t = TestRivers.Runabout();
            var s = TestRivers.Afloat();
            int tick = 0;
            Assert.IsTrue(BoatSimulator.ApplyHit(ref s, t));
            Assert.IsFalse(BoatSimulator.ApplyHit(ref s, t), "a spinning boat cannot be hit again");
            s = TestRivers.Run(s, default, t, water, (int)(0.8f * 120) + 1, ref tick);
            Assert.IsFalse(s.IsSpinning);
            Assert.IsFalse(BoatSimulator.ApplyHit(ref s, t), "immune right after the spin");
            s = TestRivers.Run(s, default, t, water, (int)(1.5f * 120) + 1, ref tick);
            Assert.IsTrue(BoatSimulator.ApplyHit(ref s, t), "hittable once immunity ends");
        }

        [Test]
        public void ShallowHullsGroundLessThanDeepOnes()
        {
            var s = ProceduralRiverSettings.Default;
            s.Depth = 0.6f;
            s.Gradient = 0f;
            s.MeanderAmplitude = 0f;
            s.BaseFlow = 0f;
            s.LaneExtraFlow = 0f;
            s.Length = 1500f;
            var water = new RiverWater(ProceduralRiver.Build(s));
            float skiff = SteadySpeed(water, BoatTuning.Create(HullStats.For(HullType.Skiff), SpeedClass.Rapid));
            float tug = SteadySpeed(water, BoatTuning.Create(HullStats.For(HullType.Tug), SpeedClass.Rapid));
            Assert.Greater(skiff, tug);
        }

        private static float SteadySpeed(IWaterQuery water, BoatTuning t, BoatModifiers? mods = null)
        {
            var m = mods ?? BoatModifiers.None;
            var s = BoatState.At(new SimVec3(0f, 0f, 20f), 0f);
            for (int i = 0; i < 20 * 120; i++)
            {
                BoatSimulator.Step(ref s, new BoatInput { Throttle = 1f }, t, water, i * BoatSimulator.TickDelta, m);
                if (s.Position.Z > 1200f) s.Position = new SimVec3(s.Position.X, s.Position.Y, 20f);
            }
            return s.Velocity.Flat.Magnitude;
        }
    }
}
