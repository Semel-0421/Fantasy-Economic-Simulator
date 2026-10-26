using System;

namespace FES.Core
{
    /// <summary>
    /// Q47.16 fixed-point number. All simulation state that feeds back into the economy uses
    /// integers or Fx so results are bit-identical on Android (ARM64) and Windows (x64).
    /// Floats are allowed only for presentation.
    /// </summary>
    /// <remarks>
    /// Fx * Fx overflows once both operands exceed ~3,000,000 in magnitude; use it for rates and
    /// multipliers. To scale an integer quantity or price, use <see cref="MulInt"/>, which is
    /// overflow-safe for any long.
    /// </remarks>
    public readonly struct Fx : IEquatable<Fx>, IComparable<Fx>
    {
        public const int FractionBits = 16;
        public const long OneRaw = 1L << FractionBits;

        public static readonly Fx Zero = new Fx(0);
        public static readonly Fx One = new Fx(OneRaw);

        public readonly long Raw;

        Fx(long raw) => Raw = raw;

        public static Fx FromRaw(long raw) => new Fx(raw);
        public static Fx FromInt(long value) => new Fx(value << FractionBits);
        /// <summary>permille / 1000, rounded to the nearest raw step (so 1800 * 10 scales to exactly 18).</summary>
        public static Fx FromPermille(long permille)
        {
            long scaled = permille << FractionBits;
            return new Fx(scaled >= 0 ? (scaled + 500) / 1000 : (scaled - 500) / 1000);
        }

        /// <summary>numerator / denominator, truncated toward zero. Returns zero when denominator is zero.</summary>
        public static Fx FromRatio(long numerator, long denominator)
        {
            if (denominator == 0) return Zero;
            if (numerator > MaxShiftable || numerator < -MaxShiftable)
                return new Fx((long)((decimal)numerator * OneRaw / denominator)); // exact, avoids << overflow
            return new Fx((numerator << FractionBits) / denominator);
        }

        /// <summary>
        /// Scales an integer quantity: <c>value * this</c>, rounded half away from zero so gains and
        /// losses are symmetric (floor rounding would bias every flow and price step downward).
        /// </summary>
        public long MulInt(long value)
        {
            if (value > SafeFactor || value < -SafeFactor || Raw > SafeFactor || Raw < -SafeFactor)
                return (long)decimal.Round((decimal)value * Raw / OneRaw, MidpointRounding.AwayFromZero);
            return RoundShift(value * Raw);
        }

        /// <summary>Converts a Q16 product to an integer, rounding half away from zero. Burst-safe.</summary>
        public static long RoundShift(long product) =>
            product >= 0 ? (product + HalfRaw) >> FractionBits : -((-product + HalfRaw) >> FractionBits);

        const long HalfRaw = OneRaw / 2;
        const long MaxShiftable = long.MaxValue >> FractionBits;
        const long SafeFactor = int.MaxValue; // |a|, |b| <= 2^31 keeps a * b inside long

        public static Fx Min(Fx a, Fx b) => a.Raw <= b.Raw ? a : b;
        public static Fx Max(Fx a, Fx b) => a.Raw >= b.Raw ? a : b;
        public static Fx Clamp(Fx value, Fx min, Fx max) => Max(min, Min(max, value));

        public static Fx operator +(Fx a, Fx b) => new Fx(a.Raw + b.Raw);
        public static Fx operator -(Fx a, Fx b) => new Fx(a.Raw - b.Raw);
        public static Fx operator -(Fx a) => new Fx(-a.Raw);
        public static Fx operator *(Fx a, Fx b) => new Fx((a.Raw * b.Raw) >> FractionBits);
        public static Fx operator /(Fx a, Fx b) => b.Raw == 0 ? Zero : new Fx((a.Raw << FractionBits) / b.Raw);
        public static bool operator ==(Fx a, Fx b) => a.Raw == b.Raw;
        public static bool operator !=(Fx a, Fx b) => a.Raw != b.Raw;
        public static bool operator <(Fx a, Fx b) => a.Raw < b.Raw;
        public static bool operator >(Fx a, Fx b) => a.Raw > b.Raw;
        public static bool operator <=(Fx a, Fx b) => a.Raw <= b.Raw;
        public static bool operator >=(Fx a, Fx b) => a.Raw >= b.Raw;

        public bool Equals(Fx other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fx other && Equals(other);
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(Fx other) => Raw.CompareTo(other.Raw);

        /// <summary>Presentation only. Never feed the result back into simulation state.</summary>
        public double ToDouble() => (double)Raw / OneRaw;

        public override string ToString() => ToDouble().ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }
}
