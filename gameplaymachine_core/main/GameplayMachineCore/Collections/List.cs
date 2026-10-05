using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace GMCore.Collections
{
    public interface IListCollection<T> : IEnumerable<T>, IGMCollection
    {
        public int Count { get; }

        public void Add(T item);

        public void AddRange(IEnumerable<T> collection);

        public bool Remove(T item);

        public void RemoveAt(int index);

        public bool Contains(T item);

        public T this[int index] { get; set; }

        public void Insert(int index, T item);

        public void Sort();

        public void Clear();
    }

    [System.Serializable]
    public class ListCollection<T> : System.Collections.Generic.List<T>, IListCollection<T>, ICollectionMemoryObject,
        IGameplayReferenceCollection
    {
        public ListCollection()
        {

        }

        public ListCollection(int capacity) : base (capacity)
        {

        }

        public object Get(object index)
        {
            return this[(int)index];
        }

        public void CopyTo(IGameplayObject gameplayObject, ODFieldName fieldIndex)
        {
            IListCollection<T> collection = gameplayObject.GetList<T>(fieldIndex);
            collection.Clear();
            foreach (var item in this)
            {
                collection.Add(item);
            }
        }

        GameplayReferenceCollectionCleanup IGameplayReferenceCollection.RemoveGameplayObjectReferences(
            GameplayMachine machine,
            GObjectID target)
        {
            bool changed = false;
            for (int index = Count - 1; index >= 0; index--)
            {
                if (!GameplayObjectReferenceWalker.Contains(this[index], machine, target))
                    continue;
                RemoveAt(index);
                changed = true;
            }
            return new GameplayReferenceCollectionCleanup { Changed = changed };
        }
    }

    [TypeSupport(typeof(ODCore.Collections.List<>), false, typeof(FieldChangeEventBinder), typeof(System.Collections.Generic.List<>))]
    public struct List<T> : ICollectionFieldObject<T>
    {
        public GameplayMachine Machine { get; set; }

        public GObjectID ObjectID { get; set; }

        public ODFieldName FieldName { get; set; }

        // Call this when get-only
        private IListCollection<T> GMList
        {
            get
            {
                return Machine.GetListConsiderCache<T>(ObjectID, FieldName, false);
            }
        }

        // Call this when you want to modify the set
        private IListCollection<T> GetOrCreateGMList()
        {
            return Machine.GetListConsiderCache<T>(ObjectID, FieldName, true);
        }

        public T this[int index]
        {
            get
            {
                IListCollection<T> set = GMList;
                if (set == null)
                {
                    throw new System.ArgumentOutOfRangeException();
                }
                return GameplayMachine.ResolveGameplayObjectReference(set[index]);
            }

            set
            {
                IListCollection<T> myCollectionList = GetOrCreateGMList();
                T oldValue = myCollectionList[index];
                Machine.ValidateGameplayReferences(value);
                myCollectionList[index] = value;
                Machine.TrackGameplayReferencesRemoved(ObjectID, FieldName, oldValue);
                Machine.TrackGameplayReferencesAdded(ObjectID, FieldName, value);
                Machine.NotifyFieldChangedEventIndexed(ObjectID, FieldName, ObjectEventTypes.Changed, null,
                    disableEqualTest: true, referenceIndexUpdated: true);
            }
        }

        public void Add(T item)
        {
            Machine.ValidateGameplayReferences(item);
            IListCollection<T> myCollectionList = GetOrCreateGMList();
            myCollectionList.Add(item);
            Machine.TrackGameplayReferencesAdded(ObjectID, FieldName, item);
            Machine.NotifyFieldChangedEventIndexed(ObjectID, FieldName, ObjectEventTypes.Changed, null,
                disableEqualTest: true, referenceIndexUpdated: true);
        }

        public void AddRange(IEnumerable<T> items)
        {
            var values = new System.Collections.Generic.List<T>(items);
            foreach (T item in values)
                Machine.ValidateGameplayReferences(item);
            IListCollection<T> myCollectionList = GetOrCreateGMList();
            foreach (T item in values)
            {
                myCollectionList.Add(item);
                Machine.TrackGameplayReferencesAdded(ObjectID, FieldName, item);
            }
            Machine.NotifyFieldChangedEventIndexed(ObjectID, FieldName, ObjectEventTypes.Changed, null,
                disableEqualTest: true, referenceIndexUpdated: true);
        }

        public void Insert(int index, T item)
        {
            Machine.ValidateGameplayReferences(item);
            IListCollection<T> myCollectionList = GetOrCreateGMList();
            myCollectionList.Insert(index, item);
            Machine.TrackGameplayReferencesAdded(ObjectID, FieldName, item);
            Machine.NotifyFieldChangedEventIndexed(ObjectID, FieldName, ObjectEventTypes.Changed, null,
                disableEqualTest: true, referenceIndexUpdated: true);
        }

        public bool Remove(T item)
        {
            IListCollection<T> myCollectionList = GetOrCreateGMList();
            if (myCollectionList == null || myCollectionList.Count == 0)
            {
                return false;
            }

            if (myCollectionList.Remove(item))
            {
                Machine.TrackGameplayReferencesRemoved(ObjectID, FieldName, item);
                Machine.NotifyFieldChangedEventIndexed(ObjectID, FieldName, ObjectEventTypes.Changed, null,
                    disableEqualTest: true, referenceIndexUpdated: true);
                return true;
            }
            return false;
        }

        public void RemoveAt(int index)
        {
            IListCollection<T> myCollectionList = GetOrCreateGMList();
            if (myCollectionList == null || myCollectionList.Count == 0)
            {
                return;
            }

            T item = myCollectionList[index];
            myCollectionList.RemoveAt(index);
            Machine.TrackGameplayReferencesRemoved(ObjectID, FieldName, item);
            Machine.NotifyFieldChangedEventIndexed(ObjectID, FieldName, ObjectEventTypes.Changed, null,
                disableEqualTest: true, referenceIndexUpdated: true);
            return;
        }

        public bool Contains(T item)
        {
            IListCollection<T> list = GMList;
            if (list == null)
            {
                return false;
            }

            return list.Contains(item);
        }

        public int Count
        {
            get
            {
                return GMList.Count;
            }
        }

        public void Clear()
        {
            IListCollection<T> myCollectionList = GetOrCreateGMList();
            if (myCollectionList == null || myCollectionList.Count == 0)
            {
                return;
            }

            myCollectionList.Clear();
            Machine.RefreshGameplayReferences(ObjectID, FieldName);
            Machine.NotifyFieldChangedEventIndexed(ObjectID, FieldName, ObjectEventTypes.Changed, null,
                disableEqualTest: true, referenceIndexUpdated: true);
        }

        #region Sorting

        public void Sort()
        {
            IListCollection<T> list = GMList;
            if (list == null)
            {
                return;
            }

            list.Sort();
            Machine.NotifyFieldChangedEventIndexed(ObjectID, FieldName, ObjectEventTypes.Changed, null,
                disableEqualTest: true, referenceIndexUpdated: true);
        }

        #endregion

        public IEnumerator<T> GetEnumerator()
        {
            IListCollection<T> list = GMList;
            if (list == null)
            {
                yield break;
            }
            foreach (var item in list)
            {
                yield return GameplayMachine.ResolveGameplayObjectReference(item);
            }
        }

        public bool SetToField(IFieldObject other)
        {
            if (Machine == other.Machine && ObjectID.Equals(other.ObjectID) && FieldName.Equals(other.FieldName))
            {
                return true;
            }

            List<T> otherSet = (List<T>)other;

            IListCollection<T> myCollectionList = GetOrCreateGMList();
            IListCollection<T> otherCollectionList = otherSet.GMList;

            myCollectionList.Clear();
            if (otherCollectionList != null)
            {
                foreach (var item in otherCollectionList)
                {
                    Machine.ValidateGameplayReferences(item);
                    myCollectionList.Add(item);
                }
            }

            Machine.RefreshGameplayReferences(ObjectID, FieldName);
            Machine.NotifyFieldChangedEventIndexed(ObjectID, FieldName, ObjectEventTypes.Changed, null,
                disableEqualTest: true, referenceIndexUpdated: true);

            return true;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            IListCollection<T> list = GMList;
            if (list == null)
            {
                yield break;
            }
            foreach (var item in list)
            {
                yield return GameplayMachine.ResolveGameplayObjectReference(item);
            }
        }

        public IGMCollection GetController(IGameplayObject gameplayObject, ODFieldName field)
        {
            return gameplayObject.GetList<T>(field);
        }
    }
}
