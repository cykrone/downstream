using Downstream.Boat;
using Downstream.Core.Water;
using UnityEngine;

namespace Downstream.Cameras
{
    /// <summary>
    /// Low chase camera from the design doc: 70 degree field of view widening to 85 on boost,
    /// looking a little ahead of the boat. Follows the interpolated view, never the raw sim.
    /// It reads the water so it follows the river's grade: the look target sits on the surface
    /// ahead (so a downhill river stays in frame), the camera height is measured from the surface
    /// under the camera, and it lifts when the water ahead drops away to show the pool below a fall.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class ChaseCamera : MonoBehaviour
    {
        [SerializeField] private BoatView _target;
        [SerializeField] private float _distance = 9f;
        [SerializeField] private float _height = 3.6f;
        [SerializeField] private float _lookAhead = 12f;
        [Tooltip("Extra height per metre the surface drops between the boat and the look point.")]
        [SerializeField] private float _liftPerMetreOfDrop = 0.5f;
        [SerializeField] private float _maxLift = 7f;
        [SerializeField] private float _baseFov = 70f;
        [SerializeField] private float _boostFov = 85f;
        [SerializeField] private float _followSharpness = 8f;
        [SerializeField] private float _lookSharpness = 6f;
        [SerializeField] private float _fovSharpness = 4f;

        private Camera _camera;
        private Vector3 _lookPoint;
        private bool _hasLookPoint;

        public BoatView Target
        {
            get => _target;
            set { _target = value; _hasLookPoint = false; }
        }

        /// <summary>The race's water, so the camera can follow the river's surface. Set by the race director.</summary>
        public RiverWater Water { get; set; }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private float SurfaceAt(Vector3 p, float fallback)
        {
            if (Water == null) return fallback;
            var s = Water.Field.SampleStatic(p.x, p.z);
            return s.HasData ? s.SurfaceHeight : fallback;
        }

        private void LateUpdate()
        {
            if (_target == null) return;
            var s = _target.State;
            var forward = s.FlatForward.ToUnity();
            var boatPos = s.Position.ToUnity();
            float boatSurface = SurfaceAt(boatPos, boatPos.y);

            // Look at the water ahead, not at the boat's own level: on a grade this pitches the
            // camera down the river so the course and the banks stay in frame.
            var ahead = boatPos + forward * _lookAhead;
            float aheadSurface = SurfaceAt(ahead, boatSurface);
            var lookTarget = new Vector3(ahead.x, aheadSurface + 1.2f, ahead.z);

            var behind = boatPos - forward * _distance;
            float behindSurface = Mathf.Max(SurfaceAt(behind, boatSurface), boatSurface);

            // Lift over drops (falls, ledges) so the pool below is visible before committing. The river's
            // own grade, estimated from the surface behind the boat, is not a drop.
            float expectedDrop = Mathf.Max(0f, (behindSurface - boatSurface) / _distance * _lookAhead);
            float drop = Mathf.Max(0f, boatSurface - aheadSurface - expectedDrop - (boatPos.y - boatSurface));
            float lift = Mathf.Clamp(drop * _liftPerMetreOfDrop, 0f, _maxLift);
            var desired = new Vector3(behind.x, behindSurface + _height + lift, behind.z);

            float k = 1f - Mathf.Exp(-_followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, k);
            if (!_hasLookPoint) { _lookPoint = lookTarget; _hasLookPoint = true; }
            _lookPoint = Vector3.Lerp(_lookPoint, lookTarget, 1f - Mathf.Exp(-_lookSharpness * Time.deltaTime));
            transform.rotation = Quaternion.LookRotation(_lookPoint - transform.position, Vector3.up);

            float fov = s.BoostTime > 0f ? _boostFov : _baseFov;
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, fov, 1f - Mathf.Exp(-_fovSharpness * Time.deltaTime));
        }
    }
}
