using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace GMCore
{
    public enum GameplayDebugAccess
    {
        ObserveOnly,
        Control,
    }

    public enum GameplayDebugObjectChangeKind : byte
    {
        FieldChanged,
        Created,
        Deleted,
    }

    public enum GameplayDebugContainerKind : byte
    {
        Single,
        List,
        Set,
        Map,
    }

    public enum GameplayDebugValueKind : byte
    {
        Null,
        Boolean,
        Integer,
        FloatingPoint,
        Decimal,
        String,
        DateTime,
        Enum,
        GameplayObject,
        Resource,
        Sequence,
        Map,
        Object,
    }

    [Serializable]
    public sealed class GameplayDebugValue
    {
        public GameplayDebugValueKind Kind;
        public string TypeName;
        public string Scalar;
        public int ObjectID;
        public List<GameplayDebugValue> Items = new List<GameplayDebugValue>();
        public List<GameplayDebugNamedValue> Members = new List<GameplayDebugNamedValue>();
        public List<GameplayDebugMapEntry> Entries = new List<GameplayDebugMapEntry>();

        public static GameplayDebugValue Null(string typeName = null) => new GameplayDebugValue
        {
            Kind = GameplayDebugValueKind.Null,
            TypeName = typeName,
        };
    }

    [Serializable]
    public sealed class GameplayDebugNamedValue
    {
        public string Name;
        public GameplayDebugValue Value;
    }

    [Serializable]
    public sealed class GameplayDebugMapEntry
    {
        public GameplayDebugValue Key;
        public GameplayDebugValue Value;
    }

    public sealed class ODDebugFieldMeta
    {
        public ulong StableID;
        public string StableName;
        public string DisplayName;
        public ODFieldName FieldName;
        public Type ValueType;
        public Type KeyType;
        public GameplayDebugContainerKind Container;
        public bool IsDriven;
        public ODFieldReplicationMode ReplicationMode;
        public bool IsGameplayObjectReference;
        public ulong GameplayObjectClassID;
        public bool IsResourceReference;
        public bool IsResourceKeyReference;
        public IReadOnlyList<ODDebugResourceOption> ResourceOptions;
        public IReadOnlyList<ODDebugResourceOption> ResourceKeyOptions;
        public Func<GameplayMachine, GObjectID, object> Read;
        public Action<GameplayMachine, GObjectID, object> Write;
    }

    public sealed class ODDebugClassMeta
    {
        public ulong StableID;
        public string StableName;
        public string DisplayName;
        public ODClassName ClassName;
        public bool IsEngineManaged;
        public IReadOnlyList<ulong> AssignableClassIDs;
        public IReadOnlyList<ODDebugFieldMeta> Fields;
    }

    public sealed class ODDebugInterfaceFieldMeta
    {
        public string Name;
        public string DisplayName;
        public Type ValueType;
        public Type KeyType;
        public GameplayDebugContainerKind Container;
        public bool NotNull;
        public bool IsGameplayObjectReference;
        public ulong GameplayObjectClassID;
        public bool IsResourceReference;
        public bool IsResourceKeyReference;
        public IReadOnlyList<ODDebugResourceOption> ResourceOptions;
        public IReadOnlyList<ODDebugResourceOption> ResourceKeyOptions;
    }

    public sealed class ODDebugResourceOption
    {
        public ODResourceID ID;
        public string Name;
        public object Value;
    }

    public sealed class ODDebugInterfaceMeta
    {
        public ulong StableID;
        public string StableName;
        public string DisplayName;
        public GameplayRpcMode RpcMode;
        public GameplayInterfaceType InterfaceType;
        public bool IsRoutine;
        public Type ParamType;
        public Type ResultType;
        public IReadOnlyList<ODDebugInterfaceFieldMeta> Inputs;
        public IReadOnlyList<ODDebugInterfaceFieldMeta> Outputs;
        public Func<GameplayMachine, IReadOnlyDictionary<string, GameplayDebugValue>, ODDebugInvocationResult> Invoke;
    }

    public sealed class ODDebugInvocationResult
    {
        public bool Succeeded;
        public string Error;
        public GameplayDebugValue Output;

        public static ODDebugInvocationResult Success(object output = null) => new ODDebugInvocationResult
        {
            Succeeded = true,
            Output = GameplayDebugValueConverter.FromObject(output),
        };

        public static ODDebugInvocationResult Failure(string error) => new ODDebugInvocationResult
        {
            Error = error ?? "Interface execution failed",
        };
    }

    public interface IODDebugModule
    {
        ulong DebugSchemaID { get; }
        IEnumerable<ODDebugClassMeta> GetAllODDebugClassMetas();
        IEnumerable<ODDebugInterfaceMeta> GetAllODDebugInterfaceMetas();
    }

    public enum GameplayExecutionTraceStage : byte
    {
        Started,
        Completed,
    }

    public sealed class GameplayExecutionTraceEvent
    {
        public GameplayExecutionTraceStage Stage;
        public long TraceID;
        public long SpanID;
        public long ParentSpanID;
        public int Depth;
        public string InterfaceName;
        public GameplayRpcMode RpcMode;
        public string Origin;
        public DateTime TimestampUtc;
        public double DurationMilliseconds;
        public bool Succeeded;
        public string Error;
        public GameplayDebugValue Input;
        public GameplayDebugValue Output;
    }

    public sealed class GameplayDebugMachineInfo
    {
        public int MachineKey;
        public GameplayDebugAccess Access;
        public GameplayMachineRole Role;
        public GameplayNetworkState NetworkState;
        public long NetworkRevision;
        public ulong NetworkSchemaID;
        public ulong ResourceCatalogID;
        public ulong DebugSchemaID;
        public int ObjectCount;
        public int RootObjectID;
        public bool DuringExecution;
        public bool HasRoutine;
        public string[] Modules;
    }

    public sealed class GameplayDebugFieldInfo
    {
        public ulong StableID;
        public string Name;
        public string TypeName;
        public string KeyTypeName;
        public GameplayDebugContainerKind Container;
        public bool IsDriven;
        public ODFieldReplicationMode ReplicationMode;
        public bool IsGameplayObjectReference;
        public ulong GameplayObjectClassID;
        public bool IsResourceReference;
        public bool IsResourceKeyReference;
        public List<GameplayDebugResourceOption> ResourceOptions = new List<GameplayDebugResourceOption>();
        public List<GameplayDebugResourceOption> ResourceKeyOptions = new List<GameplayDebugResourceOption>();
        public GameplayDebugValue DefaultValue;
    }

    public sealed class GameplayDebugResourceOption
    {
        public string ID;
        public string Name;
    }

    public sealed class GameplayDebugClassInfo
    {
        public ulong StableID;
        public string Name;
        public bool IsEngineManaged;
        public List<ulong> AssignableClassIDs = new List<ulong>();
        public List<GameplayDebugFieldInfo> Fields = new List<GameplayDebugFieldInfo>();
    }

    public sealed class GameplayDebugObjectInfo
    {
        public int ObjectID;
        public ulong ClassID;
        public string ClassName;
        public bool IsRoot;
    }

    public sealed class GameplayDebugObjectSnapshot
    {
        public GameplayDebugObjectInfo Object;
        public List<GameplayDebugFieldValue> Fields = new List<GameplayDebugFieldValue>();
    }

    public sealed class GameplayDebugFieldValue
    {
        public ulong FieldID;
        public GameplayDebugValue Value;
    }

    public sealed class GameplayDebugSchema
    {
        public List<GameplayDebugClassInfo> Classes = new List<GameplayDebugClassInfo>();
        public List<GameplayDebugInterfaceInfo> Interfaces = new List<GameplayDebugInterfaceInfo>();
    }

    public sealed class GameplayDebugInterfaceInfo
    {
        public ulong StableID;
        public string Name;
        public GameplayRpcMode RpcMode;
        public GameplayInterfaceType InterfaceType;
        public bool IsRoutine;
        public List<GameplayDebugFieldInfo> Inputs = new List<GameplayDebugFieldInfo>();
        public List<GameplayDebugFieldInfo> Outputs = new List<GameplayDebugFieldInfo>();
    }

    public static class GameplayDebugValueConverter
    {
        public static GameplayDebugValue FromObject(object value)
        {
            return FromObjectCore(value, null, null, new HashSet<object>(ObjectReferenceComparer.Instance), 0);
        }

        public static GameplayDebugValue FromObject(object value,
            IReadOnlyList<ODDebugResourceOption> resourceOptions,
            IReadOnlyList<ODDebugResourceOption> resourceKeyOptions)
        {
            return FromObjectCore(value, resourceOptions, resourceKeyOptions,
                new HashSet<object>(ObjectReferenceComparer.Instance), 0);
        }

        private static GameplayDebugValue FromObjectCore(object value,
            IReadOnlyList<ODDebugResourceOption> resourceOptions,
            IReadOnlyList<ODDebugResourceOption> resourceKeyOptions,
            HashSet<object> visited,
            int depth)
        {
            if (value == null)
                return GameplayDebugValue.Null();

            Type type = value.GetType();
            string typeName = type.FullName ?? type.Name;
            if (resourceOptions != null && !(value is IDictionary) && !(value is IEnumerable))
            {
                ODDebugResourceOption resource = resourceOptions.FirstOrDefault(item => ReferenceEquals(item.Value, value));
                if (resource == null)
                    throw new InvalidOperationException("Resource is not part of the generated OD resource catalog.");
                return Scalar(GameplayDebugValueKind.Resource, typeName, resource.ID.ToString());
            }
            if (depth >= 64)
                return GameplayDebugValue.Null(typeName);
            if (!type.IsValueType && !(value is string) && !visited.Add(value))
                return GameplayDebugValue.Null(typeName);
            if (value is bool boolean)
                return Scalar(GameplayDebugValueKind.Boolean, typeName, boolean ? "true" : "false");
            if (value is string text || value is char)
                return Scalar(GameplayDebugValueKind.String, typeName, value.ToString());
            if (value is DateTime dateTime)
                return Scalar(GameplayDebugValueKind.DateTime, typeName, dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            if (type.IsEnum)
                return Scalar(GameplayDebugValueKind.Enum, typeName, value.ToString());
            if (IsInteger(type))
                return Scalar(GameplayDebugValueKind.Integer, typeName, Convert.ToString(value, CultureInfo.InvariantCulture));
            if (type == typeof(float) || type == typeof(double))
                return Scalar(GameplayDebugValueKind.FloatingPoint, typeName, Convert.ToString(value, CultureInfo.InvariantCulture));
            if (type == typeof(decimal))
                return Scalar(GameplayDebugValueKind.Decimal, typeName, Convert.ToString(value, CultureInfo.InvariantCulture));
            if (type == typeof(XLockstep.Fixed64))
                return Scalar(GameplayDebugValueKind.Decimal, typeName, value.ToString());
            if (value is IGameplayObjectOperator gameplayObject)
            {
                return new GameplayDebugValue
                {
                    Kind = GameplayDebugValueKind.GameplayObject,
                    TypeName = typeName,
                    ObjectID = gameplayObject.ObjectID.ID,
                };
            }
            if (value is IDictionary dictionary)
            {
                var result = new GameplayDebugValue { Kind = GameplayDebugValueKind.Map, TypeName = typeName };
                foreach (DictionaryEntry item in dictionary)
                    result.Entries.Add(new GameplayDebugMapEntry
                    {
                        Key = FromObjectCore(item.Key, resourceKeyOptions, null, visited, depth + 1),
                        Value = FromObjectCore(item.Value, resourceOptions, null, visited, depth + 1),
                    });
                return result;
            }
            if (value is IEnumerable enumerable)
            {
                var result = new GameplayDebugValue { Kind = GameplayDebugValueKind.Sequence, TypeName = typeName };
                foreach (object item in enumerable)
                {
                    if (TryReadKeyValue(item, out object key, out object itemValue))
                    {
                        result.Kind = GameplayDebugValueKind.Map;
                        result.Entries.Add(new GameplayDebugMapEntry
                        {
                            Key = FromObjectCore(key, resourceKeyOptions, null, visited, depth + 1),
                            Value = FromObjectCore(itemValue, resourceOptions, null, visited, depth + 1),
                        });
                    }
                    else
                    {
                        result.Items.Add(FromObjectCore(item, resourceOptions, null, visited, depth + 1));
                    }
                }
                return result;
            }

            var objectValue = new GameplayDebugValue { Kind = GameplayDebugValueKind.Object, TypeName = typeName };
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                objectValue.Members.Add(new GameplayDebugNamedValue
                {
                    Name = field.Name,
                    Value = FromObjectCore(field.GetValue(value), null, null, visited, depth + 1),
                });
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                         .Where(item => item.CanRead && item.GetIndexParameters().Length == 0 &&
                                        item.Name != nameof(IGameplayObjectOperator.Machine) &&
                                        item.Name != nameof(IGameplayObjectOperator.ObjectID)))
            {
                if (objectValue.Members.Any(item => item.Name == property.Name))
                    continue;
                try
                {
                    objectValue.Members.Add(new GameplayDebugNamedValue
                    {
                        Name = property.Name,
                        Value = FromObjectCore(property.GetValue(value), null, null, visited, depth + 1),
                    });
                }
                catch
                {
                    // Debug capture must not break gameplay execution because a diagnostic property threw.
                }
            }
            return objectValue;
        }

        public static T ConvertTo<T>(GameplayDebugValue value, GameplayMachine machine) =>
            (T)ToObject(value, typeof(T), machine);

        public static T ConvertTo<T>(GameplayDebugValue value, GameplayMachine machine,
            IReadOnlyList<ODDebugResourceOption> resourceOptions,
            IReadOnlyList<ODDebugResourceOption> resourceKeyOptions = null) =>
            (T)ToObject(value, typeof(T), machine, resourceOptions, resourceKeyOptions);

        public static object ToObject(GameplayDebugValue value, Type targetType, GameplayMachine machine)
        {
            return ToObject(value, targetType, machine, null, null);
        }

        public static object ToObject(GameplayDebugValue value, Type targetType, GameplayMachine machine,
            IReadOnlyList<ODDebugResourceOption> resourceOptions,
            IReadOnlyList<ODDebugResourceOption> resourceKeyOptions)
        {
            Type nullableType = Nullable.GetUnderlyingType(targetType);
            Type effectiveType = nullableType ?? targetType;
            if (value == null || value.Kind == GameplayDebugValueKind.Null)
            {
                if (!effectiveType.IsValueType || nullableType != null)
                    return null;
                return Activator.CreateInstance(effectiveType);
            }

            if (resourceOptions != null && value.Kind == GameplayDebugValueKind.Resource)
            {
                if (!ODResourceID.TryParse(value.Scalar, out ODResourceID resourceID))
                    throw new InvalidDataException("OD debug resource reference has an invalid resource ID.");
                ODDebugResourceOption resource = resourceOptions.FirstOrDefault(item => item.ID == resourceID)
                    ?? throw new InvalidDataException($"OD resource '{resourceID}' is not available for this field.");
                if (resource.Value != null && !effectiveType.IsInstanceOfType(resource.Value))
                    throw new InvalidDataException("OD resource type does not match the requested field type.");
                return resource.Value;
            }

            if (typeof(IGameplayObjectOperator).IsAssignableFrom(effectiveType))
            {
                if (value.ObjectID == 0)
                    return null;
                object boxed = Activator.CreateInstance(effectiveType);
                var reference = (IGameplayObjectOperator)boxed;
                reference.Machine = machine;
                reference.ObjectID = new GObjectID { ID = value.ObjectID };
                return reference;
            }
            if (effectiveType == typeof(string)) return value.Scalar;
            if (effectiveType == typeof(char)) return string.IsNullOrEmpty(value.Scalar) ? '\0' : value.Scalar[0];
            if (effectiveType == typeof(bool)) return string.Equals(value.Scalar, "true", StringComparison.OrdinalIgnoreCase);
            if (effectiveType == typeof(DateTime)) return DateTime.Parse(value.Scalar, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (effectiveType.IsEnum) return Enum.Parse(effectiveType, value.Scalar, true);
            if (effectiveType == typeof(byte)) return byte.Parse(value.Scalar, CultureInfo.InvariantCulture);
            if (effectiveType == typeof(sbyte)) return sbyte.Parse(value.Scalar, CultureInfo.InvariantCulture);
            if (effectiveType == typeof(short)) return short.Parse(value.Scalar, CultureInfo.InvariantCulture);
            if (effectiveType == typeof(ushort)) return ushort.Parse(value.Scalar, CultureInfo.InvariantCulture);
            if (effectiveType == typeof(int)) return int.Parse(value.Scalar, CultureInfo.InvariantCulture);
            if (effectiveType == typeof(uint)) return uint.Parse(value.Scalar, CultureInfo.InvariantCulture);
            if (effectiveType == typeof(long)) return long.Parse(value.Scalar, CultureInfo.InvariantCulture);
            if (effectiveType == typeof(ulong)) return ulong.Parse(value.Scalar, CultureInfo.InvariantCulture);
            if (effectiveType == typeof(float)) return float.Parse(value.Scalar, CultureInfo.InvariantCulture);
            if (effectiveType == typeof(double)) return double.Parse(value.Scalar, CultureInfo.InvariantCulture);
            if (effectiveType == typeof(decimal)) return decimal.Parse(value.Scalar, CultureInfo.InvariantCulture);
            if (effectiveType == typeof(XLockstep.Fixed64)) return XLockstep.Fixed64.Parse(value.Scalar);

            if (effectiveType.IsArray)
            {
                Type elementType = effectiveType.GetElementType();
                Array array = Array.CreateInstance(elementType, value.Items.Count);
                for (int i = 0; i < value.Items.Count; i++)
                    array.SetValue(ToObject(value.Items[i], elementType, machine, resourceOptions, null), i);
                return array;
            }

            if (value.Kind == GameplayDebugValueKind.Map && effectiveType.IsGenericType)
            {
                object map = Activator.CreateInstance(effectiveType);
                Type[] arguments = effectiveType.GetGenericArguments();
                MethodInfo add = effectiveType.GetMethods().FirstOrDefault(item =>
                    item.Name == "Add" && item.GetParameters().Length == 2);
                if (add != null && arguments.Length == 2)
                {
                    foreach (GameplayDebugMapEntry entry in value.Entries)
                        add.Invoke(map, new[]
                        {
                            ToObject(entry.Key, arguments[0], machine, resourceKeyOptions, null),
                            ToObject(entry.Value, arguments[1], machine, resourceOptions, null),
                        });
                    return map;
                }
            }

            if (value.Kind == GameplayDebugValueKind.Sequence && effectiveType.IsGenericType)
            {
                object collection = Activator.CreateInstance(effectiveType);
                Type elementType = effectiveType.GetGenericArguments()[0];
                MethodInfo add = effectiveType.GetMethods().FirstOrDefault(item =>
                    item.Name == "Add" && item.GetParameters().Length == 1);
                if (add != null)
                {
                    foreach (GameplayDebugValue item in value.Items)
                        add.Invoke(collection, new[] { ToObject(item, elementType, machine, resourceOptions, null) });
                    return collection;
                }
            }

            object result = Activator.CreateInstance(effectiveType);
            if (value.Kind == GameplayDebugValueKind.Object)
            {
                foreach (GameplayDebugNamedValue member in value.Members)
                {
                    FieldInfo field = effectiveType.GetField(member.Name, BindingFlags.Instance | BindingFlags.Public);
                    if (field != null)
                    {
                        field.SetValue(result, ToObject(member.Value, field.FieldType, machine));
                        continue;
                    }
                    PropertyInfo property = effectiveType.GetProperty(member.Name, BindingFlags.Instance | BindingFlags.Public);
                    if (property?.CanWrite == true)
                        property.SetValue(result, ToObject(member.Value, property.PropertyType, machine));
                }
            }
            return result;
        }

        private static GameplayDebugValue Scalar(GameplayDebugValueKind kind, string typeName, string scalar) =>
            new GameplayDebugValue { Kind = kind, TypeName = typeName, Scalar = scalar };

        private static bool IsInteger(Type type) =>
            type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);

        private static bool TryReadKeyValue(object item, out object key, out object value)
        {
            key = null;
            value = null;
            if (item == null)
                return false;
            Type type = item.GetType();
            FieldInfo keyField = type.GetField("Key");
            FieldInfo valueField = type.GetField("Value");
            if (keyField == null || valueField == null)
                return false;
            key = keyField.GetValue(item);
            value = valueField.GetValue(item);
            return true;
        }

        private sealed class ObjectReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ObjectReferenceComparer Instance = new ObjectReferenceComparer();
            public new bool Equals(object left, object right) { return ReferenceEquals(left, right); }
            public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); }
        }
    }
}
