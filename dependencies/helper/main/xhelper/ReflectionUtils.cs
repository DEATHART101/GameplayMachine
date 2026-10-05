using System;

namespace XHelper
{
    public static class ReflectionUtils
    {
        public static string GetLastNamespaceName(Type type)
        {
            string result = type.Namespace;
            if (StringUtils.IsEmpty(result))
            {
                return result;
            }

            int lastDot = result.LastIndexOf('.');
            if (lastDot == -1)
            {
                return result;
            }

            return result.Substring(lastDot + 1);
        }
    }
}

