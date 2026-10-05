using System;

namespace XHelper
{
    public static class StringUtils
    {
        public static bool IsEmpty(string? str)
        {
            return str == null || str.Length == 0;
        }

        public static bool IsSame(string a, string b, bool caseSensitive = true)
        {
            if (a == null)
            {
                if (b == null)
                {
                    return true;
                }
            }
            else
            {
                if (b != null)
                {
                    if (!caseSensitive)
                    {
                        a = a.ToLower();
                        b = b.ToLower();
                    }

                    return a == b;
                }
            }

            return false;
        }
    }
}

