using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace XLockstep
{
    public sealed class DeterministicComparer<T> : IComparer<T>
    {
        public static DeterministicComparer<T> Default { get; } = new DeterministicComparer<T>();

        private DeterministicComparer() { }

        public int Compare(T? left, T? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            if (left is string leftString && right is string rightString)
                return StringComparer.Ordinal.Compare(leftString, rightString);
            if (left is IComparable<T> genericComparable)
                return genericComparable.CompareTo(right);
            if (left is IComparable comparable)
                return comparable.CompareTo(right);
            throw new InvalidOperationException(
                $"Type {typeof(T)} cannot be canonically ordered in a Lockstep collection");
        }
    }

    /// <summary>Hash lookup with canonical order maintained when values change.</summary>
    [Serializable]
    public class DeterministicSet<T> : HashSet<T>, ISet<T>
    {
        private IComparer<T> m_orderComparer;
        private SortedSet<T> m_orderedValues;

        public DeterministicSet() : this(DeterministicComparer<T>.Default) { }

        public DeterministicSet(IEnumerable<T> values) : this()
        {
            UnionWith(values);
        }

        protected DeterministicSet(IComparer<T> comparer)
        {
            m_orderComparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
            m_orderedValues = new SortedSet<T>(m_orderComparer);
        }

        protected DeterministicSet(IEnumerable<T> values, IComparer<T> comparer) : this(comparer)
        {
            UnionWith(values);
        }

        protected DeterministicSet(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            m_orderComparer = DeterministicComparer<T>.Default;
            m_orderedValues = new SortedSet<T>(m_orderComparer);
        }

        protected DeterministicSet(SerializationInfo info, StreamingContext context, IComparer<T> comparer)
            : base(info, context)
        {
            m_orderComparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
            m_orderedValues = new SortedSet<T>(m_orderComparer);
        }

        public new bool Add(T item)
        {
            if (!base.Add(item))
                return false;
            try
            {
                if (!m_orderedValues.Add(item))
                    throw InconsistentComparer();
                return true;
            }
            catch
            {
                base.Remove(item);
                throw;
            }
        }

        public new bool Remove(T item)
        {
            if (!base.Remove(item))
                return false;
            if (!m_orderedValues.Remove(item))
                throw InconsistentComparer();
            return true;
        }

        public new void Clear()
        {
            base.Clear();
            m_orderedValues.Clear();
        }

        public new int RemoveWhere(Predicate<T> match)
        {
            if (match is null) throw new ArgumentNullException(nameof(match));
            var removes = new List<T>();
            foreach (T item in m_orderedValues)
                if (match(item)) removes.Add(item);
            foreach (T item in removes)
                Remove(item);
            return removes.Count;
        }

        public new void UnionWith(IEnumerable<T> other)
        {
            if (other is null) throw new ArgumentNullException(nameof(other));
            foreach (T item in other)
                Add(item);
        }

        public new void ExceptWith(IEnumerable<T> other)
        {
            if (other is null) throw new ArgumentNullException(nameof(other));
            foreach (T item in new List<T>(other))
                Remove(item);
        }

        public new void IntersectWith(IEnumerable<T> other)
        {
            if (other is null) throw new ArgumentNullException(nameof(other));
            var keep = new HashSet<T>(other, Comparer);
            RemoveWhere(item => !keep.Contains(item));
        }

        public new void SymmetricExceptWith(IEnumerable<T> other)
        {
            if (other is null) throw new ArgumentNullException(nameof(other));
            var distinct = new HashSet<T>(other, Comparer);
            foreach (T item in distinct)
                if (!Remove(item)) Add(item);
        }

        public new IEnumerator<T> GetEnumerator() => m_orderedValues.GetEnumerator();

        bool ISet<T>.Add(T item) => Add(item);
        void ICollection<T>.Add(T item) => Add(item);
        bool ICollection<T>.Remove(T item) => Remove(item);
        void ICollection<T>.Clear() => Clear();
        void ISet<T>.UnionWith(IEnumerable<T> other) => UnionWith(other);
        void ISet<T>.ExceptWith(IEnumerable<T> other) => ExceptWith(other);
        void ISet<T>.IntersectWith(IEnumerable<T> other) => IntersectWith(other);
        void ISet<T>.SymmetricExceptWith(IEnumerable<T> other) => SymmetricExceptWith(other);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        [OnDeserialized]
        private void RebuildOrder(StreamingContext context)
        {
            m_orderComparer ??= DeterministicComparer<T>.Default;
            m_orderedValues = new SortedSet<T>(m_orderComparer);
            HashSet<T>.Enumerator enumerator = base.GetEnumerator();
            while (enumerator.MoveNext())
                if (!m_orderedValues.Add(enumerator.Current))
                    throw InconsistentComparer();
        }

        private static InvalidOperationException InconsistentComparer() => new InvalidOperationException(
            $"The equality and deterministic ordering rules for {typeof(T)} do not identify the same values");
    }

    /// <summary>Hash lookup with canonical key order maintained when entries change.</summary>
    [Serializable]
    public class DeterministicMap<TKey, TValue> : Dictionary<TKey, TValue>, IDictionary<TKey, TValue>
        where TKey : notnull
    {
        private IComparer<TKey> m_orderComparer;
        private SortedSet<TKey> m_orderedKeys;
        [NonSerialized] private ICollection<TKey>? m_keysView;
        [NonSerialized] private ICollection<TValue>? m_valuesView;

        public DeterministicMap() : this(DeterministicComparer<TKey>.Default) { }

        public DeterministicMap(IDictionary<TKey, TValue> values) : this()
        {
            foreach (KeyValuePair<TKey, TValue> pair in values)
                Add(pair.Key, pair.Value);
        }

        protected DeterministicMap(IComparer<TKey> comparer)
        {
            m_orderComparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
            m_orderedKeys = new SortedSet<TKey>(m_orderComparer);
        }

        protected DeterministicMap(IDictionary<TKey, TValue> values, IComparer<TKey> comparer) : this(comparer)
        {
            foreach (KeyValuePair<TKey, TValue> pair in values)
                Add(pair.Key, pair.Value);
        }

        protected DeterministicMap(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            m_orderComparer = DeterministicComparer<TKey>.Default;
            m_orderedKeys = new SortedSet<TKey>(m_orderComparer);
        }

        protected DeterministicMap(SerializationInfo info, StreamingContext context, IComparer<TKey> comparer)
            : base(info, context)
        {
            m_orderComparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
            m_orderedKeys = new SortedSet<TKey>(m_orderComparer);
        }

        public new TValue this[TKey key]
        {
            get => base[key];
            set
            {
                if (base.ContainsKey(key))
                {
                    base[key] = value;
                    return;
                }
                Add(key, value);
            }
        }

        public new ICollection<TKey> Keys => m_keysView ??= new OrderedKeyCollection(this);
        public new ICollection<TValue> Values => m_valuesView ??= new OrderedValueCollection(this);

        public new void Add(TKey key, TValue value)
        {
            base.Add(key, value);
            try
            {
                if (!m_orderedKeys.Add(key))
                    throw InconsistentComparer();
            }
            catch
            {
                base.Remove(key);
                throw;
            }
        }

        public new bool TryAdd(TKey key, TValue value)
        {
            if (base.ContainsKey(key))
                return false;
            Add(key, value);
            return true;
        }

        public new bool Remove(TKey key)
        {
            if (!base.Remove(key))
                return false;
            if (!m_orderedKeys.Remove(key))
                throw InconsistentComparer();
            return true;
        }

        public new void Clear()
        {
            base.Clear();
            m_orderedKeys.Clear();
        }

        public new IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            foreach (TKey key in m_orderedKeys)
                yield return new KeyValuePair<TKey, TValue>(key, base[key]);
        }

        TValue IDictionary<TKey, TValue>.this[TKey key] { get => this[key]; set => this[key] = value; }
        ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;
        ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;
        void IDictionary<TKey, TValue>.Add(TKey key, TValue value) => Add(key, value);
        bool IDictionary<TKey, TValue>.Remove(TKey key) => Remove(key);
        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) =>
            TryGetValue(item.Key, out TValue? value) &&
            EqualityComparer<TValue>.Default.Equals(value, item.Value) && Remove(item.Key);
        void ICollection<KeyValuePair<TKey, TValue>>.Clear() => Clear();
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        [OnDeserialized]
        private void RebuildOrder(StreamingContext context)
        {
            m_orderComparer ??= DeterministicComparer<TKey>.Default;
            m_orderedKeys = new SortedSet<TKey>(m_orderComparer);
            Dictionary<TKey, TValue>.KeyCollection.Enumerator enumerator = base.Keys.GetEnumerator();
            while (enumerator.MoveNext())
                if (!m_orderedKeys.Add(enumerator.Current))
                    throw InconsistentComparer();
            m_keysView = null;
            m_valuesView = null;
        }

        private static InvalidOperationException InconsistentComparer() => new InvalidOperationException(
            $"The equality and deterministic ordering rules for {typeof(TKey)} do not identify the same keys");

        private sealed class OrderedKeyCollection : ICollection<TKey>
        {
            private readonly DeterministicMap<TKey, TValue> m_owner;
            public OrderedKeyCollection(DeterministicMap<TKey, TValue> owner) => m_owner = owner;
            public int Count => m_owner.Count;
            public bool IsReadOnly => true;
            public bool Contains(TKey item) => m_owner.ContainsKey(item);
            public void CopyTo(TKey[] array, int arrayIndex) => m_owner.m_orderedKeys.CopyTo(array, arrayIndex);
            public IEnumerator<TKey> GetEnumerator() => m_owner.m_orderedKeys.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public void Add(TKey item) => throw new NotSupportedException();
            public bool Remove(TKey item) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
        }

        private sealed class OrderedValueCollection : ICollection<TValue>
        {
            private readonly DeterministicMap<TKey, TValue> m_owner;
            public OrderedValueCollection(DeterministicMap<TKey, TValue> owner) => m_owner = owner;
            public int Count => m_owner.Count;
            public bool IsReadOnly => true;
            public bool Contains(TValue item)
            {
                foreach (TValue value in this)
                    if (EqualityComparer<TValue>.Default.Equals(value, item)) return true;
                return false;
            }
            public void CopyTo(TValue[] array, int arrayIndex)
            {
                foreach (TValue value in this)
                    array[arrayIndex++] = value;
            }
            public IEnumerator<TValue> GetEnumerator()
            {
                foreach (TKey key in m_owner.m_orderedKeys)
                    yield return m_owner[key];
            }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public void Add(TValue item) => throw new NotSupportedException();
            public bool Remove(TValue item) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
        }
    }
}
