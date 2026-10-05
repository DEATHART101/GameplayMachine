using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GMCore
{
    internal readonly struct GameplayReferenceField : IEquatable<GameplayReferenceField>
    {
        public readonly GObjectID ObjectID;
        public readonly ODFieldName FieldName;

        public GameplayReferenceField(GObjectID objectID, ODFieldName fieldName)
        {
            ObjectID = objectID;
            FieldName = fieldName;
        }

        public bool Equals(GameplayReferenceField other) =>
            ObjectID.Equals(other.ObjectID) && FieldName.Equals(other.FieldName);

        public override bool Equals(object obj) =>
            obj is GameplayReferenceField other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ObjectID, FieldName);
    }

    internal sealed class GameplayReferenceIndex
    {
        private readonly Dictionary<GObjectID, HashSet<GameplayReferenceField>> m_incoming =
            new Dictionary<GObjectID, HashSet<GameplayReferenceField>>();
        private readonly Dictionary<GameplayReferenceField, Dictionary<GObjectID, int>> m_outgoing =
            new Dictionary<GameplayReferenceField, Dictionary<GObjectID, int>>();
        private readonly Dictionary<GObjectID, HashSet<GameplayReferenceField>> m_ownedFields =
            new Dictionary<GObjectID, HashSet<GameplayReferenceField>>();

        public void Clear()
        {
            m_incoming.Clear();
            m_outgoing.Clear();
            m_ownedFields.Clear();
        }

        public GameplayReferenceField[] GetIncoming(GObjectID target)
        {
            return m_incoming.TryGetValue(target, out HashSet<GameplayReferenceField> fields)
                ? fields.ToArray()
                : Array.Empty<GameplayReferenceField>();
        }

        public IEnumerable<(GameplayReferenceField Field, GObjectID Target)> GetReferences(IEnumerable<GObjectID> owners)
        {
            foreach (var owner in owners)
                if (m_ownedFields.TryGetValue(owner, out var fields))
                    foreach (var field in fields)
                        foreach (var target in m_outgoing[field].Keys)
                            yield return (field, target);
        }

        public void RestoreReference(GameplayReferenceField field, GObjectID target) => Add(field, target);

        public void ReplaceValue(
            GameplayMachine machine,
            GameplayReferenceField field,
            object oldValue,
            object newValue)
        {
            RemoveValue(machine, field, oldValue);
            AddValue(machine, field, newValue);
        }

        public void AddValue(GameplayMachine machine, GameplayReferenceField field, object value)
        {
            GameplayObjectReferenceWalker.Visit(value, machine, target => Add(field, target));
        }

        public void RemoveValue(GameplayMachine machine, GameplayReferenceField field, object value)
        {
            GameplayObjectReferenceWalker.Visit(value, machine, target => Remove(field, target));
        }

        public void RefreshValue(GameplayMachine machine, GameplayReferenceField field, object value)
        {
            RemoveField(field);
            AddValue(machine, field, value);
        }

        public void RemoveOwner(GObjectID owner)
        {
            if (!m_ownedFields.TryGetValue(owner, out HashSet<GameplayReferenceField> fields))
            {
                return;
            }

            foreach (GameplayReferenceField field in fields.ToArray())
            {
                RemoveField(field);
            }
        }

        private void Add(GameplayReferenceField field, GObjectID target)
        {
            if (!target.IsValid)
            {
                return;
            }

            if (!m_outgoing.TryGetValue(field, out Dictionary<GObjectID, int> targets))
            {
                targets = new Dictionary<GObjectID, int>();
                m_outgoing.Add(field, targets);
                if (!m_ownedFields.TryGetValue(field.ObjectID, out HashSet<GameplayReferenceField> fields))
                {
                    fields = new HashSet<GameplayReferenceField>();
                    m_ownedFields.Add(field.ObjectID, fields);
                }
                fields.Add(field);
            }

            targets.TryGetValue(target, out int count);
            targets[target] = count + 1;
            if (count != 0)
            {
                return;
            }

            if (!m_incoming.TryGetValue(target, out HashSet<GameplayReferenceField> incoming))
            {
                incoming = new HashSet<GameplayReferenceField>();
                m_incoming.Add(target, incoming);
            }
            incoming.Add(field);
        }

        private void Remove(GameplayReferenceField field, GObjectID target)
        {
            if (!m_outgoing.TryGetValue(field, out Dictionary<GObjectID, int> targets) ||
                !targets.TryGetValue(target, out int count))
            {
                return;
            }

            if (count > 1)
            {
                targets[target] = count - 1;
                return;
            }

            targets.Remove(target);
            if (m_incoming.TryGetValue(target, out HashSet<GameplayReferenceField> incoming))
            {
                incoming.Remove(field);
                if (incoming.Count == 0)
                {
                    m_incoming.Remove(target);
                }
            }
            if (targets.Count == 0)
            {
                RemoveEmptyField(field);
            }
        }

        private void RemoveField(GameplayReferenceField field)
        {
            if (!m_outgoing.TryGetValue(field, out Dictionary<GObjectID, int> targets))
            {
                return;
            }

            foreach (GObjectID target in targets.Keys)
            {
                if (!m_incoming.TryGetValue(target, out HashSet<GameplayReferenceField> incoming))
                {
                    continue;
                }
                incoming.Remove(field);
                if (incoming.Count == 0)
                {
                    m_incoming.Remove(target);
                }
            }
            RemoveEmptyField(field);
        }

        private void RemoveEmptyField(GameplayReferenceField field)
        {
            m_outgoing.Remove(field);
            if (!m_ownedFields.TryGetValue(field.ObjectID, out HashSet<GameplayReferenceField> fields))
            {
                return;
            }
            fields.Remove(field);
            if (fields.Count == 0)
            {
                m_ownedFields.Remove(field.ObjectID);
            }
        }
    }

    internal sealed class GameplayReferenceCollectionCleanup
    {
        public bool Changed;
        public List<object> RemovedItems;
    }

    internal interface IGameplayReferenceCollection
    {
        GameplayReferenceCollectionCleanup RemoveGameplayObjectReferences(
            GameplayMachine machine,
            GObjectID target);
    }

    internal static class GameplayObjectReferenceWalker
    {
        private static readonly Dictionary<Type, FieldInfo[]> s_fields =
            new Dictionary<Type, FieldInfo[]>();
        private static readonly Dictionary<Type, SwitchStructMetadata> s_switchStructs =
            new Dictionary<Type, SwitchStructMetadata>();
        private static readonly object s_fieldsLock = new object();

        public static void Visit(object value, GameplayMachine machine, Action<GObjectID> visitor)
        {
            if (value == null || visitor == null)
            {
                return;
            }
            VisitCore(value, machine, visitor, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        public static bool Contains(object value, GameplayMachine machine, GObjectID target)
        {
            bool found = false;
            Visit(value, machine, id => found |= id.Equals(target));
            return found;
        }

        public static object Remove(
            object value,
            GameplayMachine machine,
            GObjectID target,
            out bool changed)
        {
            return RemoveCore(value, null, machine, target, out changed,
                new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        private static void VisitCore(
            object value,
            GameplayMachine machine,
            Action<GObjectID> visitor,
            HashSet<object> visited)
        {
            if (value == null)
            {
                return;
            }
            if (value is IGameplayObjectOperator gameplayObject)
            {
                if (ReferenceEquals(gameplayObject.Machine, machine) && gameplayObject.ObjectID.IsValid)
                {
                    visitor(gameplayObject.ObjectID);
                }
                return;
            }
            if (value is string)
            {
                return;
            }

            Type type = value.GetType();
            if (!type.IsValueType && !visited.Add(value))
            {
                return;
            }
            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry item in dictionary)
                {
                    VisitCore(item.Key, machine, visitor, visited);
                    VisitCore(item.Value, machine, visitor, visited);
                }
                return;
            }
            if (value is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    VisitCore(item, machine, visitor, visited);
                }
                return;
            }
            if (!(value is IODStruct))
            {
                return;
            }

            if (TryGetActiveSwitchProperty(value, type, out PropertyInfo activeProperty))
            {
                VisitCore(activeProperty.GetValue(value), machine, visitor, visited);
                return;
            }

            foreach (FieldInfo field in GetFields(type))
            {
                VisitCore(field.GetValue(value), machine, visitor, visited);
            }
        }

        private static object RemoveCore(
            object value,
            Type declaredType,
            GameplayMachine machine,
            GObjectID target,
            out bool changed,
            HashSet<object> visited)
        {
            changed = false;
            if (value == null)
            {
                return null;
            }
            if (value is IGameplayObjectOperator gameplayObject)
            {
                if (ReferenceEquals(gameplayObject.Machine, machine) && gameplayObject.ObjectID.Equals(target))
                {
                    changed = true;
                    return DefaultValue(declaredType);
                }
                return value;
            }
            if (value is string)
            {
                return value;
            }

            Type type = value.GetType();
            if (!type.IsValueType && !visited.Add(value))
            {
                return value;
            }
            if (value is IDictionary dictionary)
            {
                var keys = new System.Collections.Generic.List<object>();
                foreach (DictionaryEntry item in dictionary)
                {
                    if (Contains(item.Key, machine, target) || Contains(item.Value, machine, target))
                        keys.Add(item.Key);
                }
                foreach (object key in keys)
                    dictionary.Remove(key);
                changed = keys.Count > 0;
                return value;
            }
            if (value is IList list)
            {
                for (int index = list.Count - 1; index >= 0; index--)
                {
                    if (!Contains(list[index], machine, target))
                        continue;
                    if (list.IsFixedSize)
                    {
                        list[index] = DefaultValue(type.IsArray ? type.GetElementType() : list[index]?.GetType());
                    }
                    else
                    {
                        list.RemoveAt(index);
                    }
                    changed = true;
                }
                return value;
            }
            if (value is IEnumerable enumerable && !(value is IODStruct))
            {
                MethodInfo remove = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(method => method.Name == "Remove" && method.GetParameters().Length == 1);
                if (remove == null)
                    return value;
                var removes = new System.Collections.Generic.List<object>();
                foreach (object item in enumerable)
                    if (Contains(item, machine, target))
                        removes.Add(item);
                foreach (object item in removes)
                    remove.Invoke(value, new[] { item });
                changed = removes.Count > 0;
                return value;
            }
            if (!(value is IODStruct))
            {
                return value;
            }

            object boxed = value;
            if (TryGetActiveSwitchProperty(boxed, type, out PropertyInfo activeProperty))
            {
                object oldActiveValue = activeProperty.GetValue(boxed);
                object newActiveValue = RemoveCore(oldActiveValue, activeProperty.PropertyType, machine, target,
                    out bool activeChanged, visited);
                if (activeChanged)
                {
                    activeProperty.SetValue(boxed, newActiveValue);
                    changed = true;
                }
                return boxed;
            }

            foreach (FieldInfo field in GetFields(type))
            {
                object oldFieldValue = field.GetValue(boxed);
                object newFieldValue = RemoveCore(oldFieldValue, field.FieldType, machine, target,
                    out bool fieldChanged, visited);
                if (!fieldChanged)
                {
                    continue;
                }
                field.SetValue(boxed, newFieldValue);
                changed = true;
            }
            return boxed;
        }

        private static bool TryGetActiveSwitchProperty(
            object value,
            Type type,
            out PropertyInfo activeProperty)
        {
            SwitchStructMetadata metadata;
            lock (s_fieldsLock)
            {
                if (!s_switchStructs.TryGetValue(type, out metadata))
                {
                    metadata = CreateSwitchStructMetadata(type);
                    s_switchStructs.Add(type, metadata);
                }
            }

            activeProperty = null;
            if (metadata == null)
            {
                return false;
            }

            object switchValue = metadata.SwitchType.GetValue(value);
            return switchValue != null &&
                   metadata.Cases.TryGetValue(switchValue.ToString(), out activeProperty);
        }

        private static SwitchStructMetadata CreateSwitchStructMetadata(Type type)
        {
            PropertyInfo switchType = type.GetProperty(
                "SwitchType",
                BindingFlags.Instance | BindingFlags.Public);
            if (switchType == null || !switchType.CanRead || !switchType.PropertyType.IsEnum)
            {
                return null;
            }

            var cases = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
            foreach (string name in Enum.GetNames(switchType.PropertyType))
            {
                PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                if (property != null && property.CanRead && property.CanWrite)
                {
                    cases.Add(name, property);
                }
            }
            return cases.Count == 0 ? null : new SwitchStructMetadata(switchType, cases);
        }

        private static object DefaultValue(Type type)
        {
            if (type == null || !type.IsValueType || Nullable.GetUnderlyingType(type) != null)
            {
                return null;
            }
            return Activator.CreateInstance(type);
        }

        private static FieldInfo[] GetFields(Type type)
        {
            lock (s_fieldsLock)
            {
                if (!s_fields.TryGetValue(type, out FieldInfo[] fields))
                {
                    fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(field => !field.IsStatic && !field.IsInitOnly)
                        .ToArray();
                    s_fields.Add(type, fields);
                }
                return fields;
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        private sealed class SwitchStructMetadata
        {
            public readonly PropertyInfo SwitchType;
            public readonly Dictionary<string, PropertyInfo> Cases;

            public SwitchStructMetadata(PropertyInfo switchType, Dictionary<string, PropertyInfo> cases)
            {
                SwitchType = switchType;
                Cases = cases;
            }
        }
    }

    public partial class GameplayMachine
    {
        [NonSerialized]
        private GameplayReferenceIndex m_referenceIndex;
        [NonSerialized]
        private HashSet<GObjectID> m_deletingGameplayObjects;

        internal void TrackGameplayReferencesAdded(GObjectID objectID, ODFieldName fieldName, object value)
        {
            ValidateGameplayReferences(value);
            m_referenceIndex.AddValue(this, new GameplayReferenceField(objectID, fieldName), value);
        }

        internal void TrackGameplayReferencesRemoved(GObjectID objectID, ODFieldName fieldName, object value)
        {
            m_referenceIndex.RemoveValue(this, new GameplayReferenceField(objectID, fieldName), value);
        }

        internal void RefreshGameplayReferences(GObjectID objectID, ODFieldName fieldName)
        {
            object value = GetStoredFieldValueForReferences(objectID, fieldName);
            m_referenceIndex.RefreshValue(this, new GameplayReferenceField(objectID, fieldName), value);
        }

        internal void ValidateGameplayReferences(object value)
        {
            GameplayObjectReferenceWalker.Visit(value, this, target =>
            {
                if (m_deletingGameplayObjects.Contains(target))
                {
                    throw new InvalidOperationException(
                        $"GameplayObject {target.ID} is being deleted and cannot receive new references");
                }
            });
        }

        internal void RebuildGameplayReferenceIndex()
        {
            if (!(m_objectManager is IGameplayObjectSaveManager manager))
            {
                return;
            }
            foreach (IGameplayObject gameplayObject in manager.GetGameplayObjectsForSave())
            {
                m_referenceIndex.RemoveOwner(gameplayObject.ObjectID);
                foreach (KeyValuePair<ODFieldName, object> field in manager.GetStoredFieldsForSave(gameplayObject.ObjectID))
                {
                    m_referenceIndex.AddValue(this,
                        new GameplayReferenceField(gameplayObject.ObjectID, field.Key), field.Value);
                }
            }
        }

        private void RemoveIncomingGameplayReferences(GObjectID target)
        {
            foreach (GameplayReferenceField field in m_referenceIndex.GetIncoming(target))
            {
                if (field.ObjectID.Equals(target) || !ContainsGameplayObject(field.ObjectID))
                {
                    continue;
                }

                object value = GetStoredFieldValueForReferences(field.ObjectID, field.FieldName);
                if (value is IGameplayReferenceCollection collection)
                {
                    GameplayReferenceCollectionCleanup cleanup =
                        collection.RemoveGameplayObjectReferences(this, target);
                    if (!cleanup.Changed)
                    {
                        RefreshGameplayReferences(field.ObjectID, field.FieldName);
                        continue;
                    }

                    RefreshGameplayReferences(field.ObjectID, field.FieldName);
                    if (cleanup.RemovedItems == null)
                    {
                        NotifyFieldChangedEventIndexed(field.ObjectID, field.FieldName, ObjectEventTypes.Changed,
                            null, disableEqualTest: true, referenceIndexUpdated: true);
                    }
                    else
                    {
                        NotifyFieldChangedEventsIndexed(field.ObjectID, field.FieldName, null,
                            cleanup.RemovedItems, referenceIndexUpdated: true);
                    }
                    continue;
                }

                object cleaned = GameplayObjectReferenceWalker.Remove(value, this, target, out bool changed);
                if (changed)
                {
                    SetGameplayObjectValue(field.ObjectID, field.FieldName, cleaned);
                }
                else
                {
                    RefreshGameplayReferences(field.ObjectID, field.FieldName);
                }
            }
        }

        private object GetStoredFieldValueForReferences(GObjectID objectID, ODFieldName fieldName)
        {
            if (m_cacheMode && m_cacheManager.CacheDatas.TryGetValue(
                    objectID, out GameplayCacheManager.ObjectCacheData cacheData))
            {
                if (cacheData.Collections != null && cacheData.Collections.TryGetValue(fieldName, out object collection))
                {
                    return collection;
                }
                if (cacheData.Values != null && cacheData.Values.TryGetValue(fieldName, out object value))
                {
                    return value;
                }
                if (cacheData.Created)
                {
                    return null;
                }
            }

            IGameplayObject gameplayObject = m_objectManager.GetGameplayObject(objectID);
            return gameplayObject?[fieldName.Meta];
        }
    }
}
