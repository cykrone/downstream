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
    public class RacerAITests
    {
        /// <summary>A meandering river with a mid-channel rock, a ledge with a hole and a wave train.</summary>
        private static ProceduralRiverSettings Course()
        {
            var s = ProceduralRiverSettings.Default;
            s.Length = 1500f;
            s.Width = 34f;
            s.Boulders = new[] { new RiverBoulder { Distance = 420f, Lateral = 0f, Radius = 4f, EddyLength = 10f, EddyFlow = 1.5f } };
            s.Ledges = new[] { new RiverLedge { Distance = 800f, Drop = 0.15f, HoleLength = 5f, Lateral = -6f, Width = 12f } };
            s.WaveTrains = new[] { new StandingWaveTrain { Distance = 1100f, Count = 4, Wavelength = 8f, Height = 0.5f } };
            return s;
        }

        /// <summary>Tight bends that ask for more turn than the keel gives at speed.</summary>
        private static ProceduralRiverSettings Twisty()
        {
            var s = ProceduralRiverSettings.Default;
            s.Length = 1500f;
            s.Width = 40f;
            s.MeanderAmplitude = 45f;
            s.MeanderWavelength = 230f;
            return s;
        }

        private static RaceTrack Centreline(in ProceduralRiverSettings s)
        {
            int n = (int)(s.Length / 10f) + 1;
            var points = new SimVec3[n];
            for (int i = 0; i < n; i++)
            {
                float z = i * 10f;
                points[i] = new SimVec3(ProceduralRiver.CentreX(s, z), ProceduralRiver.SurfaceAt(s, z), z);
            }
            return new RaceTrack(points);
        }

        private static (RiverWater Water, RacingLineSet Lines) Build(in ProceduralRiverSettings s)
        {
            var water = new RiverWater(ProceduralRiver.Build(s));
            return (water, RacingLineSet.Build(Centreline(s), water));
        }

        private struct Run
        {
            public float Time;
            public int Made, Pending, Budget;
            public int DeepestDrift, Drifts;
            public BoatState Final;
        }

        /// <summary>One AI boat alone down the river to 1400 m. Fails on 5 s without progress or running aground.</summary>
        private static Run Drive(RacingLineSet lines, IWaterQuery water, AILevel level, ulong seed, RivalProfile? profile = null)
        {
            var tuning = TestRivers.Runabout();
            var ai = new RacerAI(lines, tuning, profile ?? RivalRoster.Get((int)seed), AIDifficulty.For(level), 0, seed);
            var boat = BoatState.At(new SimVec3(0f, 0f, 5f), 0f);
            float best = 0f;
            int sinceProgress = 0, dry = 0;
            for (int tick = 0; tick < 120 * 120; tick++)
            {
                var input = ai.Think(boat, tick);
                BoatSimulator.Step(ref boat, input.Quantized(), tuning, water, tick * BoatSimulator.TickDelta, BoatModifiers.None);
                if (ai.Progress > best + 1f) { best = ai.Progress; sinceProgress = 0; }
                else sinceProgress++;
                // Design acceptance: no AI boat stuck for more than 5 s.
                Assert.Less(sinceProgress, 5 * 120, $"{level} seed {seed} stuck at {ai.Progress:F1} m");
                // Aground: no water under the hull for a quarter second (a hop is shorter).
                dry = water.Sample(boat.Position.X, boat.Position.Z, 0f).IsWet ? 0 : dry + 1;
                Assert.Less(dry, 30, $"{level} seed {seed} ran aground at {ai.Progress:F1} m on the {ai.Line.Kind} line");
                if (best >= 1400f)
                {
                    return new Run
                    {
                        Time = tick * BoatSimulator.TickDelta,
                        Made = ai.MistakesMade,
                        Pending = ai.MistakesPending,
                        Budget = ai.MistakeBudgetCount,
                        DeepestDrift = ai.DeepestDriftTier,
                        Drifts = ai.Drifts,
                        Final = boat,
                    };
                }
            }
            Assert.Fail($"{level} seed {seed} did not finish");
            return default;
        }

        // ---- Racing lines ---------------------------------------------------------------------------------

        [Test]
        public void EveryLineStaysInNavigableWater()
        {
            foreach (var s in new[] { Course(), Twisty() })
            {
                var (water, lines) = Build(s);
                foreach (var line in lines.Lines)
                for (float d = 0f; d < lines.Track.Length; d += 2.5f)
                {
                    var p = lines.PointOn(line, d);
                    var w = water.Sample(p.X, p.Z, 0f);
                    Assert.IsTrue(w.IsWet && w.Depth >= lines.Settings.MinDepth, $"{line.Kind} line is dry at {d:F1} m");
                    Assert.AreEqual(0, (int)(w.Features & WaterFeature.HydraulicHole), $"{line.Kind} line runs into the hole at {d:F1} m");
                }
            }
        }

        [Test]
        public void LineFamiliesDoWhatTheirNamesSay()
        {
            var (_, lines) = Build(Twisty());
            var safe = lines.Get(RacingLineKind.Safe);
            var lane = lines.Get(RacingLineKind.CurrentLane);
            var aggressive = lines.Get(RacingLineKind.Aggressive);
            float length = lines.Track.Length - 50f;

            Assert.Less(aggressive.EstimatedTime(0f, length), safe.EstimatedTime(0f, length), "the aggressive line is quicker than the safe one");
            Assert.Less(lane.EstimatedTime(0f, length), safe.EstimatedTime(0f, length), "the current lane is quicker than the safe line");

            float safeFlow = 0f, laneFlow = 0f, inside = 0f;
            int bends = 0;
            for (int i = 0; i < safe.StationCount; i++)
            {
                safeFlow += safe.FlowAt(i);
                laneFlow += lane.FlowAt(i);
                float k = lines.CurvatureAt(i * lines.Spacing);
                if (SimMath.Abs(k) > 0.02f)
                {
                    bends++;
                    inside += SimMath.Sign(k) * aggressive.Lateral(i);
                }
            }
            Assert.Greater(laneFlow, safeFlow, "the current lane line rides faster water");
            Assert.Greater(bends, 0);
            Assert.Greater(inside / bends, 3f, "the aggressive line takes the inside of bends");
        }

        [Test]
        public void AMidChannelRockMakesAShortcutAroundTheOtherSide()
        {
            var (_, lines) = Build(Course());
            var shortcut = lines.Get(RacingLineKind.Shortcut);
            Assert.IsNotNull(shortcut, "a split channel gives a shortcut line");
            Assert.AreEqual(1, shortcut.Shortcuts.Count, "one rock, one fork");
            // The rock sits on the centreline: the two lines pass it on opposite sides.
            var section = shortcut.Shortcuts[0];
            float mid = 0.5f * (section.Start + section.End);
            var safe = lines.Get(RacingLineKind.Safe);
            Assert.Less(SimMath.Sign(safe.LateralAt(mid)) * SimMath.Sign(shortcut.LateralAt(mid)), 0f, "the shortcut passes the rock on the other side");

            var (_, plain) = Build(Twisty());
            Assert.IsNull(plain.Get(RacingLineKind.Shortcut), "no fork, no shortcut line");
        }

        // ---- Driving ----------------------------------------------------------------------------------------

        [Test]
        public void EveryDifficultyRunsTheCourseWithoutStallingOrBeaching()
        {
            foreach (var s in new[] { Course(), Twisty() })
            {
                var (water, lines) = Build(s);
                foreach (AILevel level in System.Enum.GetValues(typeof(AILevel)))
                for (ulong seed = 1; seed <= 3; seed++)
                    Drive(lines, water, level, seed);
            }
        }

        [Test]
        public void HarderAIIsFaster()
        {
            foreach (var s in new[] { Course(), Twisty() })
            {
                var (water, lines) = Build(s);
                float easy = 0f, expert = 0f;
                for (ulong seed = 1; seed <= 3; seed++)
                {
                    var plain = new RivalProfile { Name = "Test", Hull = HullType.Runabout };
                    easy += Drive(lines, water, AILevel.Easy, seed, plain).Time;
                    expert += Drive(lines, water, AILevel.Expert, seed, plain).Time;
                }
                Assert.Less(expert, easy, "Expert beats Easy over three seeds");
            }
        }

        [Test]
        public void AIDriftsBendsButNeverPastItsTier()
        {
            var (water, lines) = Build(Twisty());
            var easy = Drive(lines, water, AILevel.Easy, 1);
            var expert = Drive(lines, water, AILevel.Expert, 1);
            Assert.Greater(expert.Drifts, 0, "Expert drifts the tight bends");
            Assert.GreaterOrEqual(expert.DeepestDrift, 1);
            Assert.LessOrEqual(easy.DeepestDrift, AIDifficulty.For(AILevel.Easy).MaxDriftTier);
        }

        [Test]
        public void SameSeedDrivesTheSameRace()
        {
            var (water, lines) = Build(Course());
            var a = Drive(lines, water, AILevel.Normal, 7).Final;
            var b = Drive(lines, water, AILevel.Normal, 7).Final;
            Assert.AreEqual(a.Position.X, b.Position.X);
            Assert.AreEqual(a.Position.Z, b.Position.Z);
            Assert.AreEqual(a.Yaw, b.Yaw);
        }

        // ---- Mistakes and difficulty --------------------------------------------------------------------

        [Test]
        public void MistakeBudgetMatchesTheDifficultyTable()
        {
            foreach (AILevel level in System.Enum.GetValues(typeof(AILevel)))
            {
                var d = AIDifficulty.For(level);
                for (ulong seed = 1; seed <= 50; seed++)
                {
                    var budget = MistakeBudget.Create(seed, d, 4000f);
                    Assert.That(budget.Length, Is.InRange(d.MistakesMin, d.MistakesMax), $"{level} seed {seed}");
                    Assert.AreEqual(budget, MistakeBudget.Create(seed, d, 4000f), "fixed by the seed at race start");
                    foreach (var m in budget) Assert.That(m.Distance, Is.InRange(0.05f * 4000f, 0.8f * 4000f));
                }
            }
            Assert.AreEqual(6, AIDifficulty.For(AILevel.Easy).MistakesMin);
            Assert.AreEqual(8, AIDifficulty.For(AILevel.Easy).MistakesMax);
            Assert.AreEqual(1, AIDifficulty.For(AILevel.Expert).MistakesMax);
            Assert.AreEqual(42, AIDifficulty.For(AILevel.Easy).ReactionTicks, "350 ms at 120 Hz");
        }

        [Test]
        public void EveryBudgetedMistakeIsMadeOrStillDue()
        {
            var (water, lines) = Build(Twisty());
            for (ulong seed = 1; seed <= 3; seed++)
            {
                var r = Drive(lines, water, AILevel.Easy, seed);
                Assert.AreEqual(r.Budget, r.Made + r.Pending, "nothing is dropped or made twice");
                Assert.LessOrEqual(r.Pending, 1, "only a mistake waiting for a drift or a jump is still due at 1400 m");
            }
        }

        [Test]
        public void AdaptiveDifficultyMovesBetweenRacesOnly()
        {
            Assert.AreEqual(AILevel.Hard, AdaptiveDifficulty.Next(AILevel.Normal, 1, 8, false));
            Assert.AreEqual(AILevel.Hard, AdaptiveDifficulty.Next(AILevel.Hard, 1, 8, false), "Expert stays locked");
            Assert.AreEqual(AILevel.Expert, AdaptiveDifficulty.Next(AILevel.Hard, 1, 8, true));
            Assert.AreEqual(AILevel.Normal, AdaptiveDifficulty.Next(AILevel.Normal, 4, 8, false));
            Assert.AreEqual(AILevel.Easy, AdaptiveDifficulty.Next(AILevel.Normal, 7, 8, false));
            Assert.AreEqual(AILevel.Easy, AdaptiveDifficulty.Next(AILevel.Easy, 8, 8, false));
        }

        [Test]
        public void EasyAIUsesItemsLateAndNotAlways()
        {
            var water = TestRivers.Uniform(0f, length: 300f);
            var track = new RaceTrack(new[] { new SimVec3(0f, 0f, 0f), new SimVec3(0f, 0f, 300f) });
            var tuning = TestRivers.Runabout();
            int used = 0, firstUseTicks = int.MaxValue;
            for (ulong seed = 0; seed < 40; seed++)
            {
                var race = new RaceSession(new RaceSimulation(water, track, new[] { tuning }, new[] { BoatState.At(new SimVec3(0f, 0f, 20f), 0f) }));
                race.Items = new ItemSystem(1, System.Array.Empty<ItemBuoy>(), seed, ItemRuleset.Standard);
                var gunner = new ItemAI(0, seed);
                gunner.Apply(AIDifficulty.For(AILevel.Easy));
                var inputs = new BoatInput[1];
                while (race.Phase == RacePhase.Countdown) race.Step(inputs);
                race.Items.Give(0, ItemType.TurbineX1);
                for (int k = 0; k < 6 * 120; k++)
                {
                    inputs[0] = new BoatInput { Throttle = 1f, UseItem = gunner.Think(race, race.Items) };
                    race.Step(inputs);
                    if (race.Items.Boats[0].Held == ItemType.None)
                    {
                        used++;
                        firstUseTicks = SimMath.Min(firstUseTicks, k);
                        break;
                    }
                }
            }
            Assert.Greater(used, 10, "Easy still uses most items");
            Assert.Less(used, 40, "but sits on some for good");
            Assert.GreaterOrEqual(firstUseTicks, (int)(2.5f * 120) - 1, "and never before its patience runs out");
        }

        // ---- A full race ------------------------------------------------------------------------------------

        [Test]
        public void EightRivalsRaceTheCourseWithItems()
        {
            var (water, lines) = Build(Course());
            var track = lines.Track;
            var tunings = new BoatTuning[8];
            var starts = new BoatState[8];
            var drivers = new RacerAI[8];
            var gunners = new ItemAI[8];
            for (int i = 0; i < 8; i++)
            {
                var rival = RivalRoster.Get(i);
                tunings[i] = BoatTuning.Create(HullStats.For(rival.Hull), SpeedClass.Rapid);
                starts[i] = RaceGrid.Slot(track, i);
                var level = (AILevel)(i % 4);
                drivers[i] = new RacerAI(lines, tunings[i], rival, AIDifficulty.For(level), i, 99);
                gunners[i] = new ItemAI(i, 99);
                gunners[i].Apply(AIDifficulty.For(level));
            }
            var session = new RaceSession(new RaceSimulation(water, track, tunings, starts));
            session.Items = new ItemSystem(8, ItemSystem.Layout(track, water), 3);
            var inputs = new BoatInput[8];
            for (int k = 0; k < 180 * 120 && session.Phase != RacePhase.Finished; k++)
            {
                for (int i = 0; i < 8; i++)
                {
                    inputs[i] = drivers[i].Think(session.Sim, session.Items.Boats[i].Held);
                    inputs[i].UseItem = gunners[i].Think(session, session.Items);
                }
                session.Step(inputs);
            }

            Assert.AreEqual(8, session.FinishedCount, "every rival reaches the finish");
            int lineChanges = 0;
            for (int i = 0; i < 8; i++)
            {
                Assert.AreNotEqual(RespawnReason.Stuck, session.Status[i].LastRespawn, $"{RivalRoster.Get(i).Name} got stuck");
                lineChanges += drivers[i].LineChanges;
            }
            Assert.Greater(lineChanges, 8, "rivals switch lines as the race unfolds");
        }
    }
}
