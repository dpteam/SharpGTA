using System;

namespace SharpGTA.Runtime
{
    /// <summary>Deterministic PC re3/reVC random number generator.</summary>
    public sealed class GtaRandom
    {
        private const uint OriginalSeed = 1u;
        private uint state;

        public GtaRandom()
            : this(OriginalSeed)
        {
        }

        public GtaRandom(uint seed)
        {
            state = seed;
        }

        public uint State
        {
            get { return state; }
            set { state = value; }
        }

        public void Seed(uint seed)
        {
            state = seed;
        }

        public int Next()
        {
            state = unchecked(state * 1103515245u + 12345u);
            return (int)((state >> 16) & 0x7fffu);
        }

        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException("maxExclusive");
            return Next() % maxExclusive;
        }
    }
}
