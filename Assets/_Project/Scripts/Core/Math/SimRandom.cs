using System;

namespace Downstream.Core.Math
{
    /// <summary>
    /// Small seeded PCG32 generator. Item rolls and AI mistake budgets use this instead of
    /// System.Random or UnityEngine.Random so the host and replays draw the same sequence.
    /// It is a struct so it can live inside snapshots and be rewound with the rest of the state.
    /// </summary>
    [Serializable]
    public struct SimRandom
    {
        private ulong _state;
        private ulong _inc;

        public SimRandom(ulong seed, ulong stream = 54u)
        {
            _state = 0u;
            _inc = (stream << 1) | 1u;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = old * 6364136223846793005UL + _inc;
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
        }

        /// <summary>Uniform integer in [0, maxExclusive).</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)(NextUInt() % (uint)maxExclusive);
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        public float Range(float min, float max) => min + (max - min) * NextFloat();
    }
}
