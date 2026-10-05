using System.Net;
using System.Reflection;
using GMCore;

namespace ODStudio.Editor;

internal static class OdRunnerHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        GameplayLogger? logger = null;
        try
        {
            Dictionary<string, string> options = ParseArguments(args);
            string assemblyPath = Required(options, "assembly");
            RunnerMode mode = Enum.Parse<RunnerMode>(Required(options, "mode"), true);
            string address = Required(options, "address");
            int port = int.Parse(Required(options, "port"));
            int requestedSlot = int.Parse(Required(options, "requested-slot"));
            if (requestedSlot < 0)
                throw new ArgumentOutOfRangeException(nameof(requestedSlot),
                    "Requested slot must be zero or greater.");
            int lockstepPlayerCount = int.Parse(Required(options, "lockstep-player-count"));
            if (lockstepPlayerCount is < 1 or > 64)
                throw new ArgumentOutOfRangeException(nameof(lockstepPlayerCount),
                    "Lockstep player count must be between 1 and 64.");
            GameplayLockstepTickMode lockstepTickMode = Enum.Parse<GameplayLockstepTickMode>(
                Required(options, "lockstep-tick-mode"), true);
            int debugPort = int.Parse(Required(options, "debug-port"));
            string debugToken = Required(options, "debug-token");
            logger = new GameplayLogger(new GameplayLoggerOptions
            {
                DirectoryPath = Required(options, "log-directory"),
                FilePrefix = "editor-runner",
            });

            Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
            Type runtimeType = assembly.GetTypes().Single(type =>
                !type.IsAbstract && typeof(IODGameplayRuntime).IsAssignableFrom(type));
            var runtime = (IODGameplayRuntime)Activator.CreateInstance(runtimeType)!;
            GameplayMachineSettings settings = runtime.CreateSettings();
            settings.Logger = logger;
            IODModule[] modules = runtime.CreateModules().ToArray();
            GameplayMachine machine = mode == RunnerMode.ClientProxy &&
                                      settings.SynchronizationMode != GameplaySynchronizationMode.Lockstep
                ? GameplayMachineBase.CreateClientProxy(null, settings, modules)
                : GameplayMachineBase.SpawnGameplayMachine(null, settings, modules);
            machine.SetUseDelayedCallback(true);
            GameplayLockstepRelayServer? lockstepRelay = null;
            if (settings.SynchronizationMode == GameplaySynchronizationMode.Lockstep)
            {
                var lockstepOptions = new GameplayLockstepOptions
                {
                    ExpectedPlayerCount = mode == RunnerMode.AuthorityHost ? lockstepPlayerCount : 1,
                    RequestedSlot = requestedSlot,
                    TickIntervalMilliseconds = settings.TickIntervalMilliseconds > 0
                        ? settings.TickIntervalMilliseconds
                        : 50,
                    TickMode = lockstepTickMode,
                    InputDelayTicks = lockstepTickMode == GameplayLockstepTickMode.InputDriven ? 0 : 2,
                    Logger = logger,
                };
                IGameplayNetworkTransport peerTransport;
                if (mode == RunnerMode.AuthorityHost)
                {
                    if (requestedSlot >= lockstepPlayerCount)
                        throw new ArgumentOutOfRangeException(nameof(requestedSlot),
                            "Requested slot must be less than the Lockstep player count.");
                    IPAddress listenAddress = ResolveIPv4(address);
                    IGameplayNetworkTransport relayTransport = LiteNetLibGameplayNetworkTransport.CreateServer(
                        new IPEndPoint(listenAddress, port));
                    string connectAddress = listenAddress.Equals(IPAddress.Any)
                        ? IPAddress.Loopback.ToString()
                        : listenAddress.ToString();
                    peerTransport = LiteNetLibGameplayNetworkTransport.CreateClient(connectAddress, port);
                    logger.Important(LogCatagories.Runner,
                        $"Editor Lockstep relay listening on {listenAddress}:{port}; expected players={lockstepPlayerCount}");
                    lockstepRelay = new GameplayLockstepRelayServer(relayTransport, lockstepOptions);
                    lockstepRelay.Start();
                }
                else if (mode == RunnerMode.ClientProxy)
                {
                    peerTransport = LiteNetLibGameplayNetworkTransport.CreateClient(address, port);
                    logger.Important(LogCatagories.Runner,
                        $"Editor Lockstep peer connecting to relay {address}:{port}");
                }
                else
                {
                    InMemoryGameplayNetworkTransport.CreatePair(
                        out InMemoryGameplayNetworkTransport relayTransport,
                        out InMemoryGameplayNetworkTransport inMemoryPeer);
                    peerTransport = inMemoryPeer;
                    lockstepRelay = new GameplayLockstepRelayServer(relayTransport, lockstepOptions);
                    lockstepRelay.Start();
                }

                machine.ConnectLockstepNetworking(peerTransport, lockstepOptions);
            }
            else if (mode == RunnerMode.AuthorityHost)
            {
                LiteNetLibGameplayNetworkTransport transport =
                    LiteNetLibGameplayNetworkTransport.CreateServer(
                        new IPEndPoint(ResolveIPv4(address), port));
                machine.EnableNetworking(transport);
                logger.Important(LogCatagories.Runner,
                    $"Editor Authority host listening on {address}:{port}");
            }
            else if (mode == RunnerMode.ClientProxy)
            {
                machine.ConnectNetworking(LiteNetLibGameplayNetworkTransport.CreateClient(address, port));
                logger.Important(LogCatagories.Runner,
                    $"Editor ClientProxy connecting to Authority {address}:{port}");
            }

            machine.StartDebugServer(new GameplayDebugOptions
            {
                Address = "127.0.0.1",
                Port = debugPort,
                Token = debugToken,
                Access = GameplayDebugAccess.Control,
            });
            Console.WriteLine($"ODRUNNER_READY {machine.DebugServerPort}");
            Console.Out.Flush();
            logger.Important(LogCatagories.Runner,
                $"Editor runner ready. Mode={mode}, role={machine.Role}, debug port={machine.DebugServerPort}");

            try
            {
                while (true)
                {
                    lockstepRelay?.Update();
                    machine.UpdateNetwork();
                    await Task.Delay(16);
                }
            }
            finally
            {
                machine.StopDebugServer();
                machine.DisableNetworking();
                lockstepRelay?.Dispose();
            }
        }
        catch (Exception exception)
        {
            logger?.Critical(LogCatagories.Runner, exception);
            Console.Error.WriteLine(exception);
            Console.Error.Flush();
            return 1;
        }
        finally
        {
            logger?.Dispose();
        }
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("OD Runner arguments must use --name value pairs.");
            result[args[index][2..]] = args[index + 1];
        }
        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing --{name}.");

    private static IPAddress ResolveIPv4(string address) =>
        IPAddress.TryParse(address, out IPAddress? parsed)
            ? parsed
            : Dns.GetHostAddresses(address).First(item =>
                item.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
}
