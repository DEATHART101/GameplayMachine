using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using PlayerController = GMCore.StateSync.PlayerController;
using LockstepPlayerController = GMCore.Lockstep.PlayerController;

namespace GMCore
{
    public sealed class GameplayDebugOptions
    {
        public string Address = "127.0.0.1";
        public int Port = 7788;
        public string Token;
        public GameplayDebugAccess Access = GameplayDebugAccess.Control;
        public int MaxCommandsPerUpdate = 128;
    }

    internal sealed class GameplayDebugCommandMarker
    {
        public string Name;
        public override string ToString() => Name;
    }

    public partial class GameplayMachine
    {
        [NonSerialized] private GameplayDebugServer m_debugServer;

        public bool DebugServerRunning => m_debugServer?.IsRunning == true;
        public int DebugServerPort => m_debugServer?.Port ?? 0;

        public void StartDebugServer(GameplayDebugOptions options = null)
        {
            if (m_debugServer != null)
                throw new InvalidOperationException("The OD debug server is already running");
            m_debugServer = new GameplayDebugServer(this, options ?? new GameplayDebugOptions());
            m_debugServer.Start();
            if (Logger is GameplayLogger)
            {
                Logger.Log(XLogger.LogVerbosity.Common, LogCatagories.DebugServer,
                    $"Debug server listening on {(options?.Address ?? "127.0.0.1")}:{m_debugServer.Port}, access={(options?.Access ?? GameplayDebugAccess.Control)}");
            }
        }

        public void UpdateDebug()
        {
            m_debugServer?.Update();
        }

        public void StopDebugServer()
        {
            GameplayDebugServer server = m_debugServer;
            m_debugServer = null;
            if (server != null)
                if (Logger is GameplayLogger)
                {
                    Logger.Log(XLogger.LogVerbosity.Common, LogCatagories.DebugServer,
                        $"Debug server stopped on port {server.Port}");
                }
            server?.Dispose();
        }

        internal T ExecuteDebugMutation<T>(string name, Func<GameplayMachineProxy, T> action)
        {
            if (DuringExecution)
                throw new InvalidOperationException("A debug mutation cannot begin while another interface is executing");
            var marker = new GameplayDebugCommandMarker { Name = "Debug/" + name };
            OnEnterInterfaceExecution(marker);
            try
            {
                return action(Proxy);
            }
            finally
            {
                OnExitInterfaceExecution();
            }
        }

        internal void NotifyDebugObjectChanged(GObjectID objectID,
            GameplayDebugObjectChangeKind changeKind = GameplayDebugObjectChangeKind.FieldChanged)
        {
            m_debugServer?.NotifyObjectChanged(objectID, changeKind);
        }
    }

    internal sealed class GameplayDebugServer : IDisposable
    {
        private readonly GameplayMachine m_machine;
        private readonly GameplayDebugOptions m_options;
        private readonly ConcurrentQueue<Action> m_commands = new ConcurrentQueue<Action>();
        private readonly object m_connectionsLock = new object();
        private readonly List<GameplayDebugServerConnection> m_connections = new List<GameplayDebugServerConnection>();
        private TcpListener m_listener;
        private Thread m_acceptThread;
        private volatile bool m_running;

        private readonly Dictionary<ulong, ODDebugClassMeta> m_classesByID;
        private readonly Dictionary<ODClassName, ODDebugClassMeta> m_classesByName;
        private readonly Dictionary<ulong, ODDebugInterfaceMeta> m_interfacesByID;

        public bool IsRunning => m_running;
        public int Port => ((IPEndPoint)m_listener.LocalEndpoint).Port;

        public GameplayDebugServer(GameplayMachine machine, GameplayDebugOptions options)
        {
            m_machine = machine ?? throw new ArgumentNullException(nameof(machine));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            List<ODDebugClassMeta> classes = machine.Modules.OfType<IODDebugModule>()
                .SelectMany(item => item.GetAllODDebugClassMetas() ?? Enumerable.Empty<ODDebugClassMeta>()).ToList();
            m_classesByID = classes.ToDictionary(item => item.StableID);
            m_classesByName = classes.ToDictionary(item => item.ClassName);
            m_interfacesByID = machine.Modules.OfType<IODDebugModule>()
                .SelectMany(item => item.GetAllODDebugInterfaceMetas() ?? Enumerable.Empty<ODDebugInterfaceMeta>())
                .ToDictionary(item => item.StableID);
        }

        public void Start()
        {
            IPAddress address;
            if (!IPAddress.TryParse(m_options.Address, out address))
                address = Dns.GetHostAddresses(m_options.Address).First(item => item.AddressFamily == AddressFamily.InterNetwork);
            m_listener = new TcpListener(address, m_options.Port);
            m_listener.Start();
            m_running = true;
            m_machine.ExecutionTrace += OnExecutionTrace;
            m_acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "OD Debug Accept" };
            m_acceptThread.Start();
        }

        public void Update()
        {
            int remaining = Math.Max(1, m_options.MaxCommandsPerUpdate);
            while (remaining-- > 0 && m_commands.TryDequeue(out Action command))
            {
                try { command(); }
                catch { /* Each command is responsible for returning its own error. */ }
            }
            lock (m_connectionsLock)
                m_connections.RemoveAll(item => !item.IsRunning);
        }

        public void NotifyObjectChanged(GObjectID objectID, GameplayDebugObjectChangeKind changeKind)
        {
            Broadcast(GameplayDebugProtocol.Packet(GameplayDebugMessageType.ObjectChanged, 0, writer =>
            {
                writer.Write(objectID.ID);
                writer.Write((byte)changeKind);
            }));
        }

        private void AcceptLoop()
        {
            while (m_running)
            {
                try
                {
                    TcpClient client = m_listener.AcceptTcpClient();
                    var connection = new GameplayDebugServerConnection(client, this);
                    lock (m_connectionsLock) m_connections.Add(connection);
                    connection.Start();
                }
                catch when (!m_running) { }
                catch { Thread.Sleep(50); }
            }
        }

        internal void Receive(GameplayDebugServerConnection connection, byte[] packet)
        {
            GameplayDebugProtocol.ReadPacket(packet, (type, requestID, reader) =>
            {
                if (!connection.Authenticated && type != GameplayDebugMessageType.Hello)
                    throw new InvalidDataException("OD debug handshake is required");
                switch (type)
                {
                    case GameplayDebugMessageType.Hello:
                    {
                        string token = GameplayDebugProtocol.ReadNullableString(reader);
                        if (!string.Equals(token, m_options.Token, StringComparison.Ordinal))
                        {
                            connection.Respond(requestID, false, "OD debug token was rejected", null);
                            connection.Stop();
                            return;
                        }
                        connection.Authenticated = true;
                        EnqueueCommand(connection, requestID,
                            writer => GameplayDebugProtocol.WriteMachineInfo(writer, BuildMachineInfo()));
                        break;
                    }
                    case GameplayDebugMessageType.MachineInfo:
                        EnqueueCommand(connection, requestID,
                            writer => GameplayDebugProtocol.WriteMachineInfo(writer, BuildMachineInfo()));
                        break;
                    case GameplayDebugMessageType.Schema:
                        EnqueueCommand(connection, requestID, writer => WriteSchema(writer));
                        break;
                    case GameplayDebugMessageType.Objects:
                        EnqueueCommand(connection, requestID, writer => WriteObjects(writer));
                        break;
                    case GameplayDebugMessageType.ObjectSnapshot:
                    {
                        int objectID = reader.ReadInt32();
                        EnqueueCommand(connection, requestID, writer => WriteObjectSnapshot(writer, new GObjectID { ID = objectID }));
                        break;
                    }
                    case GameplayDebugMessageType.SetField:
                    {
                        int objectID = reader.ReadInt32();
                        ulong fieldID = reader.ReadUInt64();
                        GameplayDebugValue value = GameplayDebugProtocol.ReadValue(reader);
                        EnqueueControl(connection, requestID, () => SetField(new GObjectID { ID = objectID }, fieldID, value),
                            (writer, result) => writer.Write(result));
                        break;
                    }
                    case GameplayDebugMessageType.CreateObject:
                    {
                        ulong classID = reader.ReadUInt64();
                        EnqueueControl(connection, requestID, () => CreateObject(classID),
                            (writer, result) => writer.Write(result.ID));
                        break;
                    }
                    case GameplayDebugMessageType.DeleteObject:
                    {
                        int objectID = reader.ReadInt32();
                        EnqueueControl(connection, requestID, () => DeleteObject(new GObjectID { ID = objectID }),
                            (writer, result) => writer.Write(result));
                        break;
                    }
                    case GameplayDebugMessageType.InvokeInterface:
                    {
                        ulong interfaceID = reader.ReadUInt64();
                        int playerControllerID = reader.ReadInt32();
                        int count = GameplayDebugProtocol.ReadCount(reader);
                        var inputs = new Dictionary<string, GameplayDebugValue>(StringComparer.Ordinal);
                        for (int i = 0; i < count; i++) inputs.Add(reader.ReadString(), GameplayDebugProtocol.ReadValue(reader));
                        EnqueueControl(connection, requestID, () => Invoke(interfaceID, inputs, playerControllerID), (writer, result) =>
                        {
                            writer.Write(result.Succeeded);
                            GameplayDebugProtocol.WriteNullableString(writer, result.Error);
                            GameplayDebugProtocol.WriteValue(writer, result.Output);
                        }, allowClientProxy: true);
                        break;
                    }
                    default:
                        throw new InvalidDataException("Unsupported OD debug request " + type);
                }
            });
        }

        private void EnqueueCommand(GameplayDebugServerConnection connection, long requestID, Action<BinaryWriter> write)
        {
            m_commands.Enqueue(() =>
            {
                try { connection.Respond(requestID, true, null, write); }
                catch (Exception exception) { connection.Respond(requestID, false, exception.Message, null); }
            });
        }

        private void EnqueueControl<T>(GameplayDebugServerConnection connection, long requestID,
            Func<T> action, Action<BinaryWriter, T> write, bool allowClientProxy = false)
        {
            m_commands.Enqueue(() =>
            {
                try
                {
                    if (m_options.Access != GameplayDebugAccess.Control)
                        throw new InvalidOperationException("This OD debug endpoint is observe-only");
                    if (!allowClientProxy && m_machine.Role == GameplayMachineRole.ClientProxy)
                        throw new InvalidOperationException("GameplayObject mutation is only allowed on an Authority");
                    T result = action();
                    connection.Respond(requestID, true, null, writer => write(writer, result));
                }
                catch (Exception exception)
                {
                    connection.Respond(requestID, false, exception.Message, null);
                }
            });
        }

        private GameplayDebugMachineInfo BuildMachineInfo()
        {
            return new GameplayDebugMachineInfo
            {
                MachineKey = m_machine.MachineKey.ID,
                Access = m_options.Access,
                Role = m_machine.Role,
                NetworkState = m_machine.NetworkState,
                NetworkRevision = m_machine.NetworkRevision,
                NetworkSchemaID = m_machine.NetworkSchemaID,
                ResourceCatalogID = m_machine.ResourceCatalogID,
                DebugSchemaID = ComputeDebugSchemaID(),
                ObjectCount = m_machine.GetAllGameplayObjects().Count(),
                RootObjectID = m_machine.HasRoot ? m_machine.GetRoot().ObjectID.ID : 0,
                DuringExecution = m_machine.DuringExecution,
                HasRoutine = m_machine.HasRoutine,
                Modules = m_machine.Modules.Select(item => item.Name.Name).ToArray(),
            };
        }

        private ulong ComputeDebugSchemaID()
        {
            string signature = string.Join("|", m_machine.Modules.OfType<IODDebugModule>()
                .Select(item => item.DebugSchemaID.ToString()).OrderBy(item => item, StringComparer.Ordinal));
            return ODSaveHash.Compute("od-debug:2|" + signature);
        }

        private void WriteSchema(BinaryWriter writer)
        {
            writer.Write(m_classesByID.Count);
            foreach (ODDebugClassMeta item in m_classesByID.Values.OrderBy(item => item.StableName, StringComparer.Ordinal))
            {
                writer.Write(item.StableID);
                writer.Write(item.DisplayName ?? item.StableName ?? string.Empty);
                writer.Write(item.IsEngineManaged);
                writer.Write(item.AssignableClassIDs?.Count ?? 0);
                if (item.AssignableClassIDs != null)
                    foreach (ulong classID in item.AssignableClassIDs)
                        writer.Write(classID);
                writer.Write(item.Fields?.Count ?? 0);
                if (item.Fields != null)
                    foreach (ODDebugFieldMeta field in item.Fields)
                        GameplayDebugProtocol.WriteFieldInfo(writer, ToFieldInfo(field));
            }
            writer.Write(m_interfacesByID.Count);
            foreach (ODDebugInterfaceMeta item in m_interfacesByID.Values.OrderBy(item => item.StableName, StringComparer.Ordinal))
            {
                writer.Write(item.StableID);
                writer.Write(item.DisplayName ?? item.StableName ?? string.Empty);
                writer.Write((byte)item.RpcMode);
                writer.Write((byte)item.InterfaceType);
                writer.Write(item.IsRoutine);
                WriteInterfaceFields(writer, item.Inputs);
                WriteInterfaceFields(writer, item.Outputs);
            }
        }

        private static void WriteInterfaceFields(BinaryWriter writer, IReadOnlyList<ODDebugInterfaceFieldMeta> fields)
        {
            writer.Write(fields?.Count ?? 0);
            if (fields == null) return;
            foreach (ODDebugInterfaceFieldMeta field in fields)
            {
                GameplayDebugProtocol.WriteFieldInfo(writer, new GameplayDebugFieldInfo
                {
                    Name = field.DisplayName ?? field.Name,
                    TypeName = field.ValueType?.FullName ?? string.Empty,
                    KeyTypeName = field.KeyType?.FullName,
                    Container = field.Container,
                    IsGameplayObjectReference = field.IsGameplayObjectReference,
                    GameplayObjectClassID = field.GameplayObjectClassID,
                    IsResourceReference = field.IsResourceReference,
                    IsResourceKeyReference = field.IsResourceKeyReference,
                    ResourceOptions = ToResourceOptions(field.ResourceOptions),
                    ResourceKeyOptions = ToResourceOptions(field.ResourceKeyOptions),
                    DefaultValue = CreateDefaultValue(field.ValueType, field.Container,
                        field.IsGameplayObjectReference, field.IsResourceReference),
                });
            }
        }

        private void WriteObjects(BinaryWriter writer)
        {
            GameplayDebugObjectInfo[] objects = m_machine.GetAllGameplayObjects().Select(ToObjectInfo).ToArray();
            writer.Write(objects.Length);
            foreach (GameplayDebugObjectInfo item in objects) GameplayDebugProtocol.WriteObjectInfo(writer, item);
        }

        private void WriteObjectSnapshot(BinaryWriter writer, GObjectID objectID)
        {
            if (!m_machine.ContainsGameplayObject(objectID)) throw new KeyNotFoundException("GameplayObject does not exist");
            GameplayDebugObjectInfo info = ToObjectInfo(new CommonGameplayObject { Machine = m_machine, ObjectID = objectID });
            GameplayDebugProtocol.WriteObjectInfo(writer, info);
            ODDebugClassMeta classMeta = GetClass(m_machine.GetGameplayObjectClass(objectID));
            writer.Write(classMeta.Fields.Count);
            foreach (ODDebugFieldMeta field in classMeta.Fields)
            {
                writer.Write(field.StableID);
                GameplayDebugProtocol.WriteValue(writer, ReadField(field, objectID));
            }
        }

        private GameplayDebugValue ReadField(ODDebugFieldMeta field, GObjectID objectID)
        {
            object value = field.Read != null
                ? field.Read(m_machine, objectID)
                : field.Container == GameplayDebugContainerKind.Single
                    ? m_machine.GetGameplayObjectValue(objectID, field.FieldName)
                    : m_machine.GetCollectionController(objectID, field.FieldName, false);
            return GameplayDebugValueConverter.FromObject(value, field.ResourceOptions, field.ResourceKeyOptions);
        }

        private bool SetField(GObjectID objectID, ulong fieldID, GameplayDebugValue value)
        {
            if (!m_machine.ContainsGameplayObject(objectID)) throw new KeyNotFoundException("GameplayObject does not exist");
            ODDebugClassMeta classMeta = GetClass(m_machine.GetGameplayObjectClass(objectID));
            ODDebugFieldMeta field = classMeta.Fields.FirstOrDefault(item => item.StableID == fieldID)
                ?? throw new KeyNotFoundException("OD field does not exist on this class");
            if (field.IsDriven) throw new InvalidOperationException("Driven fields are read-only");
            m_machine.ExecuteDebugMutation("Set " + field.StableName, proxy =>
            {
                if (field.Container == GameplayDebugContainerKind.Single)
                {
                    object converted = GameplayDebugValueConverter.ToObject(value, field.ValueType, m_machine,
                        field.ResourceOptions, field.ResourceKeyOptions);
                    if (field.Write != null) field.Write(m_machine, objectID, converted);
                    else proxy.SetGameplayObjectValue(objectID, field.FieldName, converted);
                }
                else
                {
                    ReplaceCollection(objectID, field, value);
                }
                return true;
            });
            return true;
        }

        private GObjectID CreateObject(ulong classID)
        {
            if (!m_classesByID.TryGetValue(classID, out ODDebugClassMeta meta))
                throw new KeyNotFoundException("OD class does not exist");
            return m_machine.ExecuteDebugMutation("Create " + meta.StableName,
                proxy => proxy.CreateGameplayObject(meta.ClassName).ObjectID);
        }

        private bool DeleteObject(GObjectID objectID)
        {
            bool deleted = m_machine.ExecuteDebugMutation("Delete " + objectID.ID, proxy => proxy.DeleteGameplayObject(objectID));
            if (!deleted) throw new KeyNotFoundException("GameplayObject does not exist");
            return true;
        }

        private ODDebugInvocationResult Invoke(ulong interfaceID,
            IReadOnlyDictionary<string, GameplayDebugValue> inputs,
            int playerControllerID)
        {
            if (!m_interfacesByID.TryGetValue(interfaceID, out ODDebugInterfaceMeta meta))
                throw new KeyNotFoundException("OD interface does not exist");
            if (meta.Invoke == null) throw new InvalidOperationException("OD interface has no generated debug invoker");
            if (meta.InterfaceType != GameplayInterfaceType.PlayerInput)
                return m_machine.WithExecutionTraceOrigin("Debug", () => meta.Invoke(m_machine, inputs));

            GameplayRpcContext? previous;
            if (m_machine.SynchronizationMode == GameplaySynchronizationMode.Lockstep)
            {
                LockstepPlayerController playerController = playerControllerID == 0
                    ? m_machine.GetLocalLockstepPlayerController()
                    : m_machine.GetGameplayObject<LockstepPlayerController>(
                        new GObjectID { ID = playerControllerID });
                previous = m_machine.PushPlayerControllerContext(playerController);
            }
            else
            {
                PlayerController playerController = playerControllerID == 0
                    ? m_machine.GetLocalPlayerController()
                    : m_machine.GetGameplayObject<PlayerController>(new GObjectID { ID = playerControllerID });
                previous = m_machine.PushPlayerControllerContext(playerController);
            }
            try
            {
                return m_machine.WithExecutionTraceOrigin("Debug", () => meta.Invoke(m_machine, inputs));
            }
            finally
            {
                m_machine.RestoreRpcContext(previous);
            }
        }

        private void ReplaceCollection(GObjectID objectID, ODDebugFieldMeta field, GameplayDebugValue value)
        {
            object controller = m_machine.GetCollectionController(objectID, field.FieldName, true);
            var oldItems = ((IEnumerable)controller).Cast<object>().ToList();
            var added = new List<object>();
            var removed = new List<object>();
            var changed = new List<object>();
            MethodInfo clear = controller.GetType().GetMethod("Clear", Type.EmptyTypes);
            if (field.Container == GameplayDebugContainerKind.Map)
            {
                var oldEntries = oldItems.ToDictionary(ReadMapKey, ReadMapValue);
                var newEntries = value.Entries.ToDictionary(
                    entry => GameplayDebugValueConverter.ToObject(entry.Key, field.KeyType, m_machine,
                        field.ResourceKeyOptions, null),
                    entry => GameplayDebugValueConverter.ToObject(entry.Value, field.ValueType, m_machine,
                        field.ResourceOptions, null));
                added.AddRange(newEntries.Keys.Where(key => !oldEntries.ContainsKey(key)));
                removed.AddRange(oldEntries.Keys.Where(key => !newEntries.ContainsKey(key)));
                changed.AddRange(newEntries.Keys.Where(key => oldEntries.TryGetValue(key, out object oldValue) &&
                                                               !Equals(oldValue, newEntries[key])));

                clear?.Invoke(controller, null);
                MethodInfo add = controller.GetType().GetMethods().First(item => item.Name == "Add" && item.GetParameters().Length == 2);
                foreach (KeyValuePair<object, object> entry in newEntries)
                    add.Invoke(controller, new[] { entry.Key, entry.Value });
            }
            else
            {
                var newItems = value.Items
                    .Select(item => GameplayDebugValueConverter.ToObject(item, field.ValueType, m_machine,
                        field.ResourceOptions, null))
                    .ToList();
                clear?.Invoke(controller, null);
                MethodInfo add = controller.GetType().GetMethods().First(item => item.Name == "Add" && item.GetParameters().Length == 1);
                foreach (object item in newItems)
                    add.Invoke(controller, new[] { item });

                if (field.Container == GameplayDebugContainerKind.Set)
                {
                    added.AddRange(newItems.Where(item => !oldItems.Contains(item)));
                    removed.AddRange(oldItems.Where(item => !newItems.Contains(item)));
                }
                else if (!oldItems.SequenceEqual(newItems))
                {
                    m_machine.NotifyFieldChangedEvent(objectID, field.FieldName, ObjectEventTypes.Changed, null,
                        disableEqualTest: true);
                }
            }

            if (field.Container == GameplayDebugContainerKind.Set || field.Container == GameplayDebugContainerKind.Map)
            {
                if (added.Count > 0 || removed.Count > 0 || changed.Count > 0)
                    m_machine.NotifyFieldChangedEvents(objectID, field.FieldName, added, removed, changed);
            }
        }

        private static object ReadMapKey(object pair) =>
            pair.GetType().GetProperty("Key")?.GetValue(pair) ??
            pair.GetType().GetField("Key")?.GetValue(pair) ??
            throw new InvalidDataException("OD map entry has no Key member");

        private static object ReadMapValue(object pair) =>
            pair.GetType().GetProperty("Value")?.GetValue(pair) ??
            pair.GetType().GetField("Value")?.GetValue(pair);

        private GameplayDebugObjectInfo ToObjectInfo(CommonGameplayObject gameplayObject)
        {
            ODClassName className = gameplayObject.ObjectClass;
            ODDebugClassMeta meta = GetClass(className);
            return new GameplayDebugObjectInfo
            {
                ObjectID = gameplayObject.ObjectID.ID,
                ClassID = meta.StableID,
                ClassName = meta.DisplayName ?? meta.StableName,
                IsRoot = m_machine.HasRoot && m_machine.GetRoot().ObjectID.Equals(gameplayObject.ObjectID),
            };
        }

        private ODDebugClassMeta GetClass(ODClassName className)
        {
            if (!m_classesByName.TryGetValue(className, out ODDebugClassMeta meta))
                throw new KeyNotFoundException("No generated debug metadata exists for class " + className);
            return meta;
        }

        private static GameplayDebugFieldInfo ToFieldInfo(ODDebugFieldMeta field) => new GameplayDebugFieldInfo
        {
            StableID = field.StableID,
            Name = field.DisplayName ?? field.StableName,
            TypeName = field.ValueType?.FullName ?? string.Empty,
            KeyTypeName = field.KeyType?.FullName,
            Container = field.Container,
            IsDriven = field.IsDriven,
            ReplicationMode = field.ReplicationMode,
            IsGameplayObjectReference = field.IsGameplayObjectReference,
            GameplayObjectClassID = field.GameplayObjectClassID,
            IsResourceReference = field.IsResourceReference,
            IsResourceKeyReference = field.IsResourceKeyReference,
            ResourceOptions = ToResourceOptions(field.ResourceOptions),
            ResourceKeyOptions = ToResourceOptions(field.ResourceKeyOptions),
            DefaultValue = CreateDefaultValue(field.ValueType, field.Container,
                field.IsGameplayObjectReference, field.IsResourceReference),
        };

        private static List<GameplayDebugResourceOption> ToResourceOptions(IReadOnlyList<ODDebugResourceOption> options) =>
            options?.Select(item => new GameplayDebugResourceOption
            {
                ID = item.ID.ToString(),
                Name = item.Name,
            }).ToList() ?? new List<GameplayDebugResourceOption>();

        private static GameplayDebugValue CreateDefaultValue(Type valueType,
            GameplayDebugContainerKind container, bool isGameplayObjectReference, bool isResourceReference)
        {
            if (isGameplayObjectReference)
                return new GameplayDebugValue
                {
                    Kind = GameplayDebugValueKind.GameplayObject,
                    TypeName = valueType?.FullName,
                };
            if (isResourceReference && container == GameplayDebugContainerKind.Single)
                return new GameplayDebugValue
                {
                    Kind = GameplayDebugValueKind.Resource,
                    TypeName = valueType?.FullName,
                };
            if (container == GameplayDebugContainerKind.List || container == GameplayDebugContainerKind.Set)
                return new GameplayDebugValue
                {
                    Kind = GameplayDebugValueKind.Sequence,
                    TypeName = valueType?.FullName,
                };
            if (container == GameplayDebugContainerKind.Map)
                return new GameplayDebugValue
                {
                    Kind = GameplayDebugValueKind.Map,
                    TypeName = valueType?.FullName,
                };
            if (valueType == null)
                return GameplayDebugValue.Null();
            object value = valueType.IsValueType ? Activator.CreateInstance(valueType) : null;
            GameplayDebugValue result = GameplayDebugValueConverter.FromObject(value);
            result.TypeName = valueType.FullName ?? valueType.Name;
            return result;
        }

        private void OnExecutionTrace(GameplayExecutionTraceEvent traceEvent)
        {
            Broadcast(GameplayDebugProtocol.Packet(GameplayDebugMessageType.Trace, 0,
                writer => GameplayDebugProtocol.WriteTrace(writer, traceEvent)));
        }

        private void Broadcast(byte[] packet)
        {
            lock (m_connectionsLock)
                foreach (GameplayDebugServerConnection connection in m_connections.Where(item => item.Authenticated).ToArray())
                    connection.Send(packet);
        }

        public void Dispose()
        {
            m_running = false;
            m_machine.ExecutionTrace -= OnExecutionTrace;
            try { m_listener?.Stop(); } catch { }
            if (m_acceptThread != null && Thread.CurrentThread != m_acceptThread)
                m_acceptThread.Join(1000);
            lock (m_connectionsLock)
            {
                foreach (GameplayDebugServerConnection connection in m_connections.ToArray()) connection.Dispose();
                m_connections.Clear();
            }
        }
    }

    internal sealed class GameplayDebugServerConnection : IDisposable
    {
        private readonly TcpClient m_client;
        private readonly GameplayDebugServer m_server;
        private readonly ConcurrentQueue<byte[]> m_outgoing = new ConcurrentQueue<byte[]>();
        private readonly AutoResetEvent m_sendSignal = new AutoResetEvent(false);
        private Thread m_receiveThread;
        private Thread m_sendThread;
        private volatile bool m_running;

        public bool Authenticated;
        public bool IsRunning => m_running;

        public GameplayDebugServerConnection(TcpClient client, GameplayDebugServer server)
        {
            m_client = client;
            m_server = server;
            m_client.NoDelay = true;
        }

        public void Start()
        {
            m_running = true;
            m_receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "OD Debug Receive" };
            m_sendThread = new Thread(SendLoop) { IsBackground = true, Name = "OD Debug Send" };
            m_receiveThread.Start();
            m_sendThread.Start();
        }

        public void Respond(long requestID, bool success, string error, Action<BinaryWriter> body)
        {
            Send(GameplayDebugProtocol.Packet(GameplayDebugMessageType.Response, requestID, writer =>
            {
                writer.Write(success);
                GameplayDebugProtocol.WriteNullableString(writer, error);
                if (success) body?.Invoke(writer);
            }));
        }

        public void Send(byte[] packet)
        {
            if (!m_running) return;
            m_outgoing.Enqueue(packet);
            m_sendSignal.Set();
        }

        private void ReceiveLoop()
        {
            try
            {
                NetworkStream stream = m_client.GetStream();
                while (m_running)
                {
                    if (!GameplayDebugProtocol.TryReadFrame(stream, out byte[] packet))
                        break;
                    m_server.Receive(this, packet);
                }
            }
            catch { }
            finally { Stop(); }
        }

        private void SendLoop()
        {
            try
            {
                NetworkStream stream = m_client.GetStream();
                while (m_running)
                {
                    if (!m_outgoing.TryDequeue(out byte[] packet))
                    {
                        m_sendSignal.WaitOne(100);
                        continue;
                    }
                    GameplayDebugProtocol.WriteFrame(stream, packet);
                }
            }
            catch { Stop(); }
        }

        public void Stop()
        {
            m_running = false;
            try { m_sendSignal.Set(); } catch (ObjectDisposedException) { }
            try { m_client.Close(); } catch { }
        }

        public void Dispose()
        {
            Stop();
            if (m_receiveThread != null && Thread.CurrentThread != m_receiveThread)
                m_receiveThread.Join(1000);
            if (m_sendThread != null && Thread.CurrentThread != m_sendThread)
                m_sendThread.Join(1000);
            m_sendSignal.Dispose();
        }
    }
}
