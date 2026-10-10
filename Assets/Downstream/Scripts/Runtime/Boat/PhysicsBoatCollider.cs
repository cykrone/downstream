using Downstream.Core.Boat;
using Downstream.Core.Math;
using UnityEngine;

namespace Downstream.Boat
{
    /// <summary>
    /// World collision for the sim using PhysX queries only: a capsule proxy is pushed out of static
    /// colliders with Physics.ComputePenetration, and velocity into the contact is removed so boats
    /// slide along banks (design: glancing hits slide, only head-on hits stop). Never used in the
    /// deterministic time-trial path, which will collide against baked bank data instead.
    /// </summary>
    public sealed class PhysicsBoatCollider : IBoatCollider
    {
        private readonly CapsuleCollider _proxy;
        private readonly Collider[] _hits = new Collider[16];
        private readonly int _mask;

        public PhysicsBoatCollider(CapsuleCollider proxy, LayerMask worldMask)
        {
            _proxy = proxy;
            _mask = worldMask;
        }

        public bool Resolve(ref SimVec3 position, ref SimVec3 velocity, float yaw, in BoatTuning tuning, out float impactSpeed)
        {
            impactSpeed = 0f;
            var pos = position.ToUnity();
            var rot = Quaternion.Euler(0f, yaw * SimMath.Rad2Deg, 0f);
            float half = Mathf.Max(0f, tuning.HullLength * 0.5f - _proxy.radius);
            var axis = rot * Vector3.forward * half;
            int count = Physics.OverlapCapsuleNonAlloc(pos - axis, pos + axis, _proxy.radius, _hits, _mask, QueryTriggerInteraction.Ignore);
            bool touched = false;
            for (int i = 0; i < count; i++)
            {
                var other = _hits[i];
                if (other == _proxy) continue;
                if (!Physics.ComputePenetration(_proxy, pos, rot, other, other.transform.position, other.transform.rotation,
                        out var dir, out float distance)) continue;
                pos += dir * distance;
                var n = dir.ToSim();
                float into = SimVec3.Dot(velocity, n);
                if (into < 0f)
                {
                    impactSpeed = Mathf.Max(impactSpeed, -into);
                    velocity -= n * (into * (1f + tuning.WallRestitution));
                }
                touched = true;
            }
            position = pos.ToSim();
            return touched;
        }
    }
}
