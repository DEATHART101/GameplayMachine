using System;

namespace GMCore
{
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
    public class TypeSupport : System.Attribute
    {
        public Type TargetType;
        public bool GetOnly;
        public Type BinderType;
        public Type FallBackType;

        public TypeSupport(Type targetType, bool getOnly, Type binderType, Type fallBackType)
        {
            TargetType = targetType;
            GetOnly = getOnly;
            BinderType = binderType;
            FallBackType = fallBackType;
        }
    }

    public enum CollectionTypes
    {
        Set,
        Map,
        List,
    }
}

