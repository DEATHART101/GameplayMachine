using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Resources;
using ODCore;
using ODCore.Serialization;
using System.IO;
using System;
using System.Threading;
using XLifetimeObject;
using System.Runtime.InteropServices;
using CommonSerialize;
using PlayerController = GMCore.StateSync.PlayerController;
using LockstepPlayerController = GMCore.Lockstep.PlayerController;
using System.Linq;

namespace GMCore
{
    public enum GameplaySynchronizationMode
    {
        StateSync = 0,
        Lockstep = 1,
    }

    public enum GameplayLocalPlayerMode
    {
        Enabled = 0,
        Disabled = 1,
    }

    public enum GameplayTickMode
    {
        FixedRate = 0,
        None = 1,
    }

    public struct GameplayMachineSettings
    {
        public ODCore.Serialization.ODDatabase Database;
        public XLogger.Logger Logger;
        public List<IGMSerializer> Serializers;

        public IGameplayObjectManager GameplayObjectManager;
        public Func<GameplayMachine> GameplayMachineFactory;
        public GameplayInterestSettings Interest;
        public ODClassName? RootGameplayObjectClass;
        public IODScene InitialScene;
        public Action<GameplayMachine> AuthorityInitialized;
        public GameplaySynchronizationMode SynchronizationMode;
        public GameplayLocalPlayerMode LocalPlayerMode;
        public GameplayTickMode TickMode;
        public int TickIntervalMilliseconds;
        public bool LogInterfaceExecutionFailures;
    }

    public enum LogCatagories
    {
        GameplayMachine,
        Network,
        Lockstep,
        LockstepRelay,
        DebugServer,
        Runner,
    }

    [System.Serializable]
    public struct MachineKey
    {
        public int ID;

        public override string ToString()
        {
            return ID.ToString();
        }
    }

    [System.Serializable]
    public struct MachineObject
    {
        public GameplayMachine obj;
    }

    [System.Serializable]
    public struct ODFieldName : System.IEquatable<ODFieldName>, IStringSerializable
    {
        public object NameObject;

        public ODFieldMeta Meta
        {
            get
            {
                return GameplayMachineBase.GetODFieldMeta(this);
            }
        }

        public bool Equals(ODFieldName other)
        {
            return object.Equals(NameObject, other.NameObject);
        }

        public override bool Equals(object obj)
        {
            if (obj is ODFieldName other)
            {
                return this.Equals(other);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(NameObject);
        }

        public override string ToString()
        {
            return NameObject?.ToString();
        }

        public void ConvertFrom(string str)
        {
            throw new NotImplementedException();
        }

        public string ConvertTo()
        {
            return ToString();
        }
    }

    public enum ODFieldReplicationMode
    {
        None = 0,
        OwnerOnly = 1,
        AllClients = 2,
    }

    public enum GameplayObjectReplicationMode
    {
        None = 0,
        OwnerOnly = 1,
        AllClients = 2,
    }

    public enum GameplayObjectRelevancyMode
    {
        Always = 0,
        Spatial = 1,
    }

    public enum GameplayObjectPartitionMode
    {
        Manual = 0,
        Persistent = 1,
        Spatial = 2,
    }

    [System.Serializable]
    public struct ODFieldMeta
    {
        public ODClassName FromClass;
        
        public ODFieldName Name;

        public object DefaultObject;
        public Type FieldType;
        [NonSerialized] public Func<object> InitialValueFactory;
        public ODFieldReplicationMode ReplicationMode;
        public bool IsDriven;
        public List<ODFieldName> DrivenBys;
        public List<ODFieldName> DrivingOfs;
    }

    [System.Serializable]
    public struct ODClassName : IEquatable<ODClassName>
    {
        public object NameObject;

        public ODClassMeta Meta
        {
            get
            {
                return GameplayMachineBase.GetODClassMeta(this);
            }
        }

        public override string ToString()
        {
            return NameObject?.ToString();
        }

        public bool Equals(ODClassName other)
        {
            return object.Equals(NameObject, other.NameObject);
        }

        public override bool Equals(object obj)
        {
            if (obj is ODClassName other)
            {
                return this.Equals(other);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(NameObject);
        }
    }

    [System.Serializable]
    public struct ODClassMeta
    {
        public ODModuleName FromModule;

        public ODClassName Name;

        public System.Type ODType;
        public GameplayObjectReplicationMode ReplicationMode;
        public GameplayObjectRelevancyMode RelevancyMode;
        public GameplayObjectPartitionMode PartitionMode;
        public bool IsEngineManaged;
        public List<ODClassName> DirectBaseClasses;
        public List<ODFieldMeta> Fields;
    }

    public interface IODClass
    {
        public ODClassName GetODClassName();
    }

    [System.Serializable]
    public struct ODStructName : System.IEquatable<ODStructName>
    {
        public object NameObject;

        public ODStructMeta Meta
        {
            get
            {
                return GameplayMachineBase.GetODStructMeta(this);
            }
        }

        public override string ToString()
        {
            return NameObject?.ToString();
        }

        public bool Equals(ODStructName other)
        {
            return object.Equals(NameObject, other.NameObject);
        }

        public override bool Equals(object obj)
        {
            if (obj is ODStructName other)
            {
                return this.Equals(other);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(NameObject);
        }
    }

    [System.Serializable]
    public struct ODStructMeta
    {
        public ODModuleName FromModule;

        public ODStructName Name;

        public System.Type ODType;
        public ODStructName? BaseStruct;
        public Type BaseType;
    }

    public interface IODStruct
    {
        public ODStructName GetODStructName();
    }

    [System.Serializable]
    public struct ODEventName : IEquatable<ODEventName>
    {
        public object NameObject;

        public ODEventMeta Meta
        {
            get
            {
                return GameplayMachineBase.GetODEventMeta(this);
            }
        }

        public override string ToString()
        {
            return NameObject?.ToString();
        }

        public bool Equals(ODEventName other)
        {
            return object.Equals(NameObject, other.NameObject);
        }

        public override bool Equals(object obj)
        {
            if (obj is ODEventName other)
            {
                return this.Equals(other);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(NameObject);
        }
    }

    [System.Serializable]
    public struct ODEventMeta
    {
        public ODModuleName FromModule;

        public System.Type ODType;
        public ODEventName Name;
        public bool Multicast;
    }

    public interface IODEvent
    {
        public ODEventName GetODEventName();
    }

    public interface IMulticastEvent : IODEvent
    {
    }

    public interface IODClassResource
    {
        public ODClassName ODClassName { get; }
        public void Set(IGameplayObjectOperator gameplayObject);
    }

    /// <summary>
    /// A generated gameplay-object graph. Implementations create all objects before assigning fields
    /// so references inside the scene can point in either direction.
    /// </summary>
    public sealed class GameplaySceneInstance
    {
        private readonly Dictionary<Guid, CommonGameplayObject> m_objects = new Dictionary<Guid, CommonGameplayObject>();
        private GameplayMachine m_machine;
        public SceneInstanceID ID { get; internal set; }
        public string Key { get; internal set; }
        public PartitionID MainPartitionID { get; internal set; }
        public GObjectID RootObjectID { get; internal set; }
        public GameplaySceneInstance() { }
        internal GameplaySceneInstance(GameplayMachine machine, SceneInstanceID id, string key)
        { m_machine = machine; ID = id; Key = key; }

        public IReadOnlyDictionary<Guid, CommonGameplayObject> Objects { get { return m_objects; } }
        public CommonGameplayObject? Root => m_machine != null && m_machine.ContainsGameplayObject(RootObjectID)
            ? new CommonGameplayObject { Machine = m_machine, ObjectID = RootObjectID } : (CommonGameplayObject?)null;

        public void Register(Guid sceneObjectID, IGameplayObjectOperator gameplayObject)
        {
            if (sceneObjectID == Guid.Empty)
                throw new ArgumentException("Scene object ID cannot be empty", nameof(sceneObjectID));
            if (m_machine != null && !ReferenceEquals(m_machine, gameplayObject.Machine))
                throw new ArgumentException("Scene objects must belong to the same machine");
            m_machine = gameplayObject.Machine;
            m_objects.Add(sceneObjectID, new CommonGameplayObject
            {
                Machine = gameplayObject.Machine,
                ObjectID = gameplayObject.ObjectID,
            });
        }

        public T Get<T>(Guid sceneObjectID) where T : struct, IGameplayObjectOperator, IODClass
        {
            CommonGameplayObject gameplayObject;
            if (!m_objects.TryGetValue(sceneObjectID, out gameplayObject))
                throw new KeyNotFoundException("Scene object does not exist in this scene instance");
            T? value = gameplayObject.Cast<T>();
            if (!value.HasValue)
                throw new InvalidCastException("Scene object cannot be cast to " + typeof(T).FullName);
            return value.Value;
        }

        public void SetRoot(Guid sceneObjectID)
        {
            CommonGameplayObject gameplayObject;
            if (!m_objects.TryGetValue(sceneObjectID, out gameplayObject))
                throw new KeyNotFoundException("Scene root does not exist in this scene instance");
            m_machine.SetSceneRoot(this, gameplayObject.ObjectID);
        }
    }

    public interface IODScene
    {
        void Create(GameplayMachine.GameplayMachineProxy machine, GameplaySceneInstance instance);
    }

    [System.Serializable]
    public struct ODModuleName : System.IEquatable<ODModuleName>
    {
        public string Name;

        public override string ToString()
        {
            return Name;
        }

        public bool Equals(ODModuleName other)
        {
            return string.Equals(Name, other.Name, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            if (obj is ODModuleName other)
            {
                return this.Equals(other);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Name == null ? 0 : StringComparer.Ordinal.GetHashCode(Name);
        }
    }

    [System.Serializable]
    public struct ODModuleMeta
    {
        public ODModuleName Name;

        public Dictionary<ODFieldName, ODFieldMeta> Fields;
        public Dictionary<ODClassName, ODClassMeta> Classes;
        public Dictionary<ODStructName, ODStructMeta> Structs;
        public Dictionary<ODEventName, ODEventMeta> Events;
    }

    public interface IODModule
    {
        ODModuleName Name { get; }
        IEnumerable<ODClassMeta> GetAllODClassMetas();
        IEnumerable<ODStructMeta> GetAllODStructMetas();
        IEnumerable<ODEventMeta> GetAllODEventMetas();
        IEnumerable<ODClassSaveMeta> GetAllODClassSaveMetas();
        IEnumerable<ODFieldSaveMeta> GetAllODFieldSaveMetas();
    }

    /// <summary>
    /// Platform-neutral entry point generated by an OD project. Runtime hosts use this contract to
    /// create a machine without depending on a concrete game assembly or presentation framework.
    /// </summary>
    public interface IODGameplayRuntime
    {
        string Name { get; }
        GameplayMachineSettings CreateSettings();
        IReadOnlyList<IODModule> CreateModules();
        IReadOnlyDictionary<string, IODScene> Scenes { get; }
    }

    public enum ObjectEventTypes
    {
        Changed, // The object was changed
        Add,    // An item was added to this collection object
        Remove, // An item was removed from this collection object
        ItemChanged, // An existing map item was changed
    }

    public interface IFieldObject
    {
        public GameplayMachine Machine { get;  set; }
        public GObjectID ObjectID { get; set; }
        public ODFieldName FieldName { get; set; }

        public bool SetToField(IFieldObject other);

        public IGMCollection GetController(IGameplayObject gameplayObject, ODFieldName field);
    }

    public interface ICollectionFieldObject<T> : IFieldObject, IEnumerable<T>
    {
        public CollectionBinder<T> GetBinder()
        {
            return new CollectionBinder<T>()
            {
                Machine = Machine,
                ObjectID = ObjectID,
                FieldName = FieldName,
            };
        }
    }

    [System.Serializable]
    public struct KVPair<K, V>
    {
        public K Key;
        public V Value;

        public KVPair(K key, V value)
        {
            Key = key;
            Value = value;
        }
    }

    public interface ICollectionFieldObject<K, V> : IFieldObject, IEnumerable<KVPair<K, V>>
    {
        public CollectionBinder<K, V> GetBinder()
        {
            return new CollectionBinder<K, V>()
            {
                Machine = Machine,
                ObjectID = ObjectID,
                FieldName = FieldName,
            };
        }
    }

    public interface IGMCollection
    {
        public object Get(object index);
    }

    public interface IReplicatedCollection : IGMCollection
    {
        void ApplyReplicationAdd(object key, object value);
        void ApplyReplicationRemove(object key);
        void ApplyReplicationItemChanged(object key, object value);
    }

    public interface ICollectionMemoryObject
    {
        public void CopyTo(IGameplayObject gameplayObject, ODFieldName fieldIndex);
    }

    [System.Serializable]
    public class ODModuleCache
    {
        public Dictionary<ODFieldName, ODFieldMeta> DrivenFields = new Dictionary<ODFieldName, ODFieldMeta>();
    }

    [System.Serializable]
    public abstract class GameplayMachineBase
    {
        #region Machine Runtime Management

        private static Dictionary<MachineKey, MachineObject> s_machines = new Dictionary<MachineKey, MachineObject>();

        public static GameplayMachine GetGameplayMachine(MachineKey machineKey)
        {
            MachineObject result;
            if (s_machines.TryGetValue(machineKey, out result))
            {
                return result.obj;
            }
            return null;
        }

        public static bool RemoveGameplayMachine(MachineKey machineKey)
        {
            return s_machines.Remove(machineKey);
        }

        public static byte[] SaveGameplayMachine(GameplayMachine machine)
        {
            if (machine == null)
            {
                throw new ArgumentNullException(nameof(machine));
            }
            return GameplayMachineSaveFormat.Save(machine, machine.ObjectManager, machine.Serializers);
        }

        public static GameplayMachine LoadGameplayMachine(
            MachineKey key,
            GameplayMachineSettings settings,
            byte[] bytes,
            params IODModule[] modules)
        {
            IODModule[] runtimeModules = IncludeBuiltInModules(modules);
            RegisterODModules(runtimeModules);

            GameplayMachine result = CreateGameplayMachine(settings);
            IGameplayObjectManager manager = settings.GameplayObjectManager ?? new GameplayObjectManager(result);
            result.MachineKey = key;
            result.Init(manager, settings);
            result.ConfigureRuntime(GameplayMachineRole.Authority, runtimeModules);
            GameplayMachineSaveFormat.Load(result, manager, bytes, settings.Serializers);
            if (settings.LocalPlayerMode == GameplayLocalPlayerMode.Enabled)
                result.InitializeAuthorityPlayerController();

            MachineObject value;
            value.obj = result;
            s_machines[key] = value;

            return result;
        }

        public static GameplayMachine SpawnGameplayMachine(
            MachineKey? spawnKey,
            GameplayMachineSettings settings,
            params IODModule[] modules)
        {
            if (spawnKey != null)
            {
                if (s_machines.ContainsKey(spawnKey.Value))
                {
                    throw new System.Exception($"Spawnkey {spawnKey} already in use!");
                }
            }

            IODModule[] runtimeModules = IncludeBuiltInModules(modules);
            RegisterODModules(runtimeModules);

            GameplayMachine result = CreateGameplayMachine(settings);

            var manager = settings.GameplayObjectManager ?? new GameplayObjectManager(result);

            result.MachineKey = spawnKey == null ? new MachineKey() { ID = -1 } : spawnKey.Value;
            result.Init(manager, settings);
            result.ConfigureRuntime(GameplayMachineRole.Authority, runtimeModules);
            result.InitializeNewAuthority(settings);
            if (settings.LocalPlayerMode == GameplayLocalPlayerMode.Enabled)
                result.InitializeAuthorityPlayerController();

            if (spawnKey != null)
            {
                MachineObject value;
                value.obj = result;
                s_machines[spawnKey.Value] = value;
            }

            return result;
        }

        public static GameplayMachine LoadPartitionedGameplayMachine(
            MachineKey key, GameplayMachineSettings settings, IGameplayPartitionStore store,
            bool restoreLoadedPartitions = true, params IODModule[] modules)
        {
            IODModule[] runtimeModules = IncludeBuiltInModules(modules);
            RegisterODModules(runtimeModules);
            GameplayMachine result = CreateGameplayMachine(settings);
            var manager = settings.GameplayObjectManager ?? new GameplayObjectManager(result);
            result.MachineKey = key;
            result.Init(manager, settings);
            result.ConfigureRuntime(GameplayMachineRole.Authority, runtimeModules);
            result.RestorePartitionedWorld(store, restoreLoadedPartitions);
            if (settings.LocalPlayerMode == GameplayLocalPlayerMode.Enabled)
                result.InitializeAuthorityPlayerController();
            s_machines[key] = new MachineObject { obj = result };
            return result;
        }

        public static GameplayMachine CreateClientProxy(
            MachineKey? machineKey,
            GameplayMachineSettings settings,
            params IODModule[] modules)
        {
            if (machineKey != null && s_machines.ContainsKey(machineKey.Value))
            {
                throw new Exception($"Machine key {machineKey} is already in use");
            }

            IODModule[] runtimeModules = IncludeBuiltInModules(modules);
            RegisterODModules(runtimeModules);
            GameplayMachine result = CreateGameplayMachine(settings);
            IGameplayObjectManager manager = settings.GameplayObjectManager ?? new GameplayObjectManager(result);
            result.MachineKey = machineKey ?? new MachineKey { ID = -1 };
            result.Init(manager, settings);
            result.ConfigureRuntime(GameplayMachineRole.ClientProxy, runtimeModules);

            if (machineKey != null)
            {
                s_machines[machineKey.Value] = new MachineObject { obj = result };
            }
            return result;
        }

        private static GameplayMachine CreateGameplayMachine(GameplayMachineSettings settings)
        {
            GameplayMachine result = settings.GameplayMachineFactory == null
                ? new GameplayMachine()
                : settings.GameplayMachineFactory();
            if (result == null)
                throw new InvalidOperationException("GameplayMachineFactory returned null");
            return result;
        }

        private static IODModule[] IncludeBuiltInModules(IEnumerable<IODModule> modules)
        {
            return new[] { (IODModule)BuiltInGameplayModule.Instance }
                .Concat(modules?.Where(item => item != null) ?? Enumerable.Empty<IODModule>())
                .GroupBy(item => item.Name)
                .Select(group => group.First())
                .ToArray();
        }

        #endregion

        #region Machine Registration

        private static readonly Dictionary<ODModuleName, ODModuleMeta> s_moduleMetas = new Dictionary<ODModuleName, ODModuleMeta>();
        private static readonly Dictionary<ODModuleName, ODModuleCache> s_moduleCaches = new Dictionary<ODModuleName, ODModuleCache>();
        private static readonly Dictionary<ODClassName, ODClassMeta> s_classMetas = new Dictionary<ODClassName, ODClassMeta>();
        private static readonly Dictionary<ODFieldName, ODFieldMeta> s_fieldMetas = new Dictionary<ODFieldName, ODFieldMeta>();
        private static readonly Dictionary<ODStructName, ODStructMeta> s_structMetas = new Dictionary<ODStructName, ODStructMeta>();
        private static readonly Dictionary<ODEventName, ODEventMeta> s_eventMetas = new Dictionary<ODEventName, ODEventMeta>();
        private static readonly Dictionary<ODFieldName, ODFieldMeta> s_drivenFieldMetas = new Dictionary<ODFieldName, ODFieldMeta>();
        private static readonly Dictionary<ODClassName, ODClassSaveMeta> s_classSaveMetas = new Dictionary<ODClassName, ODClassSaveMeta>();
        private static readonly Dictionary<ulong, ODClassSaveMeta> s_classSaveMetasByID = new Dictionary<ulong, ODClassSaveMeta>();
        private static readonly Dictionary<ODFieldName, ODFieldSaveMeta> s_fieldSaveMetas = new Dictionary<ODFieldName, ODFieldSaveMeta>();
        private static readonly Dictionary<ulong, ODFieldSaveMeta> s_fieldSaveMetasByID = new Dictionary<ulong, ODFieldSaveMeta>();
        private static readonly object s_moduleRegistrationLock = new object();

        public static void RegisterODModules(IEnumerable<IODModule> modules)
        {
            if (modules == null)
            {
                return;
            }

            foreach (IODModule module in modules)
            {
                RegisterODModule(module);
            }
        }

        public static void RegisterODModule(IODModule module)
        {
            if (module == null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            lock (s_moduleRegistrationLock)
            {
                RegisterODModuleCore(module);
            }
        }

        private static void RegisterODModuleCore(IODModule module)
        {
            ODModuleName name = module.Name;
            if (string.IsNullOrWhiteSpace(name.Name))
            {
                throw new ArgumentException("OD module name cannot be empty", nameof(module));
            }
            if (s_moduleMetas.ContainsKey(name))
            {
                return;
            }

            ODModuleMeta meta;
            meta.Name = name;
            meta.Classes = new Dictionary<ODClassName, ODClassMeta>();
            meta.Fields = new Dictionary<ODFieldName, ODFieldMeta>();
            meta.Structs = new Dictionary<ODStructName, ODStructMeta>();
            meta.Events = new Dictionary<ODEventName, ODEventMeta>();

            IEnumerable<ODClassMeta> classMetas = module.GetAllODClassMetas()
                ?? throw new InvalidOperationException($"OD module {name} returned null class metadata");
            foreach (ODClassMeta classMeta in classMetas)
            {
                EnsureMetadataModule(name, classMeta.FromModule, "class", classMeta.Name);
                meta.Classes.Add(classMeta.Name, classMeta);
                foreach (ODFieldMeta fieldMeta in classMeta.Fields
                    ?? throw new InvalidOperationException($"OD class {classMeta.Name} returned null field metadata"))
                {
                    meta.Fields.Add(fieldMeta.Name, fieldMeta);
                }
            }

            IEnumerable<ODStructMeta> structMetas = module.GetAllODStructMetas()
                ?? throw new InvalidOperationException($"OD module {name} returned null struct metadata");
            foreach (ODStructMeta structMeta in structMetas)
            {
                EnsureMetadataModule(name, structMeta.FromModule, "struct", structMeta.Name);
                meta.Structs.Add(structMeta.Name, structMeta);
            }

            IEnumerable<ODEventMeta> eventMetas = module.GetAllODEventMetas()
                ?? throw new InvalidOperationException($"OD module {name} returned null event metadata");
            foreach (ODEventMeta eventMeta in eventMetas)
            {
                EnsureMetadataModule(name, eventMeta.FromModule, "event", eventMeta.Name);
                meta.Events.Add(eventMeta.Name, eventMeta);
            }

            Dictionary<ODClassName, ODClassSaveMeta> classSaveMetas = new Dictionary<ODClassName, ODClassSaveMeta>();
            Dictionary<ulong, ODClassSaveMeta> classSaveMetasByID = new Dictionary<ulong, ODClassSaveMeta>();
            IEnumerable<ODClassSaveMeta> classSaveMetaItems = module.GetAllODClassSaveMetas()
                ?? throw new InvalidOperationException($"OD module {name} returned null class save metadata");
            foreach (ODClassSaveMeta saveMeta in classSaveMetaItems)
            {
                ValidateStableSaveID(saveMeta.StableID, saveMeta.StableName, "class");
                if (!meta.Classes.ContainsKey(saveMeta.ClassName))
                {
                    throw new InvalidOperationException($"OD save class {saveMeta.StableName} is not declared by module {name}");
                }
                classSaveMetas.Add(saveMeta.ClassName, saveMeta);
                classSaveMetasByID.Add(saveMeta.StableID, saveMeta);
            }

            Dictionary<ODFieldName, ODFieldSaveMeta> fieldSaveMetas = new Dictionary<ODFieldName, ODFieldSaveMeta>();
            Dictionary<ulong, ODFieldSaveMeta> fieldSaveMetasByID = new Dictionary<ulong, ODFieldSaveMeta>();
            IEnumerable<ODFieldSaveMeta> fieldSaveMetaItems = module.GetAllODFieldSaveMetas()
                ?? throw new InvalidOperationException($"OD module {name} returned null field save metadata");
            foreach (ODFieldSaveMeta saveMeta in fieldSaveMetaItems)
            {
                ValidateStableSaveID(saveMeta.StableID, saveMeta.StableName, "field");
                if (!meta.Fields.ContainsKey(saveMeta.FieldName))
                {
                    throw new InvalidOperationException($"OD save field {saveMeta.StableName} is not declared by module {name}");
                }
                if (saveMeta.Writer == null || saveMeta.Reader == null)
                {
                    throw new InvalidOperationException($"OD save field {saveMeta.StableName} has an incomplete codec");
                }
                if (saveMeta.DeltaKind != ODCollectionDeltaKind.None)
                {
                    if (saveMeta.DeltaKeyWriter == null || saveMeta.DeltaKeyReader == null ||
                        saveMeta.DeltaKind == ODCollectionDeltaKind.Map &&
                        (saveMeta.DeltaValueWriter == null || saveMeta.DeltaValueReader == null))
                        throw new InvalidOperationException($"OD save field {saveMeta.StableName} has incomplete delta codecs");
                }
                fieldSaveMetas.Add(saveMeta.FieldName, saveMeta);
                fieldSaveMetasByID.Add(saveMeta.StableID, saveMeta);
            }

            int storedFieldCount = meta.Fields.Values.Count(field => !field.IsDriven);
            if (classSaveMetas.Count != meta.Classes.Count || fieldSaveMetas.Count != storedFieldCount)
            {
                throw new InvalidOperationException($"OD module {name} does not provide save metadata for every class and field");
            }

            ODModuleCache moduleCache = new ODModuleCache();
            foreach (KeyValuePair<ODFieldName, ODFieldMeta> field in meta.Fields)
            {
                if (field.Value.DrivingOfs != null && field.Value.DrivingOfs.Count > 0)
                {
                    moduleCache.DrivenFields.Add(field.Key, field.Value);
                }
            }

            EnsureUnique(s_classMetas, meta.Classes.Keys, "class");
            EnsureUnique(s_fieldMetas, meta.Fields.Keys, "field");
            EnsureUnique(s_structMetas, meta.Structs.Keys, "struct");
            EnsureUnique(s_eventMetas, meta.Events.Keys, "event");
            EnsureUnique(s_drivenFieldMetas, moduleCache.DrivenFields.Keys, "driven field");
            EnsureUnique(s_classSaveMetas, classSaveMetas.Keys, "save class");
            EnsureUnique(s_classSaveMetasByID, classSaveMetasByID.Keys, "save class ID");
            EnsureUnique(s_fieldSaveMetas, fieldSaveMetas.Keys, "save field");
            EnsureUnique(s_fieldSaveMetasByID, fieldSaveMetasByID.Keys, "save field ID");

            AddRange(s_classMetas, meta.Classes);
            AddRange(s_fieldMetas, meta.Fields);
            AddRange(s_structMetas, meta.Structs);
            AddRange(s_eventMetas, meta.Events);
            AddRange(s_drivenFieldMetas, moduleCache.DrivenFields);
            AddRange(s_classSaveMetas, classSaveMetas);
            AddRange(s_classSaveMetasByID, classSaveMetasByID);
            AddRange(s_fieldSaveMetas, fieldSaveMetas);
            AddRange(s_fieldSaveMetasByID, fieldSaveMetasByID);
            s_moduleMetas.Add(name, meta);
            s_moduleCaches.Add(name, moduleCache);
        }

        private static void ValidateStableSaveID(ulong stableID, string stableName, string kind)
        {
            if (string.IsNullOrWhiteSpace(stableName))
            {
                throw new InvalidOperationException($"OD save {kind} name cannot be empty");
            }
            if (ODSaveHash.Compute(stableName) != stableID)
            {
                throw new InvalidOperationException($"OD save {kind} {stableName} has an invalid stable ID");
            }
        }

        private static void EnsureMetadataModule<TName>(ODModuleName expected, ODModuleName actual, string kind, TName name)
        {
            if (!expected.Equals(actual))
            {
                throw new InvalidOperationException(
                    $"OD {kind} {name} belongs to module {actual}, but was returned by module {expected}");
            }
        }

        private static void EnsureUnique<TKey, TValue>(Dictionary<TKey, TValue> target, IEnumerable<TKey> keys, string kind)
        {
            foreach (TKey key in keys)
            {
                if (target.ContainsKey(key))
                {
                    throw new InvalidOperationException($"OD {kind} {key} is already registered");
                }
            }
        }

        private static void AddRange<TKey, TValue>(Dictionary<TKey, TValue> target, Dictionary<TKey, TValue> values)
        {
            foreach (KeyValuePair<TKey, TValue> value in values)
            {
                target.Add(value.Key, value.Value);
            }
        }

        public static IEnumerable<ODClassMeta> GetAllClasses(ODModuleName? specificModule = null)
        {
            if (specificModule == null)
            {
                return s_classMetas.Values;
            }

            ODModuleMeta module;
            if (s_moduleMetas.TryGetValue(specificModule.Value, out module))
            {
                return module.Classes.Values;
            }
            return Array.Empty<ODClassMeta>();
        }

        public static IEnumerable<ODEventMeta> GetAllEvents(ODModuleName? specificModule = null)
        {
            if (specificModule == null)
            {
                return s_eventMetas.Values;
            }

            ODModuleMeta module;
            if (s_moduleMetas.TryGetValue(specificModule.Value, out module))
            {
                return module.Events.Values;
            }
            return Array.Empty<ODEventMeta>();
        }

        public static ODClassMeta GetODClassMeta(ODClassName name)
        {
            ODClassMeta result;
            if (s_classMetas.TryGetValue(name, out result))
            {
                return result;
            }

            throw new Exception($"Class {name} is unregistered");
        }

        public static ODFieldMeta GetODFieldMeta(ODFieldName name)
        {
            ODFieldMeta result;
            if (s_fieldMetas.TryGetValue(name, out result))
            {
                return result;
            }

            throw new Exception($"Field {name} is unregistered");
        }

        public static ODStructMeta GetODStructMeta(ODStructName name)
        {
            ODStructMeta result;
            if (s_structMetas.TryGetValue(name, out result))
            {
                return result;
            }

            throw new Exception($"Class {name} is unregistered");
        }

        public static ODEventMeta GetODEventMeta(ODEventName name)
        {
            ODEventMeta result;
            if (s_eventMetas.TryGetValue(name, out result))
            {
                return result;
            }

            throw new Exception($"Class {name} is unregistered");
        }

        public static ODClassSaveMeta GetODClassSaveMeta(ODClassName name)
        {
            ODClassSaveMeta result;
            if (s_classSaveMetas.TryGetValue(name, out result))
            {
                return result;
            }
            throw new InvalidOperationException($"No save metadata is registered for class {name}");
        }

        public static ODClassSaveMeta GetODClassSaveMeta(ulong stableID)
        {
            ODClassSaveMeta result;
            if (s_classSaveMetasByID.TryGetValue(stableID, out result))
            {
                return result;
            }
            throw new InvalidDataException($"Gameplay save contains unknown class ID {stableID}");
        }

        public static ODFieldSaveMeta GetODFieldSaveMeta(ODFieldName name)
        {
            ODFieldSaveMeta result;
            if (s_fieldSaveMetas.TryGetValue(name, out result))
            {
                return result;
            }
            throw new InvalidOperationException($"No save metadata is registered for field {name}");
        }

        public static bool TryGetODFieldSaveMeta(ulong stableID, out ODFieldSaveMeta result)
        {
            return s_fieldSaveMetasByID.TryGetValue(stableID, out result);
        }

        public static bool TryGetDrivingOfMeta(ODFieldName fieldName, out ODFieldMeta result)
        {
            return s_drivenFieldMetas.TryGetValue(fieldName, out result);
        }

        #endregion

        #region Helper

        public static bool CanCastTo(ODClassName odClass, ODClassName targetClass)
        {
            if (odClass.Equals(targetClass))
            {
                return true;
            }

            ODClassMeta meta = odClass.Meta;
            foreach (var item in meta.DirectBaseClasses)
            {
                if (CanCastTo(item, targetClass))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }

    #region Events

    public class AfterGameplayObjectCreatedParam
    {
        public GObjectID ObjectID;
    }

    public class AfterGameplayObjectDeletedParam
    {
        public GObjectID ObjectID;
    }

    #endregion

    #region Temp

    public interface ICheckableInterface<TOut>
        where TOut : struct
    {
        public ODCore.EventError CanExecute(GameplayMachine.GameplayMachineProxy machine);
        public void DoExecute(GameplayMachine.GameplayMachineProxy machine, ref TOut outResult);
    }

    public enum GameplayRpcMode
    {
        None,
        Authority,
        Multicast,
        Predictable,
        Lockstep,
    }

    public enum GameplayInterfaceType
    {
        Gameplay,
        PlayerInput,
    }

    public enum GameplayPredictionMode
    {
        None,
        OnlySuccess,
        OnlyFailure,
        Both,
    }

    public interface IGameplayInterface
    {
    }

    public interface IPlayerInputInterface
    {
    }

    /// <summary>
    /// A mutable, resource-configured call object. Generated implementations copy a template and
    /// materialize the existing value-type interface parameter only when invoked by a proxy.
    /// </summary>
    public interface IODInterfaceCall
    {
        ulong InterfaceID { get; }
        GameplayInterfaceType InterfaceType { get; }
        IODInterfaceCall CopyUntyped();
        ODCore.EventError CanExecute(GameplayMachine.GameplayMachineProxy machine);
        CheckableInterfaceResult Invoke(GameplayMachine.GameplayMachineProxy machine);
    }

    public interface IReplicatedInterface
    {
        GameplayRpcMode RpcMode { get; }
        ODCore.EventError CanExecute(GameplayMachine.GameplayMachineProxy machine);
        void DoExecute(GameplayMachine.GameplayMachineProxy machine);
    }

    public interface IReplicatedInterface<TOut> : IReplicatedInterface
        where TOut : struct
    {
        void DoExecute(GameplayMachine.GameplayMachineProxy machine, ref TOut outResult);
    }

    public interface IPredictableInterface : IReplicatedInterface
    {
        GameplayPredictionMode PredictionMode { get; }
        void OnPredictClient(GameplayMachine.GameplayMachineProxy machine);
        void OnSuccessClient(GameplayMachine.GameplayMachineProxy machine);
        void OnFailClient(GameplayMachine.GameplayMachineProxy machine);
    }

    public struct CheckableInterfaceResult
    {
        public ODCore.EventError ErrorMessage;

        public bool Sussceeded
        {
            get
            {
                return ErrorMessage;
            }
        }
    }

    public struct CheckableInterfaceResult<T>
            where T : struct
    {
        private T m_result;
        private bool m_resultAccessDenied;
        public CheckableInterfaceResult CheckableResult;

        public T Result
        {
            get
            {
                if (m_resultAccessDenied)
                    throw new InvalidOperationException(
                        "Authority RPC output is not available on a ClientProxy; observe replicated state instead");
                return m_result;
            }
            set
            {
                if (m_resultAccessDenied)
                    throw new InvalidOperationException(
                        "Authority RPC output is not available on a ClientProxy; observe replicated state instead");
                m_result = value;
            }
        }

        internal void DenyResultAccess()
        {
            m_resultAccessDenied = true;
        }

        public ODCore.EventError ErrorMessage
        {
            get
            {
                return CheckableResult.ErrorMessage;
            }

            set
            {
                CheckableResult.ErrorMessage = value;
            }
        }

        public bool Sussceeded
        {
            get
            {
                return CheckableResult.Sussceeded;
            }
        }
    }

    public struct CheckableEventResult<T>
        where T : struct, IGameplayObjectOperator
    {
        public ODCore.EventError ErrorMessage;
        public T Result;

        public bool Sussceeded
        {
            get
            {
                return ErrorMessage;
            }
        }
    }

    public interface ICheckableEvent<T>
        where T : struct, IGameplayObjectOperator
    {
        public ODCore.EventError CanExecute(GameplayMachine machine);
        public void DoExecute(GameplayMachine machine, T outResult);
    }

    public interface ICheckableRoutine
    {
        public ODCore.EventError CanExecute(GameplayMachine.GameplayMachineProxy machine);
        public IEnumerator DoExecute(GameplayMachine.GameplayMachineProxy machine);
    }

    #endregion

    #region Exceptions

    public class NotExistException : System.Exception
    {
        public override string Message
        {
            get
            {
                return $"GameplayObject's id dosen't exist in this Machine (Maybe already deleted)";
            }
        }
    }

    public class ProxyUsedOutsideExecutionException : System.Exception
    {
        public override string Message
        {
            get
            {
                return $"Trying to use Proxy outside of an execution";
            }
        }
    }

    public class DataModifiedOutsideExecutionException : System.Exception
    {
        public override string Message
        {
            get
            {
                return $"You can only modify any data by executing interfaces";
            }
        }
    }

    #endregion



    [System.Serializable]
    public partial class GameplayMachine : GameplayMachineBase
    {
        #region Define

        #region Proxy

        [System.Serializable]
        public class GameplayMachineProxy
        {
            private GameplayMachine m_machine;

            protected GameplayMachine Machine
            {
                get
                {
                    return m_machine;
                }
            }

            public GameplayMachineProxy(GameplayMachine machine)
            {
                m_machine = machine;
            }

            #region GameplayObject

            public T CreateGameplayObject<T>(IEnumerable<IODClassResource> resources)
                where T : struct, IGameplayObjectOperator, IODClass
            {
                return m_machine.CreateGameplayObject<T>(resources);
            }

            public CommonGameplayObject CreateGameplayObject(ODClassName nameOfObject, IEnumerable<IODClassResource> resources)
            {
                return m_machine.CreateGameplayObject(nameOfObject, resources);
            }

            public T CreateGameplayObject<T>()
                where T : struct, IGameplayObjectOperator, IODClass
            {
                return m_machine.CreateGameplayObject<T>();
            }

            public CommonGameplayObject CreateGameplayObject(ODClassName nameOfObject)
            {
                return m_machine.CreateGameplayObject(nameOfObject);
            }

            public GameplaySceneInstance CreateScene(IODScene scene)
            {
                return m_machine.CreateScene(scene);
            }

            public PartitionID PersistentPartitionID => m_machine.PersistentPartitionID;
            public IEnumerable<GameplayPartition> Partitions => m_machine.Partitions;
            public IEnumerable<GameplaySceneInstance> Scenes => m_machine.Scenes;
            public IEnumerable<CommonGameplayObject> GetAllGameplayObjects() => m_machine.GetAllGameplayObjects();
            public void SetPartitionInterest(PlayerController player, IEnumerable<PartitionID> partitions) => m_machine.SetPartitionInterest(player, partitions);
            public bool IsPartitionInInterest(PlayerController player, PartitionID partition) =>
                m_machine.IsPartitionInInterest(player, partition);
            public bool IsPartitionInLocalInterest(PartitionID partition) =>
                m_machine.IsPartitionInLocalInterest(partition);
            public PartitionID CreatePartition(PartitionKind kind, string key, SceneInstanceID scene = default,
                GameplayPartitionCoordinate? coordinate = null) => m_machine.CreatePartition(kind, key, scene, coordinate);
            public bool TryGetPartitionAtCoordinate(GameplayPartitionCoordinate coordinate, out PartitionID partition) =>
                m_machine.TryGetPartitionAtCoordinate(coordinate, out partition);
            public IDisposable UsePartition(PartitionID id) => m_machine.UsePartition(id);
            public void MoveGameplayObject(GObjectID id, PartitionID destination) => m_machine.MoveGameplayObject(id, destination);
            public void SetPartitionOwner(GObjectID child, GObjectID owner) => m_machine.SetPartitionOwner(child, owner);
            public void LoadPartition(PartitionID id) => m_machine.LoadPartition(id);
            public void UnloadPartition(PartitionID id) => m_machine.UnloadPartition(id);
            public void DeletePartition(PartitionID id) => m_machine.DeletePartition(id);
            public void DestroyScene(SceneInstanceID id) => m_machine.DestroyScene(id);

            public void SetRoot(IGameplayObjectOperator gameplayObject)
            {
                m_machine.SetRoot(gameplayObject);
            }

            public CommonGameplayObject GetRoot()
            {
                return m_machine.GetRoot();
            }

            public bool HasRoot
            {
                get { return m_machine.HasRoot; }
            }

            public GameplayRpcContext? CurrentRpcContext
            {
                get { return m_machine.CurrentRpcContext; }
            }

            public PlayerController? LocalPlayerController
            {
                get { return m_machine.LocalPlayerController; }
            }

            public IEnumerable<PlayerController> GetPlayerControllers()
            {
                return m_machine.GetPlayerControllers();
            }

            public PlayerController GetLocalPlayerController()
            {
                return m_machine.GetLocalPlayerController();
            }

            public bool TryGetExecutingPlayerController(out PlayerController playerController)
            {
                return m_machine.TryGetExecutingPlayerController(out playerController);
            }

            public PlayerController GetExecutingPlayerController()
            {
                if (TryGetExecutingPlayerController(out PlayerController playerController))
                    return playerController;
                throw new InvalidOperationException(
                    "PlayerInput interface requires a PlayerController execution context");
            }

            public LockstepPlayerController? LocalLockstepPlayerController
            {
                get { return m_machine.LocalLockstepPlayerController; }
            }

            public IEnumerable<LockstepPlayerController> GetLockstepPlayerControllers()
            {
                return m_machine.GetLockstepPlayerControllers();
            }

            public LockstepPlayerController GetLocalLockstepPlayerController()
            {
                return m_machine.GetLocalLockstepPlayerController();
            }

            public LockstepPlayerController GetPlayerControllerBySlot(int slotID)
            {
                return m_machine.GetPlayerControllerBySlot(slotID);
            }

            public bool TryGetExecutingLockstepPlayerController(
                out LockstepPlayerController playerController)
            {
                return m_machine.TryGetExecutingLockstepPlayerController(out playerController);
            }

            public LockstepPlayerController GetExecutingLockstepPlayerController()
            {
                if (TryGetExecutingLockstepPlayerController(out LockstepPlayerController playerController))
                    return playerController;
                throw new InvalidOperationException(
                    "PlayerInput interface requires a LockstepPlayerController execution context");
            }

            public PlayerController? GetNetworkOwner(IGameplayObjectOperator gameplayObject)
            {
                return m_machine.GetNetworkOwner(gameplayObject);
            }

            public GObjectID GetNetworkOwnerID(IGameplayObjectOperator gameplayObject)
            {
                return m_machine.GetNetworkOwnerID(gameplayObject);
            }

            public void SetNetworkOwner(
                IGameplayObjectOperator gameplayObject,
                IGameplayObjectOperator playerController)
            {
                m_machine.SetNetworkOwner(gameplayObject, playerController);
            }

            public void ClearNetworkOwner(IGameplayObjectOperator gameplayObject)
            {
                m_machine.ClearNetworkOwner(gameplayObject);
            }

            public GameplayObjectReplicationMode GetGameplayObjectReplicationMode(
                IGameplayObjectOperator gameplayObject)
            {
                return m_machine.GetGameplayObjectReplicationMode(gameplayObject);
            }

            public TValue GetGameplayObjectValue<TValue, TObject>(TObject obj, ODFieldName index)
                where TObject : struct, IGameplayObjectOperator
            {
                return m_machine.GetGameplayObjectValue<TValue, TObject>(obj, index);
            }

            public TValue GetGameplayObjectValue<TValue>(GObjectID id, ODFieldName index)
            {
                return m_machine.GetGameplayObjectValue<TValue>(id, index);
            }

            public object GetGameplayObjectValue(GObjectID id, ODFieldName index)
            {
                return m_machine.GetGameplayObjectValue(id, index);
            }

            public bool DeleteGameplayObject<T>(T gameplayObject)
                where T : struct, IGameplayObjectOperator
            {
                return m_machine.DeleteGameplayObject<T>(gameplayObject);
            }

            public bool DeleteGameplayObject(GObjectID id)
            {
                return m_machine.DeleteGameplayObject(id);
            }

            public void SetGameplayObjectValue(GObjectID id, ODFieldName index, object val)
            {
                m_machine.SetGameplayObjectValue(id, index, val);
            }

            public ODClassName GetGameplayObjectClass(GObjectID id)
            {
                return m_machine.GetGameplayObjectClass(id);
            }

            public T GetGameplayObject<T>(GObjectID id)
                where T : struct, IGameplayObjectOperator, IODClass
            {
                return m_machine.GetGameplayObject<T>(id);
            }

            #endregion

            #region Execution

            public EventError CanExecute<TOut>(ICheckableInterface<TOut> param)
                where TOut : struct
            {
                return m_machine.CanExecute(param);
            }

            public EventError CanExecute(IODInterfaceCall param)
            {
                if (!m_machine.DuringExecution)
                    throw new ProxyUsedOutsideExecutionException();
                if (param == null)
                    return "Interface call cannot be null";
                if (param.InterfaceType != GameplayInterfaceType.Gameplay)
                    return "GameplayMachineProxy can only validate ordinary Gameplay interface calls";
                return param.CanExecute(this);
            }

            public CheckableInterfaceResult<TOut> Execute<TOut>(ICheckableInterface<TOut> param)
                where TOut : struct
            {
                if (!m_machine.DuringExecution)
                {
                    throw new ProxyUsedOutsideExecutionException();
                }

                EventError routeError = m_machine.CanRouteGameplayInterface(param);
                if (!routeError)
                    return new CheckableInterfaceResult<TOut> { ErrorMessage = routeError };
                return m_machine.TraceExecution(param, () => m_machine.Internal_Execute(param));
            }

            public CheckableInterfaceResult Execute(IODInterfaceCall param)
            {
                if (!m_machine.DuringExecution)
                    throw new ProxyUsedOutsideExecutionException();
                if (param == null)
                    return new CheckableInterfaceResult { ErrorMessage = "Interface call cannot be null" };
                if (param.InterfaceType != GameplayInterfaceType.Gameplay)
                    return new CheckableInterfaceResult
                    {
                        ErrorMessage = "GameplayMachineProxy can only execute ordinary Gameplay interface calls",
                    };
                return param.Invoke(this);
            }

            public CheckableInterfaceResult Execute(IReplicatedInterface param)
            {
                if (!m_machine.DuringExecution)
                {
                    throw new ProxyUsedOutsideExecutionException();
                }

                if (m_machine.SynchronizationMode == GameplaySynchronizationMode.Lockstep &&
                    param is IPlayerInputInterface)
                {
                    return new CheckableInterfaceResult
                    {
                        ErrorMessage = "GameplayMachineProxy cannot execute PlayerInput interfaces in Lockstep mode",
                    };
                }

                m_machine.ThrowIfClientMulticastExecution(param);
                CheckableInterfaceResult result = default;
                EventError routeError = m_machine.CanRouteReplicatedInterface(param);
                if (!routeError)
                {
                    result.ErrorMessage = routeError;
                    return result;
                }
                if (m_machine.Role == GameplayMachineRole.ClientProxy)
                {
                    if (param.RpcMode == GameplayRpcMode.Predictable)
                        return m_machine.TraceExecution(param, () => m_machine.Internal_PredictClient((IPredictableInterface)param));
                    return m_machine.TraceExecution(param, () =>
                    {
                        CheckableInterfaceResult routed = default;
                        routed.ErrorMessage = m_machine.SendAuthorityInterface(param);
                        return routed;
                    });
                }
                return m_machine.TraceExecution(param, () => m_machine.Internal_ExecuteReplicated(param, broadcastMulticast: true));
            }

            public CheckableInterfaceResult<TOut> Execute<TOut>(IReplicatedInterface<TOut> param)
                where TOut : struct
            {
                if (!m_machine.DuringExecution)
                    throw new ProxyUsedOutsideExecutionException();

                m_machine.ThrowIfClientMulticastExecution(param);
                CheckableInterfaceResult<TOut> result = default;
                if (m_machine.Role == GameplayMachineRole.ClientProxy)
                    result.DenyResultAccess();
                EventError routeError = m_machine.CanRouteOutputReplicatedInterface(param);
                if (!routeError)
                {
                    result.ErrorMessage = routeError;
                    return result;
                }
                if (m_machine.Role == GameplayMachineRole.ClientProxy)
                {
                    result.ErrorMessage = m_machine.SendAuthorityInterface(param);
                    return m_machine.TraceExecution(param, () => result);
                }
                return m_machine.TraceExecution(param,
                    () => m_machine.Internal_ExecuteReplicated(param, broadcastMulticast: true));
            }

            public CheckableInterfaceResult Execute(ICheckableRoutine param)
            {
                if (!m_machine.DuringExecution)
                {
                    throw new ProxyUsedOutsideExecutionException();
                }

                EventError routeError = m_machine.CanRouteGameplayInterface(param);
                if (!routeError)
                    return new CheckableInterfaceResult { ErrorMessage = routeError };
                return m_machine.TraceExecution(param, () => m_machine.Internal_Execute(param));
            }

            public IEnumerator RoutineExecute(ICheckableRoutine param)
            {
                if (!m_machine.DuringExecution)
                {
                    throw new ProxyUsedOutsideExecutionException();
                }

                EventError routeError = m_machine.CanRouteGameplayInterface(param);
                if (!routeError)
                    throw new InvalidOperationException(routeError.ToString());
                return m_machine.TraceRoutineExecution(param, () => m_machine.Internal_RoutineExecute(param));
            }

            public IEnumerator RoutineExecute<TOut>(ICheckableInterface<TOut> param)
                where TOut : struct
            {
                if (!m_machine.DuringExecution)
                {
                    throw new ProxyUsedOutsideExecutionException();
                }

                EventError routeError = m_machine.CanRouteGameplayInterface(param);
                if (!routeError)
                    throw new InvalidOperationException(routeError.ToString());
                return m_machine.TraceRoutineExecution(param, () => m_machine.Internal_RoutineExecute(param));
            }

            #endregion

            #region Event

            public void BroadCastMachineEvent(object evtDelegateContext)
            {
                m_machine.BroadCastMachineEvent(evtDelegateContext);
            }

            #endregion
        }

        #endregion

        public class DelayEvents
        {
            #region Define

            private class DelayObjectEvents
            {
                public GObjectID ObjectID;

                // Storing their first old values
                public Dictionary<ODFieldName, object> FieldValues;
                public HashSet<ODFieldName> FieldValuesWithoutEqual;

                // Dictionary<ODFieldName, Dictionary<ObjectValue, AddRemoveCounter>>
                public Dictionary<ODFieldName, Dictionary<object, int>> CollectionValues;

                // Dictionary<ODFieldName, Dictionary<Key, Object first value>>
                public Dictionary<ODFieldName, Dictionary<object, object>> CollectionChangedValues;
            }

            #endregion

            private GameplayMachine m_machine;

            private Dictionary<GObjectID, DelayObjectEvents> m_delayedObjectEvents = new Dictionary<GObjectID, DelayObjectEvents>();
            private List<object> m_delayedMachineEvents = new List<object>();
            private HashSet<GObjectID> m_createdObjects = new HashSet<GObjectID>();
            private HashSet<GObjectID> m_deletedObjects = new HashSet<GObjectID>();

            public DelayEvents(GameplayMachine machine)
            {
                m_machine = machine;
            }

            public void StoreObjectEvent(GObjectID objectID, ODFieldName bindKey, ObjectEventTypes objectEventType, object context, object oldValue = null, bool disableEqualTest = false)
            {
                int countDelta = 0;
                DelayObjectEvents outEvents;
                switch (objectEventType)
                {
                    case ObjectEventTypes.Changed:
                        {
                            if (!m_delayedObjectEvents.TryGetValue(objectID, out outEvents))
                            {
                                outEvents = new DelayObjectEvents()
                                {
                                    ObjectID = objectID,
                                    FieldValues = new Dictionary<ODFieldName, object>(),
                                };
                                m_delayedObjectEvents.Add(objectID, outEvents);
                            }
                            if (disableEqualTest)
                            {
                                outEvents.FieldValuesWithoutEqual = outEvents.FieldValuesWithoutEqual ?? new HashSet<ODFieldName>();
                                outEvents.FieldValuesWithoutEqual.Add(bindKey);
                            }
                            else
                            {
                                outEvents.FieldValues = outEvents.FieldValues ?? new Dictionary<ODFieldName, object>();
                                if (!outEvents.FieldValues.ContainsKey(bindKey))
                                {
                                    outEvents.FieldValues.Add(bindKey, oldValue);
                                }
                            }
                        }
                        return; // This is return, not break!
                    case ObjectEventTypes.Add:
                        countDelta = 1;
                        break;
                    case ObjectEventTypes.Remove:
                        countDelta = -1;
                        break;
                    case ObjectEventTypes.ItemChanged:
                        {
                            if (!m_delayedObjectEvents.TryGetValue(objectID, out outEvents))
                            {
                                outEvents = new DelayObjectEvents()
                                {
                                    ObjectID = objectID,
                                    CollectionChangedValues = new Dictionary<ODFieldName, Dictionary<object, object>>(),
                                };
                                m_delayedObjectEvents.Add(objectID, outEvents);
                            }
                            outEvents.CollectionChangedValues = outEvents.CollectionChangedValues ?? new Dictionary<ODFieldName, Dictionary<object, object>>();

                            Dictionary<object, object> valueDict;
                            if (!outEvents.CollectionChangedValues.TryGetValue(bindKey, out valueDict))
                            {
                                valueDict = new Dictionary<object, object>();
                                outEvents.CollectionChangedValues.Add(bindKey, valueDict);
                            }

                            if (!valueDict.ContainsKey(context))
                            {
                                if (oldValue == null)
                                {
                                    throw new System.Exception($"ItemChanged should only be called when value is not set to null(ID: {objectID}, Field: {bindKey}, FieldKey: {context})");
                                }
                                valueDict.Add(context, oldValue);
                            }
                        }
                        return; // This is return, not break!
                    default:
                        break;
                }

                // Only add & remove case will go here
                if (!m_delayedObjectEvents.TryGetValue(objectID, out outEvents))
                {
                    outEvents = new DelayObjectEvents()
                    {
                        ObjectID = objectID,
                        CollectionValues = new Dictionary<ODFieldName, Dictionary<object, int>>(),
                    };
                    m_delayedObjectEvents.Add(objectID, outEvents);
                }
                outEvents.CollectionValues = outEvents.CollectionValues ?? new Dictionary<ODFieldName, Dictionary<object, int>>();

                Dictionary<object, int> valueCounterDict;
                if (!outEvents.CollectionValues.TryGetValue(bindKey, out valueCounterDict))
                {
                    valueCounterDict = new Dictionary<object, int>();
                    outEvents.CollectionValues.Add(bindKey, valueCounterDict);
                }

                int counter;
                if (!valueCounterDict.TryGetValue(context, out counter))
                {
                    valueCounterDict.Add(context, countDelta);
                }
                else
                {
                    valueCounterDict[context] = counter + countDelta;
                }
            }

            public void StoreMachineEvent<T>(T evtDelegateContext)
            {
                m_delayedMachineEvents.Add(evtDelegateContext);
            }

            public void StoreObjectLifeEvent(GObjectID id, bool create)
            {
                HashSet<GObjectID> set = create ? m_createdObjects : m_deletedObjects;
                set.Add(id);
            }

            public void FlushAllEvents()
            {
                foreach (var item in m_delayedObjectEvents)
                {
                    if (m_deletedObjects.Contains(item.Key))
                    {
                        continue;
                    }

                    var delayedDrivenFields = new List<ODFieldName>();

                    if (item.Value.FieldValues != null)
                    {
                        foreach (var evt in item.Value.FieldValues)
                        {
                            object currentValue = m_machine.GetGameplayObjectValue(item.Key, evt.Key);
                            if (!Equals(currentValue, evt.Value))
                            {
                                m_machine.BroadCastFieldChangedEvent(item.Key, evt.Key,
                                    ObjectEventTypes.Changed, null, delayedDrivenFields);
                            }
                        }
                    }

                    if (item.Value.FieldValuesWithoutEqual != null)
                    {
                        foreach (var field in item.Value.FieldValuesWithoutEqual)
                        {
                            m_machine.BroadCastFieldChangedEvent(item.Key, field,
                                ObjectEventTypes.Changed, null, delayedDrivenFields);
                        }
                    }

                    List<object> adds = null;
                    List<object> removes = null;
                    List<object> changes = null;
                    if (item.Value.CollectionValues != null)
                    {
                        adds = adds ?? new List<object>();
                        removes = removes ?? new List<object>();
                        changes = changes ?? new List<object>();

                        foreach (KeyValuePair<ODFieldName, Dictionary<object, int>> evt in item.Value.CollectionValues)
                        {
                            foreach (KeyValuePair<object, int> counters in evt.Value)
                            {
                                int count = 0;
                                ObjectEventTypes eventType;
                                if (counters.Value > 0)
                                {
                                    eventType = ObjectEventTypes.Add;
                                    count = counters.Value;
                                }
                                else if (counters.Value < 0)
                                {
                                    eventType = ObjectEventTypes.Remove;
                                    count = -counters.Value;
                                }
                                else
                                {
                                    continue;
                                }
                                for (int i = 0; i < count; i++)
                                {
                                    List<object> list = eventType == ObjectEventTypes.Add ? adds : removes;
                                    list.Add(counters.Key);
                                }
                            }
                            
                            if (item.Value.CollectionChangedValues != null)
                            {
                                Dictionary<object, object> fieldChangedValues;
                                if (item.Value.CollectionChangedValues.TryGetValue(evt.Key, out fieldChangedValues))
                                {
                                    foreach (KeyValuePair<object, object> changedKey in fieldChangedValues)
                                    {
                                        IGMCollection collection = m_machine.GetCollectionController(item.Key, evt.Key, false);
                                        object currentValue = collection.Get(changedKey.Key);
                                        if (currentValue != null)
                                        {
                                            // The key was not removed
                                            if (!Equals(changedKey.Value, currentValue))
                                            {
                                                changes.Add(changedKey.Key);
                                            }
                                        }
                                    }
                                    item.Value.CollectionChangedValues.Remove(evt.Key);
                                }
                            }

                            m_machine.BroadCastFieldChangedEvent(item.Key, evt.Key,
                                adds, removes, changes, delayedDrivenFields);
                            adds.Clear();
                            removes.Clear();
                            changes.Clear();
                        }
                    }

                    if (item.Value.CollectionChangedValues != null)
                    {
                        changes = changes ?? new List<object>();

                        foreach (var evt in item.Value.CollectionChangedValues)
                        {
                            foreach (var changedKey in evt.Value)
                            {
                                IGMCollection collection = m_machine.GetCollectionController(item.Key, evt.Key, false);
                                object currentValue = collection.Get(changedKey.Key);
                                if (currentValue != null)
                                {
                                    // The key was not removed
                                    if (!Equals(changedKey.Value, currentValue))
                                    {
                                        changes.Add(changedKey.Key);
                                    }
                                }
                            }

                            m_machine.BroadCastFieldChangedEvent(item.Key, evt.Key,
                                null, null, changes, delayedDrivenFields);
                            changes.Clear();
                        }
                    }

                    m_machine.BroadCastDelayedDrivenFieldChanges(item.Key, delayedDrivenFields);
                    //item.Value.FieldValues.Clear();
                }
                m_delayedObjectEvents.Clear();

                foreach (var item in m_delayedMachineEvents)
                {
                    m_machine.BroadCastMachineEvent(item);
                }
                m_delayedMachineEvents.Clear();

                foreach (var item in m_createdObjects)
                {
                    if (m_deletedObjects.Contains(item))
                    {
                        continue;
                    }
                    m_machine.BroadCastObjectLifeEvent(item, true);
                }
                foreach (var item in m_deletedObjects)
                {
                    if (m_createdObjects.Contains(item))
                    {
                        continue;
                    }
                    m_machine.BroadCastObjectLifeEvent(item, false);
                }
                m_createdObjects.Clear();
                m_deletedObjects.Clear();
            }
        }

        #endregion

        [System.NonSerialized]
        private MachineKey m_machineKey;

        [System.NonSerialized]
        private ODCore.Serialization.ODDatabase m_dataBase;

        [System.NonSerialized]
        private XLogger.Logger m_logger;

        [System.NonSerialized]
        private bool m_logInterfaceExecutionFailures;

        [System.NonSerialized]
        private List<IGMSerializer> m_serializers;

        [System.NonSerialized]
        private XEventSystem.TypedEventSystem m_machineEvents;

        [System.NonSerialized]
        private GameplayEventSystemManager m_eventSystemManager;

        private IGameplayObjectManager m_objectManager;

        private GObjectID m_rootObjectID;

        [System.NonSerialized]
        private object m_currentExecutingInterface;
        [System.NonSerialized]
        private XEnumerator.XEnumerator m_routineEnumerator;

        [System.NonSerialized]
        private bool m_modifyDataInsideExecutionOnly;

        [System.NonSerialized]
        private GameplayTickMode m_tickMode;

        [System.NonSerialized]
        private int m_tickIntervalMilliseconds;

        [System.NonSerialized]
        private double m_tickAccumulatorMilliseconds;

        [System.NonSerialized]
        private long m_lastUpdateTimestamp;

        [System.NonSerialized]
        private long m_tickCount;

        #region Delayed Events

        [System.NonSerialized]
        private bool m_useDelayedCallback;

        [System.NonSerialized]
        private DelayEvents m_delayedEvents;

        #endregion

        #region Proxy

        [System.NonSerialized]
        protected GameplayMachineProxy m_proxy;

        #endregion

        #region Cache mode

        [System.NonSerialized]
        private bool m_cacheMode;

        [System.NonSerialized]
        private GameplayCacheManager m_cacheManager;

        #endregion

        public GameplayMachine()
        {
            
        }

        public void Init(IGameplayObjectManager objectManager, GameplayMachineSettings settings)
        {
            if (objectManager != null)
            {
                m_objectManager = objectManager;
            }

            m_dataBase = settings.Database;
            m_logger = settings.Logger ?? new XLogger.Logger();
            m_logInterfaceExecutionFailures = settings.LogInterfaceExecutionFailures;
            m_serializers = settings.Serializers;
            m_synchronizationMode = settings.SynchronizationMode;
            if (!Enum.IsDefined(typeof(GameplayTickMode), settings.TickMode))
                throw new ArgumentOutOfRangeException(nameof(settings.TickMode));
            m_tickMode = settings.TickMode;
            m_tickIntervalMilliseconds = settings.TickIntervalMilliseconds > 0
                ? settings.TickIntervalMilliseconds
                : 50;
            m_tickAccumulatorMilliseconds = 0d;
            m_lastUpdateTimestamp = Stopwatch.GetTimestamp();
            m_tickCount = 0;
            if (m_synchronizationMode == GameplaySynchronizationMode.Lockstep)
                m_modifyDataInsideExecutionOnly = true;

            m_machineEvents = new XEventSystem.TypedEventSystem();
            m_eventSystemManager = new GameplayEventSystemManager(this);
            m_cacheManager = new GameplayCacheManager();
            m_referenceIndex = new GameplayReferenceIndex();
            m_deletingGameplayObjects = new HashSet<GObjectID>();
            InitializeGameplayNetworkMetadata();
            InitializeGameplaySpatialRelevancy(settings.Interest);
        }

        public MachineKey MachineKey
        {
            get
            {
                return m_machineKey;
            }

            set
            {
                m_machineKey = value;
            }
        }

        public XLogger.Logger Logger
        {
            get
            {
                return m_logger;
            }
        }

        public List<IGMSerializer> Serializers
        {
            get
            {
                return m_serializers;
            }
        }

        public GameplayEventSystemManager EventManager
        {
            get
            {
                return m_eventSystemManager;
            }
        }

        public bool DuringExecution
        {
            get
            {
                return m_currentExecutingInterface != null;
            }
        }

        public bool HasRoutine
        {
            get
            {
                return m_routineEnumerator != null;
            }
        }

        public bool UseDelayedCallback
        {
            get
            {
                return m_useDelayedCallback;
            }
        }

        private GameplayMachineProxy Proxy
        {
            get
            {
                if (m_proxy == null)
                {
                    m_proxy = CreateGameplayMachineProxy();
                }

                return m_proxy;
            }
        }

        protected virtual GameplayMachineProxy CreateGameplayMachineProxy()
        {
            return new GameplayMachineProxy(this);
        }

        protected virtual void OnAuthorityStarted() { }

        /// <summary>
        /// Runs once on every Lockstep peer after all PlayerControllers have received their
        /// zero-based SlotID and joined, and before the first Lockstep frame or OnTick call.
        /// </summary>
        protected virtual void OnGameStart() { }

        /// <summary>
        /// Runs at the configured fixed interval on a State Sync Authority and once after all
        /// player inputs in every Lockstep frame. ClientProxy machines never run authoritative ticks.
        /// </summary>
        protected virtual void OnTick() { }

        public GameplayTickMode TickMode => m_tickMode;

        public int TickIntervalMilliseconds => m_tickIntervalMilliseconds;

        public long TickCount => m_tickCount;

        internal void RunGameplayTick()
        {
            m_engineMutationDepth++;
            try
            {
                m_tickCount++;
                OnTick();
            }
            finally
            {
                m_engineMutationDepth--;
            }
        }

        #region Execution

        public void SetModifyDataInsideExecutionOnly(bool val)
        {
            if (DuringExecution)
            {
                m_logger?.LogErrors(XLogger.LogVerbosity.Low, LogCatagories.GameplayMachine, $"You can only call {nameof(SetModifyDataInsideExecutionOnly)} when not executing any interface");
                return;
            }

            m_modifyDataInsideExecutionOnly = val;
        }

        public bool ShouldModifyDataInsideExecutionOnly
        {
            get
            {
                return m_modifyDataInsideExecutionOnly || m_useDelayedCallback;
            }
        }

        public EventError CanExecute<TOut>(ICheckableInterface<TOut> param)
            where TOut : struct
        {
            EventError routeError = CanRouteGameplayInterface(param);
            if (!routeError)
                return routeError;
            return param.CanExecute(Proxy);
        }

        public EventError CanExecute(IODInterfaceCall param)
        {
            if (param == null)
                return "Interface call cannot be null";
            if (param.InterfaceType != GameplayInterfaceType.Gameplay)
                return "IODInterfaceCall can only validate ordinary Gameplay interfaces";
            return param.CanExecute(Proxy);
        }

        public EventError CanExecute(IReplicatedInterface param)
        {
            EventError routeError = CanRouteReplicatedInterface(param);
            if (!routeError)
                return routeError;
            if (m_synchronizationMode == GameplaySynchronizationMode.Lockstep)
                return CanSubmitLockstepInterface(param);
            if (Role == GameplayMachineRole.ClientProxy)
            {
                if (param.RpcMode == GameplayRpcMode.Predictable)
                {
                    EventError sendError = CanSendPredictableInterface((IPredictableInterface)param);
                    return sendError ? param.CanExecute(Proxy) : sendError;
                }
                return CanSendAuthorityInterface(param);
            }
            return param.CanExecute(Proxy);
        }

        public CheckableInterfaceResult<TOut> Execute<TOut>(ICheckableInterface<TOut> param)
            where TOut : struct
        {
            CheckableInterfaceResult<TOut> result;
            EventError routeError = CanRouteGameplayInterface(param);
            if (!routeError)
                return new CheckableInterfaceResult<TOut> { ErrorMessage = routeError };
            if (m_currentExecutingInterface != null)
            {
                string errorMsg = $"Cannot Execute new interface {param} when interface {m_currentExecutingInterface} is running, you must call {nameof(GameplayMachine.GameplayMachineProxy.Execute)} from within the interface.";
                m_logger.LogError(XLogger.LogVerbosity.Common, LogCatagories.GameplayMachine, errorMsg);
                result = new CheckableInterfaceResult<TOut>()
                {
                    ErrorMessage = errorMsg
                };
                return result;
            }

            if (m_routineEnumerator != null)
            {
                string errorMsg = $"Cannot Execute new interface {param} when old routine is running, you must call {nameof(GameplayMachine.GameplayMachineProxy.Execute)} from within the routine.";
                m_logger.LogError(XLogger.LogVerbosity.Common, LogCatagories.GameplayMachine, errorMsg);
                result = new CheckableInterfaceResult<TOut>()
                {
                    ErrorMessage = errorMsg
                };
                return result;
            }

            OnEnterInterfaceExecution(param);
            try
            {
                result = TraceExecution(param, () => Internal_Execute(param));
            }
            finally
            {
                OnExitInterfaceExecution();
            }

            return result;
        }

        public CheckableInterfaceResult Execute(IODInterfaceCall param)
        {
            if (param == null)
                return new CheckableInterfaceResult { ErrorMessage = "Interface call cannot be null" };
            if (param.InterfaceType != GameplayInterfaceType.Gameplay)
                return new CheckableInterfaceResult
                {
                    ErrorMessage = "IODInterfaceCall can only execute ordinary Gameplay interfaces",
                };
            if (m_currentExecutingInterface != null)
                return new CheckableInterfaceResult
                {
                    ErrorMessage = $"Cannot Execute new interface {param} when interface {m_currentExecutingInterface} is running, you must call {nameof(GameplayMachineProxy.Execute)} from within the interface.",
                };
            if (m_routineEnumerator != null)
                return new CheckableInterfaceResult
                {
                    ErrorMessage = $"Cannot Execute new interface {param} when an old routine is running.",
                };

            OnEnterInterfaceExecution(param);
            try
            {
                return TraceExecution(param, () => param.Invoke(Proxy));
            }
            finally
            {
                OnExitInterfaceExecution();
            }
        }

        public CheckableInterfaceResult Execute(IReplicatedInterface param)
        {
            ThrowIfClientMulticastExecution(param);
            CheckableInterfaceResult result = default;
            EventError routeError = CanRouteReplicatedInterface(param);
            if (!routeError)
            {
                result.ErrorMessage = routeError;
                return result;
            }
            if (m_synchronizationMode == GameplaySynchronizationMode.Lockstep)
            {
                if (DuringExecution)
                {
                    result.ErrorMessage = "PlayerInput interfaces must be submitted from outside GameplayMachine execution in Lockstep mode";
                    return result;
                }
                result.ErrorMessage = SubmitLockstepInterface(param);
                return result;
            }
            if (m_currentExecutingInterface != null)
            {
                string errorMsg = $"Cannot Execute new interface {param} when interface {m_currentExecutingInterface} is running, you must call {nameof(GameplayMachine.GameplayMachineProxy.Execute)} from within the interface.";
                m_logger.LogError(XLogger.LogVerbosity.Common, LogCatagories.GameplayMachine, errorMsg);
                result.ErrorMessage = errorMsg;
                return result;
            }
            if (m_routineEnumerator != null)
            {
                string errorMsg = $"Cannot Execute new interface {param} when old routine is running, you must call {nameof(GameplayMachine.GameplayMachineProxy.Execute)} from within the routine.";
                m_logger.LogError(XLogger.LogVerbosity.Common, LogCatagories.GameplayMachine, errorMsg);
                result.ErrorMessage = errorMsg;
                return result;
            }

            OnEnterInterfaceExecution(param);
            try
            {
                result = TraceExecution(param, () =>
                {
                    if (Role == GameplayMachineRole.ClientProxy)
                    {
                        if (param.RpcMode == GameplayRpcMode.Predictable)
                            return Internal_PredictClient((IPredictableInterface)param);
                        CheckableInterfaceResult routed = default;
                        routed.ErrorMessage = SendAuthorityInterface(param);
                        return routed;
                    }
                    return Internal_ExecuteReplicated(param, broadcastMulticast: true);
                });
            }
            finally
            {
                OnExitInterfaceExecution();
            }
            return result;
        }

        public CheckableInterfaceResult<TOut> Execute<TOut>(IReplicatedInterface<TOut> param)
            where TOut : struct
        {
            ThrowIfClientMulticastExecution(param);
            CheckableInterfaceResult<TOut> result = default;
            if (Role == GameplayMachineRole.ClientProxy)
                result.DenyResultAccess();
            EventError routeError = CanRouteOutputReplicatedInterface(param);
            if (!routeError)
            {
                result.ErrorMessage = routeError;
                return result;
            }
            if (m_currentExecutingInterface != null)
            {
                string errorMsg = $"Cannot Execute new interface {param} when interface {m_currentExecutingInterface} is running, you must call {nameof(GameplayMachine.GameplayMachineProxy.Execute)} from within the interface.";
                m_logger.LogError(XLogger.LogVerbosity.Common, LogCatagories.GameplayMachine, errorMsg);
                result.ErrorMessage = errorMsg;
                return result;
            }
            if (m_routineEnumerator != null)
            {
                string errorMsg = $"Cannot Execute new interface {param} when old routine is running, you must call {nameof(GameplayMachine.GameplayMachineProxy.Execute)} from within the routine.";
                m_logger.LogError(XLogger.LogVerbosity.Common, LogCatagories.GameplayMachine, errorMsg);
                result.ErrorMessage = errorMsg;
                return result;
            }

            OnEnterInterfaceExecution(param);
            try
            {
                if (Role == GameplayMachineRole.ClientProxy)
                {
                    result.ErrorMessage = SendAuthorityInterface(param);
                    return TraceExecution(param, () => result);
                }
                return TraceExecution(param, () => Internal_ExecuteReplicated(param, broadcastMulticast: true));
            }
            finally
            {
                OnExitInterfaceExecution();
            }
        }

        public CheckableInterfaceResult Execute(ICheckableRoutine param, bool instantly = false)
        {
            CheckableInterfaceResult result;
            if (Role == GameplayMachineRole.ClientProxy)
            {
                result = default;
                result.ErrorMessage = "ClientProxy GameplayMachines cannot execute routines";
                return result;
            }
            if (m_currentExecutingInterface != null)
            {
                string errorMsg = $"Cannot Execute new interface {param} when interface {m_currentExecutingInterface} is running, you must call {nameof(GameplayMachine.GameplayMachineProxy.Execute)} from within the interface.";
                m_logger.LogError(XLogger.LogVerbosity.Common, LogCatagories.GameplayMachine, errorMsg);
                result = new CheckableInterfaceResult()
                {
                    ErrorMessage = errorMsg
                };
                return result;
            }

            if (m_routineEnumerator != null)
            {
                string errorMsg = $"Cannot Execute new interface {param} when old routine is running, you must call {nameof(GameplayMachine.GameplayMachineProxy.Execute)} from within the routine.";
                m_logger.LogError(XLogger.LogVerbosity.Common, LogCatagories.GameplayMachine, errorMsg);
                result = new CheckableInterfaceResult()
                {
                    ErrorMessage = errorMsg
                };
                return result;
            }

            if (!instantly)
            {

                m_routineEnumerator = new XEnumerator.XEnumerator();
                m_routineEnumerator.Start(TraceRoutineExecution(param, () => Internal_RoutineExecute(param)));
                result = default;
            }
            else
            {

                OnEnterInterfaceExecution(param);
                try
                {
                    result = TraceExecution(param, () => Internal_Execute(param));
                }
                finally
                {
                    OnExitInterfaceExecution();
                }
            }
            return result;
        }

        private CheckableInterfaceResult<TOut> Internal_Execute<TOut>(ICheckableInterface<TOut> param)
            where TOut : struct
        {
            CheckableInterfaceResult<TOut> result = default;
            result.ErrorMessage = param.CanExecute(Proxy);
            if (!result.ErrorMessage)
            {
                LogInterfaceExecutionFailure(param, result.ErrorMessage);
                return result;
            }

            TOut output = default;
            param.DoExecute(Proxy, ref output);
            result.Result = output;
            return result;
        }

        private CheckableInterfaceResult<TOut> Internal_ExecuteReplicated<TOut>(
            IReplicatedInterface<TOut> param,
            bool broadcastMulticast)
            where TOut : struct
        {
            CheckableInterfaceResult<TOut> result = default;
            result.ErrorMessage = param.CanExecute(Proxy);
            if (!result.ErrorMessage)
            {
                LogInterfaceExecutionFailure(param, result.ErrorMessage);
                return result;
            }

            TOut output = default;
            param.DoExecute(Proxy, ref output);
            result.Result = output;
            if (broadcastMulticast && param.RpcMode == GameplayRpcMode.Multicast)
                BroadcastMulticastInterface(param);
            return result;
        }

        private CheckableInterfaceResult Internal_ExecuteReplicated(IReplicatedInterface param, bool broadcastMulticast)
        {
            CheckableInterfaceResult result = default;
            result.ErrorMessage = param.CanExecute(Proxy);
            if (!result.ErrorMessage)
            {
                LogInterfaceExecutionFailure(param, result.ErrorMessage);
                return result;
            }

            param.DoExecute(Proxy);
            if (broadcastMulticast && param.RpcMode == GameplayRpcMode.Multicast)
                BroadcastMulticastInterface(param);
            return result;
        }

        private CheckableInterfaceResult Internal_PredictClient(IPredictableInterface param)
        {
            CheckableInterfaceResult result = default;
            result.ErrorMessage = CanSendPredictableInterface(param);
            if (!result.ErrorMessage)
                return result;

            result.ErrorMessage = param.CanExecute(Proxy);
            if (!result.ErrorMessage)
                return result;

            IPredictableInterface snapshot;
            result.ErrorMessage = SnapshotPredictableInterface(param, out snapshot);
            if (!result.ErrorMessage)
                return result;

            param.OnPredictClient(Proxy);
            result.ErrorMessage = SendPredictableInterface(snapshot);
            if (!result.ErrorMessage && ShouldReportPrediction(param.PredictionMode, false))
                param.OnFailClient(Proxy);
            return result;
        }

        internal void CompletePredictionFromNetwork(IPredictableInterface param, bool succeeded)
        {
            bool ownsExecution = !DuringExecution;
            if (ownsExecution)
                OnEnterInterfaceExecution(param);
            if (succeeded && ShouldReportPrediction(param.PredictionMode, true))
                param.OnSuccessClient(Proxy);
            else if (!succeeded && ShouldReportPrediction(param.PredictionMode, false))
                param.OnFailClient(Proxy);
            if (ownsExecution)
                OnExitInterfaceExecution();
        }

        internal CheckableInterfaceResult ExecuteReplicatedFromNetwork(IReplicatedInterface param, GameplayRpcMode expectedMode)
        {
            CheckableInterfaceResult result = default;
            if (param == null || param.RpcMode != expectedMode)
            {
                result.ErrorMessage = $"Received replicated interface with an invalid RPC mode; expected {expectedMode}";
                return result;
            }
            if (DuringExecution)
                return TraceExecution(param, () => Internal_ExecuteReplicated(param, broadcastMulticast: false), "Network");

            OnEnterInterfaceExecution(param);
            try
            {
                result = TraceExecution(param, () => Internal_ExecuteReplicated(param, broadcastMulticast: false), "Network");
            }
            finally
            {
                OnExitInterfaceExecution();
            }
            return result;
        }

        private EventError CanRouteReplicatedInterface(IReplicatedInterface param)
        {
            if (param == null)
                return "Replicated interface cannot be null";
            if (param is IGameplayInterface)
            {
                if (param.RpcMode != GameplayRpcMode.None)
                    return "Gameplay interfaces cannot use RPC";
                if (m_synchronizationMode == GameplaySynchronizationMode.Lockstep)
                    return DuringExecution
                        ? (EventError)true
                        : "Gameplay interfaces can only be executed by GameplayMachineProxy in Lockstep mode";
                return Role == GameplayMachineRole.Authority
                    ? (EventError)true
                    : "Gameplay interfaces can only be executed by an Authority";
            }
            if (!(param is IPlayerInputInterface))
                return "Only PlayerInput interfaces can use RPC";
            if (m_synchronizationMode == GameplaySynchronizationMode.Lockstep)
                return param.RpcMode == GameplayRpcMode.Lockstep
                    ? (EventError)true
                    : "PlayerInput interfaces must use Lockstep transport in a Lockstep project";
            if (param.RpcMode == GameplayRpcMode.None)
                return "PlayerInput interfaces must use Authority or Predictable transport";
            if (param.RpcMode == GameplayRpcMode.Predictable && !(param is IPredictableInterface))
                return "Predictable interfaces must implement IPredictableInterface";
            if (param.RpcMode != GameplayRpcMode.Authority && param.RpcMode != GameplayRpcMode.Predictable)
                return "PlayerInput interfaces must use Authority or Predictable transport";
            return true;
        }

        private EventError CanRouteGameplayInterface(object param)
        {
            if (m_synchronizationMode == GameplaySynchronizationMode.Lockstep)
                return param is IGameplayInterface && DuringExecution
                    ? (EventError)true
                    : "Gameplay interfaces can only be executed by GameplayMachineProxy in Lockstep mode";
            return param is IGameplayInterface && Role != GameplayMachineRole.Authority
                ? (EventError)"Gameplay interfaces can only be executed by an Authority"
                : (EventError)true;
        }

        private static bool ShouldReportPrediction(GameplayPredictionMode mode, bool succeeded)
        {
            return mode == GameplayPredictionMode.Both ||
                   succeeded && mode == GameplayPredictionMode.OnlySuccess ||
                   !succeeded && mode == GameplayPredictionMode.OnlyFailure;
        }

        private EventError CanRouteOutputReplicatedInterface<TOut>(IReplicatedInterface<TOut> param)
            where TOut : struct
        {
            EventError routeError = CanRouteReplicatedInterface(param);
            if (!routeError)
                return routeError;
            return "PlayerInput interfaces cannot declare outputs";
        }

        private void ThrowIfClientMulticastExecution(IReplicatedInterface param)
        {
            if (Role == GameplayMachineRole.ClientProxy && param != null &&
                param.RpcMode == GameplayRpcMode.Multicast)
                throw new InvalidOperationException("Multicast interfaces can only be executed by an Authority");
        }

        private CheckableInterfaceResult Internal_Execute(ICheckableRoutine param)
        {
            CheckableInterfaceResult result = default;
            result.ErrorMessage = param.CanExecute(Proxy);
            if (!result.ErrorMessage)
            {
                LogInterfaceExecutionFailure(param, result.ErrorMessage);
                return result;
            }

            XEnumerator.XEnumerator runner = new XEnumerator.XEnumerator();
            XEnumerator.XEnumerator oldRunner = m_routineEnumerator;
            m_routineEnumerator = runner;
            runner.Start(param.DoExecute(Proxy));
            while (runner.Step())
            {

            }
            m_routineEnumerator = oldRunner;

            return result;
        }

        private IEnumerator Internal_RoutineExecute(ICheckableRoutine param)
        {
            if (m_routineEnumerator == null)
            {
                m_logger.LogError(XLogger.LogVerbosity.Common, LogCatagories.GameplayMachine, $"Trying to execute: \"{param}\" but no routine is running (Don't call this from outside routine!)");
                return null;
            }

            EventError errorMessage = param.CanExecute(Proxy);
            if (!errorMessage)
            {
                LogInterfaceExecutionFailure(param, errorMessage);
                return null;
            }

            return param.DoExecute(Proxy);
        }

        private IEnumerator WrapperExecute<TOut>(ICheckableInterface<TOut> param)
            where TOut : struct
        {
            CheckableInterfaceResult<TOut> result = default;
            TOut output = default;
            param.DoExecute(Proxy, ref output);
            result.Result = output;
            yield break;
        }

        private IEnumerator Internal_RoutineExecute<TOut>(ICheckableInterface<TOut> param)
            where TOut : struct
        {
            if (m_routineEnumerator == null)
            {
                m_logger.LogError(XLogger.LogVerbosity.Common, LogCatagories.GameplayMachine, $"Trying to execute: \"{param}\" but no routine is running (Don't call this from outside routine!)");
                return null;
            }

            EventError errorMessage = param.CanExecute(Proxy);
            if (!errorMessage)
            {
                LogInterfaceExecutionFailure(param, errorMessage);
                return null;
            }

            return WrapperExecute(param);
        }

        private void LogInterfaceExecutionFailure(object param, EventError errorMessage)
        {
            if (!m_logInterfaceExecutionFailures)
                return;

            m_logger.LogError(XLogger.LogVerbosity.Common, LogCatagories.GameplayMachine,
                $"Trying to execute: \"{param}\" but failed: \"{errorMessage}\"");
        }

        public bool StepRoutine()
        {
            if (DuringExecution)
            {
                m_logger.LogError(XLogger.LogVerbosity.Common, LogCatagories.GameplayMachine, $"Trying to {nameof(StepRoutine)} but failed because Machine is during execution");
                return false;
            }

            if (m_routineEnumerator == null)
            {
                m_logger.LogError(XLogger.LogVerbosity.Common, LogCatagories.GameplayMachine, $"Trying to {nameof(StepRoutine)} but failed because no routine is running");
                return false;
            }

            OnEnterInterfaceExecution(m_routineEnumerator);
            if (!m_routineEnumerator.Step(true))
            {
                StopRoutine();
                OnExitInterfaceExecution();
                return false;
            }

            OnExitInterfaceExecution();
            return true;
        }

        private void StopRoutine()
        {
            m_routineEnumerator?.Clear();
            m_routineEnumerator = null;
        }

        private void OnEnterInterfaceExecution(object param)
        {
            m_currentExecutingInterface = param;
        }

        private void OnExitInterfaceExecution()
        {
            bool delayed = m_useDelayedCallback;
            m_currentExecutingInterface = null;
            if (delayed)
            {
                m_delayedEvents.FlushAllEvents();
            }
            FlushNetworkChanges();
        }

        #endregion

        #region Event

        private bool ShouldStoreEvent
        {
            get
            {
                return m_useDelayedCallback && (DuringExecution || m_applyingReplication);
            }
        }

        public IReturnableHandle BindMachineEvent<T>(XEventSystem.TemplateEvtDelegateHandler<T> callBack)
        {
            return m_machineEvents.BindEvtDelegate(callBack);
        }

        public void UnBindMachineEvent<T>(XEventSystem.TemplateEvtDelegateHandler<T> callBack)
        {
            m_machineEvents.UnBindEvtDelegate(callBack);
        }

        public IReturnableHandle BindMachineEvent(Type evtContextType, XEventSystem.EvtCommonHandler callBack)
        {
            return m_machineEvents.BindEvtDelegate(evtContextType, callBack);
        }

        public void UnBindMachineEvent(Type evtContextType, XEventSystem.EvtCommonHandler callBack)
        {
            m_machineEvents.UnBindEvtDelegate(evtContextType, callBack);
        }

        public void BroadCastMachineEvent(object evtDelegateContext)
        {
            if (evtDelegateContext is IMulticastEvent multicastEvent)
            {
                if (Role == GameplayMachineRole.ClientProxy)
                    throw new InvalidOperationException(
                        "Multicast events can only be broadcast by an Authority");
                BroadcastMulticastEvent(multicastEvent);
            }
            DispatchMachineEvent(evtDelegateContext);
        }

        internal void ReceiveMulticastEvent(IMulticastEvent multicastEvent)
        {
            DispatchMachineEvent(multicastEvent);
        }

        private void DispatchMachineEvent(object evtDelegateContext)
        {
            if (ShouldStoreEvent)
            {
                m_delayedEvents.StoreMachineEvent(evtDelegateContext);
            }
            else
            {
                m_machineEvents.BroadCastEvtDelegate(evtDelegateContext);
            }
        }

        public void ClearMachineEvent<T>()
        {
            m_machineEvents.ClearEvent<T>();
        }

        public void ClearMachineEvent(Type bindKey)
        {
            m_machineEvents.ClearEvent(bindKey);
        }

        #endregion

        #region Cache Mode

        public void EnterCacheMode()
        {
            if (m_cacheMode)
            {
                return;
            }

            m_cacheMode = true;
        }

        public void LeaveCacheMode()
        {
            foreach (var item in m_cacheManager.CacheDatas)
            {
                GObjectID id = item.Key;
                GameplayCacheManager.ObjectCacheData data = item.Value;

                IGameplayObject updateObj;

                if (data.Created)
                {
                    if (data.Deleted)
                    {
                        continue;
                    }

                    updateObj = m_objectManager.CreateGameplayObject(data.ClassName, id);
                }
                else
                {
                    updateObj = m_objectManager.GetGameplayObject(id);
                }

                if (data.Deleted)
                {
                    RemoveGameplayObject(id);
                    continue;
                }

                if (data.Values != null)
                {
                    foreach (KeyValuePair<ODFieldName, object> changedItem in data.Values)
                    {
                        updateObj[changedItem.Key.Meta] = changedItem.Value;
                    }
                }

                if (data.Collections != null)
                {
                    foreach (KeyValuePair<ODFieldName, object> collection in data.Collections)
                    {
                        ICollectionMemoryObject gMCollection = collection.Value as ICollectionMemoryObject;
                        gMCollection.CopyTo(updateObj, collection.Key);
                    }
                }
            }

            m_cacheMode = false;
            RebuildGameplayReferenceIndex();
            RebuildGameplaySpatialIndex();
        }

        #endregion

        internal IGameplayObjectManager ObjectManager
        {
            get { return m_objectManager; }
        }

        internal GObjectID RootObjectID
        {
            get { return m_rootObjectID; }
        }

        #region GameplayObject

        public void SetRoot(IGameplayObjectOperator gameplayObject)
        {
            CheckDataModifiedOutsideExecution();
            CheckValid(gameplayObject);
            if (!GetGameplayObjectPartition(gameplayObject.ObjectID).Equals(PersistentPartitionID))
                throw new InvalidOperationException("Machine root must belong to the Persistent partition");
            SetRootObjectID(gameplayObject.ObjectID, true);
        }

        public CommonGameplayObject GetRoot()
        {
            if (!m_rootObjectID.IsValid)
            {
                throw new InvalidOperationException("GameplayMachine root has not been set");
            }

            GetGameplayObject(m_rootObjectID, true);
            return new CommonGameplayObject
            {
                Machine = this,
                ObjectID = m_rootObjectID,
            };
        }

        public bool HasRoot
        {
            get { return m_rootObjectID.IsValid; }
        }

        internal void SetRootForLoad(GObjectID rootObjectID)
        {
            if (rootObjectID.IsValid && !m_objectManager.ContainsGameplayObject(rootObjectID))
            {
                throw new InvalidDataException($"GameplayMachine root object {rootObjectID.ID} does not exist");
            }
            SetRootObjectID(rootObjectID, false);
        }

        private void SetRootObjectID(GObjectID rootObjectID, bool recordNetworkChange)
        {
            if (m_rootObjectID.Equals(rootObjectID))
            {
                return;
            }

            m_rootObjectID = rootObjectID;
            if (recordNetworkChange)
            {
                RecordNetworkRootChange();
            }
        }

        private IGameplayObject GetGameplayObject(GObjectID id, bool existenceCheck = false)
        {
            IGameplayObject result = m_objectManager.GetGameplayObject(id);
            if (existenceCheck && result == null)
            {
                throw new NotExistException();
            }
            return result;
        }

        private bool RemoveGameplayObject(GObjectID id)
        {
            return m_objectManager.RemoveGameplayObject(id);
        }

        public T CreateGameplayObject<T>(IEnumerable<IODClassResource> resources)
            where T : struct, IGameplayObjectOperator, IODClass
        {
            return DirectCast<T>(CreateGameplayObject(GetODClassName<T>(), resources));
        }

        public CommonGameplayObject CreateGameplayObject(ODClassName nameOfObject, IEnumerable<IODClassResource> resources)
        {
            CommonGameplayObject result = CreateGameplayObject(nameOfObject);
            foreach (var item in resources)
            {
                item.Set(result);
            }
            return result;
        }

        public T CreateGameplayObject<T>()
            where T : struct, IGameplayObjectOperator, IODClass
        {
            return DirectCast<T>(CreateGameplayObject(GetODClassName<T>()));
        }

        public CommonGameplayObject CreateGameplayObject(ODClassName nameOfObject)
        {
            return CreateGameplayObjectCore(nameOfObject, false, true);
        }

        internal CommonGameplayObject CreateEngineGameplayObject(ODClassName nameOfObject)
        {
            return CreateGameplayObjectCore(nameOfObject, true, false);
        }

        private CommonGameplayObject CreateGameplayObjectCore(
            ODClassName nameOfObject,
            bool allowEngineManagedClass,
            bool checkExecution)
        {
            if (m_synchronizationMode == GameplaySynchronizationMode.Lockstep &&
                (nameOfObject.Equals(Actor.ClassName) || nameOfObject.Equals(Pawn.ClassName)))
                throw new InvalidOperationException("Actor, Pawn, and Transform are unavailable in Lockstep project mode");
            if (GetODClassMeta(nameOfObject).IsEngineManaged && !allowEngineManagedClass)
                throw new InvalidOperationException($"{nameOfObject} instances are managed by GameplayMachine");
            if (checkExecution)
                CheckDataModifiedOutsideExecution();

            if (!GetPartition(allowEngineManagedClass ? PersistentPartitionID : m_creationPartition).IsLoaded)
                throw new InvalidOperationException("Cannot create objects in an unloaded partition");

            GObjectID? overrideID = m_role == GameplayMachineRole.ClientProxy &&
                                    m_synchronizationMode != GameplaySynchronizationMode.Lockstep
                ? AllocateClientLocalObjectID()
                : (GObjectID?)null;
            GObjectID newObjID;
            if (m_cacheMode)
            {
                newObjID = overrideID ?? m_objectManager.GenerateUniqueID();
                m_cacheManager.CreateGameplayObject(newObjID, nameOfObject);
                InitializeCachedGameplayObjectFields(newObjID, nameOfObject);
            }
            else
            {
                IGameplayObject obj = m_objectManager.CreateGameplayObject(nameOfObject, overrideID);
                InitializeGameplayObjectFields(obj);
                newObjID = obj.ObjectID;
            }

            CommonGameplayObject result = new CommonGameplayObject();

            result.ObjectID = newObjID;
            result.Machine = this;

            InitializeGameplayObjectNetworkMetadata(newObjID, nameOfObject);
            RegisterGameplayObjectPartition(newObjID, allowEngineManagedClass ? PersistentPartitionID : m_creationPartition);
            MarkPartitionDirty(newObjID);
            BroadCastObjectLifeEvent(newObjID, true);

            return result;
        }

        public TValue GetOrCreateGameplayObjectValue<TValue>(GObjectID id, ODFieldName index)
            where TValue : class, new()
        {
            TValue result = GetGameplayObjectValue<TValue>(id, index);
            if (result == null)
            {
                result = new TValue();
                SetGameplayObjectValue(id, index, result);
            }
            return result;
        }

        internal void InitializeNewAuthority(GameplayMachineSettings settings)
        {
            m_engineMutationDepth++;
            try
            {
                if (settings.RootGameplayObjectClass.HasValue)
                {
                    ODClassName rootClass = settings.RootGameplayObjectClass.Value;
                    ODClassMeta rootMeta = GetODClassMeta(rootClass);
                    if (!rootMeta.IsEngineManaged)
                        throw new InvalidOperationException($"Configured root class {rootClass} is not engine-managed");

                    CommonGameplayObject root = CreateEngineGameplayObject(rootClass);
                    SetRootObjectID(root.ObjectID, true);
                }

                if (settings.InitialScene != null)
                    CreateScene(settings.InitialScene);

                OnAuthorityStarted();
                settings.AuthorityInitialized?.Invoke(this);
            }
            finally
            {
                m_engineMutationDepth--;
            }
        }

        public GameplaySceneInstance CreateScene(IODScene scene)
        {
            return CreateScene(scene, null);
        }

        internal void InitializeGameplayObjectFields(IGameplayObject gameplayObject)
        {
            if (gameplayObject == null)
            {
                throw new ArgumentNullException(nameof(gameplayObject));
            }

            InitializeGameplayObjectFields(gameplayObject.ObjectClass,
                (field, value) => gameplayObject[field] = value);
        }

        private void InitializeCachedGameplayObjectFields(GObjectID objectID, ODClassName className)
        {
            InitializeGameplayObjectFields(className,
                (field, value) => m_cacheManager.SetGameplayObjectCollection(objectID, field.Name, value));
        }

        private static void InitializeGameplayObjectFields(
            ODClassName className,
            Action<ODFieldMeta, object> setValue)
        {
            HashSet<ODClassName> visitedClasses = new HashSet<ODClassName>();
            HashSet<ODFieldName> initializedFields = new HashSet<ODFieldName>();
            InitializeClass(className);

            void InitializeClass(ODClassName currentClass)
            {
                if (!visitedClasses.Add(currentClass))
                {
                    return;
                }

                ODClassMeta classMeta = GetODClassMeta(currentClass);
                foreach (ODClassName baseClass in classMeta.DirectBaseClasses)
                {
                    InitializeClass(baseClass);
                }

                foreach (ODFieldMeta field in classMeta.Fields)
                {
                    if (field.InitialValueFactory == null || !initializedFields.Add(field.Name))
                    {
                        continue;
                    }

                    object value = field.InitialValueFactory();
                    if (!(value is ICollectionMemoryObject))
                    {
                        throw new InvalidOperationException(
                            $"Initial value factory for collection field {field.Name} must create an {nameof(ICollectionMemoryObject)}");
                    }
                    setValue(field, value);
                }
            }
        }

        public TValue GetGameplayObjectValue<TValue, TObject>(TObject obj, ODFieldName index)
            where TObject : struct, IGameplayObjectOperator
        {
            return GetGameplayObjectValue<TValue>(obj.ObjectID, index);
        }

        public TValue GetGameplayObjectValue<TValue>(GObjectID id, ODFieldName index)
        {
            object result = GetGameplayObjectValue(id, index);
            if (result == null)
            {
                return default;
            }
            else
            {
                return (TValue)result;
            }
        }

        public object GetGameplayObjectValue(GObjectID id, ODFieldName index)
        {
            object val = null;
            bool valueCached = false;
            ODFieldMeta meta = index.Meta;

            if (val is IFieldObject)
            {
                throw new Exception($"Cannot Set/Get GameplayObjectValue on {nameof(IFieldObject)}s ({index}), please only use corresponding interfaces only");
            }

            if (m_cacheMode)
            {
                valueCached = m_cacheManager.TryGetGameplayObjectValue(id, index, out val);
            }
            if (!valueCached)
            {
                IGameplayObject gObject = GetGameplayObject(id, true);
                val = gObject[meta];
            }

            val = val ?? meta.DefaultObject;
            if (val != null)
            {
                Type fieldType = Nullable.GetUnderlyingType(meta.FieldType) ?? meta.FieldType;
                if (val.GetType() != fieldType)
                {
                    throw new Exception($"Type dismatch! Value type returned from {nameof(IGameplayObject)} is {val.GetType()}(Should be {meta.FieldType})");
                }
            }

            return val;
        }

        public bool DeleteGameplayObject<T>(T gameplayObject)
            where T : struct, IGameplayObjectOperator
        {
            if (!IsValid(gameplayObject))
            {
                return false;
            }

            return DeleteGameplayObject(gameplayObject.ObjectID);
        }

        public bool DeleteGameplayObject(GObjectID id)
        {
            return DeleteGameplayObjectCore(id, true);
        }

        internal bool DeleteEngineGameplayObject(GObjectID id)
        {
            return DeleteGameplayObjectCore(id, false);
        }

        private bool DeleteGameplayObjectCore(GObjectID id, bool checkExecution)
        {
            if (checkExecution)
                CheckDataModifiedOutsideExecution();

            using var loadedPartitions = LoadIncomingPartitionsForDeletion(id);

            IGameplayObject gObject = GetGameplayObject(id);
            if (checkExecution && gObject != null)
            {
                ODClassName objectClass = GetGameplayObjectClass(id);
                if (GetODClassMeta(objectClass).IsEngineManaged)
                    throw new InvalidOperationException($"{objectClass} instances are managed by GameplayMachine");
            }
            if (!ContainsGameplayObject(id) || !m_deletingGameplayObjects.Add(id))
            {
                return false;
            }

            try
            {
                RemoveIncomingGameplayReferences(id);
                m_referenceIndex.RemoveOwner(id);
                m_eventSystemManager.ClearEvent(id);

                if (m_cacheMode)
                {
                    // If this object dosen't exist
                    if (gObject == null && !m_cacheManager.GetGameplayObjectCreated(id))
                    {
                        return false;
                    }
                    m_cacheManager.DeleteGameplayObject(id);
                }
                else
                {
                    if (gObject == null)
                    {
                        return false;
                    }

                    if (!RemoveGameplayObject(id))
                    {
                        return false;
                    }
                }

                if (m_rootObjectID.Equals(id))
                {
                    SetRootObjectID(default, true);
                }

                BroadCastObjectLifeEvent(id, false);
                RemoveGameplayObjectNetworkMetadata(id);
                ForgetGameplayObjectPartition(id);

                return true;
            }
            finally
            {
                m_deletingGameplayObjects.Remove(id);
            }
        }

        public IGMCollection GetCollectionController(GObjectID id, ODFieldName index, bool forModification)
        {
            if (forModification)
            {
                CheckDataModifiedOutsideExecution();
            }

            ODFieldMeta meta = index.Meta;
            return (meta.DefaultObject as IFieldObject).GetController(GetGameplayObject(id), index);
        }

        public void SetGameplayObjectValue(GObjectID id, ODFieldName index, object val, bool settingDisplayerData = false)
        {
            CheckDataModifiedOutsideExecution(settingDisplayerData);

            ODFieldMeta fieldMeta = index.Meta;
            val = val ?? fieldMeta.DefaultObject;

            if (val is IFieldObject)
            {
                throw new Exception($"Cannot Set/Get GameplayObjectValue on {nameof(IFieldObject)}s ({index}), please only use corresponding interfaces only");
            }

            ValidateGameplayReferences(val);
            object oldVal = GetGameplayObjectValue(id, index);
            SetValueConsiderCache(id, fieldMeta, val);
            m_referenceIndex.ReplaceValue(this, new GameplayReferenceField(id, index), oldVal, val);
            NotifyFieldChangedEventIndexed(id, index, ObjectEventTypes.Changed, null, val, oldVal,
                referenceIndexUpdated: true);
        }

        public GMCore.Collections.ISetCollection<T> GetSetConsiderCache<T>(GObjectID id, ODFieldName fieldIndex, bool forModification)
        {
            if (forModification)
            {
                CheckDataModifiedOutsideExecution();
            }

            if (m_cacheMode)
            {
                Collections.SetCollection<T> setObject;
                if (m_cacheManager.GetOrCreateCollection(id, fieldIndex, out setObject))
                {
                    IGameplayObject gObject = m_objectManager.GetGameplayObject(id);
                    GMCore.Collections.ISetCollection<T> objectSet = gObject.GetSet<T>(fieldIndex);

                    foreach (T item in objectSet)
                    {
                        setObject.Add(item);
                    }

                    return setObject;
                }
                else
                {
                    return setObject;
                }
            }
            else
            {
                IGameplayObject gObject = m_objectManager.GetGameplayObject(id);
                return gObject.GetSet<T>(fieldIndex);
            }
        }

        public GMCore.Collections.IListCollection<T> GetListConsiderCache<T>(GObjectID id, ODFieldName fieldIndex, bool forModification)
        {
            if (forModification)
            {
                CheckDataModifiedOutsideExecution();
            }

            if (m_cacheMode)
            {
                Collections.ListCollection<T> listObject;
                if (m_cacheManager.GetOrCreateCollection(id, fieldIndex, out listObject))
                {
                    IGameplayObject gObject = m_objectManager.GetGameplayObject(id);
                    GMCore.Collections.IListCollection<T> objectSet = gObject.GetList<T>(fieldIndex);

                    foreach (T item in objectSet)
                    {
                        listObject.Add(item);
                    }

                    return listObject;
                }
                else
                {
                    return listObject;
                }
            }
            else
            {
                IGameplayObject gObject = m_objectManager.GetGameplayObject(id);
                return gObject.GetList<T>(fieldIndex);
            }
        }

        public GMCore.Collections.IMapCollection<K, V> GetMapConsiderCache<K, V>(GObjectID id, ODFieldName fieldIndex, bool forModification)
        {
            if (forModification)
            {
                CheckDataModifiedOutsideExecution();
            }

            if (m_cacheMode)
            {
                Collections.MapCollection<K, V> mapObject;
                if (m_cacheManager.GetOrCreateCollection(id, fieldIndex, out mapObject))
                {
                    IGameplayObject gObject = m_objectManager.GetGameplayObject(id);
                    GMCore.Collections.IMapCollection<K, V> objectSet = gObject.GetMap<K, V>(fieldIndex);

                    foreach (var item in objectSet)
                    {
                        mapObject.Add(item.Key, item.Value);
                    }

                    return mapObject;
                }
                else
                {
                    return mapObject;
                }
            }
            else
            {
                IGameplayObject gObject = m_objectManager.GetGameplayObject(id);
                return gObject.GetMap<K, V>(fieldIndex);
            }
        }

        //public object GetOrCreateCollectionConsiderCache<TCollection, TKey, TValue>(GObjectID id, ODFieldName fieldIndex)
        //    where TCollection : IGMCollection, new()
        //{
        //    CheckDataModifiedOutsideExecution();

        //    if (m_cacheMode)
        //    {
        //        object cacheValue;
        //        if (m_cacheManager.TryGetGameplayObjectValue(id, fieldIndex,  out cacheValue))
        //        {
        //            return (TCollection)cacheValue;
        //        }
        //        else
        //        {
        //            TCollection collectionForCache;
        //            IGameplayObject gObject = GetGameplayObject(id);
        //            if (gObject != null)
        //            {
        //                // Copy the collection to cache then return
        //                object gObjectValue = gObject[fieldIndex];
        //                if (gObjectValue != null)
        //                {
        //                    collectionForCache = (TCollection)((gObjectValue as IGMCollection).CopyTo());
        //                }
        //                else
        //                {
        //                    collectionForCache = new TCollection();
        //                }
        //            }
        //            else
        //            {
        //                collectionForCache = new TCollection();
        //            }

        //            m_cacheManager.SetGameplayObjectValue(id, fieldIndex, collectionForCache);
        //            return collectionForCache;
        //        }
        //    }
        //    else
        //    {
        //        IGameplayObject gObject = m_objectManager.GetGameplayObject(id);
        //        return gObject.GetSet<TKey>(fieldIndex);
        //    }
        //}

        // Please actually remove the objects (when objectEventType == Remove) before calling these
        public void NotifyFieldChangedEvent(GObjectID objectID, ODFieldName index, ObjectEventTypes objectEventType, object context, object newValue = null, object oldValue = null, bool disableEqualTest = false)
        {
            NotifyFieldChangedEventCore(objectID, index, objectEventType, context, newValue, oldValue,
                disableEqualTest, false);
        }

        internal void NotifyFieldChangedEventIndexed(GObjectID objectID, ODFieldName index,
            ObjectEventTypes objectEventType, object context, object newValue = null, object oldValue = null,
            bool disableEqualTest = false, bool referenceIndexUpdated = true)
        {
            NotifyFieldChangedEventCore(objectID, index, objectEventType, context, newValue, oldValue,
                disableEqualTest, true);
        }

        private void NotifyFieldChangedEventCore(GObjectID objectID, ODFieldName index,
            ObjectEventTypes objectEventType, object context, object newValue, object oldValue,
            bool disableEqualTest, bool referenceIndexUpdated)
        {
            if (!referenceIndexUpdated)
                RefreshGameplayReferences(objectID, index);
            bool valueChanged = objectEventType != ObjectEventTypes.ItemChanged && objectEventType != ObjectEventTypes.Changed ||
                disableEqualTest || !Equals(newValue, oldValue);
            if (valueChanged)
                MarkPartitionDirty(objectID);
            if (valueChanged)
                RecordGameplaySpatialFieldChange(objectID, index);
            if (valueChanged)
                RecordNetworkFieldChange(objectID, index, objectEventType, context, newValue, oldValue);
            if (ShouldStoreEvent)
            {
                m_delayedEvents.StoreObjectEvent(objectID, index, objectEventType, context, oldValue, disableEqualTest: disableEqualTest);
            }
            else
            {
                if (objectEventType == ObjectEventTypes.ItemChanged || objectEventType == ObjectEventTypes.Changed)
                {
                    if (valueChanged)
                    {
                        BroadCastFieldChangedEvent(objectID, index, objectEventType, context);
                    }
                }
                else
                {
                    BroadCastFieldChangedEvent(objectID, index, objectEventType, context);
                }
            }
            if (valueChanged)
                NotifyDebugObjectChanged(objectID);
        }

        public void NotifyFieldChangedEvents(GObjectID objectID, ODFieldName index, IEnumerable addedValues, IEnumerable removedValues, IEnumerable changedValues = null)
        {
            NotifyFieldChangedEventsCore(objectID, index, addedValues, removedValues, changedValues, false);
        }

        internal void NotifyFieldChangedEventsIndexed(GObjectID objectID, ODFieldName index,
            IEnumerable addedValues, IEnumerable removedValues, IEnumerable changedValues = null,
            bool referenceIndexUpdated = true)
        {
            NotifyFieldChangedEventsCore(objectID, index, addedValues, removedValues, changedValues, true);
        }

        private void NotifyFieldChangedEventsCore(GObjectID objectID, ODFieldName index,
            IEnumerable addedValues, IEnumerable removedValues, IEnumerable changedValues,
            bool referenceIndexUpdated)
        {
            if (!referenceIndexUpdated)
                RefreshGameplayReferences(objectID, index);
            IEnumerable addedForNetwork = addedValues == null ? null : addedValues.Cast<object>().ToList();
            IEnumerable removedForNetwork = removedValues == null ? null : removedValues.Cast<object>().ToList();
            IEnumerable changedForNetwork = changedValues == null ? null : changedValues.Cast<object>().ToList();
            RecordGameplaySpatialFieldChange(objectID, index);
            RecordNetworkFieldChanges(objectID, index, addedForNetwork, removedForNetwork, changedForNetwork);
            MarkPartitionDirty(objectID);
            addedValues = addedForNetwork;
            removedValues = removedForNetwork;
            changedValues = changedForNetwork;
            if (ShouldStoreEvent)
            {
                if (addedValues != null)
                {
                    foreach (var item in addedValues)
                    {
                        m_delayedEvents.StoreObjectEvent(objectID, index, ObjectEventTypes.Add, item);
                    }
                }
                if (removedValues != null)
                {
                    foreach (var item in removedValues)
                    {
                        m_delayedEvents.StoreObjectEvent(objectID, index, ObjectEventTypes.Remove, item);
                    }
                }
                if (changedValues != null)
                {
                    foreach (var item in changedValues)
                    {
                        m_delayedEvents.StoreObjectEvent(objectID, index, ObjectEventTypes.ItemChanged, item);
                    }
                }
            }
            else
            {
                BroadCastFieldChangedEvent(objectID, index, addedValues, removedValues, changedValues);
            }
            NotifyDebugObjectChanged(objectID);
        }

        public ODClassName GetGameplayObjectClass(GObjectID id)
        {
            if (m_cacheMode)
            {
                ODClassName result;
                if (m_cacheManager.TryGetGameplayObjectClass(id, out result))
                {
                    return result;
                }
            }
            IGameplayObject gObject = GetGameplayObject(id);
            return gObject.ObjectClass;
        }

        public IEnumerable<CommonGameplayObject> GetAllGameplayObjects()
        {
            return (m_objectManager as GameplayObjectManager).GetAllGameplayObjects();
        }

        public bool ContainsGameplayObject(GObjectID id)
        {
            if (!id.IsValid)
            {
                return false;
            }

            if (m_cacheMode)
            {
                if (m_cacheManager.TryGetGameplayObjectDeleted(id, out bool deleted) && deleted)
                {
                    return false;
                }

                if (m_cacheManager.GetGameplayObjectCreated(id))
                {
                    return true;
                }
            }

            return m_objectManager.ContainsGameplayObject(id);
        }

        public static T ResolveGameplayObjectReference<T>(T value)
        {
            if (!GameplayObjectReferenceType<T>.IsReference)
            {
                return value;
            }

            if (!(value is IGameplayObjectOperator gameplayObject))
            {
                return value;
            }

            if (gameplayObject.Machine == null ||
                !gameplayObject.Machine.ContainsGameplayObject(gameplayObject.ObjectID))
            {
                return default;
            }

            return value;
        }

        private static class GameplayObjectReferenceType<T>
        {
            public static readonly bool IsReference = typeof(IGameplayObjectOperator).IsAssignableFrom(
                Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
        }

        public T GetGameplayObject<T>(GObjectID id)
            where T : struct, IGameplayObjectOperator, IODClass
        {
            IGameplayObject gameplayObject = GetGameplayObject(id, true);
            if (!CanCastTo(gameplayObject.ObjectClass, GetODClassName<T>()))
            {
                throw new InvalidCastException($"GameplayObject {id.ID} cannot be cast to {typeof(T)}");
            }

            T result = default;
            result.Machine = this;
            result.ObjectID = id;
            return result;
        }

        public T? Cast<T>(CommonGameplayObject obj)
            where T : struct, IGameplayObjectOperator, IODClass
        {
            IGameplayObject gameplayObject = CheckValid(obj);
            if (CanCastTo(gameplayObject.ObjectClass, GetODClassName<T>()))
            {
                return DirectCast<T>(obj);
            }

            return null;
        }

        public T? Cast<T>(IGameplayObjectOperator obj)
            where T : struct, IGameplayObjectOperator, IODClass
        {
            IGameplayObject gameplayObject = CheckValid(obj);
            if (CanCastTo(gameplayObject.ObjectClass, GetODClassName<T>()))
            {
                return DirectCast<T>(obj);
            }

            return null;
        }

        private T DirectCast<T>(IGameplayObjectOperator obj)
            where T : struct, IGameplayObjectOperator, IODClass
        {
            T t = default;
            t.Machine = obj.Machine;
            t.ObjectID = obj.ObjectID;
            return t;
        }

        public IGameplayObject CheckValid(IGameplayObjectOperator opertaorObj)
        {
            if (opertaorObj.Machine != this)
            {
                throw new Exception($"GameplayObject doesn't belong to this Machine");
            }

            IGameplayObject result;
            if (!m_objectManager.TryGetGameplayObject(opertaorObj.ObjectID, out result))
            {
                throw new NotExistException();
            }

            return result;
        }

        public IGameplayObject CheckValid(CommonGameplayObject commonObject)
        {
            if (commonObject.Machine != this)
            {
                throw new Exception($"GameplayObject doesn't belong to this Machine");
            }

            IGameplayObject result;
            if (!m_objectManager.TryGetGameplayObject(commonObject.ObjectID, out result))
            {
                throw new NotExistException();
            }

            return result;
        }

        public void CheckValid(IGameplayObject gameplayObject)
        {
            if (gameplayObject == null)
            {
                throw new Exception($"GameplayObject is null");
            }

            IGameplayObject result;
            if (!m_objectManager.TryGetGameplayObject(gameplayObject.ObjectID, out result))
            {
                throw new NotExistException();
            }

            if (gameplayObject != result)
            {
                throw new Exception($"GameplayObject doesn't belong to this Machine");
            }
        }

        private void SetValueConsiderCache(GObjectID id, ODFieldMeta meta, object val)
        {
            if (m_cacheMode)
            {
                m_cacheManager.SetGameplayObjectValue(id, meta.Name, val);
            }
            else
            {
                IGameplayObject gObject = m_objectManager.GetGameplayObject(id);
                gObject[meta] = val;
            }
        }

        private void BroadCastObjectLifeEvent(GObjectID objectID, bool create)
        {
            RecordGameplaySpatialObjectLife(objectID, create);
            RecordNetworkObjectLife(objectID, create);
            if (ShouldStoreEvent)
            {
                m_delayedEvents.StoreObjectLifeEvent(objectID, create);
            }
            else
            {
                if (create)
                {
                    AfterGameplayObjectCreatedParam createdParam = new AfterGameplayObjectCreatedParam();
                    createdParam.ObjectID = objectID;
                    BroadCastMachineEvent(createdParam);
                }
                else
                {
                    AfterGameplayObjectDeletedParam deletedParam = new AfterGameplayObjectDeletedParam();
                    deletedParam.ObjectID = objectID;
                    BroadCastMachineEvent(deletedParam);
                }
            }
            NotifyDebugObjectChanged(objectID, create
                ? GameplayDebugObjectChangeKind.Created
                : GameplayDebugObjectChangeKind.Deleted);
        }

        internal void NotifyGameplayObjectCreatedFromReplication(GObjectID objectID) =>
            BroadCastObjectLifeEvent(objectID, true);

        private void BroadCastFieldChangedEvent(GObjectID objectID, ODFieldName index,
            ObjectEventTypes objectEventType, object context = null,
            List<ODFieldName> delayedDrivenFields = null)
        {
            if (objectEventType != ObjectEventTypes.Changed)
            {
                CollectionItemChangeType changeType = objectEventType == ObjectEventTypes.Add
                    ? CollectionItemChangeType.Add
                    : objectEventType == ObjectEventTypes.Remove
                        ? CollectionItemChangeType.Remove
                        : CollectionItemChangeType.Changed;
                m_eventSystemManager.BroadCastEvtDelegate(objectID, index,
                    new CollectionItemChangedContext(changeType, context), ObjectEventTypes.ItemChanged);
            }

            m_eventSystemManager.BroadCastEvtDelegate(objectID, index, null, ObjectEventTypes.Changed);

            ODFieldMeta fieldMeta;
            if (TryGetDrivingOfMeta(index, out fieldMeta))
            {
                foreach (ODFieldName item in fieldMeta.DrivingOfs)
                {
                    if (CanCastTo(GetGameplayObjectClass(objectID), item.Meta.FromClass))
                        BroadCastOrDelayDrivenFieldChange(objectID, item, delayedDrivenFields);
                }
            }
        }

        private void BroadCastFieldChangedEvent(GObjectID objectID, ODFieldName index,
            IEnumerable adds, IEnumerable removes, IEnumerable changedValues = null,
            List<ODFieldName> delayedDrivenFields = null)
        {
            bool isCollection = adds != null || removes != null || changedValues != null;
            bool anyThingChanged = false;
            if (adds != null)
            {
                foreach (var item in adds)
                {
                    m_eventSystemManager.BroadCastEvtDelegate(objectID, index,
                        new CollectionItemChangedContext(CollectionItemChangeType.Add, item), ObjectEventTypes.ItemChanged);
                    anyThingChanged = true;

                }
            }
            if (removes != null)
            {
                foreach (var item in removes)
                {
                    m_eventSystemManager.BroadCastEvtDelegate(objectID, index,
                        new CollectionItemChangedContext(CollectionItemChangeType.Remove, item), ObjectEventTypes.ItemChanged);
                    anyThingChanged = true;
                }
            }
            if (changedValues != null)
            {
                foreach (var item in changedValues)
                {
                    m_eventSystemManager.BroadCastEvtDelegate(objectID, index,
                        new CollectionItemChangedContext(CollectionItemChangeType.Changed, item), ObjectEventTypes.ItemChanged);
                    anyThingChanged = true;
                }
            }

            if (isCollection && !anyThingChanged)
            {
                return;
            }

            m_eventSystemManager.BroadCastEvtDelegate(objectID, index, null, ObjectEventTypes.Changed);

            ODFieldMeta fieldMeta;
            if (TryGetDrivingOfMeta(index, out fieldMeta))
            {
                foreach (ODFieldName item in fieldMeta.DrivingOfs)
                {
                    if (CanCastTo(GetGameplayObjectClass(objectID), item.Meta.FromClass))
                        BroadCastOrDelayDrivenFieldChange(objectID, item, delayedDrivenFields);
                }
            }
        }

        private void BroadCastOrDelayDrivenFieldChange(GObjectID objectID, ODFieldName field,
            List<ODFieldName> delayedDrivenFields)
        {
            if (delayedDrivenFields == null)
            {
                m_eventSystemManager.BroadCastEvtDelegate(
                    objectID, field, null, ObjectEventTypes.Changed);
                return;
            }

            if (!delayedDrivenFields.Contains(field))
                delayedDrivenFields.Add(field);
        }

        private void BroadCastDelayedDrivenFieldChanges(GObjectID objectID,
            List<ODFieldName> delayedDrivenFields)
        {
            foreach (ODFieldName field in delayedDrivenFields)
            {
                m_eventSystemManager.BroadCastEvtDelegate(
                    objectID, field, null, ObjectEventTypes.Changed);
            }
        }

        #endregion

        #region Delayed Callback

        public void SetUseDelayedCallback(bool useDelayedCallback)
        {
            if (m_useDelayedCallback == useDelayedCallback)
            {
                return;
            }

            if (DuringExecution)
            {
                m_logger?.LogError(XLogger.LogVerbosity.Low, LogCatagories.GameplayMachine, $"Cannot SetUseDelayedCallback during machine execution");
                return;
            }

            m_useDelayedCallback = useDelayedCallback;
            if (m_useDelayedCallback)
            {
                m_delayedEvents = new DelayEvents(this);
            }
            else
            {
                m_delayedEvents = null;
            }
        }

        private void CheckDataModifiedOutsideExecution(bool settingDisplayerData = false)
        {
            if (m_applyingReplication)
            {
                return;
            }

            if (Role == GameplayMachineRole.ClientProxy && !settingDisplayerData && !DuringExecution)
            {
                throw new InvalidOperationException("ClientProxy GameplayMachines can only be modified by interfaces or Authority replication");
            }

            if (!ShouldModifyDataInsideExecutionOnly || m_engineMutationDepth > 0)
            {
                return;
            }

            if (!DuringExecution && !settingDisplayerData)
            {
                throw new DataModifiedOutsideExecutionException();
            }
        }

        #endregion

        public static bool IsValid<T>(T? gameplayObject)
            where T : struct, IGameplayObjectOperator
        {
            if (gameplayObject == null)
            {
                return false;
            }

            return IsValid(gameplayObject.Value);
        }

        public static bool IsValid<T>(T gameplayObject)
            where T : struct, IGameplayObjectOperator
        {
            if (gameplayObject.Machine == null || !gameplayObject.ObjectID.IsValid)
            {
                return false;
            }

            return true;
        }

        public static bool IsSame<T>(object lhs, T? rhs)
            where T : struct, IGameplayObjectOperator
        {
            return IsSame(lhs as T?, rhs);
        }

        public static bool IsSame<T>(T? lhs, object rhs)
            where T : struct, IGameplayObjectOperator
        {
            return IsSame(lhs, rhs as T?);
        }

        public static bool IsSame<T>(T? lhs, T? rhs)
            where T : struct, IGameplayObjectOperator
        {
            bool lhsValid = IsValid(lhs);
            bool rhsValid = IsValid(rhs);
            if (lhsValid != rhsValid)
            {
                return false;
            }

            if (!lhsValid && !rhsValid)
            {
                return true;
            }

            return lhs.Value.ObjectID.Equals(rhs.Value.ObjectID);
        }

        public static ODClassName GetODClassName<T>()
            where T : struct, IODClass
        {
            T t = default;
            return t.GetODClassName();
        }
    }
}
