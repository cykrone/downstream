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
        [SerializeField] private float _gripHalfWidth = 0.36f;

        private Vector3 _torsoRest, _headRest, _paddleRest, _shoulderL, _shoulderR;
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
            _paddleRest = _paddle.localPosition;
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
            float leanTarget = -s.DriftDirection * 14f + Mathf.Clamp(s.YawRate * Mathf.Rad2Deg * 0.08f, -8f, 8f);
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

            // Paddle: roll dips a blade, pitch sweeps the shaft, the whole thing slides back on the pull
            // and lifts clear when the boat is in the air.
            float roll = Mathf.Lerp(-46f * dip, 0f, _raise);
            float pitch = Mathf.Lerp(-12f * sweep, -35f, _raise);
            _paddle.localRotation = Quaternion.Euler(pitch, -6f * dip, roll);
            _paddle.localPosition = _paddleRest + new Vector3(0.42f * dip * (1f - _raise), 0.08f * _raise + 0.03f * Mathf.Abs(dip), -0.14f * (1f - sweep) * 0.5f * (1f - _raise));

            // Arms: a straight segment from each shoulder to its grip on the shaft.
            Reach(_armL, _shoulderL, _paddle.TransformPoint(new Vector3(-_gripHalfWidth, 0f, 0f)));
            Reach(_armR, _shoulderR, _paddle.TransformPoint(new Vector3(_gripHalfWidth, 0f, 0f)));
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
