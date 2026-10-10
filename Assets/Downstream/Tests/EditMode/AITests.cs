using Downstream.Core.AI;
using Downstream.Core.Boat;
using Downstream.Core.Math;
using Downstream.Core.Race;
using Downstream.Core.Water;
using NUnit.Framework;

namespace Downstream.Core.Tests
{
    public class AITests
    {
        [Test]
        public void AIRunsAMeanderingRiverWithoutGettingStuck()
        {
            var settings = ProceduralRiverSettings.Default;
            settings.Length = 1500f;
            var water = new RiverWater(ProceduralRiver.Build(settings));
            var points = new SimVec3[151];
            for (int i = 0; i < points.Length; i++)
            {
                float z = i * 10f;
                points[i] = new SimVec3(ProceduralRiver.CentreX(settings, z), 0f, z);
            }
            var track = new RaceTrack(points);
            var ai = new LineFollowerAI(track);
            var tuning = TestRivers.Runabout();
            var boat = BoatState.At(new SimVec3(0f, 0f, 5f), 0f);
            int hint = 0;
            float bestProgress = 0f;
            int ticksSinceProgress = 0;
            float halfWidth = settings.Width * 0.5f;

            for (int tick = 0; tick < 120 * 90 && bestProgress < 1400f; tick++)
            {
                var input = ai.Think(boat);
                BoatSimulator.Step(ref boat, input.Quantized(), tuning, water, tick * BoatSimulator.TickDelta, BoatModifiers.None);
                float p = track.Project(boat.Position, ref hint);
                if (p > bestProgress + 1f) { bestProgress = p; ticksSinceProgress = 0; }
                else ticksSinceProgress++;
                // Design acceptance: no AI boat stuck for more than 5 s.
                Assert.Less(ticksSinceProgress, 5 * 120, $"stuck at {p:F1} m");
                float off = SimMath.Abs(boat.Position.X - ProceduralRiver.CentreX(settings, boat.Position.Z));
                Assert.Less(off, halfWidth, $"left the river at {p:F1} m");
            }
            Assert.GreaterOrEqual(bestProgress, 1400f);
        }
    }
}
