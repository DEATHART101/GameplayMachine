#nullable enable

using System.Globalization;
using System.Net;
using GMCore;
using global::Godot;
using GodotArray = Godot.Collections.Array;
using GodotDictionary = Godot.Collections.Dictionary;
using ReturnableHandle = XLifetimeObject.IReturnableHandle;

namespace ODStudio.Godot.Runtime;

public enum GameplayMachineStartupMode
{
    Offline,
    Authority,
    ClientProxy,
}

public enum GameplayMachineTransport
{
    Udp,
    Tcp,
}

public interface IGameplayMachineRunner
{
    GameplayMachine? Machine { get; }
    event Action? Started;
    event Action? Stopping;
}

public abstract class GameplayMachineRunnerCore : IGameplayMachineRunner
{
    public event Action? MachineStarted;
    public event Action? MachineStopped;
    public event Action? MachineStopping;
    public event Action<string>? MachineFailed;
    public event Action<string, string>? NetworkStateChanged;
    public event Action<int, string>? GameplayObjectCreated;
    public event Action<int>? GameplayObjectDeleted;
    public event Action<int, string, Variant>? GameplayObjectFieldChanged;

    event Action? IGameplayMachineRunner.Started
    {
        add => MachineStarted += value;
        remove => MachineStarted -= value;
    }

    event Action? IGameplayMachineRunner.Stopping
    {
        add => MachineStopping += value;
        remove => MachineStopping -= value;
    }

    public bool StartOnReady { get; set; } = true;
    public GameplayMachineStartupMode StartupMode { get; set; } = GameplayMachineStartupMode.Offline;
    public bool ModifyDataInsideExecutionOnly { get; set; } = true;
    public bool UseDelayedCallbacks { get; set; } = true;

    public bool EnableSave { get; set; } = true;
    public bool LoadExistingSave { get; set; } = true;
    public string SaveFileName { get; set; } = "game.sav";
    public bool UsePartitionedSave { get; set; }
    public bool EmitObjectFieldSignals { get; set; }

    public GameplayMachineTransport NetworkTransport { get; set; } = GameplayMachineTransport.Udp;
    public string AuthorityAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 7777;

    public int LockstepPlayerCount { get; set; } = 2;
    public int RequestedSlot { get; set; }
    public string LockstepRecordingDirectory { get; set; } = string.Empty;

    public bool EnableDebugServer { get; set; }
    public int DebugPort { get; set; } = 7788;

    private readonly Dictionary<int, List<ReturnableHandle>> _fieldBindings = new();
    private readonly List<ReturnableHandle> _machineBindings = new();
    private MachineKey _machineKey;
    private GameplayNetworkState _previousNetworkState = GameplayNetworkState.Disabled;
    private FileGameplayPartitionStore? _partitionStore;
    private GameplayLockstepRelayServer? _lockstepRelay;
    private GameplayMachineSettings? _settings;
    private IDisposable? _ownedLogger;
    private readonly string _runnerName;
    private readonly int _instanceId;

    protected GameplayMachineRunnerCore(string runnerName, int instanceId)
    {
        _runnerName = runnerName;
        _instanceId = instanceId;
    }

    public GameplayMachine? Machine { get; private set; }

    public bool IsRunning() => Machine != null;

    public bool IsReady() => Machine != null &&
        (StartupMode == GameplayMachineStartupMode.Offline &&
         _settings?.SynchronizationMode != GameplaySynchronizationMode.Lockstep ||
         Machine.NetworkState == GameplayNetworkState.Ready);

    public string GetMachineRole() => Machine?.Role.ToString() ?? "Disabled";

    public string GetNetworkState() => Machine?.NetworkState.ToString() ?? "Disabled";

    public int GetRootId() => Machine?.HasRoot == true ? Machine.GetRoot().ObjectID.ID : 0;

    public int GetLocalPlayerControllerId()
    {
        if (Machine == null)
            return 0;

        return Machine.SynchronizationMode == GameplaySynchronizationMode.Lockstep
            ? Machine.LocalLockstepPlayerController?.ObjectID.ID ?? 0
            : Machine.LocalPlayerController?.ObjectID.ID ?? 0;
    }

    public void Ready()
    {
        if (StartOnReady)
            StartMachine();
    }

    public void Process(double delta)
    {
        if (Machine == null)
            return;

        try
        {
            _lockstepRelay?.Update();
            Machine.Update(delta);
            _lockstepRelay?.Update();
            PublishNetworkStateIfChanged();
            OnMachineProcess(Machine, delta);
        }
        catch (Exception exception)
        {
            ReportFailure("GameplayMachine update failed", exception);
        }
    }

    public void ExitTree() => StopMachine();

    public void StartOffline()
    {
        if (Machine != null)
            return;
        StartupMode = GameplayMachineStartupMode.Offline;
        StartMachine();
    }

    public void StartHost(int port)
    {
        if (Machine != null)
            return;
        if (!TrySetConnectionPort(port))
            return;
        StartupMode = GameplayMachineStartupMode.Authority;
        StartMachine();
    }

    public void StartProxy(string address, int port)
    {
        if (Machine != null)
            return;
        if (string.IsNullOrWhiteSpace(address))
        {
            ReportFailure("GameplayMachine proxy configuration failed",
                new ArgumentException("Authority address cannot be empty", nameof(address)));
            return;
        }
        if (!TrySetConnectionPort(port))
            return;
        AuthorityAddress = address.Trim();
        StartupMode = GameplayMachineStartupMode.ClientProxy;
        StartMachine();
    }

    public void StartMachine()
    {
        if (Machine != null)
            return;

        try
        {
            IODModule[] modules = CreateModules() ?? Array.Empty<IODModule>();
            _machineKey = new MachineKey { ID = _instanceId };
            GameplayMachineSettings settings = CreateSettings();
            if (settings.Logger == null)
            {
                settings.Logger = new GodotGameplayLogger();
                _ownedLogger = settings.Logger as IDisposable;
            }
            _settings = settings;
            bool loadedSave = false;

            if (StartupMode == GameplayMachineStartupMode.ClientProxy &&
                settings.SynchronizationMode != GameplaySynchronizationMode.Lockstep)
            {
                Machine = GameplayMachineBase.CreateClientProxy(_machineKey, settings, modules);
            }
            else if (EnableSave && LoadExistingSave && !UsePartitionedSave && File.Exists(GetSavePath()))
            {
                Machine = GameplayMachineBase.LoadGameplayMachine(
                    _machineKey, settings, File.ReadAllBytes(GetSavePath()), modules);
                loadedSave = true;
            }
            else if (EnableSave && LoadExistingSave && UsePartitionedSave && File.Exists(Path.Combine(GetPartitionSavePath(), "world.gmw")))
            {
                _partitionStore = new FileGameplayPartitionStore(GetPartitionSavePath());
                Machine = GameplayMachineBase.LoadPartitionedGameplayMachine(_machineKey, settings, _partitionStore, true, modules);
                loadedSave = true;
            }
            else
            {
                Machine = GameplayMachineBase.SpawnGameplayMachine(_machineKey, settings, modules);
            }

            AttachMachine(Machine);
            Machine.SetUseDelayedCallback(UseDelayedCallbacks);
            Machine.SetModifyDataInsideExecutionOnly(ModifyDataInsideExecutionOnly);
            if (EnableDebugServer)
                Machine.StartDebugServer(new GameplayDebugOptions { Port = DebugPort });
            StartNetworking(Machine);
            PublishNetworkStateIfChanged();
            OnMachineStarted(loadedSave);
            MachineStarted?.Invoke();
            GD.Print($"[GameplayMachineRunner:{_runnerName}] Started. Role={Machine.Role}, Objects={Machine.GetAllGameplayObjects().Count()}.");
        }
        catch (Exception exception)
        {
            CleanupMachine();
            ReportFailure("GameplayMachine start failed", exception);
        }
    }

    public void StopMachine()
    {
        if (Machine == null)
            return;
        if (EnableSave && Machine.Role == GameplayMachineRole.Authority)
            SaveMachine();
        CleanupMachine();
        MachineStopped?.Invoke();
    }

    public bool SaveMachine()
    {
        if (!EnableSave || Machine == null || Machine.Role != GameplayMachineRole.Authority)
            return false;

        try
        {
            if (UsePartitionedSave)
            {
                _partitionStore ??= new FileGameplayPartitionStore(GetPartitionSavePath());
                Machine.SavePartitionedWorld(_partitionStore);
                return true;
            }
            string path = GetSavePath();
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, GameplayMachineBase.SaveGameplayMachine(Machine));
            return true;
        }
        catch (Exception exception)
        {
            ReportFailure("GameplayMachine save failed", exception);
            return false;
        }
    }

    public bool HasSave()
    {
        return UsePartitionedSave
            ? File.Exists(Path.Combine(GetPartitionSavePath(), "world.gmw"))
            : File.Exists(GetSavePath());
    }

    public bool DeleteSave()
    {
        try
        {
            if (UsePartitionedSave)
            {
                string directory = GetPartitionSavePath();
                _partitionStore = null;
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
                return true;
            }
            string path = GetSavePath();
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch (Exception exception)
        {
            ReportFailure("GameplayMachine save deletion failed", exception);
            return false;
        }
    }

    public GodotArray GetGameplayObjects()
    {
        var result = new GodotArray();
        if (Machine == null)
            return result;
        foreach (CommonGameplayObject gameplayObject in Machine.GetAllGameplayObjects())
            result.Add(GetGameplayObject(gameplayObject.ObjectID.ID));
        return result;
    }

    public GodotDictionary GetGameplayObject(int objectId)
    {
        var result = new GodotDictionary();
        if (Machine == null)
            return result;

        var id = new GObjectID { ID = objectId };
        if (!Machine.ContainsGameplayObject(id))
            return result;

        ODDebugClassMeta? classMeta = FindClassMeta(id);
        result["id"] = objectId;
        result["class"] = classMeta?.DisplayName ?? Machine.GetGameplayObjectClass(id).ToString();
        var fields = new GodotDictionary();
        if (classMeta != null)
        {
            foreach (ODDebugFieldMeta field in classMeta.Fields)
            {
                try
                {
                    object? value = field.Read?.Invoke(Machine, id) ??
                                    Machine.GetGameplayObjectValue(id, field.FieldName);
                    fields[field.DisplayName ?? field.StableName] = ToVariant(
                        GameplayDebugValueConverter.FromObject(
                            value, field.ResourceOptions, field.ResourceKeyOptions));
                }
                catch (Exception exception)
                {
                    GD.PushWarning($"Could not read {classMeta.DisplayName}.{field.DisplayName}: {exception.Message}");
                    fields[field.DisplayName ?? field.StableName] = default(Variant);
                }
            }
        }
        result["fields"] = fields;
        return result;
    }

    public GodotArray GetInterfaces()
    {
        var result = new GodotArray();
        foreach (ODDebugInterfaceMeta metadata in GetInterfaceMetas())
        {
            var item = new GodotDictionary
            {
                ["name"] = metadata.DisplayName,
                ["rpc_mode"] = metadata.RpcMode.ToString(),
            };
            var inputs = new GodotArray();
            foreach (ODDebugInterfaceFieldMeta input in metadata.Inputs)
            {
                inputs.Add(new GodotDictionary
                {
                    ["name"] = input.DisplayName ?? input.Name,
                    ["type"] = input.ValueType?.Name ?? "Unknown",
                    ["gameplay_object"] = input.IsGameplayObjectReference,
                    ["resource"] = input.IsResourceReference,
                });
            }
            item["inputs"] = inputs;
            result.Add(item);
        }
        return result;
    }

    public GodotDictionary ExecuteInterface(string interfaceName, GodotDictionary inputs)
    {
        if (Machine == null)
            return InvocationResult(false, "GameplayMachine is not running", default);

        ODDebugInterfaceMeta? metadata = GetInterfaceMetas().FirstOrDefault(item =>
            string.Equals(item.DisplayName, interfaceName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.StableName, interfaceName, StringComparison.Ordinal));
        if (metadata?.Invoke == null)
            return InvocationResult(false, $"Unknown interface '{interfaceName}'", default);

        try
        {
            var converted = new Dictionary<string, GameplayDebugValue>(StringComparer.Ordinal);
            foreach (ODDebugInterfaceFieldMeta input in metadata.Inputs)
            {
                string displayName = input.DisplayName ?? input.Name;
                if (inputs.TryGetValue(displayName, out Variant value) ||
                    inputs.TryGetValue(input.Name, out value))
                {
                    converted[input.Name] = FromVariant(value, input);
                }
            }

            ODDebugInvocationResult invocation = metadata.Invoke(Machine, converted);
            return InvocationResult(invocation.Succeeded, invocation.Error, ToVariant(invocation.Output));
        }
        catch (Exception exception)
        {
            ReportFailure($"Interface '{interfaceName}' failed", exception);
            return InvocationResult(false, exception.GetBaseException().Message, default);
        }
    }

    protected abstract IODModule[] CreateModules();

    protected virtual void OnMachineStarted(bool loadedSave) { }
    protected virtual void OnMachineStopping() { }

    protected virtual void OnMachineProcess(GameplayMachine machine, double delta) { }

    protected virtual GameplayMachineSettings CreateSettings() => new();

    protected virtual GameplayNetworkOptions CreateNetworkOptions() => new();

    protected virtual GameplayLockstepOptions CreateLockstepOptions() => new();

    protected virtual string GetSavePath() => Path.Combine(
        ProjectSettings.GlobalizePath("user://"), SaveFileName);
    protected virtual string GetPartitionSavePath() => GetSavePath() + ".partitions";

    private bool TrySetConnectionPort(int port)
    {
        if (port is >= 1 and <= 65535)
        {
            Port = port;
            return true;
        }

        ReportFailure("GameplayMachine network configuration failed",
            new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 1 and 65535"));
        return false;
    }

    private void StartNetworking(GameplayMachine machine)
    {
        if (_settings?.SynchronizationMode == GameplaySynchronizationMode.Lockstep)
        {
            StartLockstepNetworking(machine);
            return;
        }

        switch (StartupMode)
        {
            case GameplayMachineStartupMode.Offline:
                return;
            case GameplayMachineStartupMode.Authority:
                machine.EnableNetworking(CreateServerTransport(), CreateNetworkOptions());
                return;
            case GameplayMachineStartupMode.ClientProxy:
                machine.ConnectNetworking(CreateClientTransport(), CreateNetworkOptions());
                return;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void StartLockstepNetworking(GameplayMachine machine)
    {
        GameplayLockstepOptions options = CreateLockstepOptions();
        options.Logger ??= machine.Logger;
        options.RequestedSlot = RequestedSlot;
        options.TickIntervalMilliseconds = _settings?.TickIntervalMilliseconds ?? options.TickIntervalMilliseconds;
        if (!string.IsNullOrWhiteSpace(LockstepRecordingDirectory))
            options.RecordingDirectory = ProjectSettings.GlobalizePath(LockstepRecordingDirectory);

        if (StartupMode == GameplayMachineStartupMode.Offline)
        {
            options.ExpectedPlayerCount = 1;
            InMemoryGameplayNetworkTransport.CreatePair(
                out InMemoryGameplayNetworkTransport relayTransport,
                out InMemoryGameplayNetworkTransport peerTransport);
            _lockstepRelay = new GameplayLockstepRelayServer(relayTransport, options);
            _lockstepRelay.Start();
            machine.ConnectLockstepNetworking(peerTransport, options);
            return;
        }

        if (StartupMode == GameplayMachineStartupMode.Authority)
        {
            if (LockstepPlayerCount <= 0)
                throw new InvalidOperationException("Lockstep player count must be greater than zero");
            options.ExpectedPlayerCount = LockstepPlayerCount;
            _lockstepRelay = new GameplayLockstepRelayServer(CreateServerTransport(), options);
            _lockstepRelay.Start();
            IGameplayNetworkTransport localPeer = NetworkTransport == GameplayMachineTransport.Udp
                ? LiteNetLibGameplayNetworkTransport.CreateClient("127.0.0.1", Port)
                : TcpGameplayNetworkTransport.CreateClient("127.0.0.1", Port);
            machine.ConnectLockstepNetworking(localPeer, options);
            return;
        }

        machine.ConnectLockstepNetworking(CreateClientTransport(), options);
    }

    private IGameplayNetworkTransport CreateServerTransport()
    {
        var endpoint = new IPEndPoint(IPAddress.Any, Port);
        return NetworkTransport == GameplayMachineTransport.Udp
            ? LiteNetLibGameplayNetworkTransport.CreateServer(endpoint)
            : TcpGameplayNetworkTransport.CreateServer(endpoint);
    }

    private IGameplayNetworkTransport CreateClientTransport() =>
        NetworkTransport == GameplayMachineTransport.Udp
            ? LiteNetLibGameplayNetworkTransport.CreateClient(AuthorityAddress, Port)
            : TcpGameplayNetworkTransport.CreateClient(AuthorityAddress, Port);

    private void AttachMachine(GameplayMachine machine)
    {
        _machineBindings.Add(machine.BindMachineEvent<AfterGameplayObjectCreatedParam>(OnGameplayObjectCreated));
        _machineBindings.Add(machine.BindMachineEvent<AfterGameplayObjectDeletedParam>(OnGameplayObjectDeleted));
        _machineBindings.Add(machine.BindMachineEvent<GameplayObjectAvailabilityChangedParam>(change =>
        {
            if (change.IsAvailable) AttachGameplayObject(change.ObjectID, true);
            else OnGameplayObjectDeleted(new AfterGameplayObjectDeletedParam { ObjectID = change.ObjectID });
        }));
        _machineBindings.Add(machine.BindMachineEvent<GameplayNetworkStateChangedParam>(change =>
        {
            string message = $"[GameplayMachineRunner:{_runnerName}] Network {change.OldState} -> {change.NewState}. {change.Reason}";
            if (change.NewState == GameplayNetworkState.Disabled && !string.IsNullOrEmpty(change.Reason)) GD.PushError(message);
            else GD.Print(message);
        }));
        foreach (CommonGameplayObject gameplayObject in machine.GetAllGameplayObjects().ToArray())
            AttachGameplayObject(gameplayObject.ObjectID, false);
    }

    private void OnGameplayObjectCreated(AfterGameplayObjectCreatedParam parameter) =>
        AttachGameplayObject(parameter.ObjectID, true);

    private void OnGameplayObjectDeleted(AfterGameplayObjectDeletedParam parameter)
    {
        ReleaseFieldBindings(parameter.ObjectID.ID);
        GameplayObjectDeleted?.Invoke(parameter.ObjectID.ID);
    }

    private void AttachGameplayObject(GObjectID objectId, bool emitCreated)
    {
        if (Machine == null || _fieldBindings.ContainsKey(objectId.ID))
            return;

        ODDebugClassMeta? classMeta = FindClassMeta(objectId);
        var handles = new List<ReturnableHandle>();
        if (classMeta != null && EmitObjectFieldSignals)
        {
            foreach (ODDebugFieldMeta field in classMeta.Fields)
            {
                ODDebugFieldMeta captured = field;
                handles.Add(new FieldChangeEventBinder
                {
                    Machine = Machine,
                    ObjectID = objectId,
                    FieldName = captured.FieldName,
                }.Bind(_ => EmitFieldChanged(objectId, captured)));
            }
        }
        _fieldBindings.Add(objectId.ID, handles);
        if (emitCreated)
            GameplayObjectCreated?.Invoke(objectId.ID,
                classMeta?.DisplayName ?? Machine.GetGameplayObjectClass(objectId).ToString());
    }

    private void EmitFieldChanged(GObjectID objectId, ODDebugFieldMeta field)
    {
        if (Machine == null || !Machine.ContainsGameplayObject(objectId))
            return;
        object? value = field.Read?.Invoke(Machine, objectId) ??
                        Machine.GetGameplayObjectValue(objectId, field.FieldName);
        GameplayObjectFieldChanged?.Invoke(objectId.ID,
            field.DisplayName ?? field.StableName,
            ToVariant(GameplayDebugValueConverter.FromObject(
                value, field.ResourceOptions, field.ResourceKeyOptions)));
    }

    private ODDebugClassMeta? FindClassMeta(GObjectID objectId)
    {
        if (Machine == null)
            return null;
        ODClassName className = Machine.GetGameplayObjectClass(objectId);
        return Machine.Modules.OfType<IODDebugModule>()
            .SelectMany(module => module.GetAllODDebugClassMetas() ?? Enumerable.Empty<ODDebugClassMeta>())
            .FirstOrDefault(metadata => metadata.ClassName.Equals(className));
    }

    private IEnumerable<ODDebugInterfaceMeta> GetInterfaceMetas() =>
        Machine?.Modules.OfType<IODDebugModule>()
            .SelectMany(module => module.GetAllODDebugInterfaceMetas() ?? Enumerable.Empty<ODDebugInterfaceMeta>())
            .OrderBy(metadata => metadata.DisplayName)
        ?? Enumerable.Empty<ODDebugInterfaceMeta>();

    private void PublishNetworkStateIfChanged()
    {
        GameplayNetworkState current = Machine?.NetworkState ?? GameplayNetworkState.Disabled;
        if (current == _previousNetworkState)
            return;
        GameplayNetworkState previous = _previousNetworkState;
        _previousNetworkState = current;
        NetworkStateChanged?.Invoke(previous.ToString(), current.ToString());
    }

    private void CleanupMachine()
    {
        GameplayMachine? machine = Machine;
        if (machine == null)
        {
            _ownedLogger?.Dispose();
            _ownedLogger = null;
            return;
        }
        MachineStopping?.Invoke();
        OnMachineStopping();
        foreach (int objectId in _fieldBindings.Keys.ToArray())
            ReleaseFieldBindings(objectId);
        foreach (ReturnableHandle binding in _machineBindings)
            binding.Return();
        _machineBindings.Clear();
        machine.StopDebugServer();
        machine.DisableNetworking();
        _lockstepRelay?.Dispose();
        _lockstepRelay = null;
        Machine = null;
        _settings = null;
        GameplayMachineBase.RemoveGameplayMachine(_machineKey);
        _previousNetworkState = GameplayNetworkState.Disabled;
        _ownedLogger?.Dispose();
        _ownedLogger = null;
    }

    private void ReleaseFieldBindings(int objectId)
    {
        if (!_fieldBindings.Remove(objectId, out List<ReturnableHandle>? handles))
            return;
        foreach (ReturnableHandle handle in handles)
            handle.Return();
    }

    private void ReportFailure(string operation, Exception exception)
    {
        string message = $"{operation}: {exception.GetBaseException().Message}";
        GD.PushError(message);
        MachineFailed?.Invoke(message);
    }

    private static GodotDictionary InvocationResult(bool succeeded, string? error, Variant output) => new()
    {
        ["succeeded"] = succeeded,
        ["error"] = error ?? string.Empty,
        ["output"] = output,
    };

    private static Variant ToVariant(GameplayDebugValue? value)
    {
        if (value == null || value.Kind == GameplayDebugValueKind.Null)
            return default;
        switch (value.Kind)
        {
            case GameplayDebugValueKind.Boolean:
                return string.Equals(value.Scalar, "true", StringComparison.OrdinalIgnoreCase);
            case GameplayDebugValueKind.Integer:
                return long.TryParse(value.Scalar, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer)
                    ? integer : 0L;
            case GameplayDebugValueKind.FloatingPoint:
            case GameplayDebugValueKind.Decimal:
                return double.TryParse(value.Scalar, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                    ? number : 0d;
            case GameplayDebugValueKind.GameplayObject:
                return value.ObjectID;
            case GameplayDebugValueKind.Sequence:
            {
                var result = new GodotArray();
                foreach (GameplayDebugValue item in value.Items)
                    result.Add(ToVariant(item));
                return result;
            }
            case GameplayDebugValueKind.Map:
            {
                var result = new GodotDictionary();
                foreach (GameplayDebugMapEntry entry in value.Entries)
                    result[ToVariant(entry.Key)] = ToVariant(entry.Value);
                return result;
            }
            case GameplayDebugValueKind.Object:
            {
                var result = new GodotDictionary();
                foreach (GameplayDebugNamedValue member in value.Members)
                    result[member.Name] = ToVariant(member.Value);
                return result;
            }
            default:
                return value.Scalar ?? string.Empty;
        }
    }

    private static GameplayDebugValue FromVariant(Variant value, ODDebugInterfaceFieldMeta metadata)
    {
        if (value.VariantType == Variant.Type.Nil)
            return GameplayDebugValue.Null(metadata.ValueType?.FullName);
        if (metadata.IsGameplayObjectReference)
        {
            return new GameplayDebugValue
            {
                Kind = GameplayDebugValueKind.GameplayObject,
                TypeName = metadata.ValueType?.FullName,
                ObjectID = checked((int)value.AsInt64()),
            };
        }
        if (metadata.IsResourceReference)
        {
            return new GameplayDebugValue
            {
                Kind = GameplayDebugValueKind.Resource,
                TypeName = metadata.ValueType?.FullName,
                Scalar = value.AsString(),
            };
        }
        if (metadata.Container == GameplayDebugContainerKind.Map)
        {
            var result = new GameplayDebugValue { Kind = GameplayDebugValueKind.Map };
            foreach (KeyValuePair<Variant, Variant> entry in value.AsGodotDictionary())
                result.Entries.Add(new GameplayDebugMapEntry
                {
                    Key = FromVariant(entry.Key),
                    Value = FromVariant(entry.Value),
                });
            return result;
        }
        if (metadata.Container != GameplayDebugContainerKind.Single)
        {
            var result = new GameplayDebugValue { Kind = GameplayDebugValueKind.Sequence };
            foreach (Variant item in value.AsGodotArray())
                result.Items.Add(FromVariant(item));
            return result;
        }
        return FromVariant(value);
    }

    private static GameplayDebugValue FromVariant(Variant value)
    {
        switch (value.VariantType)
        {
            case Variant.Type.Nil:
                return GameplayDebugValue.Null();
            case Variant.Type.Bool:
                return Scalar(GameplayDebugValueKind.Boolean, value.AsBool() ? "true" : "false");
            case Variant.Type.Int:
                return Scalar(GameplayDebugValueKind.Integer,
                    value.AsInt64().ToString(CultureInfo.InvariantCulture));
            case Variant.Type.Float:
                return Scalar(GameplayDebugValueKind.FloatingPoint,
                    value.AsDouble().ToString(CultureInfo.InvariantCulture));
            case Variant.Type.Array:
            {
                var result = new GameplayDebugValue { Kind = GameplayDebugValueKind.Sequence };
                foreach (Variant item in value.AsGodotArray())
                    result.Items.Add(FromVariant(item));
                return result;
            }
            case Variant.Type.Dictionary:
            {
                var result = new GameplayDebugValue { Kind = GameplayDebugValueKind.Object };
                foreach (KeyValuePair<Variant, Variant> entry in value.AsGodotDictionary())
                    result.Members.Add(new GameplayDebugNamedValue
                    {
                        Name = entry.Key.AsString(),
                        Value = FromVariant(entry.Value),
                    });
                return result;
            }
            default:
                return Scalar(GameplayDebugValueKind.String, value.AsString());
        }
    }

    private static GameplayDebugValue Scalar(GameplayDebugValueKind kind, string value) => new()
    {
        Kind = kind,
        Scalar = value,
    };
}
