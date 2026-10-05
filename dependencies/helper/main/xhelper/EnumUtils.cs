using System;
using System.Collections.Generic;

namespace XHelper
{
    public static class EnumUtils
    {
        public static bool IsSame<E>(E a, E b)
            where E : struct, System.Enum
        {
            return EqualityComparer<E>.Default.Equals(a, b);
        }
    }
}

