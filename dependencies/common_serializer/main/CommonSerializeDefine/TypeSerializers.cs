using CommonSerialize;

using System;
using System.Collections;
using System.Collections.Generic;

using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CommonSerialize
{
    public class NullableSerializer : ITypeSerializer
    {
        public bool CanSerialize(Type type)
        {
            return Nullable.GetUnderlyingType(type) != null;
        }

        public object Deserialize(string obj, Type type, IDeserializer deserializer)
        {
            if (string.IsNullOrEmpty(obj))
            {
                return null;
            }

            Type trueType = Nullable.GetUnderlyingType(type);
            return deserializer.DeserializeData(trueType, obj);
        }

        public string Serialize(object obj, Type type, ISerializer serializer)
        {
            if (obj == null)
            {
                return "";
            }

            return obj.ToString();
        }
    }

    public class IntSerializer : ITypeSerializer
    {
        public bool CanSerialize(Type type)
        {
            return type == typeof(int);
        }

        public object Deserialize(string obj, Type type, IDeserializer deserializer)
        {
            return int.Parse(obj);
        }

        public string Serialize(object obj, Type type, ISerializer serializer)
        {
            return obj?.ToString();
        }
    }

    public class FloatSerializer : ITypeSerializer
    {
        public bool CanSerialize(Type type)
        {
            return type == typeof(float);
        }

        public object Deserialize(string obj, Type type, IDeserializer deserializer)
        {
            return float.Parse(obj);
        }

        public string Serialize(object obj, Type type, ISerializer serializer)
        {
            return obj?.ToString();
        }
    }

    public class StringSerializer : ITypeSerializer
    {
        public bool CanSerialize(Type type)
        {
            return type == typeof(string);
        }

        public object Deserialize(string obj, Type type, IDeserializer deserializer)
        {
            return obj;
        }

        public string Serialize(object obj, Type type, ISerializer serializer)
        {
            return obj?.ToString();
        }
    }

    public class BoolSerializer : ITypeSerializer
    {
        public bool CanSerialize(Type type)
        {
            return type == typeof(bool);
        }

        public object Deserialize(string obj, Type type, IDeserializer deserializer)
        {
            if (string.IsNullOrEmpty(obj))
            {
                return false;
            }
            int intValue;
            if (int.TryParse(obj, out intValue))
            {
                return intValue != 0;
            }
            return bool.Parse(obj);
        }

        public string Serialize(object obj, Type type, ISerializer serializer)
        {
            return obj?.ToString();
        }
    }

    public class StringEnumSerializer : ITypeSerializer
    {
        public bool CanSerialize(Type type)
        {
            return type.IsEnum;
        }

        public object Deserialize(string obj, Type type, IDeserializer deserializer)
        {
            return Enum.Parse(type, obj);
        }

        public string Serialize(object obj, Type type, ISerializer serializer)
        {
            return obj?.ToString();
        }
    }

    public class IntEnumSerializer : ITypeSerializer
    {
        public bool CanSerialize(Type type)
        {
            return type.IsEnum;
        }

        public object Deserialize(string obj, Type type, IDeserializer deserializer)
        {
            return Enum.ToObject(type, int.Parse(obj));
        }

        public string Serialize(object obj, Type type, ISerializer serializer)
        {
            int value = (int)obj;
            return value.ToString();
        }
    }

    public class ListSerializer : ITypeSerializer
    {
        public bool CanSerialize(Type type)
        {
            return type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>));
        }

        public object Deserialize(string obj, Type type, IDeserializer deserializer)
        {
            string[] strs = obj.Split(',', '，');
            Type elementType;
            if (type.IsArray)
            {
                elementType = type.GetElementType();
            }
            else
            {
                elementType = type.GetGenericArguments()[0];
            }

            object[] objs = new object[strs.Length];
            for (int i = 0; i < strs.Length; i++)
            {
                objs[i] = deserializer.DeserializeData(elementType, strs[i]);
            }

            object result;
            if (type.IsArray)
            {
                Array array = Array.CreateInstance(elementType, strs.Length);
                for (int i = 0; i < strs.Length; i++)
                {
                    array.SetValue(objs[i], i);
                }
                result = array;
            }
            else
            {
                IList list = Activator.CreateInstance(type) as IList;
                for (int i = 0; i < strs.Length; i++)
                {
                    list.Add(objs[i]);
                }
                result = list;
            }

            return result;
        }

        public string Serialize(object obj, Type type, ISerializer serializer)
        {
            List<string> strs = new List<string>();
            IEnumerable enumerable = obj as IEnumerable;
            foreach (var item in enumerable)
            {
                strs.Add(item.ToString());
            }
            return string.Join(", ", strs);
        }
    }

    public class StaticSerializers
    {
        public static readonly ITypeSerializer[] TypeSerializers = new ITypeSerializer[]
        {
            new IntSerializer(),
            new FloatSerializer(),
            new BoolSerializer(),
            new StringSerializer(),
            new StringEnumSerializer(),
            new ListSerializer(),
            new NullableSerializer(),
        };
    }
}
