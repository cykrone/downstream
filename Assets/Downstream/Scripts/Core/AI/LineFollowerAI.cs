using Downstream.Core.Boat;
using Downstream.Core.Math;
using Downstream.Core.Race;

namespace Downstream.Core.AI
{
    /// <summary>
    /// The first, simplest AI driver: follows the river centreline with a speed-scaled look-ahead.
    /// Like every AI in the design, it only produces a <see cref="BoatInput"/>; it never touches
    /// boat state, so it cannot cheat. Racing lines, utility selection, personalities and the
    /// seeded mistake budget build on top of this.
    /// </summary>
    public sealed class LineFollowerAI
    {
        private readonly RaceTrack _track;
        private int _hint;

        public float LookAheadSeconds { get; set; } = 1.1f;
        public float MinLookAhead { get; set; } = 10f;
        public float SteerGain { get; set; } = 2.2f;
        /// <summary>Yaw-rate damping: the keel scrubs speed on every correction, so the follower settles instead of hunting.</summary>
        public float YawDamping { get; set; } = 0.35f;
        public float LateralOffset { get; set; }
        public float Throttle { get; set; } = 1f;

        public LineFollowerAI(RaceTrack track)
        {
            _track = track;
        }

        public BoatInput Think(in BoatState boat)
        {
            float progress = _track.Project(boat.Position, ref _hint);
            float speed = boat.Velocity.Flat.Magnitude;
            float ahead = SimMath.Max(MinLookAhead, speed * LookAheadSeconds);
            var target = _track.PointAt(progress + ahead);
            if (LateralOffset != 0f)
            {
                var next = _track.PointAt(progress + ahead + 1f);
                var dir = (next - target).Flat.Normalized;
                target += new SimVec3(dir.Z, 0f, -dir.X) * LateralOffset;
            }

            var to = (target - boat.Position).Flat;
            float desiredYaw = SimMath.Atan2(to.X, to.Z);
            float error = SimMath.WrapAngle(desiredYaw - boat.Yaw);
            return new BoatInput
            {
                Throttle = Throttle,
                Steer = SimMath.Clamp(error * SteerGain - boat.YawRate * YawDamping, -1f, 1f),
            };
        }
    }
}
