using Downstream.Core.Water;
using NUnit.Framework;

namespace Downstream.Core.Tests
{
    public class WaterTests
    {
        [Test]
        public void BilinearSampleBlendsNeighbouringTexels()
        {
            var field = new RiverField(0f, 0f, 1, 1);
            field.SetTexel(0, 0, 1f, 0f, 0f, 2f, 0f, WaterFeature.None);
            field.SetTexel(1, 0, 1f, 0f, 0f, 4f, 0f, WaterFeature.None);
            field.SetTexel(0, 1, 1f, 0f, 0f, 2f, 0f, WaterFeature.None);
            field.SetTexel(1, 1, 1f, 0f, 0f, 4f, 0f, WaterFeature.None);
            // Halfway between the texel centres at x = 0.25 and x = 0.75.
            var s = field.SampleStatic(0.5f, 0.5f);
            Assert.IsTrue(s.HasData);
            Assert.AreEqual(3f, s.FlowZ, 1e-5f);
        }

        [Test]
        public void PointsOutsideEveryTileAreDry()
        {
            var water = TestRivers.Uniform(1f);
            Assert.IsFalse(water.Sample(5000f, 5000f, 0f).IsWet);
            Assert.IsTrue(water.Sample(0f, 100f, 0f).IsWet);
        }

        [Test]
        public void SparseTilesOnlyCoverTheRiver()
        {
            var s = ProceduralRiverSettings.Default;
            s.Length = 6000f;
            s.Width = 40f;
            s.MeanderAmplitude = 60f;
            var field = ProceduralRiver.Build(s);
            int fullGrid = field.TilesX * field.TilesZ;
            Assert.Less(field.TileCount, fullGrid);
            // Design estimate: 20-60 MB per track for a 6 km river (stored as halves on disk; floats here).
            Assert.Less(field.MemoryBytes, 120L * 1024 * 1024);
        }

        [Test]
        public void FloodPulseRaisesTheWaterAndFloodsTheBank()
        {
            var s = ProceduralRiverSettings.Default;
            s.Gradient = 0f;
            s.MeanderAmplitude = 0f;
            s.FloodableBank = 10f;
            var layers = new WaterLayers
            {
                Floods = new[] { new FloodPulse { StartTime = 10f, StartDistance = 0f, Speed = 20f, Rise = 1.2f, Length = 50f, FlowMultiplier = 1.5f } },
            };
            var water = new RiverWater(ProceduralRiver.Build(s), layers);
            float bankX = s.Width * 0.5f + 5f;

            Assert.IsFalse(water.Sample(bankX, 200f, 0f).IsWet, "bank is dry before the flood");
            // Front reaches 200 m at t = 20 s; fully risen 50 m later (t = 22.5 s).
            Assert.IsFalse(water.Sample(bankX, 200f, 19f).IsWet, "front has not arrived yet");
            Assert.IsTrue(water.Sample(bankX, 200f, 25f).IsWet, "bank floods once the pulse passes");

            float before = water.Sample(0f, 200f, 0f).SurfaceHeight;
            float after = water.Sample(0f, 200f, 25f).SurfaceHeight;
            Assert.AreEqual(1.2f, after - before, 1e-4f);
        }

        [Test]
        public void FloodPulseReachesTrailersBeforeLeaders()
        {
            var pulse = new FloodPulse { StartTime = 0f, StartDistance = 0f, Speed = 15f, Rise = 1f, Length = 30f, FlowMultiplier = 1.2f };
            Assert.Greater(pulse.Strength(100f, 10f), pulse.Strength(140f, 10f));
        }

        [Test]
        public void TideTurnsFromEbbToFlood()
        {
            var tide = new TideClock { Enabled = true, TurnTime = 90f, TransitionSeconds = 20f, LowLevel = -0.5f, HighLevel = 0.8f, EbbFlowScale = 1.3f, FloodFlowScale = -0.4f };
            Assert.AreEqual(1.3f, tide.FlowScale(10f), 1e-5f);
            Assert.AreEqual(-0.4f, tide.FlowScale(200f), 1e-5f);
            Assert.AreEqual(0.8f, tide.Level(200f), 1e-5f);
        }

        [Test]
        public void WaterIsAPureFunctionOfPositionAndTime()
        {
            var water = new RiverWater(ProceduralRiver.Build(ProceduralRiverSettings.Default), new WaterLayers
            {
                Waves = new[] { new GerstnerWave { DirectionX = 1f, DirectionZ = 0.5f, Wavelength = 9f, Amplitude = 0.3f, Speed = 3f } },
            });
            var a = water.Sample(3.3f, 120.7f, 42.5f);
            var b = water.Sample(3.3f, 120.7f, 42.5f);
            Assert.AreEqual(a.SurfaceHeight, b.SurfaceHeight);
            Assert.AreEqual(a.Flow, b.Flow);
        }

        [Test]
        public void CurrentLaneIsFlaggedAndFaster()
        {
            var s = ProceduralRiverSettings.Default;
            s.MeanderAmplitude = 0f;
            var water = new RiverWater(ProceduralRiver.Build(s));
            var lane = water.Sample(0f, 100f, 0f);
            var edge = water.Sample(s.Width * 0.5f - 2f, 100f, 0f);
            Assert.IsTrue((lane.Features & WaterFeature.CurrentLane) != 0);
            Assert.IsFalse((edge.Features & WaterFeature.CurrentLane) != 0);
            Assert.Greater(lane.Flow.Z, edge.Flow.Z);
        }
    }
}
