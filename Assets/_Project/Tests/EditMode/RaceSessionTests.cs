using System.Linq;
using Downstream.Core.AI;
using Downstream.Core.Boat;
using Downstream.Core.Math;
using Downstream.Core.Race;
using Downstream.Core.Sim;
using Downstream.Core.Water;
using NUnit.Framework;

namespace Downstream.Core.Tests
{
    public class RaceSessionTests
    {
        [Test]
        public void CountdownHoldsTheGridThenStartsTheClockAtZero()
        {
            var session = MakeSession(4, length: 400f);
            var grid = session.Sim.State.Boats[0].Position;
            var inputs = Full(4);
            int goTick = -1;
            for (int k = 0; k < 400 && goTick < 0; k++)
            {
                session.Step(inputs);
                if ((session.LastEvents[0] & RaceEvents.Go) != 0) goTick = session.Sim.Tick;
                else Assert.AreEqual(RacePhase.Countdown, session.Phase);
            }
            Assert.AreEqual(360, goTick, "3 s at 120 Hz");
            Assert.AreEqual(0f, session.RaceClock, 1e-6f);
            var held = session.Sim.State.Boats[0].Position;
            Assert.AreEqual(grid.X, held.X, 1e-4f);
            Assert.AreEqual(grid.Z, held.Z, 1e-4f);
        }

        [Test]
        public void BoatsFinishInOrderWithSubTickTimes()
        {
            var session = MakeSession(3, length: 400f);
            var inputs = new[] { new BoatInput { Throttle = 1f }, new BoatInput { Throttle = 0.8f }, new BoatInput { Throttle = 0.6f } };
            RunUntilFinished(session, inputs, 60 * 120);

            Assert.AreEqual(RacePhase.Finished, session.Phase);
            var results = session.Results();
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, new[] { results[0].Boat, results[1].Boat, results[2].Boat });
            Assert.IsTrue(results[2].Finished);
            Assert.Less(results[0].Time, results[1].Time);
            float tick = BoatSimulator.TickDelta;
            Assert.AreNotEqual(0f, results[0].Time % tick, "finish times are interpolated inside the tick");
        }

        [Test]
        public void StragglersAreRankedByDistanceWhenTheFinishTimesOut()
        {
            var rules = RaceRules.Default;
            rules.FinishTimeoutSeconds = 5f;
            var session = MakeSession(2, length: 300f, rules: rules);
            var inputs = new[] { new BoatInput { Throttle = 1f }, new BoatInput { Throttle = 0.15f } };
            RunUntilFinished(session, inputs, 120 * 120);
            var results = session.Results();
            Assert.AreEqual(RacePhase.Finished, session.Phase);
            Assert.AreEqual(0, results[0].Boat);
            Assert.IsFalse(results[1].Finished);
            Assert.AreEqual(2, results[1].Place);
            Assert.AreEqual(5f, (session.Sim.Tick - session.FirstFinishTick) * BoatSimulator.TickDelta, 0.01f);
        }

        [Test]
        public void ABoatOnDryLandIsPutBackOnTheRiver()
        {
            var session = MakeSession(1, length: 600f);
            var inputs = new BoatInput[1];
            RunCountdown(session, inputs);
            session.Sim.State.Boats[0].Position = new SimVec3(300f, 0f, 120f); // far up the bank

            RaceEvents seen = RaceEvents.None;
            for (int k = 0; k < 4 * 120; k++)
            {
                session.Step(inputs);
                seen |= session.LastEvents[0];
            }

            Assert.IsTrue((seen & RaceEvents.RespawnStarted) != 0);
            Assert.IsTrue((seen & RaceEvents.Respawned) != 0);
            Assert.AreEqual(RespawnReason.Stranded, session.Status[0].LastRespawn);
            var b = session.Sim.State.Boats[0];
            Assert.IsTrue(session.Sim.Water.Sample(b.Position.X, b.Position.Z, session.Sim.RaceTime).IsWet, "back on the water");
        }

        [Test]
        public void AStalledBoatIsRespawnedAfterFourSeconds()
        {
            var session = MakeSession(1, length: 600f, flow: 0f);
            var inputs = new BoatInput[1];
            RunCountdown(session, inputs);
            int started = -1;
            for (int k = 0; k < 6 * 120 && started < 0; k++)
            {
                session.Step(inputs);
                if ((session.LastEvents[0] & RaceEvents.RespawnStarted) != 0) started = k + 1;
            }
            Assert.AreEqual(RespawnReason.Stuck, session.Status[0].LastRespawn);
            Assert.AreEqual(4f, started * BoatSimulator.TickDelta, 2 * BoatSimulator.TickDelta);
        }

        [Test]
        public void ARespawnCostsAboutTwoAndAHalfSeconds()
        {
            float clean = TimeTo(500f, respawnAt: -1f);
            float respawned = TimeTo(500f, respawnAt: 200f);
            float cost = respawned - clean;
            Assert.That(cost, Is.InRange(2f, 3.2f), $"respawn cost {cost:F2} s");
        }

        [Test]
        public void CupPointsFollowTheDesignTable()
        {
            CollectionAssert.AreEqual(new[] { 15, 12, 10, 8, 6, 4, 2, 1, 0 },
                new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }.Select(CupStandings.PointsFor));
        }

        [Test]
        public void CupTiesGoToTheBetterLastRace()
        {
            var cup = new CupStandings(2);
            cup.AddRace(new[] { new RaceResult { Boat = 0, Place = 1 }, new RaceResult { Boat = 1, Place = 2 } });
            cup.AddRace(new[] { new RaceResult { Boat = 1, Place = 1 }, new RaceResult { Boat = 0, Place = 2 } });
            Assert.AreEqual(27, cup.PointsOf(0));
            Assert.AreEqual(27, cup.PointsOf(1));
            Assert.AreEqual(1, cup.Order()[0]);
        }

        [Test]
        public void EightAIBoatsFinishAGreyboxRiverWithEveryFeature()
        {
            var s = ProceduralRiverSettings.Default;
            s.Length = 1500f;
            s.WaterfallDistance = 700f;
            s.WaterfallDrop = 6f;
            s.Boulders = new[] { new RiverBoulder { Distance = 300f, Lateral = 8f, Radius = 2f, EddyLength = 10f, EddyFlow = 1.5f } };
            s.Ledges = new[] { new RiverLedge { Distance = 450f, Drop = 0.4f, HoleLength = 4f, Lateral = -9f, Width = 8f } };
            s.WaveTrains = new[] { new StandingWaveTrain { Distance = 1000f, Count = 4, Wavelength = 12f, Height = 0.6f } };
            var water = new RiverWater(ProceduralRiver.Build(s));
            var points = new SimVec3[151];
            for (int i = 0; i < points.Length; i++)
            {
                float d = i * 10f;
                points[i] = new SimVec3(ProceduralRiver.CentreX(s, d), ProceduralRiver.SurfaceAt(s, d), d);
            }
            var track = new RaceTrack(points);
            var tunings = new BoatTuning[8];
            var starts = new BoatState[8];
            var ai = new LineFollowerAI[8];
            var hulls = new[] { HullType.Skiff, HullType.Jetboat, HullType.Runabout, HullType.Hydrofoil, HullType.Tug };
            for (int i = 0; i < 8; i++)
            {
                tunings[i] = BoatTuning.Create(HullStats.For(hulls[i % hulls.Length]), SpeedClass.Rapid);
                starts[i] = RaceGrid.Slot(track, i);
                ai[i] = new LineFollowerAI(track) { LateralOffset = ((i % 3) - 1) * 3f };
            }
            var session = new RaceSession(new RaceSimulation(water, track, tunings, starts));
            var inputs = new BoatInput[8];
            for (int k = 0; k < 180 * 120 && session.Phase != RacePhase.Finished; k++)
            {
                for (int i = 0; i < 8; i++) inputs[i] = ai[i].Think(session.Sim.State.Boats[i]);
                session.Step(inputs);
            }

            Assert.AreEqual(8, session.FinishedCount, "every boat reaches the mouth");
            for (int i = 0; i < 8; i++)
                Assert.AreNotEqual(RespawnReason.Stuck, session.Status[i].LastRespawn, $"boat {i} got stuck");
        }

        private static float TimeTo(float distance, float respawnAt)
        {
            var session = MakeSession(1, length: 1200f, flow: 0f);
            var inputs = Full(1);
            RunCountdown(session, inputs);
            bool done = false;
            for (int k = 0; k < 120 * 120; k++)
            {
                session.Step(inputs);
                float p = session.Status[0].IsRespawning ? 0f : session.Sim.State.Progress[0];
                if (!done && respawnAt > 0f && p >= respawnAt)
                {
                    session.BeginRespawn(0, RespawnReason.Stranded);
                    done = true;
                }
                if (p >= distance) return session.RaceClock;
            }
            return float.MaxValue;
        }

        private static RaceSession MakeSession(int boats, float length, float flow = 1.5f, RaceRules? rules = null)
        {
            var water = TestRivers.Uniform(flow, length: length + 200f);
            var track = new RaceTrack(new[] { new SimVec3(0, 0, 0), new SimVec3(0, 0, length) });
            var tunings = new BoatTuning[boats];
            var starts = new BoatState[boats];
            for (int i = 0; i < boats; i++)
            {
                tunings[i] = TestRivers.Runabout();
                starts[i] = BoatState.At(new SimVec3(-12f + 8f * i, 0f, 20f), 0f);
            }
            return new RaceSession(new RaceSimulation(water, track, tunings, starts), rules);
        }

        private static BoatInput[] Full(int n)
        {
            var inputs = new BoatInput[n];
            for (int i = 0; i < n; i++) inputs[i] = new BoatInput { Throttle = 1f };
            return inputs;
        }

        private static void RunCountdown(RaceSession session, BoatInput[] inputs)
        {
            while (session.Phase == RacePhase.Countdown) session.Step(inputs);
        }

        private static void RunUntilFinished(RaceSession session, BoatInput[] inputs, int maxTicks)
        {
            for (int k = 0; k < maxTicks && session.Phase != RacePhase.Finished; k++) session.Step(inputs);
        }
    }
}
