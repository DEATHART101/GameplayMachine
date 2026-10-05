using System;
using System.Globalization;
using System.Numerics;

namespace XLockstep
{
    /// <summary>A deterministic signed fixed-point number with six decimal places.</summary>
    [Serializable]
    public readonly struct Fixed64 : IEquatable<Fixed64>, IComparable<Fixed64>
    {
        public const long Scale = 1_000_000L;

        public long RawValue { get; }

        private Fixed64(long rawValue)
        {
            RawValue = rawValue;
        }

        public static Fixed64 Zero => FromRaw(0);
        public static Fixed64 One => FromRaw(Scale);

        public static Fixed64 FromRaw(long rawValue) => new Fixed64(rawValue);
        public static Fixed64 FromInt32(int value) => new Fixed64(checked(value * Scale));
        public static Fixed64 FromInt64(long value) => new Fixed64(checked(value * Scale));

        public static Fixed64 FromDecimal(decimal value)
        {
            decimal scaled = decimal.Round(value * Scale, 0, MidpointRounding.ToEven);
            return FromRaw(decimal.ToInt64(scaled));
        }

        public static Fixed64 Parse(string value) =>
            FromDecimal(decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture));

        public decimal ToDecimal() => RawValue / (decimal)Scale;
        public double ToDouble() => RawValue / (double)Scale;

        public int CompareTo(Fixed64 other) => RawValue.CompareTo(other.RawValue);
        public bool Equals(Fixed64 other) => RawValue == other.RawValue;
        public override bool Equals(object? obj) => obj is Fixed64 other && Equals(other);
        public override int GetHashCode() => RawValue.GetHashCode();
        public override string ToString() => ToDecimal().ToString(CultureInfo.InvariantCulture);

        public static Fixed64 operator +(Fixed64 left, Fixed64 right) =>
            FromRaw(checked(left.RawValue + right.RawValue));
        public static Fixed64 operator -(Fixed64 left, Fixed64 right) =>
            FromRaw(checked(left.RawValue - right.RawValue));
        public static Fixed64 operator +(Fixed64 value) => value;
        public static Fixed64 operator -(Fixed64 value) => FromRaw(checked(-value.RawValue));
        public static Fixed64 operator *(Fixed64 left, Fixed64 right) =>
            FromRaw(CheckedToInt64((BigInteger)left.RawValue * right.RawValue / Scale));
        public static Fixed64 operator /(Fixed64 left, Fixed64 right)
        {
            if (right.RawValue == 0)
                throw new DivideByZeroException();
            return FromRaw(CheckedToInt64((BigInteger)left.RawValue * Scale / right.RawValue));
        }
        public static Fixed64 operator %(Fixed64 left, Fixed64 right)
        {
            if (right.RawValue == 0)
                throw new DivideByZeroException();
            return FromRaw(left.RawValue % right.RawValue);
        }

        public static bool operator ==(Fixed64 left, Fixed64 right) => left.Equals(right);
        public static bool operator !=(Fixed64 left, Fixed64 right) => !left.Equals(right);
        public static bool operator <(Fixed64 left, Fixed64 right) => left.RawValue < right.RawValue;
        public static bool operator >(Fixed64 left, Fixed64 right) => left.RawValue > right.RawValue;
        public static bool operator <=(Fixed64 left, Fixed64 right) => left.RawValue <= right.RawValue;
        public static bool operator >=(Fixed64 left, Fixed64 right) => left.RawValue >= right.RawValue;

        public static implicit operator Fixed64(int value) => FromInt32(value);
        public static explicit operator int(Fixed64 value) => checked((int)(value.RawValue / Scale));
        public static explicit operator long(Fixed64 value) => value.RawValue / Scale;
        public static explicit operator decimal(Fixed64 value) => value.ToDecimal();
        public static explicit operator double(Fixed64 value) => value.ToDouble();

        private static long CheckedToInt64(BigInteger value)
        {
            if (value < long.MinValue || value > long.MaxValue)
                throw new OverflowException("Fixed64 operation overflowed");
            return (long)value;
        }
    }
}
