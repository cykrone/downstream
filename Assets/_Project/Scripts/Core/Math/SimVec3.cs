using System;

namespace Downstream.Core.Math
{
    /// <summary>
    /// Plain scalar 3D vector used by the gameplay simulation. The core never touches
    /// UnityEngine types so it can be tested outside the editor and kept deterministic:
    /// every operation is written out component by component, with no SIMD paths.
    /// Axes follow Unity: +X right, +Y up, +Z forward.
    /// </summary>
    [Serializable]
    public struct SimVec3 : IEquatable<SimVec3>
    {
        public float X;
        public float Y;
        public float Z;

        public SimVec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly SimVec3 Zero = new SimVec3(0f, 0f, 0f);
        public static readonly SimVec3 Up = new SimVec3(0f, 1f, 0f);
        public static readonly SimVec3 Forward = new SimVec3(0f, 0f, 1f);
        public static readonly SimVec3 Right = new SimVec3(1f, 0f, 0f);

        public static SimVec3 operator +(SimVec3 a, SimVec3 b) => new SimVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static SimVec3 operator -(SimVec3 a, SimVec3 b) => new SimVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static SimVec3 operator -(SimVec3 a) => new SimVec3(-a.X, -a.Y, -a.Z);
        public static SimVec3 operator *(SimVec3 a, float s) => new SimVec3(a.X * s, a.Y * s, a.Z * s);
        public static SimVec3 operator *(float s, SimVec3 a) => new SimVec3(a.X * s, a.Y * s, a.Z * s);
        public static SimVec3 operator /(SimVec3 a, float s) => new SimVec3(a.X / s, a.Y / s, a.Z / s);

        public static float Dot(SimVec3 a, SimVec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static SimVec3 Cross(SimVec3 a, SimVec3 b) =>
            new SimVec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public float SqrMagnitude => X * X + Y * Y + Z * Z;
        public float Magnitude => SimMath.Sqrt(SqrMagnitude);

        public SimVec3 Normalized
        {
            get
            {
                float m = Magnitude;
                return m > 1e-6f ? this / m : Zero;
            }
        }

        /// <summary>The horizontal (XZ) part of the vector, with Y set to zero.</summary>
        public SimVec3 Flat => new SimVec3(X, 0f, Z);

        public static SimVec3 Lerp(SimVec3 a, SimVec3 b, float t) =>
            new SimVec3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

        public bool Equals(SimVec3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is SimVec3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
    }
}
