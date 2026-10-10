using Downstream.Core.Boat;
using Downstream.Core.Math;

namespace Downstream.Core.Race
{
    /// <summary>The starting grid: two columns, staggered 8 m apart, facing downstream.</summary>
    public static class RaceGrid
    {
        public const float FrontRowDistance = 30f;
        public const float RowSpacing = 8f;
        public const float ColumnOffset = 3f;

        /// <summary>Grid slot <paramref name="index"/> (0 = pole) on <paramref name="track"/>.</summary>
        public static BoatState Slot(RaceTrack track, int index)
        {
            float distance = FrontRowDistance - (index / 2) * RowSpacing;
            var p = track.PointAt(distance);
            var ahead = track.PointAt(distance + 2f);
            var dir = (ahead - p).Flat.Normalized;
            var side = new SimVec3(dir.Z, 0f, -dir.X) * ((index % 2 == 0) ? -ColumnOffset : ColumnOffset);
            return BoatState.At(p + side, SimMath.Atan2(dir.X, dir.Z));
        }
    }
}
