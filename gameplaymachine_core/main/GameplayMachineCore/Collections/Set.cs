using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace GMCore.Collections
{
    [Serializable]
    internal sealed class GameplayCollectionComparer<T> : IComparer<T>
    {
        public static GameplayCollectionComparer<T> Default { get; } = new GameplayCollectionComparer<T>();

        private GameplayCollectionComparer() { }

        public int Compare(T left, T right)
        {
            if (left is IGameplayObjectOperator leftObject && right is IGameplayObjectOperator rightObject)
                return leftObject.ObjectID.CompareTo(rightObject.ObjectID);
            return XLockstep.DeterministicComparer<T>.Default.Compare(left, right);
        }
    }

    public interface ISetCollection<T> : IEnumerable<T>, IGMCollection
    {
        public int Count { get; }

        public bool Add(T item);

        public bool Remove(T item);

        public bool Contains(T item);

        public void Clear();

        public int AddRange(IEnumerable<T> collection);
    }

    [System.Serializable]
    public class SetCollection<T> : XLockstep.DeterministicSet<T>, ISetCollection<T>, ICollectionMemoryObject, IReplicatedCollection,
        IGameplayReferenceCollection
    {
        public SetCollection() : base(GameplayCollectionComparer<T>.Default) { }
        protected SetCollection(SerializationInfo info, StreamingContext context)
            : base(info, context, GameplayCollectionComparer<T>.Default) { }

        public object Get(object index)
        {
            return index;
        }

        public void ApplyReplicationAdd(object key, object value)
        {
            if (!Add((T)key))
                throw new InvalidOperationException("Replicated set add targeted an existing value");
        }

        public void ApplyReplicationRemove(object key)
        {
            if (!Remove((T)key))
                throw new InvalidOperationException("Replicated set remove targeted a missing value");
        }

        public void ApplyReplicationItemChanged(object key, object value) =>
            throw new InvalidOperationException("Sets do not support ItemChanged replication operations");

        public void CopyTo(IGameplayObject gameplayObject, ODFieldName fieldIndex)
        {
            ISetCollection<T> collection = gameplayObject.GetSet<T>(fieldIndex);
            collection.Clear();
            collection.AddRange(this);
        }

        public int AddRange(IEnumerable<T> collection)
        {
            int result = 0;
            foreach (var item in collection)
            {
                if (Add(item))
                {
                    result++;
                }
            }

            return result;
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        GameplayReferenceCollectionCleanup IGameplayReferenceCollection.RemoveGameplayObjectReferences(
            GameplayMachine machine,
            GObjectID target)
        {
            var removes = this.Where(item => GameplayObjectReferenceWalker.Contains(item, machine, target)).ToList();
            foreach (T item in removes)
                Remove(item);
            return new GameplayReferenceCollectionCleanup
            {
                Changed = removes.Count > 0,
                RemovedItems = removes.Cast<object>().ToList(),
            };
        }
    }

    [TypeSupport(typeof(ODCore.Collections.Set<>), false, typeof(CollectionBinder<>), typeof(System.Collections.Generic.HashSet<>))]
    public struct Set<T> : ICollectionFieldObject<T>
    {
        public GameplayMachine Machine { get; set; }

        public GObjectID ObjectID { get; set; }

        public ODFieldName FieldName { get; set; }

        // Call this when get-only
        private ISetCollection<T> GMSet
        {
            get
            {
                return Machine.GetSetConsiderCache<T>(ObjectID, FieldName, false);
            }
        }

        // Call this when you want to modify the set
        private ISetCollection<T> GetOrCreateGMSet()
        {
            //return Machine.GetOrCreateCollectionConsiderCache<SetCollection<T>, T, int>(ObjectID, FieldName) as ISetCollection<T>;
            return Machine.GetSetConsiderCache<T>(ObjectID, FieldName, true);
        }

        public bool Add(T item)
        {
            Machine.ValidateGameplayReferences(item);
            ISetCollection<T> myCollectionSet = GetOrCreateGMSet();
            if (myCollectionSet.Add(item))
            {
                Machine.TrackGameplayReferencesAdded(ObjectID, FieldName, item);
                Machine.NotifyFieldChangedEventIndexed(ObjectID, FieldName, ObjectEventTypes.Add, item,
                    referenceIndexUpdated: true);
                return true;
            }
            return false;
        }

        public int AddRange(IEnumerable<T> items)
        {
            ISetCollection<T> myCollectionSet = GetOrCreateGMSet();
            System.Collections.Generic.List<T> successAdds = null;
            foreach (var item in items)
            {
                successAdds = successAdds ?? new System.Collections.Generic.List<T>();
                Machine.ValidateGameplayReferences(item);
                if (myCollectionSet.Add(item))
                {
                    successAdds.Add(item);
                    Machine.TrackGameplayReferencesAdded(ObjectID, FieldName, item);
                }
            }

            if (successAdds != null)
            {
                Machine.NotifyFieldChangedEventsIndexed(ObjectID, FieldName, successAdds, null,
                    referenceIndexUpdated: true);
            }

            return successAdds == null ? 0 : successAdds.Count;
        }

        public bool Remove(T item)
        {
            ISetCollection<T> myCollectionSet = GetOrCreateGMSet();
            if (myCollectionSet == null || myCollectionSet.Count == 0)
            {
                return false;
            }

            if (myCollectionSet.Remove(item))
            {
                Machine.TrackGameplayReferencesRemoved(ObjectID, FieldName, item);
                Machine.NotifyFieldChangedEventIndexed(ObjectID, FieldName, ObjectEventTypes.Remove, item,
                    referenceIndexUpdated: true);
                return true;
            }
            return false;
        }

        public bool Contains(T item)
        {
            var set = GMSet;
            if (set == null)
            {
                return false;
            }

            return set.Contains(item);
        }

        public void Clear()
        {
            ISetCollection<T> myCollectionSet = GetOrCreateGMSet();
            if (myCollectionSet == null || myCollectionSet.Count == 0)
            {
                return;
            }
            System.Collections.Generic.List<T> removes = new System.Collections.Generic.List<T>(myCollectionSet);
            myCollectionSet.Clear();
            Machine.RefreshGameplayReferences(ObjectID, FieldName);
            Machine.NotifyFieldChangedEventsIndexed(ObjectID, FieldName, null, removes,
                referenceIndexUpdated: true);
        }

        public int Count
        {
            get
            {
                var set = GMSet;
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

            Set<T> otherSet = (Set<T>)other;

            ISetCollection<T> myCollectionSet = GetOrCreateGMSet();
            ISetCollection<T> otherCollectionSet = otherSet.GMSet;

            if (otherCollectionSet == null || otherCollectionSet.Count == 0)
            {
                Clear();
            }
            else
            {
                if (myCollectionSet == null || myCollectionSet.Count == 0)
                {
                    foreach (T item in otherCollectionSet)
                    {
                        Machine.ValidateGameplayReferences(item);
                        myCollectionSet.Add(item);
                    }
                    Machine.RefreshGameplayReferences(ObjectID, FieldName);
                    Machine.NotifyFieldChangedEventsIndexed(ObjectID, FieldName, otherCollectionSet, null,
                        referenceIndexUpdated: true);
                }
                else
                {
                    HashSet<T> toAdds = new HashSet<T>();
                    HashSet<T> toRemoves = new HashSet<T>();

                    foreach (T item in otherCollectionSet)
                    {
                        if (!myCollectionSet.Contains(item))
                        {
                            toAdds.Add(item);
                        }
                    }

                    foreach (T item in myCollectionSet)
                    {
                        if (!otherCollectionSet.Contains(item))
                        {
                            toRemoves.Add(item);
                        }
                    }

                    foreach (T item in toAdds)
                    {
                        Machine.ValidateGameplayReferences(item);
                        myCollectionSet.Add(item);
                    }

                    foreach (T item in toRemoves)
                    {
                        myCollectionSet.Remove(item);
                    }

                    Machine.RefreshGameplayReferences(ObjectID, FieldName);
                    Machine.NotifyFieldChangedEventsIndexed(ObjectID, FieldName, toAdds, toRemoves,
                        referenceIndexUpdated: true);
                }
            }

            return true;
        }

        public IEnumerator<T> GetEnumerator()
        {
            var set = GMSet;
            if (set == null)
            {
                yield break;
            }
            foreach (var item in set)
            {
                yield return GameplayMachine.ResolveGameplayObjectReference(item);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            var set = GMSet;
            if (set == null)
            {
                yield break;
            }
            foreach (var item in set)
            {
                yield return GameplayMachine.ResolveGameplayObjectReference(item);
            }
        }

        public IGMCollection GetController(IGameplayObject gameplayObject, ODFieldName field)
        {
            return gameplayObject.GetSet<T>(field);
        }
    }
}
