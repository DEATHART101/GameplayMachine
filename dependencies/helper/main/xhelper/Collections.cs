using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace XHelper.Collections
{
    #region UniqueList

    [System.Serializable]
    public class UniqueList<T> : IEnumerable<T>, ICollection<T>
    {
        public List<T> m_list = new List<T>();

        [Newtonsoft.Json.JsonIgnore]
        public int Count
        {
            get
            {
                return m_list.Count;
            }
        }

        public bool IsReadOnly => false;

        public bool Add(T item)
        {
            int count = Count;
            for (int i = 0; i < count; i++)
            {
                if (m_list[i].Equals(item))
                {
                    return false;
                }
            }

            m_list.Add(item);
            return true;
        }

        public bool Remove(T item)
        {
            return m_list.Remove(item);
        }

        public void Clear()
        {
            m_list.Clear();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return m_list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return m_list.GetEnumerator();
        }

        void ICollection<T>.Add(T item)
        {
            Add(item);
        }

        public bool Contains(T item)
        {
            return m_list.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            m_list.CopyTo(array, arrayIndex);
        }
    }

    #endregion

    #region Table

    public interface ITable<TKey, TValue> : IEnumerable<KVPair<TKey, TValue>>, ICollection<KVPair<TKey, TValue>>
        where TKey : struct
        where TValue : struct
    {
        public TValue? this[TKey index] { get; set; }
    }

    [System.Serializable]
    public struct KVPair<TKey, TValue>
        where TKey : struct
        where TValue : struct
    {
        public TKey Key;
        public TValue Value;

        public static implicit operator KVPair<TKey, TValue>(KeyValuePair<TKey, TValue> pair)
        {
            KVPair<TKey, TValue> result;
            result.Key = pair.Key;
            result.Value = pair.Value;
            return result;
        }
    }

    [System.Serializable]
    public struct StructTable<TKey, TValue> : ITable<TKey, TValue>
        where TKey : struct
        where TValue : struct
    {
        private Dictionary<TKey, TValue> m_dict;

        private void EnsureDict()
        {
            if (m_dict == null)
            {
                m_dict = new Dictionary<TKey, TValue>();
            }
        }

        public TValue? this[TKey index]
        {
            get
            {
                if (m_dict == null)
                {
                    return null;
                }

                TValue result;
                if (m_dict.TryGetValue(index, out result))
                {
                    return result;
                }

                return null;
            }

            set
            {
                if (value == null)
                {
                    if (m_dict == null)
                    {
                        return;
                    }
                    else
                    {
                        Remove(index);
                    }
                }
                else
                {
                    EnsureDict();
                    m_dict[index] = value.Value;
                }
            }
        }

        public int Count
        {
            get
            {
                if (m_dict == null)
                {
                    return 0;
                }
                else
                {
                    return m_dict.Count;
                }
            }
        }

        public bool IsReadOnly => false;

        public void Add(TKey key, TValue value)
        {
            EnsureDict();
            m_dict.Add(key, value);
        }

        public bool Remove(TKey key)
        {
            if (m_dict != null)
            {
                return m_dict.Remove(key);
            }

            return false;
        }

        public void Clear()
        {
            if (m_dict != null)
            {
                m_dict.Clear();
            }
        }

        public bool ContainsKey(TKey key)
        {
            if (m_dict != null)
            {
                return m_dict.ContainsKey(key);
            }

            return false;
        }

        public bool ContainsValue(TValue value)
        {
            if (m_dict != null)
            {
                return m_dict.ContainsValue(value);
            }

            return false;
        }

        public IEnumerator<KVPair<TKey, TValue>> GetEnumerator()
        {
            if (m_dict != null)
            {
                foreach (var item in m_dict)
                {
                    KVPair<TKey, TValue> result;
                    result.Key = item.Key;
                    result.Value = item.Value;
                    yield return result;
                }
            }

            yield break;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            if (m_dict != null)
            {
                foreach (var item in m_dict)
                {
                    yield return item;
                }
            }

            yield break;
        }

        public void Add(KVPair<TKey, TValue> item)
        {
            EnsureDict();

            m_dict.Add(item.Key, item.Value);
        }

        public bool Contains(KVPair<TKey, TValue> item)
        {
            if (m_dict == null)
            {
                return false;
            }

            TValue findValue;
            if (!m_dict.TryGetValue(item.Key, out findValue))
            {
                return false;
            }

            return findValue.Equals(item.Value);
        }

        public void CopyTo(KVPair<TKey, TValue>[] array, int arrayIndex)
        {
            if (m_dict == null)
            {
                return;
            }

            foreach (var item in m_dict)
            {
                array[arrayIndex] = item;
                arrayIndex++;
            }
        }

        public bool Remove(KVPair<TKey, TValue> item)
        {
            if (m_dict == null)
            {
                return false;
            }

            TValue findValue;
            if (!m_dict.TryGetValue(item.Key, out findValue))
            {
                return false;
            }

            if (!findValue.Equals(item.Value))
            {
                return false;
            }

            return m_dict.Remove(item.Key);
        }
    }

    [System.Serializable]
    public struct Table<TKey, TValue> : ITable<TKey, TValue>
        where TKey : struct
        where TValue : struct
    {
        private StructTable<TKey, TValue> m_table;

        public TValue? this[TKey index]
        {
            get
            {
                return m_table[index];
            }

            set
            {
                m_table[index] = value;
            }
        }

        public int Count
        {
            get
            {
                return m_table.Count;
            }
        }

        public bool IsReadOnly => m_table.IsReadOnly;

        public void Add(TKey key, TValue value)
        {
            m_table.Add(key, value);
        }

        public bool Remove(TKey key)
        {
            return m_table.Remove(key);
        }

        public void Clear()
        {
            m_table.Clear();
        }

        public bool ContainsKey(TKey key)
        {
            return m_table.ContainsKey(key);
        }

        public bool ContainsValue(TValue value)
        {
            return m_table.ContainsValue(value);
        }

        public IEnumerator<KVPair<TKey, TValue>> GetEnumerator()
        {
            return m_table.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return m_table.GetEnumerator();
        }

        public void Add(KVPair<TKey, TValue> item)
        {
            m_table.Add(item);
        }

        public bool Contains(KVPair<TKey, TValue> item)
        {
            return m_table.Contains(item);
        }

        public void CopyTo(KVPair<TKey, TValue>[] array, int arrayIndex)
        {
            m_table.CopyTo(array, arrayIndex);
        }

        public bool Remove(KVPair<TKey, TValue> item)
        {
            return m_table.Remove(item);
        }
    }

    #endregion

    #region List

    #endregion
}
