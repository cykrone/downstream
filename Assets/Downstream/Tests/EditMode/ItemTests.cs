using Downstream.Core.AI;
using Downstream.Core.Boat;
using Downstream.Core.Items;
using Downstream.Core.Math;
using Downstream.Core.Race;
using Downstream.Core.Sim;
using Downstream.Core.Water;
using NUnit.Framework;

namespace Downstream.Core.Tests
{
    /// <summary>The design's item rules and counterplay, one test per rule.</summary>
    public class ItemTests
    {
        private static readonly BoatInput Press = new BoatInput { UseItem = true };

        [Test]
        public void SkillRulesetNeverHandsOutBoosts()
        {
            var rng = new SimRandom(11);
            for (int place = 1; place <= 8; place++)
            for (int k = 0; k < 2000; k++)
            {
                var item = ItemDistribution.Roll(place, ref rng, ItemRuleset.Skill);
                Assert.IsFalse(ItemDistribution.IsBoost(item), $"{item} at place {place}");
                Assert.AreNotEqual(ItemType.None, item);
            }
        }

        [Test]
        public void NoneRulesetHasNoBuoys()
        {
            var items = new ItemSystem(2, new[] { new ItemBuoy { Position = SimVec3.Zero } }, 1, ItemRuleset.None);
            Assert.AreEqual(0, items.Buoys.Length);
        }

        [Test]
        public void BuoyRowsStopBeforeTheFinal15Percent()
        {
            var water = TestRivers.Uniform(1f, width: 40f, length: 3000f);
            var track = StraightTrack(2800f);
            var buoys = ItemSystem.Layout(track, water);
            Assert.Greater(buoys.Length, 0);
            foreach (var b in buoys)
            {
                Assert.LessOrEqual(b.Distance, 0.85f * track.Length);
                Assert.IsTrue(water.Sample(b.Position.X, b.Position.Z, 0f).IsWet);
            }
            Assert.AreEqual(5, System.Array.FindAll(buoys, b => b.Distance == 150f).Length, "rows of 5");
        }

        [Test]
        public void DrivingThroughABuoyPicksUpAnItemForYourPlace()
        {
            var race = Race(1, buoys: new[] { new ItemBuoy { Position = new SimVec3(0f, 0f, 60f), Distance = 60f } });
            Run(race, new[] { new BoatInput { Throttle = 1f } }, 5 * 120);
            Assert.AreNotEqual(ItemType.None, race.Items.Boats[0].Held);
            Assert.AreNotEqual(ItemType.DamBurst, race.Items.Boats[0].Held, "a lone boat leads, and leaders never draw Dam Burst");
        }

        [Test]
        public void TurbineBoostsForOneSecondAndLongerInACurrentLane()
        {
            Assert.AreEqual(1.0f, TurbineBoost(lane: false), 1e-4f);
            Assert.AreEqual(1.3f, TurbineBoost(lane: true), 1e-4f);
        }

        [Test]
        public void PikeHitsABoatAheadUnlessItHops()
        {
            Assert.IsTrue(PikeAtBoatAhead(hop: false, gap: 40f, out _), "a pike should hit");
            Assert.IsFalse(PikeAtBoatAhead(hop: true, gap: 40f, out bool dodged), "a hop clears the pike");
            Assert.IsTrue(dodged);
        }

        [Test]
        public void NoAttackLandsWithinHalfASecondOfLaunch()
        {
            // 10 m ahead the pike reaches the boat in about 0.25 s, before it is armed, so it passes under.
            Assert.IsFalse(PikeAtBoatAhead(hop: false, gap: 10f, out _));
        }

        [Test]
        public void DroppedMinesDriftDownstreamWithTheCurrent()
        {
            var race = Race(1, flow: 2f);
            race.Items.Give(0, ItemType.LilyMine);
            HoldButton(race, 0, 0.4f);
            int mine = FindObject(race.Items, ItemObjectKind.Mine);
            Assert.GreaterOrEqual(mine, 0, "holding then releasing drops the mine behind");
            float z0 = race.Items.Objects[mine].Position.Z;
            Run(race, new[] { new BoatInput { Throttle = 0.5f } }, 3 * 120); // drive on so the mine is left behind
            Assert.AreEqual(ItemObjectKind.Mine, race.Items.Objects[mine].Kind);
            Assert.AreEqual(6f, race.Items.Objects[mine].Position.Z - z0, 0.3f, "3 s at 2 m/s");
        }

        [Test]
        public void ReedShieldBlocksOneHit()
        {
            var race = Race(2, gap: 30f);
            race.Items.Give(1, ItemType.ReedShield);
            Tap(race, 1);
            Assert.Greater(race.Items.Boats[1].ShieldTime, 5.9f);
            race.Items.Give(0, ItemType.Pike);
            var seen = Fire(race, 0, 120);
            Assert.IsTrue((seen[1] & ItemEvents.Blocked) != 0);
            Assert.IsFalse(race.Sim.State.Boats[1].IsSpinning);
            Assert.AreEqual(0f, race.Items.Boats[1].ShieldTime);
        }

        [Test]
        public void ATrailingItemShieldsTheBoatFromBehind()
        {
            var race = Race(2, gap: 30f);
            race.Items.Give(1, ItemType.LilyMine);
            var inputs = new BoatInput[2];
            inputs[1].UseItem = true;
            Run(race, inputs, 40); // hold: the mine trails behind
            Assert.AreEqual(ItemType.LilyMine, race.Items.Boats[1].Behind);
            race.Items.Give(0, ItemType.Pike);
            var seen = Fire(race, 0, 120, holdBoat: 1);
            Assert.IsTrue((seen[1] & ItemEvents.Blocked) != 0);
            Assert.AreEqual(ItemType.None, race.Items.Boats[1].Behind);
            Assert.IsFalse(race.Sim.State.Boats[1].IsSpinning);
        }

        [Test]
        public void KingfisherWarnsForThreeSecondsAndIgnoresShields()
        {
            var race = Race(2, gap: 30f);
            race.Items.Give(1, ItemType.ReedShield);
            Tap(race, 1);
            race.Items.Give(0, ItemType.Kingfisher);
            Tap(race, 0);
            Assert.AreEqual(3f, race.Items.Boats[1].KingfisherIncoming, 0.02f);
            var seen = Run(race, new BoatInput[2], (int)(3.1f * 120));
            Assert.IsTrue((seen[1] & ItemEvents.Hit) != 0, "the shield does not stop a Kingfisher");
        }

        [Test]
        public void ATimedHopDodgesTheKingfisher()
        {
            var race = Race(2, gap: 30f);
            race.Items.Give(0, ItemType.Kingfisher);
            Tap(race, 0);
            int strikeIn = (int)System.MathF.Round(race.Items.Boats[1].KingfisherIncoming * 120f);
            Run(race, new BoatInput[2], strikeIn - 20);
            var hop = new BoatInput[2];
            hop[1].HopDrift = true;
            var seen = Run(race, hop, 40);
            Assert.IsTrue((seen[1] & ItemEvents.Dodged) != 0);
            Assert.IsFalse((seen[1] & ItemEvents.Hit) != 0);
        }

        [Test]
        public void AttacksCannotLaunchInTheFinal10Percent()
        {
            var race = Race(1, length: 300f, startZ: 275f);
            race.Items.Give(0, ItemType.Pike);
            Tap(race, 0);
            Assert.AreEqual(ItemType.Pike, race.Items.Boats[0].Held, "the pike stays in the slot");
            Assert.AreEqual(-1, FindObject(race.Items, ItemObjectKind.Pike));
        }

        [Test]
        public void KingfisherCannotLaunchInTheFinal20Percent()
        {
            var race = Race(2, length: 300f, startZ: 250f, gap: 20f);
            race.Items.Give(0, ItemType.Kingfisher);
            Tap(race, 0);
            Assert.AreEqual(ItemType.Kingfisher, race.Items.Boats[0].Held);
        }

        [Test]
        public void DamBurstSpeedsUpPlacesFiveToEight()
        {
            var race = Race(8, gap: 10f);
            int last = 0; // boat 0 starts furthest back
            race.Items.Give(0, ItemType.DamBurst);
            Tap(race, 0);
            for (int i = 0; i < 8; i++)
            {
                bool trailing = race.Status[i].Place >= 5;
                Assert.AreEqual(trailing ? 6f : 0f, race.Items.Boats[i].DamBurstTime, 0.05f, $"boat {i} in place {race.Status[i].Place}");
            }
            Run(race, new BoatInput[8], 1);
            Assert.AreEqual(1.2f, race.Sim.Modifiers[last].FlowMultiplier, 1e-5f);
            Assert.AreEqual(1f, race.Sim.Modifiers[7].FlowMultiplier, 1e-5f);
        }

        [Test]
        public void WakeBlasterSwampsTheBoatInTheSlot()
        {
            var race = Race(2, gap: 8f, speed: 20f);
            race.Items.Give(1, ItemType.WakeBlaster); // boat 1 leads by 8 m
            Tap(race, 1);
            var cruise = new[] { new BoatInput { Throttle = 1f }, new BoatInput { Throttle = 1f } };
            var seen = Run(race, cruise, 6);
            Assert.IsTrue((seen[0] & ItemEvents.Swamped) != 0);
            Assert.AreEqual(RaceSimulation.SwampedGripScale, race.Sim.Modifiers[0].GripScale, 1e-6f);
        }

        [Test]
        public void EnteringAWhirlpoolAlongItsSwirlSlingsAndCuttingAcrossSpins()
        {
            Assert.AreEqual(ItemEvents.Slung, WhirlEntry(tangential: true));
            Assert.AreEqual(ItemEvents.Hit, WhirlEntry(tangential: false));
        }

        [Test]
        public void ItemRacesReplayIdentically()
        {
            Assert.AreEqual(ItemRace(21), ItemRace(21));
        }

        // ---- Harness -------------------------------------------------------------------------------

        private static RaceTrack StraightTrack(float length) => new RaceTrack(new[] { new SimVec3(0, 0, 0), new SimVec3(0, 0, length) });

        /// <summary>
        /// A race already past its countdown on a straight river. Boat i starts <paramref name="gap"/> metres
        /// ahead of boat i-1, so the highest index leads.
        /// </summary>
        private static RaceSession Race(int boats, float flow = 0f, float length = 1500f, float startZ = 40f, float gap = 20f,
            float speed = 0f, ItemBuoy[] buoys = null)
        {
            var water = TestRivers.Uniform(flow, length: length + 300f);
            var tunings = new BoatTuning[boats];
            var starts = new BoatState[boats];
            for (int i = 0; i < boats; i++)
            {
                tunings[i] = TestRivers.Runabout();
                starts[i] = BoatState.At(new SimVec3(0f, 0f, startZ + gap * i), 0f);
                starts[i].Velocity = new SimVec3(0f, 0f, speed);
            }
            var rules = RaceRules.Default;
            rules.CountdownSeconds = 0f;
            rules.StuckSeconds = 1000f;
            var race = new RaceSession(new RaceSimulation(water, StraightTrack(length), tunings, starts), rules);
            race.Items = new ItemSystem(boats, buoys ?? System.Array.Empty<ItemBuoy>(), 5);
            // Let the hulls settle onto the water (and keep their start speed) before anything happens.
            for (int k = 0; k < 30; k++)
            {
                race.Step(new BoatInput[boats]);
                for (int i = 0; i < boats; i++)
                {
                    ref var b = ref race.Sim.State.Boats[i];
                    b.Position = new SimVec3(0f, b.Position.Y, startZ + gap * i);
                    b.Velocity = new SimVec3(0f, b.Velocity.Y, speed);
                }
            }
            return race;
        }

        private static ItemEvents[] Run(RaceSession race, BoatInput[] inputs, int ticks)
        {
            var seen = new ItemEvents[race.Sim.BoatCount];
            for (int k = 0; k < ticks; k++)
            {
                race.Step(inputs);
                for (int i = 0; i < seen.Length; i++) seen[i] |= race.Items.LastEvents[i];
            }
            return seen;
        }

        private static void Tap(RaceSession race, int boat)
        {
            var inputs = new BoatInput[race.Sim.BoatCount];
            inputs[boat] = Press;
            race.Step(inputs);
            race.Step(new BoatInput[race.Sim.BoatCount]);
        }

        private static void HoldButton(RaceSession race, int boat, float seconds)
        {
            var inputs = new BoatInput[race.Sim.BoatCount];
            inputs[boat] = Press;
            Run(race, inputs, (int)(seconds * 120));
            race.Step(new BoatInput[race.Sim.BoatCount]);
        }

        /// <summary>Taps boat <paramref name="boat"/>'s item, then runs, optionally with another boat still holding its button.</summary>
        private static ItemEvents[] Fire(RaceSession race, int boat, int ticks, int holdBoat = -1)
        {
            var inputs = new BoatInput[race.Sim.BoatCount];
            if (holdBoat >= 0) inputs[holdBoat].UseItem = true;
            inputs[boat].UseItem = true;
            var seen = Run(race, inputs, 1);
            inputs[boat].UseItem = false;
            var rest = Run(race, inputs, ticks);
            for (int i = 0; i < seen.Length; i++) seen[i] |= rest[i];
            return seen;
        }

        private static int FindObject(ItemSystem items, ItemObjectKind kind)
        {
            for (int k = 0; k < items.Objects.Length; k++)
                if (items.Objects[k].Kind == kind) return k;
            return -1;
        }

        private static float TurbineBoost(bool lane)
        {
            var race = Race(1);
            race.Sim.State.Boats[0].InCurrentLane = lane;
            race.Items.Give(0, ItemType.TurbineX1);
            var inputs = new BoatInput[1];
            inputs[0] = Press;
            race.Step(inputs);
            race.Sim.State.Boats[0].InCurrentLane = lane;
            inputs[0] = default;
            race.Items.PostStep(race, inputs); // release, read the boost the item set
            return race.Sim.State.Boats[0].BoostTime;
        }

        private static bool PikeAtBoatAhead(bool hop, float gap, out bool dodged)
        {
            var race = Race(2, gap: gap);
            race.Items.Give(0, ItemType.Pike);
            var inputs = new BoatInput[2];
            inputs[0] = Press;
            race.Step(inputs);
            inputs[0] = default;
            bool hit = false;
            dodged = false;
            for (int k = 0; k < 240; k++)
            {
                // The target hops as the pike closes in.
                int pike = FindObject(race.Items, ItemObjectKind.Pike);
                bool close = pike >= 0 && race.Sim.State.Boats[1].Position.Z - race.Items.Objects[pike].Position.Z < 14f;
                inputs[1].HopDrift = hop && close;
                race.Step(inputs);
                hit |= (race.Items.LastEvents[1] & ItemEvents.Hit) != 0;
                dodged |= (race.Items.LastEvents[1] & ItemEvents.Dodged) != 0;
            }
            return hit;
        }

        private static ItemEvents WhirlEntry(bool tangential)
        {
            var race = Race(1);
            var o = new ItemObject { Kind = ItemObjectKind.Whirlpool, Owner = -1, Target = -1, Position = new SimVec3(0f, 0f, 120f), Age = 1f, Life = 5f };
            race.Items.Objects[0] = o;
            ref var b = ref race.Sim.State.Boats[0];
            // Tangential: pass the pool's east side heading north (counter-clockwise). Radial: straight at its centre.
            b.Position = new SimVec3(tangential ? 5.5f : 0f, b.Position.Y, tangential ? 116f : 110f);
            b.Velocity = new SimVec3(0f, 0f, 15f);
            var seen = Run(race, new[] { new BoatInput { Throttle = 1f } }, 60);
            return seen[0] & (ItemEvents.Slung | ItemEvents.Hit);
        }

        private static ulong ItemRace(ulong seed)
        {
            var water = TestRivers.Uniform(1.5f, length: 2500f);
            var track = StraightTrack(2200f);
            var tunings = new BoatTuning[8];
            var starts = new BoatState[8];
            var drivers = new LineFollowerAI[8];
            var gunners = new ItemAI[8];
            for (int i = 0; i < 8; i++)
            {
                tunings[i] = TestRivers.Runabout();
                starts[i] = RaceGrid.Slot(track, i);
                drivers[i] = new LineFollowerAI(track) { LateralOffset = ((i % 3) - 1) * 4f, Throttle = 0.9f + 0.01f * i };
                gunners[i] = new ItemAI(i);
            }
            var race = new RaceSession(new RaceSimulation(water, track, tunings, starts));
            race.Items = new ItemSystem(8, ItemSystem.Layout(track, water, spacing: 250f), seed);
            var inputs = new BoatInput[8];
            int uses = 0;
            for (int k = 0; k < 120 * 120 && race.Phase != RacePhase.Finished; k++)
            {
                for (int i = 0; i < 8; i++)
                {
                    inputs[i] = drivers[i].Think(race.Sim.State.Boats[i]);
                    inputs[i].UseItem = gunners[i].Think(race, race.Items);
                }
                race.Step(inputs);
                for (int i = 0; i < 8; i++)
                    if ((race.Items.LastEvents[i] & ItemEvents.Used) != 0) uses++;
            }
            Assert.Greater(uses, 8, "AI should use items");
            Assert.AreEqual(8, race.FinishedCount);
            return race.Sim.StateHash();
        }
    }
}
