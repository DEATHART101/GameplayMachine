namespace ODCore.Math
{
    [System.Serializable]
    public struct PosInteger
    {
        public Integer Value;

        public static PosInteger operator +(PosInteger lhs, PosInteger rhs)
        {
            return lhs.Value + rhs.Value;
        }

        public static PosInteger operator -(PosInteger lhs, PosInteger rhs)
        {
            return lhs.Value - rhs.Value;
        }

        public static PosInteger operator *(PosInteger lhs, PosInteger rhs)
        {
            return lhs.Value * rhs.Value;
        }

        public static PosInteger operator /(PosInteger lhs, PosInteger rhs)
        {
            return lhs.Value / rhs.Value;
        }

        public static bool operator <(PosInteger lhs, PosInteger rhs)
        {
            return lhs.Value < rhs.Value;
        }

        public static bool operator >(PosInteger lhs, PosInteger rhs)
        {
            return lhs.Value > rhs.Value;
        }

        public static bool operator <=(PosInteger lhs, PosInteger rhs)
        {
            return lhs.Value <= rhs.Value;
        }

        public static bool operator >=(PosInteger lhs, PosInteger rhs)
        {
            return lhs.Value >= rhs.Value;
        }

        public static PosInteger operator %(PosInteger lhs, PosInteger rhs)
        {
            return lhs.Value % rhs.Value;
        }

        public static PosInteger operator ++(PosInteger val)
        {
            return ++val.Value;
        }

        public static PosInteger operator --(PosInteger val)
        {
            return --val.Value;
        }

        public static bool operator ==(PosInteger lhs, PosInteger rhs)
        {
            return lhs.Value == rhs.Value;
        }

        public static bool operator !=(PosInteger lhs, PosInteger rhs)
        {
            return lhs.Value != rhs.Value;
        }

        public static implicit operator PosInteger(Integer val)
        {
            PosInteger result;
            result.Value = val < 0 ? 0 : val;
            return result;
        }

        public static implicit operator PosInteger(int val)
        {
            Integer integer = val;
            return integer;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public override bool Equals(object obj)
        {
            if (obj is PosInteger other)
            {
                return Value == other.Value;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }

    [System.Serializable]
    public struct NegInteger
    {

    }

    [System.Serializable]
    public struct Integer
    {
        public int Value;

        public static Integer operator+(Integer lhs, Integer rhs)
        {
            return lhs.Value + rhs.Value;
        }

        public static Integer operator -(Integer lhs, Integer rhs)
        {
            return lhs.Value - rhs.Value;
        }

        public static Integer operator *(Integer lhs, Integer rhs)
        {
            return lhs.Value * rhs.Value;
        }

        public static Integer operator /(Integer lhs, Integer rhs)
        {
            return lhs.Value / rhs.Value;
        }

        public static bool operator <(Integer lhs, Integer rhs)
        {
            return lhs.Value < rhs.Value;
        }

        public static bool operator >(Integer lhs, Integer rhs)
        {
            return lhs.Value > rhs.Value;
        }

        public static bool operator <=(Integer lhs, Integer rhs)
        {
            return lhs.Value <= rhs.Value;
        }

        public static bool operator >=(Integer lhs, Integer rhs)
        {
            return lhs.Value >= rhs.Value;
        }

        public static Integer operator %(Integer lhs, Integer rhs)
        {
            return lhs.Value % rhs.Value;
        }

        public static Integer operator ++(Integer val)
        {
            return ++val.Value;
        }

        public static Integer operator --(Integer val)
        {
            return --val.Value;
        }

        public static Integer operator -(Integer val)
        {
            return -val.Value;
        }

        public static bool operator ==(Integer lhs, Integer rhs)
        {
            return lhs.Value == rhs.Value;
        }

        public static bool operator !=(Integer lhs, Integer rhs)
        {
            return lhs.Value != rhs.Value;
        }

        public static implicit operator Integer(int val)
        {
            Integer result;
            result.Value = val;
            return result;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public override bool Equals(object obj)
        {
            if (obj is Integer other)
            {
                return Value == other.Value;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }

    [System.Serializable]
    public struct Float
    {
        public float Value;

        public static Float operator +(Float lhs, Float rhs)
        {
            return lhs.Value + rhs.Value;
        }

        public static Float operator -(Float lhs, Float rhs)
        {
            return lhs.Value - rhs.Value;
        }

        public static Float operator *(Float lhs, Float rhs)
        {
            return lhs.Value * rhs.Value;
        }

        public static Float operator /(Float lhs, Float rhs)
        {
            return lhs.Value / rhs.Value;
        }

        public static bool operator <(Float lhs, Float rhs)
        {
            return lhs.Value < rhs.Value;
        }

        public static bool operator >(Float lhs, Float rhs)
        {
            return lhs.Value > rhs.Value;
        }

        public static bool operator <=(Float lhs, Float rhs)
        {
            return lhs.Value <= rhs.Value;
        }

        public static bool operator >=(Float lhs, Float rhs)
        {
            return lhs.Value >= rhs.Value;
        }

        public static Float operator %(Float lhs, Float rhs)
        {
            return lhs.Value % rhs.Value;
        }

        public static Float operator -(Float val)
        {
            return -val.Value;
        }

        public static bool operator ==(Float lhs, Float rhs)
        {
            return lhs.Value == rhs.Value;
        }

        public static bool operator !=(Float lhs, Float rhs)
        {
            return lhs.Value != rhs.Value;
        }

        public static implicit operator Float(float val)
        {
            Float result;
            result.Value = val;
            return result;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public override bool Equals(object obj)
        {
            if (obj is Float other)
            {
                return Value == other.Value;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }

    [System.Serializable]
    public struct Double
    {

    }

    [System.Serializable]
    public struct DetFloat
    {

    }
}