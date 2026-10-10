using Downstream.Core.Boat;
using UnityEngine;

namespace Downstream.Boat
{
    /// <summary>
    /// Procedural pilot animation driven only by the presented <see cref="BoatState"/>: a kayak stroke
    /// cycle whose rate follows the boat's speed (blades dip alternately, the shaft sweeps fore and aft,
    /// the arms follow the grips), a torso that twists with the stroke, leans forward with speed and
    /// into a drift, a head that stays level and looks into the turn, and a raised paddle in the air.
    /// </summary>
    public sealed class BoatAnimator : MonoBehaviour
    {
        [SerializeField] private Transform _torso;
        [SerializeField] private Transform _head;
        [SerializeField] private Transform _paddle;
        [SerializeField] private Transform _armL;
        [SerializeField] private Transform _armR;
        [SerializeField] private float _fullStrokeSpeed = 18f;
        [SerializeField] private float _gripHalfWidth = BlockBoat.PaddleGripHalf;

        private Vector3 _torsoRest, _headRest, _shoulderL, _shoulderR;
        private float _phase;
        private float _lean, _twist, _pitch, _raise;
        private bool _ready;

        /// <summary>Called by the scene builder when the prefab is assembled.</summary>
        public void Configure(Transform torso, Transform head, Transform paddle, Transform armL, Transform armR)
        {
            _torso = torso; _head = head; _paddle = paddle; _armL = armL; _armR = armR;
        }

        private void Awake()
        {
            if (_torso == null || _paddle == null) return;
            _torsoRest = _torso.localPosition;
            _headRest = _head != null ? _head.localPosition : Vector3.zero;
            _shoulderL = _armL != null ? _armL.localPosition : Vector3.zero;
            _shoulderR = _armR != null ? _armR.localPosition : Vector3.zero;
            _ready = true;
        }

        public void Apply(in BoatState s, float dt)
        {
            if (!_ready) return;
            var vel = s.Velocity.ToUnity();
            float speed = vel.magnitude;
            float k = Mathf.Clamp01(speed / _fullStrokeSpeed);
            bool boosting = s.BoostTime > 0f;
            bool airborne = s.Airborne;

            // Stroke rate: a lazy paddle at rest, a sprint at top speed, faster still on a boost.
            float hz = airborne ? 0f : (0.45f + 1.5f * k) * (boosting ? 1.35f : 1f);
            _phase += dt * hz;
            float a = _phase * Mathf.PI * 2f;
            float dip = Mathf.Sin(a);      // +1: right blade in the water
            float sweep = Mathf.Cos(a);    // +1: shaft forward, about to plant; -1: pulled back

            // Targets, smoothed so sim-side flips (drift start, landing) read as a body reacting, not snapping.
            float leanTarget = -s.DriftDirection * 14f + Mathf.Clamp(s.YawRate * Mathf.Rad2Deg * 0.08f, -8f, 8f) - 6f * dip * (1f - _raise); // and into the stroke
            float pitchTarget = 4f + 9f * k + (boosting ? 5f : 0f) - (airborne ? 10f : 0f);
            float twistTarget = airborne ? 0f : 9f * dip;
            float raiseTarget = airborne ? 1f : 0f;
            float blend = 1f - Mathf.Exp(-8f * dt);
            _lean = Mathf.Lerp(_lean, leanTarget, blend);
            _pitch = Mathf.Lerp(_pitch, pitchTarget, blend);
            _twist = Mathf.Lerp(_twist, twistTarget, blend);
            _raise = Mathf.Lerp(_raise, raiseTarget, blend);

            // Torso: pitch forward, roll into the drift, twist with the stroke, bob with the pull.
            _torso.localRotation = Quaternion.Euler(_pitch, _twist, _lean);
            _torso.localPosition = _torsoRest + new Vector3(0f, -0.015f * (1f - sweep) * (1f - _raise), 0.02f * sweep);

            // Head: counters the torso roll to stay level, looks into the turn, nods a little with the stroke.
            if (_head != null)
            {
                float look = Mathf.Clamp(s.YawRate * Mathf.Rad2Deg * 0.25f, -20f, 20f) + s.DriftDirection * 10f;
                _head.localRotation = Quaternion.Euler(-_pitch * 0.5f + 2f * dip, look, -_lean * 0.7f);
                _head.localPosition = _headRest;
            }

            // Paddle: solved in the pilot-root frame so the torso's lean and twist never tilt it into the
            // hull, then expressed under the torso (its parent).
            var pose = SolvePaddle(dip, sweep, _raise);
            var inv = Quaternion.Inverse(_torso.localRotation);
            _paddle.localRotation = inv * pose.Rotation;
            _paddle.localPosition = inv * (pose.Position - _torso.localPosition);

            // Arms: a straight segment from each shoulder to its grip on the shaft.
            Reach(_armL, _shoulderL, _paddle.TransformPoint(new Vector3(-_gripHalfWidth, 0f, 0f)));
            Reach(_armR, _shoulderR, _paddle.TransformPoint(new Vector3(_gripHalfWidth, 0f, 0f)));
        }

        /// <summary>Paddle pose in the pilot-root (hips) frame.</summary>
        public struct PaddlePose { public Vector3 Position; public Quaternion Rotation; }

        /// <summary>Grip centre to a blade tip.</summary>
        public const float ShaftHalfLength = BlockBoat.PaddleShaft * 0.5f + 0.23f;
        /// <summary>Along the shaft from the grip centre where the blade begins.</summary>
        public const float BladeStart = BlockBoat.PaddleShaft * 0.5f - 0.23f;
        public const float ShaftRadius = 0.035f;
        public const float BladeHalfWidth = 0.13f;
        /// <summary>Gap kept between any part of the paddle and the hull section.</summary>
        public const float HullClearance = 0.10f;

        /// <summary>
        /// The stroke: the shaft rolls the dipped blade down (48 degrees at full dip) and slides half a metre to
        /// that side so it reaches the water outboard of the gunwale, the blades sweep fore and aft, and the
        /// whole paddle lifts level when the boat is in the air. Then every sample from the grip to the dipped
        /// tip is tested against the hull section at its height and station, and the paddle is pushed
        /// outboard by the largest shortfall, so the blade's arc stays outside the hull by the clearance.
        /// Mirrored: the left stroke (dip &lt; 0) half a cycle later is the right stroke reflected in x.
        /// </summary>
        public static PaddlePose SolvePaddle(float dip, float sweep, float raise)
        {
            float side = dip >= 0f ? 1f : -1f;
            float m = Mathf.Abs(dip);
            float roll = Mathf.Lerp(-48f * dip, 0f, raise);
            float yaw = Mathf.Lerp(-24f * sweep, 0f, raise);
            float feather = Mathf.Lerp(-12f * sweep * side, -35f, raise); // about x, so by stroke progress: the same twist on either side
            var rot = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, roll) * Quaternion.Euler(feather, 0f, 0f);
            var pos = BlockBoat.GripFromHips + new Vector3(
                0.45f * dip * (1f - raise),
                0.10f * raise - 0.04f * m * (1f - raise),
                -0.10f * (1f - sweep * side) * 0.5f * (1f - raise)); // the pull slides the paddle aft on either side
            var dir = rot * new Vector3(side, 0f, 0f);
            var up = rot * Vector3.up;
            var fwd = rot * Vector3.forward;
            float shift = 0f;
            const int samples = 36;
            for (int i = 1; i <= samples; i++)
            {
                float along = ShaftHalfLength * i / samples;
                float radius = along >= BladeStart ? BladeHalfWidth : ShaftRadius;
                var axis = pos + dir * along;
                // Eight points around the shaft (or the blade's extent) at this station: an edge lower than
                // the axis can sit at a wider part of the section, so each is tested at its own height.
                for (int q = 0; q < 8; q++)
                {
                    float ang = q * Mathf.PI * 0.25f;
                    // Pilot units to hull space: the pilot is scaled as a whole about the hips.
                    var hull = (axis + (up * Mathf.Cos(ang) + fwd * Mathf.Sin(ang)) * radius) * BlockBoat.PilotScale + BlockBoat.Hips;
                    float section = BlockBoat.HalfWidthAt(hull.y, hull.z);
                    if (section <= 0f) continue;
                    float deficit = (section + HullClearance - Mathf.Abs(hull.x)) / BlockBoat.PilotScale;
                    if (deficit > shift) shift = deficit;
                }
            }
            pos.x += side * shift;
            return new PaddlePose { Position = pos, Rotation = rot };
        }

        private void Reach(Transform arm, Vector3 shoulderLocal, Vector3 handWorld)
        {
            if (arm == null) return;
            var parent = arm.parent;
            var shoulderWorld = parent.TransformPoint(shoulderLocal);
            var d = handWorld - shoulderWorld;
            float len = Mathf.Max(d.magnitude, 0.05f);
            arm.position = shoulderWorld;
            arm.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
            var ls = arm.localScale;
            arm.localScale = new Vector3(ls.x, len / parent.lossyScale.y, ls.z);
            // The sleeve is a child of the stretched arm: counter-scale it to a fixed 0.24 m at the shoulder.
            if (arm.childCount > 0)
            {
                var sleeve = arm.GetChild(0);
                float inv = parent.lossyScale.y / len;
                sleeve.localScale = new Vector3(1f, 0.24f * inv, 1f);
                sleeve.localPosition = new Vector3(0f, 0.01f * inv, 0f);
            }
        }
    }
}
