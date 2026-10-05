using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using XEventSystem;

namespace GMCore.Collections
{
    public interface IMapCollection<TKey, TValue> : IEnumerable<KVPair<TKey, TValue>>, IGMCollection
    {
        public int Count { get; }

        public void Add(TKey key, TValue value);

        public bool Remove(TKey key);

        public bool ContainsKey(TKey key);

        public bool TryGetValue(TKey key, out TValue value);

        public bool TryAdd(TKey key, TValue value);

        public void AddOrUpdateRange(IEnumerable<KVPair<TKey, TValue>> pairs);

        public TValue this[TKey key] { get; set; }

        public IEnumerable<TKey> Keys
        {
            get
            {
                foreach (var item in this)
                {
                    yield return item.Key;
                }
            }
        }

        public void Clear();
    }

    [System.Serializable]
    public class MapCollection<K, V> : XLockstep.DeterministicMap<K, V>, IMapCollection<K, V>, ICollectionMemoryObject,
        IReplicatedCollection, IGameplayReferenceCollection
    {
        public MapCollection() : base(GameplayCollectionComparer<K>.Default) { }

        protected MapCollection(SerializationInfo info, StreamingContext context)
            : base(info, context, GameplayCollectionComparer<K>.Default) { }

        public object Get(object index)
        {
            IDictionary thisMap = this;
            return thisMap[index];
        }

        public void ApplyReplicationAdd(object key, object value)
        {
            Add((K)key, (V)value);
        }

        public void ApplyReplicationRemove(object key)
        {
            if (!Remove((K)key))
                throw new InvalidOperationException("Replicated map remove targeted a missing key");
        }

        public void ApplyReplicationItemChanged(object key, object value)
        {
            K typedKey = (K)key;
            if (!ContainsKey(typedKey))
                throw new InvalidOperationException("Replicated map change targeted a missing key");
            this[typedKey] = (V)value;
        }

        public void CopyTo(IGameplayObject gameplayObject, ODFieldName fieldIndex)
        {
            IMapCollection<K, V> collection = gameplayObject.GetMap<K, V>(fieldIndex);
            collection.Clear();
            collection.AddOrUpdateRange(this);
        }

        public void AddOrUpdateRange(IEnumerable<KVPair<K, V>> pairs)
        {
            foreach (var item in pairs)
            {
                this[item.Key] = item.Value;
            }
        }

        IEnumerator<KVPair<K, V>> IEnumerable<KVPair<K, V>>.GetEnumerator()
        {
            foreach (KeyValuePair<K, V> item in (XLockstep.DeterministicMap<K, V>)this)
            {
                yield return new KVPair<K, V>()
                {
                    Key = item.Key,
                    Value = item.Value,
                };
            }
        }

        GameplayReferenceCollectionCleanup IGameplayReferenceCollection.RemoveGameplayObjectReferences(
            GameplayMachine machine,
            GObjectID target)
        {
            var removes = ((IEnumerable<KeyValuePair<K, V>>)this).Where(item =>
                    GameplayObjectReferenceWalker.Contains(item.Key, machine, target) ||
                    GameplayObjectReferenceWalker.Contains(item.Value, machine, target))
                .Select(item => item.Key)
                .ToList();
            foreach (K key in removes)
                Remove(key);
            return new GameplayReferenceCollectionCleanup
            {
                Changed = removes.Count > 0,
                RemovedItems = removes.Cast<object>().ToList(),
            };
        }
    }

    [TypeSupport(typeof(ODCore.Collections.Map<,>), false, typeof(CollectionBinder<,>), typeof(System.Collections.Generic.Dictionary<,>))]
    public struct Map<K, V> : ICollectionFieldObject<K, V>
    {
        public GameplayMachine Machine { get; set; }

        public GObjectID ObjectID { get; set; }

        public ODFieldName FieldName { get; set; }

        // Call this when get-only
        private IMapCollection<K, V> GMMap
        {
            get
            {
                return Machine.GetMapConsiderCache<K, V>(ObjectID, FieldName, false);
            }
        }

        // Call this when you want to modify the set
        private IMapCollection<K, V> GetOrCreateGMMap()
        {
            return Machine.GetMapConsiderCache<K, V>(ObjectID, FieldName, true);
        }

        public V this[K key]
        {
            get
            {
                IMapCollection<K, V> set = GMMap;
                if (set == null)
                {
                    throw new System.Collections.Generic.KeyNotFoundException();
                }
                return GameplayMachine.ResolveGameplayObjectReference(set[key]);
            }

            set
            {
                IMapCollection<K, V> myCollectionSet = GetOrCreateGMMap();
                V currentValue;
                bool hasKey = myCollectionSet.TryGetValue(key, out currentValue);
                Machine.ValidateGameplayReferences(key);
                Machine.ValidateGameplayReferences(value);
                myCollectionSet[key] = value;
                if (hasKey)
                {
                    Machine.TrackGameplayReferencesRemoved(ObjectID, FieldName, currentValue);
                    Machine.TrackGameplayReferencesAdded(ObjectID, FieldName, value);
                    Machine.NotifyFieldChangedEventIndexed(ObjectID, FieldName, ObjectEventTypes.ItemChanged, key,
                        value, currentValue, referenceIndexUpdated: true);
                }
                else
                {
                    Machine.TrackGameplayReferencesAdded(ObjectID, FieldName, key);
                    Machine.TrackGameplayReferencesAdded(ObjectID, FieldName, value);
                    Machine.NotifyFieldChangedEventIndexed(ObjectID, FieldName, ObjectEventTypes.Add, key,
                        referenceIndexUpdated: true);
                }
            }
        }

        public bool TryGet(K key, out V value)
        {
            IMapCollection<K, V> set = GMMap;
            if (set == null)
            {
                value = default(V);
                return false;
            }
            bool result = set.TryGetValue(key, out value);
            if (result)
            {
                value = GameplayMachine.ResolveGameplayObjectReference(value);
            }
            return result;
        }

        public bool Add(K key, V value)
        {
            Machine.ValidateGameplayReferences(key);
            Machine.ValidateGameplayReferences(value);
            IMapCollection<K, V> myCollectionMap = GetOrCreateGMMap();
            if (myCollectionMap.TryAdd(key, value))
            {
                Machine.TrackGameplayReferencesAdded(ObjectID, FieldName, key);
                Machine.TrackGameplayReferencesAdded(ObjectID, FieldName, value);
                Machine.NotifyFieldChangedEventIndexed(ObjectID, FieldName, ObjectEventTypes.Add, key,
                    referenceIndexUpdated: true);
                return true;
            }
            return false;
        }

        public int AddRange(IEnumerable<KVPair<K, V>> items)
        {
            IMapCollection<K, V> myCollectionMap = GetOrCreateGMMap();
            System.Collections.Generic.List<K> successAdds = null;
            foreach (var item in items)
            {
                successAdds = successAdds ?? new System.Collections.Generic.List<K>();
                Machine.ValidateGameplayReferences(item.Key);
                Machine.ValidateGameplayReferences(item.Value);
                if (myCollectionMap.TryAdd(item.Key, item.Value))
                {
                    successAdds.Add(item.Key);
                    Machine.TrackGameplayReferencesAdded(ObjectID, FieldName, item.Key);
                    Machine.TrackGameplayReferencesAdded(ObjectID, FieldName, item.Value);
                }
            }

            if (successAdds != null)
            {
                Machine.NotifyFieldChangedEventsIndexed(ObjectID, FieldName, successAdds, null,
                    referenceIndexUpdated: true);
            }
            
            return successAdds == null ? 0 : successAdds.Count;
        }

        public int AddRange(IEnumerable<KeyValuePair<K, V>> items)
        {
            IMapCollection<K, V> myCollectionMap = GetOrCreateGMMap();
            System.Collections.Generic.List<K> successAdds = null;
            foreach (var item in items)
            {
                successAdds = successAdds ?? new System.Collections.Generic.List<K>();
                Machine.ValidateGameplayReferences(item.Key);
                Machine.ValidateGameplayReferences(item.Value);
                if (myCollectionMap.TryAdd(item.Key, item.Value))
                {
                    successAdds.Add(item.Key);
                    Machine.TrackGameplayReferencesAdded(ObjectID, FieldName, item.Key);
                    Machine.TrackGameplayReferencesAdded(ObjectID, FieldName, item.Value);
                }
            }

            if (successAdds != null)
            {
                Machine.NotifyFieldChangedEventsIndexed(ObjectID, FieldName, successAdds, null,
                    referenceIndexUpdated: true);
            }

            return successAdds == null ? 0 : successAdds.Count;
        }

        public bool Remove(K key)
        {
            IMapCollection<K, V> myCollectionMap = GetOrCreateGMMap();
            if (myCollectionMap == null || myCollectionMap.Count == 0)
            {
                return false;
            }

            if (myCollectionMap.TryGetValue(key, out V oldValue) && myCollectionMap.Remove(key))
            {
                Machine.TrackGameplayReferencesRemoved(ObjectID, FieldName, key);
                Machine.TrackGameplayReferencesRemoved(ObjectID, FieldName, oldValue);
                Machine.NotifyFieldChangedEventIndexed(ObjectID, FieldName, ObjectEventTypes.Remove, key,
                    referenceIndexUpdated: true);
                return true;
            }
            return false;
        }

        public bool Contains(K key)
        {
            IMapCollection<K, V> set = GMMap;
            if (set == null)
            {
                return false;
            }

            return set.ContainsKey(key);
        }

        public void Clear()
        {
            IMapCollection<K, V> myCollectionMap = GetOrCreateGMMap();
            if (myCollectionMap == null || myCollectionMap.Count == 0)
            {
                return;
            }
            System.Collections.Generic.List<K> removes = new System.Collections.Generic.List<K>(myCollectionMap.Keys);
            myCollectionMap.Clear();
            Machine.RefreshGameplayReferences(ObjectID, FieldName);
            Machine.NotifyFieldChangedEventsIndexed(ObjectID, FieldName, null, removes,
                referenceIndexUpdated: true);
        }

        public int Count
        {
            get
            {
                var set = GMMap;
                if (set == null)
                {
                    return 0;
                }

                return set.Count;
            }
        }

        public bool SetToField(IFieldObject other)
        {
            if (Machine == other.Machine && ObjectID.Equals(other.ObjectID) && FieldName.Equals(other.FieldName))
            {
                return true;
            }

            Map<K, V> otherSet = (Map<K, V>)other;

            IMapCollection<K, V> myCollectionMap = GetOrCreateGMMap();
            IMapCollection<K, V> otherCollectionMap = otherSet.GMMap;

            if (otherCollectionMap == null || otherCollectionMap.Count == 0)
            {
                Clear();
            }
            else
            {
                if (myCollectionMap == null || myCollectionMap.Count == 0)
                {
                    foreach (KVPair<K, V> item in otherCollectionMap)
                    {
                        Machine.ValidateGameplayReferences(item.Key);
                        Machine.ValidateGameplayReferences(item.Value);
                        myCollectionMap.Add(item.Key, item.Value);
                    }
                    Machine.RefreshGameplayReferences(ObjectID, FieldName);
                    Machine.NotifyFieldChangedEventsIndexed(ObjectID, FieldName, otherCollectionMap.Keys, null,
                        referenceIndexUpdated: true);
                }
                else
                {
                    HashSet<K> toAdds = new HashSet<K>();
                    HashSet<K> toRemoves = new HashSet<K>();
                    HashSet<K> toChanges = new HashSet<K>();

                    foreach (KVPair<K, V> item in otherCollectionMap)
                    {
                        if (!myCollectionMap.ContainsKey(item.Key))
                        {
                            toAdds.Add(item.Key);
                        }
                        else
                        {
                            toChanges.Add(item.Key);
                            Machine.ValidateGameplayReferences(item.Key);
                            Machine.ValidateGameplayReferences(item.Value);
                            myCollectionMap[item.Key] = item.Value;
                        }
                    }

                    foreach (KVPair<K, V> item in myCollectionMap)
                    {
                        if (!otherCollectionMap.ContainsKey(item.Key))
                        {
                            toRemoves.Add(item.Key);
                        }
                    }

                    foreach (K item in toAdds)
                    {
                        Machine.ValidateGameplayReferences(item);
                        Machine.ValidateGameplayReferences(otherCollectionMap[item]);
                        myCollectionMap.Add(item, otherCollectionMap[item]);
                    }

                    foreach (K item in toRemoves)
                    {
                        myCollectionMap.Remove(item);
                    }

                    Machine.RefreshGameplayReferences(ObjectID, FieldName);
                    Machine.NotifyFieldChangedEventsIndexed(ObjectID, FieldName, toAdds, toRemoves, toChanges,
                        referenceIndexUpdated: true);
                }
            }

            return true;
        }

        public IEnumerator<KVPair<K, V>> GetEnumerator()
        {
            var map = GMMap;
            if (map == null)
            {
                yield break;
            }
            foreach (var item in map)
            {
                yield return new KVPair<K, V>()
                {
                    Key = GameplayMachine.ResolveGameplayObjectReference(item.Key),
                    Value = GameplayMachine.ResolveGameplayObjectReference(item.Value),
                };
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            var map = GMMap;
            if (map == null)
            {
                yield break;
            }
            foreach (var item in map)
            {
                yield return new KVPair<K, V>()
                {
                    Key = GameplayMachine.ResolveGameplayObjectReference(item.Key),
                    Value = GameplayMachine.ResolveGameplayObjectReference(item.Value),
                };
            }
        }

        public IGMCollection GetController(IGameplayObject gameplayObject, ODFieldName fieldIndex)
        {
            return gameplayObject.GetMap<K, V>(fieldIndex);
        }
    }
}
