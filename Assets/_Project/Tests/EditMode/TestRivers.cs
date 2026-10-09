using Downstream.Core.Boat;
using Downstream.Core.Math;
using Downstream.Core.Water;

namespace Downstream.Core.Tests
{
    /// <summary>Small procedural rivers shared by the tests.</summary>
    internal static class TestRivers
    {
        /// <summary>A wide, straight, level channel along +Z with uniform flow everywhere.</summary>
        public static RiverWater Uniform(float flow, float width = 120f, float length = 1500f, bool lane = false)
        {
            var s = ProceduralRiverSettings.Default;
            s.Length = length;
            s.Width = width;
            s.Depth = 3f;
            s.Gradient = 0f;
            s.MeanderAmplitude = 0f;
            s.BaseFlow = 0f;
            s.LaneExtraFlow = lane ? flow : 0f;
            s.LaneWidth = lane ? width * 2f : 0f;
            if (!lane) s.BaseFlow = flow;
            var field = ProceduralRiver.Build(s);
            if (!lane && flow != 0f)
            {
                // Make the base flow uniform across the channel (the builder tapers it at the banks).
                for (int iz = 0; iz < field.TexelCountZ; iz++)
                for (int ix = 0; ix < field.TexelCountX; ix++)
                {
                    field.TexelCentre(ix, iz, out float x, out float z);
                    var st = field.SampleStatic(x, z);
                    if (!st.HasData || st.BedHeight > st.SurfaceHeight) continue;
                    field.SetTexel(ix, iz, st.SurfaceHeight, st.BedHeight, 0f, flow, st.RiverDistance, st.Features);
                }
            }
            return new RiverWater(field);
        }

        public static BoatTuning Runabout(SpeedClass c = SpeedClass.Rapid) => BoatTuning.Create(HullStats.For(HullType.Runabout), c);

        /// <summary>Runs ticks with a constant input and returns the final state.</summary>
        public static BoatState Run(BoatState s, BoatInput input, BoatTuning t, IWaterQuery water, int ticks, ref int tick)
        {
            for (int i = 0; i < ticks; i++, tick++)
                BoatSimulator.Step(ref s, input.Quantized(), t, water, tick * BoatSimulator.TickDelta, BoatModifiers.None);
            return s;
        }

        public static BoatState Afloat(float z = 20f) => BoatState.At(new SimVec3(0f, 0f, z), 0f);
    }
}
