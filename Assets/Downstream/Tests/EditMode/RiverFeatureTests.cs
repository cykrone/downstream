using Downstream.Core.Boat;
using Downstream.Core.Math;
using Downstream.Core.Race;
using Downstream.Core.Sim;
using Downstream.Core.Water;
using NUnit.Framework;

namespace Downstream.Core.Tests
{
    /// <summary>
    /// The design's "water mechanics kart games can't do": eddy pivots, hydraulic holes, crest surfing,
    /// wake edges, plus boat-to-boat bumps. Numbers come from the Acceptance criteria where they exist.
    /// </summary>
    public class RiverFeatureTests
    {
        [Test]
        public void EddyPivots120DegreesWithinOneSecondOnRapid()
        {
            float withEddy = PivotSeconds(eddy: true);
            float without = PivotSeconds(eddy: false);
            Assert.LessOrEqual(withEddy, 1.0f, $"eddy pivot took {withEddy:F3} s");
            Assert.Greater(without, withEddy, "the eddy should be what makes the pivot fast");
        }

        [Test]
        public void HydraulicHoleHoldsTheBoatForAtMost1Point5Seconds()
        {
            var water = new RiverWater(ProceduralRiver.Build(LedgeRiver()));
            var t = TestRivers.Runabout();
            var s = BoatState.At(new SimVec3(0f, 0f, 20f), 0f);
            int grabbed = -1, released = -1;
            float slowest = float.MaxValue;
            for (int i = 0; i < 20 * 120; i++)
            {
                var e = BoatSimulator.Step(ref s, new BoatInput { Throttle = 1f }, t, water, i * BoatSimulator.TickDelta, BoatModifiers.None);
                if ((e & BoatEvents.HoleGrabbed) != 0 && grabbed < 0) grabbed = i;
                if ((e & BoatEvents.HoleReleased) != 0 && released < 0) released = i;
                if (grabbed >= 0 && released < 0 && i - grabbed > 36) slowest = SimMath.Min(slowest, s.Velocity.Flat.Magnitude);
                if (released >= 0 && s.Position.Z > 260f) break;
            }

            Assert.GreaterOrEqual(grabbed, 0, "the hole never grabbed the boat");
            Assert.GreaterOrEqual(released, 0, "the hole never let go");
            float held = (released - grabbed + 1) * BoatSimulator.TickDelta;
            Assert.LessOrEqual(held, 1.5f + BoatSimulator.TickDelta, $"held {held:F3} s");
            Assert.Less(slowest, 2f, "a held boat should be stalled");
            Assert.Greater(s.Position.Z, 260f, "the boat should paddle on downstream after the release");
        }

        [Test]
        public void HoppingBreaksOutOfAHoleEarly()
        {
            var water = new RiverWater(ProceduralRiver.Build(LedgeRiver()));
            var t = TestRivers.Runabout();
            var s = BoatState.At(new SimVec3(0f, 0f, 20f), 0f);
            int grabbed = -1, released = -1;
            for (int i = 0; i < 20 * 120 && released < 0; i++)
            {
                bool hop = grabbed >= 0 && i - grabbed == 48;
                var e = BoatSimulator.Step(ref s, new BoatInput { Throttle = 1f, HopDrift = hop }, t, water, i * BoatSimulator.TickDelta, BoatModifiers.None);
                if ((e & BoatEvents.HoleGrabbed) != 0 && grabbed < 0) grabbed = i;
                if ((e & BoatEvents.HoleReleased) != 0) released = i;
            }
            Assert.AreEqual(48, released - grabbed);
        }

        [Test]
        public void SurfingACrestHoldsSpeedWithoutThrottle()
        {
            var t = TestRivers.Runabout();
            float crest = CoastSpeedRatio(TestRivers.Paint(TestRivers.Uniform(0f), WaterFeature.Crest, 0f), t);
            float flat = CoastSpeedRatio(TestRivers.Uniform(0f), t);
            Assert.AreEqual(1f, crest, 0.02f, "speed kept on the crest face over 2 s");
            Assert.Less(flat, 0.8f, "off the crest the boat coasts down");
        }

        [Test]
        public void HoppingOffACrestLaunchesHigher()
        {
            var t = TestRivers.Runabout();
            float crest = HopPeak(TestRivers.Paint(TestRivers.Uniform(0f), WaterFeature.Crest, 0f), t);
            float flat = HopPeak(TestRivers.Uniform(0f), t);
            Assert.Greater(crest, 2f * flat, $"crest hop {crest:F2} m, flat hop {flat:F2} m");
        }

        [Test]
        public void GreyboxWaveTrainMarksOnlyDownslopeFacesAsCrests()
        {
            var s = ProceduralRiverSettings.Default;
            s.Gradient = 0f;
            s.MeanderAmplitude = 0f;
            s.WaveTrains = new[] { new StandingWaveTrain { Distance = 100f, Count = 3, Wavelength = 10f, Height = 1f } };
            var water = new RiverWater(ProceduralRiver.Build(s));
            int crests = 0;
            for (float z = 90f; z < 140f; z += 0.5f)
            {
                var w = water.Sample(0f, z, 0f);
                if ((w.Features & WaterFeature.Crest) == 0) continue;
                crests++;
                Assert.Less(water.Sample(0f, z + 0.5f, 0f).SurfaceHeight, water.Sample(0f, z - 0.5f, 0f).SurfaceHeight + 1e-3f, $"crest at z={z} is not on a downslope");
            }
            Assert.Greater(crests, 20);
        }

        [Test]
        public void GreyboxBoulderIsDryWithAnUpstreamEddyBehindIt()
        {
            var s = ProceduralRiverSettings.Default;
            s.Gradient = 0f;
            s.MeanderAmplitude = 0f;
            s.Boulders = new[] { new RiverBoulder { Distance = 100f, Lateral = 5f, Radius = 2f, EddyLength = 10f, EddyFlow = 1.5f } };
            var water = new RiverWater(ProceduralRiver.Build(s));
            Assert.IsFalse(water.Sample(5f, 100f, 0f).IsWet, "the rock is dry");
            var eddy = water.Sample(5f, 106f, 0f);
            Assert.IsTrue((eddy.Features & WaterFeature.Eddy) != 0);
            Assert.Less(eddy.Flow.Z, 0f, "eddy water circles upstream");
            Assert.Greater(water.Sample(-5f, 106f, 0f).Flow.Z, 0f, "the main current still runs downstream");
        }

        [Test]
        public void ClippingAWakeEdgeCostsGripWithinATenthOfASecond()
        {
            var plain = LateralSlip(withLeader: false, out _);
            var clipped = LateralSlip(withLeader: true, out int edgeTick);
            Assert.GreaterOrEqual(edgeTick, 0, "never touched the wake edge");
            Assert.LessOrEqual(edgeTick * BoatSimulator.TickDelta, 0.1f);
            Assert.Greater(clipped, plain * 1.2f, $"slip with wake {clipped:F3} m/s vs {plain:F3} m/s");
        }

        [Test]
        public void TheWakeSlotItselfIsNotAnEdge()
        {
            var sim = TwoBoats(lateral: 0f, lane: true);
            sim.Step(new BoatInput[2]);
            Assert.IsTrue(sim.Modifiers[1].InWakeSlot);
            Assert.IsFalse(sim.Modifiers[1].OnWakeEdge);
        }

        [Test]
        public void HeavyHullsShoveLightOnes()
        {
            var tug = BoatTuning.Create(HullStats.For(HullType.Tug), SpeedClass.Rapid);
            var skiff = BoatTuning.Create(HullStats.For(HullType.Skiff), SpeedClass.Rapid);
            float skiffKick = RamKick(rammer: tug, target: skiff, out bool bumped1);
            float tugKick = RamKick(rammer: skiff, target: tug, out bool bumped2);
            Assert.IsTrue(bumped1 && bumped2, "the boats never touched");
            Assert.Greater(skiffKick, 1.5f * tugKick, $"skiff gained {skiffKick:F2} m/s, tug gained {tugKick:F2} m/s");
        }

        [Test]
        public void BumpedHullsEndApart()
        {
            var t = TestRivers.Runabout();
            RamKick(t, t, out _, out var sim);
            var a = sim.State.Boats[0].Position;
            var b = sim.State.Boats[1].Position;
            Assert.GreaterOrEqual((b - a).Flat.Magnitude, t.HullLength - 0.05f);
        }

        /// <summary>One boat drives into the stern of a stopped one; returns the target's speed gain.</summary>
        private static float RamKick(BoatTuning rammer, BoatTuning target, out bool bumped) => RamKick(rammer, target, out bumped, out _);

        private static float RamKick(BoatTuning rammer, BoatTuning target, out bool bumped, out RaceSimulation sim)
        {
            var water = TestRivers.Uniform(0f);
            var track = new RaceTrack(new[] { new SimVec3(0, 0, 0), new SimVec3(0, 0, 1500) });
            var a = BoatState.At(new SimVec3(0f, 0f, 100f), 0f);
            a.Velocity = new SimVec3(0f, 0f, 12f);
            var b = BoatState.At(new SimVec3(0f, 0f, 106f), 0f);
            sim = new RaceSimulation(water, track, new[] { rammer, target }, new[] { a, b });
            bumped = false;
            var inputs = new BoatInput[2];
            for (int i = 0; i < 60; i++)
            {
                sim.Step(inputs);
                if ((sim.LastEvents[1] & BoatEvents.BoatBump) != 0 && !bumped)
                {
                    bumped = true;
                    return sim.State.Boats[1].Velocity.Z;
                }
            }
            return 0f;
        }

        private static ProceduralRiverSettings LedgeRiver()
        {
            var s = ProceduralRiverSettings.Default;
            s.Length = 600f;
            s.Gradient = 0f;
            s.MeanderAmplitude = 0f;
            // A low ledge: a fast boat that drops a big one flies clear of the hole (boofing), which is fine but not what this tests.
            s.Ledges = new[] { new RiverLedge { Distance = 150f, Drop = 0.15f, HoleLength = 6f, Lateral = 0f, Width = 40f } };
            return s;
        }

        private static float PivotSeconds(bool eddy)
        {
            var water = TestRivers.Uniform(0f, width: 600f, length: 600f);
            if (eddy) water = TestRivers.Paint(water, WaterFeature.Eddy, -2f);
            var t = TestRivers.Runabout(SpeedClass.Rapid);
            var s = BoatState.At(new SimVec3(0f, 0f, 300f), 0f);
            s.Velocity = new SimVec3(0f, 0f, 15f);
            int tick = 0;
            s = TestRivers.Run(s, new BoatInput { Throttle = 0.6f }, t, water, 60, ref tick);
            float startYaw = s.Yaw, turned = 0f, prev = s.Yaw;
            for (int i = 1; i <= 3 * 120; i++, tick++)
            {
                BoatSimulator.Step(ref s, new BoatInput { Throttle = 0.6f, Steer = 1f }, t, water, tick * BoatSimulator.TickDelta, BoatModifiers.None);
                turned += SimMath.WrapAngle(s.Yaw - prev);
                prev = s.Yaw;
                if (turned >= 120f * SimMath.Deg2Rad) return i * BoatSimulator.TickDelta;
            }
            return float.MaxValue;
        }

        private static float CoastSpeedRatio(IWaterQuery water, BoatTuning t)
        {
            int tick = 0;
            var s = TestRivers.Run(TestRivers.Afloat(), new BoatInput { Throttle = 1f }, t, water, 5 * 120, ref tick);
            float before = s.Velocity.Flat.Magnitude;
            s = TestRivers.Run(s, default, t, water, 2 * 120, ref tick);
            return s.Velocity.Flat.Magnitude / before;
        }

        private static float HopPeak(IWaterQuery water, BoatTuning t)
        {
            int tick = 0;
            var s = TestRivers.Run(TestRivers.Afloat(), default, t, water, 600, ref tick);
            float rest = s.Position.Y, peak = rest;
            BoatSimulator.Step(ref s, new BoatInput { HopDrift = true }, t, water, tick++ * BoatSimulator.TickDelta, BoatModifiers.None);
            for (int i = 0; i < 240; i++, tick++)
            {
                BoatSimulator.Step(ref s, default, t, water, tick * BoatSimulator.TickDelta, BoatModifiers.None);
                peak = SimMath.Max(peak, s.Position.Y);
            }
            return peak - rest;
        }

        private static RaceSimulation TwoBoats(float lateral, bool lane)
        {
            var water = lane ? TestRivers.Uniform(0.01f, lane: true) : TestRivers.Uniform(0f);
            var track = new RaceTrack(new[] { new SimVec3(0, 0, 0), new SimVec3(0, 0, 1500) });
            var t = TestRivers.Runabout();
            var leader = BoatState.At(new SimVec3(0f, 0f, 108f), 0f);
            leader.Velocity = new SimVec3(0f, 0f, 20f);
            var follower = BoatState.At(new SimVec3(lateral, 0f, 100f), 0f);
            follower.Velocity = new SimVec3(0f, 0f, 20f);
            var sim = new RaceSimulation(water, track, new[] { t, t }, new[] { leader, follower });
            // Settle buoyancy flags (lane, wetness) before measuring.
            sim.State.Boats[0].InCurrentLane = lane;
            sim.State.Boats[1].InCurrentLane = lane;
            return sim;
        }

        /// <summary>Gives the follower a sideways slide and returns its sideways speed 0.1 s later.</summary>
        private static float LateralSlip(bool withLeader, out int edgeTick)
        {
            var sim = TwoBoats(lateral: 2.2f, lane: false);
            if (!withLeader) sim.State.Boats[0].Position = new SimVec3(200f, 0f, 108f);
            sim.State.Boats[1].Velocity = new SimVec3(5f, 0f, 20f);
            var inputs = new[] { new BoatInput { Throttle = 0.8f }, new BoatInput { Throttle = 0.8f } };
            edgeTick = -1;
            for (int i = 0; i < 12; i++)
            {
                sim.Step(inputs);
                if (edgeTick < 0 && (sim.LastEvents[1] & BoatEvents.WakeEdge) != 0) edgeTick = i + 1;
            }
            return SimMath.Abs(sim.State.Boats[1].Velocity.X);
        }
    }
}
