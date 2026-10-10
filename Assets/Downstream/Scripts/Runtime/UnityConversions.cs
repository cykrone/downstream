using Downstream.Core.Boat;
using Downstream.Core.Math;
using UnityEngine;

namespace Downstream
{
    /// <summary>The only place sim types meet UnityEngine types.</summary>
    public static class UnityConversions
    {
        public static Vector3 ToUnity(this SimVec3 v) => new Vector3(v.X, v.Y, v.Z);

        public static SimVec3 ToSim(this Vector3 v) => new SimVec3(v.x, v.y, v.z);

        /// <summary>The sim uses pitch positive = nose up and roll positive = right side down; Unity's Euler signs are the opposite.</summary>
        public static Quaternion BoatRotation(in BoatState s) =>
            Quaternion.Euler(-s.Pitch * SimMath.Rad2Deg, s.Yaw * SimMath.Rad2Deg, -s.Roll * SimMath.Rad2Deg);
    }
}
