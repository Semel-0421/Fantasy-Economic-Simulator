namespace FES.Core
{
    /// <summary>
    /// Stateless, platform-independent hashing used for all simulation randomness.
    /// Draws are keyed by (seed, turn, stream, entity), so results do not depend on thread count,
    /// job scheduling or iteration order, and no RNG state needs to be saved.
    /// </summary>
    public static class DetHash
    {
        internal const ulong FnvOffset = 14695981039346656037UL;
        internal const ulong FnvPrime = 1099511628211UL;
        internal const ulong Gamma = 0x9E3779B97F4A7C15UL;

        /// <summary>SplitMix64 step: advance by the golden-ratio gamma, then finalize.</summary>
        public static ulong Mix(ulong x)
        {
            x += Gamma;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        public static ulong Combine(ulong a, ulong b) => Mix(a ^ Mix(b));

        public static ulong Hash(ulong seed, int turn, uint stream, int entity, uint salt = 0)
        {
            ulong h = Mix(seed);
            h = Combine(h, (ulong)(uint)turn);
            h = Combine(h, stream);
            h = Combine(h, (ulong)(uint)entity);
            return Combine(h, salt);
        }

        internal static ulong FnvStep(ulong hash, byte value) => (hash ^ value) * FnvPrime;

        /// <summary>Stable 32-bit id for a string (FNV-1a). Never use string.GetHashCode for simulation keys.</summary>
        public static uint Of(string text)
        {
            ulong h = FnvOffset;
            foreach (char c in text)
            {
                h ^= c;
                h *= FnvPrime;
            }
            return (uint)(h ^ (h >> 32));
        }
    }

    /// <summary>Small deterministic generator for a burst of draws from one keyed seed.</summary>
    public struct DetRandom
    {
        ulong _state;

        public DetRandom(ulong seed) => _state = seed;

        public static DetRandom For(ulong worldSeed, int turn, uint stream, int entity, uint salt = 0) =>
            new DetRandom(DetHash.Hash(worldSeed, turn, stream, entity, salt));

        public ulong NextULong()
        {
            ulong z = DetHash.Mix(_state);
            _state += DetHash.Gamma;
            return z;
        }

        public uint NextUInt() => (uint)(NextULong() >> 32);

        /// <summary>Uniform integer in [0, maxExclusive). Returns 0 when maxExclusive &lt;= 0.</summary>
        public int NextInt(int maxExclusive) =>
            maxExclusive <= 0 ? 0 : (int)(((ulong)NextUInt() * (ulong)maxExclusive) >> 32);

        public int NextInt(int minInclusive, int maxExclusive) => minInclusive + NextInt(maxExclusive - minInclusive);

        /// <summary>Uniform fixed-point value in [0, 1).</summary>
        public Fx NextFx01() => Fx.FromRaw((long)(NextUInt() >> (32 - Fx.FractionBits)));

        /// <summary>True with the given probability in permille (0..1000).</summary>
        public bool ChancePermille(int permille) => NextInt(1000) < permille;

        /// <summary>Uniform multiplier in [1 - spread, 1 + spread], spread in permille.</summary>
        public Fx NextSpread(int spreadPermille) =>
            Fx.FromPermille(1000 - spreadPermille + NextInt(2 * spreadPermille + 1));
    }
}
