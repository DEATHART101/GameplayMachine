using GMCore.Collections;
using System;
using System.Collections;
using System.Collections.Generic;

namespace GMCore
{
    public class GameplayObjectDeletedException : System.Exception { }

    public interface IGameplayObject
    {
        public GObjectID ObjectID { get; set; }

        public ODClassName ObjectClass { get; set; }

        public object this[ODFieldMeta meta] { get; set; }

        public ISetCollection<T> GetSet<T>(ODFieldName fieldName);

        public IListCollection<T> GetList<T>(ODFieldName fieldName);

        public IMapCollection<TKey, TValue> GetMap<TKey, TValue>(ODFieldName fieldName);
    }

    [System.Serializable]
    public class GameplayObject : IGameplayObject
    {
        private GObjectID m_objectID;
        private Dictionary<ODFieldName, object> m_datas;
        private ODClassName m_objectClass;

        public GObjectID ObjectID { get => m_objectID; set => m_objectID = value; }
        public ODClassName ObjectClass { get => m_objectClass; set => m_objectClass = value; }

        internal int StoredFieldCount { get { return m_datas == null ? 0 : m_datas.Count; } }

        internal IEnumerable<KeyValuePair<ODFieldName, object>> GetStoredFields()
        {
            if (m_datas == null)
            {
                return Array.Empty<KeyValuePair<ODFieldName, object>>();
            }
            return m_datas;
        }

        internal void SetStoredField(ODFieldMeta meta, object value)
        {
            this[meta] = value;
        }

        internal void ClearStoredFields()
        {
            m_datas?.Clear();
        }

        public object this[ODFieldMeta meta]
        {
            get
            {
                if (m_datas == null)
                {
                    return null;
                }

                object result;
                if (m_datas.TryGetValue(meta.Name, out result))
                {
                    return result;
                }

                return null;
            }

            set
            {
                if (value == null)
                {
                    if (m_datas == null)
                    {
                        return;
                    }
                    else
                    {
                        m_datas.Remove(meta.Name);
                    }
                }
                else
                {
                    if (m_datas == null)
                    {
                        m_datas = new Dictionary<ODFieldName, object>();
                    }
                    m_datas[meta.Name] = value;
                }
            }
        }

        public ISetCollection<T> GetSet<T>(ODFieldName fieldName)
        {
            return GetOrAdd<SetCollection<T>>(fieldName);
        }

        public IListCollection<T> GetList<T>(ODFieldName fieldName)
        {
            return GetOrAdd<ListCollection<T>>(fieldName);
        }

        public IMapCollection<TKey, TValue> GetMap<TKey, TValue>(ODFieldName fieldName)
        {
            return GetOrAdd<MapCollection<TKey, TValue>>(fieldName);
        }

        private T GetOrAdd<T>(ODFieldName fieldName)
            where T : new()
        {
            if (m_datas == null)
            {
                T newObj = new  T();
                m_datas = new Dictionary<ODFieldName, object>()
                {
                    { fieldName, newObj },
                };
                return newObj;
            }
            else
            {
                object outValue;
                if (m_datas.TryGetValue(fieldName, out outValue))
                {
                    return (T)outValue;
                }
                else
                {
                    T newObj = new T();
                    m_datas.Add(fieldName, newObj);
                    return newObj;
                }
            }
        }
    }
}

