using Downstream.Core.Boat;
using Downstream.Core.Items;
using Downstream.Core.Math;
using Downstream.Core.Race;
using Downstream.Core.Sim;
using NUnit.Framework;

namespace Downstream.Core.Tests
{
    public class RaceAndItemTests
    {
        [Test]
        public void EveryPositionBandSumsTo100Percent()
        {
            for (int band = 0; band < ItemDistribution.BandCount; band++)
                Assert.AreEqual(100, ItemDistribution.BandTotal(band), $"band {band}");
        }

        [Test]
        public void LeaderNeverDrawsKingfisherOrDamBurst()
        {
            var rng = new SimRandom(7);
            for (int i = 0; i < 10000; i++)
            {
                var item = ItemDistribution.Roll(1, ref rng);
                Assert.AreNotEqual(ItemType.Kingfisher, item);
                Assert.AreNotEqual(ItemType.DamBurst, item);
            }
        }

        [Test]
        public void SeededRollsRepeat()
        {
            var a = new SimRandom(99);
            var b = new SimRandom(99);
            for (int i = 0; i < 200; i++)
                Assert.AreEqual(ItemDistribution.Roll(7, ref a), ItemDistribution.Roll(7, ref b));
        }

        [Test]
        public void InputPackingRoundTrips()
        {
            var input = new BoatInput { Throttle = 0.5f, Steer = -1f, Pitch = 0.25f, HopDrift = true, UseItem = false };
            var q = input.Quantized();
            Assert.AreEqual(q, BoatInput.Unpack(q.Pack()));
            Assert.AreEqual(0.5f, q.Throttle, 0.01f);
            Assert.AreEqual(-1f, q.Steer, 1e-6f);
            Assert.IsTrue(q.HopDrift);
        }

        [Test]
        public void TrackProjectionMeasuresDistanceAlongTheRiver()
        {
            var track = new RaceTrack(new[] { new SimVec3(0, 0, 0), new SimVec3(0, 0, 100), new SimVec3(100, 0, 100) });
            int hint = 0;
            Assert.AreEqual(50f, track.Project(new SimVec3(3f, 0f, 50f), ref hint), 1e-4f);
            Assert.AreEqual(150f, track.Project(new SimVec3(50f, 0f, 104f), ref hint), 1e-4f);
            Assert.AreEqual(200f, track.Length, 1e-4f);
        }

        [Test]
        public void IdenticalInputsGiveIdenticalRaces()
        {
            ulong a = RunRace(seed: 5, ticks: 1200);
            ulong b = RunRace(seed: 5, ticks: 1200);
            ulong c = RunRace(seed: 6, ticks: 1200);
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
        }

        [Test]
        public void StandingsFollowDistanceDownstream()
        {
            var sim = MakeRace(4);
            var inputs = new BoatInput[4];
            for (int i = 0; i < 4; i++) inputs[i] = new BoatInput { Throttle = 0.25f + 0.25f * i };
            for (int k = 0; k < 600; k++) sim.Step(inputs);
            var order = sim.Standings();
            CollectionAssert.AreEqual(new[] { 3, 2, 1, 0 }, order);
            Assert.AreEqual(1, sim.PlaceOf(3));
        }

        [Test]
        public void PredictorReplaysToMatchTheHost()
        {
            var water = TestRivers.Uniform(2f);
            var t = TestRivers.Runabout();
            var start = TestRivers.Afloat();
            var predictor = new OwnBoatPredictor(start, 0);
            var host = start;
            var rng = new SimRandom(3);
            var inputs = new BoatInput[40];
            for (int i = 0; i < inputs.Length; i++)
                inputs[i] = new BoatInput { Throttle = 1f, Steer = rng.Range(-1f, 1f), HopDrift = rng.NextFloat() < 0.2f }.Quantized();

            for (int i = 0; i < 40; i++) predictor.Predict(inputs[i], t, water, BoatModifiers.None);

            // The host saw the same inputs but the boat was nudged at tick 20 (say, a bump the client missed).
            for (int i = 0; i <= 20; i++)
            {
                BoatSimulator.Step(ref host, inputs[i], t, water, i * BoatSimulator.TickDelta, BoatModifiers.None);
                if (i == 15) host.Position += new SimVec3(1f, 0f, 0f);
            }

            Assert.IsTrue(predictor.Reconcile(20, host, t, water, BoatModifiers.None));
            for (int i = 21; i < 40; i++)
                BoatSimulator.Step(ref host, inputs[i], t, water, i * BoatSimulator.TickDelta, BoatModifiers.None);

            Assert.AreEqual(host.Position, predictor.Current.Position);
            Assert.AreEqual(19, predictor.ReplayCount);
        }

        [Test]
        public void ClockRunsWholeTicksAndReportsTheBlend()
        {
            var clock = new FixedStepClock(1f / 120f);
            Assert.AreEqual(2, clock.Advance(1f / 60f + 0.001f));
            Assert.That(clock.Alpha, Is.InRange(0f, 1f));
            clock.MaxStepsPerFrame = 4;
            Assert.AreEqual(4, clock.Advance(1f));
        }

        private static RaceSimulation MakeRace(int boats)
        {
            var water = TestRivers.Uniform(1.5f);
            var track = new RaceTrack(new[] { new SimVec3(0, 0, 0), new SimVec3(0, 0, 1500) });
            var tunings = new BoatTuning[boats];
            var starts = new BoatState[boats];
            for (int i = 0; i < boats; i++)
            {
                tunings[i] = TestRivers.Runabout();
                starts[i] = BoatState.At(new SimVec3(-12f + 8f * i, 0f, 20f), 0f);
            }
            return new RaceSimulation(water, track, tunings, starts);
        }

        private static ulong RunRace(ulong seed, int ticks)
        {
            var sim = MakeRace(8);
            var rng = new SimRandom(seed);
            var inputs = new BoatInput[8];
            for (int k = 0; k < ticks; k++)
            {
                for (int i = 0; i < 8; i++)
                    inputs[i] = new BoatInput { Throttle = rng.Range(0.5f, 1f), Steer = rng.Range(-0.3f, 0.3f), HopDrift = rng.NextFloat() < 0.05f };
                sim.Step(inputs);
            }
            return sim.StateHash();
        }
    }
}
