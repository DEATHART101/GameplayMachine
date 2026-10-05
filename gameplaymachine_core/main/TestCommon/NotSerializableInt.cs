using GMCore;
using System;
using System.Collections.Generic;

namespace TestCommon
{
    public class NotSerializableInt
    {
        public int Value;
    }

    public class MyCustomObjectSurrogate : IGMSerializer
    {
        public void Write(GameplaySaveWriter writer, object value)
        {
            writer.WriteInt32(((NotSerializableInt)value).Value);
        }

        public IEnumerable<Type> GetSerializeTypes()
        {
            yield return typeof(NotSerializableInt);
        }

        public object Read(GameplaySaveReader reader, Type type)
        {
            return new NotSerializableInt { Value = reader.ReadInt32() };
        }
    }
}
