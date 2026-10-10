namespace Downstream.Core.Math
{
    /// <summary>
    /// Every transcendental the simulation uses goes through here. Today these forward to
    /// System.MathF, which is consistent for one x64 build. If leaderboard re-simulation ever
    /// has to match across runtimes (Mono editor vs IL2CPP player vs the server), swap these
    /// bodies for table- or polynomial-based versions without touching any caller.
    /// </summary>
    public static class SimMath
    {
        public const float Pi = 3.14159265358979f;
        public const float Deg2Rad = Pi / 180f;
        public const float Rad2Deg = 180f / Pi;

        public static float Sqrt(float x) => System.MathF.Sqrt(x);
        public static float Sin(float x) => System.MathF.Sin(x);
        public static float Cos(float x) => System.MathF.Cos(x);
        public static float Atan2(float y, float x) => System.MathF.Atan2(y, x);
        public static float Abs(float x) => x < 0f ? -x : x;
        public static float Sign(float x) => x > 0f ? 1f : (x < 0f ? -1f : 0f);
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Clamp(float x, float lo, float hi) => x < lo ? lo : (x > hi ? hi : x);
        public static float Clamp01(float x) => Clamp(x, 0f, 1f);
        public static int FloorToInt(float x) => (int)System.MathF.Floor(x);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Moves <paramref name="current"/> towards <paramref name="target"/> by at most <paramref name="maxDelta"/>.</summary>
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            float d = target - current;
            if (Abs(d) <= maxDelta) return target;
            return current + Sign(d) * maxDelta;
        }

        /// <summary>Wraps an angle in radians to the range (-pi, pi].</summary>
        public static float WrapAngle(float a)
        {
            const float twoPi = 2f * Pi;
            a %= twoPi;
            if (a > Pi) a -= twoPi;
            else if (a <= -Pi) a += twoPi;
            return a;
        }
    }
}
