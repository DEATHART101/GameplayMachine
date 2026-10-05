using System;
using System.Collections.Generic;

namespace XHelper
{
    public static class ArrayUtils
    {
        public static bool IsEmpty<T>(IList<T> list)
        {
            return list == null || list.Count == 0;
        }
    }
}

