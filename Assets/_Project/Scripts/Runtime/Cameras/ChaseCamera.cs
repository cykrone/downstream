using Downstream.Boat;
using UnityEngine;

namespace Downstream.Cameras
{
    /// <summary>
    /// Low chase camera from the design doc: 70 degree field of view widening to 85 on boost,
    /// looking a little ahead of the boat. Follows the interpolated view, never the raw sim.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class ChaseCamera : MonoBehaviour
    {
        [SerializeField] private BoatView _target;
        [SerializeField] private float _distance = 7.5f;
        [SerializeField] private float _height = 2.6f;
        [SerializeField] private float _lookAhead = 6f;
        [SerializeField] private float _baseFov = 70f;
        [SerializeField] private float _boostFov = 85f;
        [SerializeField] private float _followSharpness = 8f;
        [SerializeField] private float _fovSharpness = 4f;

        private Camera _camera;

        public BoatView Target
        {
            get => _target;
            set => _target = value;
        }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            if (_target == null) return;
            var s = _target.State;
            var forward = s.FlatForward.ToUnity();
            var boatPos = s.Position.ToUnity();
            var desired = boatPos - forward * _distance + Vector3.up * _height;
            float k = 1f - Mathf.Exp(-_followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, k);
            transform.rotation = Quaternion.LookRotation(boatPos + forward * _lookAhead - transform.position, Vector3.up);

            float fov = s.BoostTime > 0f ? _boostFov : _baseFov;
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, fov, 1f - Mathf.Exp(-_fovSharpness * Time.deltaTime));
        }
    }
}
