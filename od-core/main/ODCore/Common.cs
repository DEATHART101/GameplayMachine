using System;
using System.Collections.Generic;

namespace ODCore
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
    public class IsA : System.Attribute
    {
        public Type Type;

        public IsA(Type type)
        {
            Type = type;
        }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class NoRepeatElement : System.Attribute
    {
        
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class DrivenBy : System.Attribute
    {
        public string[] OtherFields;

        public DrivenBy(params string[] otherFields)
        {
            OtherFields = otherFields;
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class Invokes : System.Attribute
    {
        public Type Type;

        public Invokes(Type type)
        {
            Type = type;
        }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
    public class InterfaceParam : System.Attribute
    {
        public Type OutParamType;

        public InterfaceParam(Type type)
        {
            OutParamType = type;
        }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
    public class EventParam : System.Attribute
    {
        public Type EventType;

        public EventParam(Type type)
        {
            EventType = type;
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false)]
    public class NoExport : System.Attribute
    {

    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
    public class SubFunction : System.Attribute
    {
        public string FunctionName;
        public string FieldName;
        public Type ClassType;

        public SubFunction(string functionName, string fieldName, Type classType)
        {
            FunctionName = functionName;
            FieldName = fieldName;
            ClassType = classType;
        }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
    public class RoutineParam : System.Attribute
    {

    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
    public class SwitchStruct : System.Attribute
    {
        public Type[] Interfaces;

        public SwitchStruct(params Type[] interfaces)
        {
            Interfaces = interfaces;
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class Resource : System.Attribute
    {

    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class Owned : System.Attribute
    {

    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    public class ClassOwnedOnly : System.Attribute
    {

    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
    public class Collection : System.Attribute
    {

    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class DisplayData : System.Attribute
    {

    }

    [NoExport]
    public class Connection
    {

    }

    [NoExport]
    public class ParentChildsConnection<Parent, Child> : Connection
        where Parent : class
        where Child : class
    {

    }

    [NoExport]
    public struct Name<T>
    {

    }

    [NoExport]
    public struct Field<T>
    {

    }

    [NoExport]
    public struct Field
    {

    }

    [NoExport]
    public struct EventError
    {
        public const string Default_Error_Message = "Error";

        public string Error;

        public static implicit operator EventError (string error)
        {
            EventError result;
            if (error == null || error.Length == 0)
            {
                result.Error = null;
            }
            else
            {
                result.Error = error;
            }

            return result;
        }

        public static implicit operator bool (EventError eventError)
        {
            return eventError.Error == null || eventError.Error.Length == 0;
        }

        public static implicit operator EventError (bool bol)
        {
            EventError result;

            if (bol)
            {
                result.Error = null;
            }
            else
            {
                result.Error = Default_Error_Message;
            }

            return result;
        }

        public override string ToString()
        {
            if (!this)
            {
                return Error;
            }

            return "";
        }
    }
}
