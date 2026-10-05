using System;
using System.Collections;
using System.Collections.Generic;

namespace CommonSerialize
{
    public interface IDeserializer
    {
        public object DeserializeData(Type targetType, string data);

        public object Deserialize(Type targetType, IEnumerable<KeyValuePair<string, string>> datas);

        public IEnumerable<T> Deserialize<T>(IListDataProvider datas);

        public Dictionary<string, T> Deserialize<T>(IMapDataProvider datas);

        public Dictionary<string, T> DeserializeMap<T>(IDataMatrix matrix);
    }

    public interface ISerializer
    {
        public string SerializeData(Type targetType, object data);

        public IEnumerable<KeyValuePair<string, string>> Serialize(Type targetType, object obj);
    }

    public interface ITypeSerializer
    {
        public bool CanSerialize(Type type);

        public string Serialize(object obj, Type type, ISerializer serializer);

        public object Deserialize(string obj, Type type, IDeserializer deserializer);
    }

    public interface IStringSerializable
    {
        public string ConvertTo();

        public void ConvertFrom(string str);
    }
}
