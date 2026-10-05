using System.Diagnostics;
using System.Security;
using System.Text;
using ODStudio.Export;
using ODStudio.Model;
using ODStudio.Package;

namespace ODStudio.Generator;

public enum GameplayMachineBuildOutput
{
    StateSyncCommandLineGame,
    StateSyncExportedGame,
    LockstepRelayServer,
    LockstepCommandLineGame,
    LockstepExportedGame,
}

public enum GameplayMachineExportPlatform
{
    CSharp,
    Unity,
    Godot,
}

public sealed record GameplayMachineBuildResult(
    string OutputDirectory,
    IReadOnlyList<OdIssue> Issues,
    IReadOnlyList<string> Files)
{
    public bool Succeeded => Issues.All(item => item.Severity != OdIssueSeverity.Error);
}

public static class GameplayMachineBuildPublisher
{
    public static IReadOnlyList<GameplayMachineBuildOutput> OutputsFor(OdNetworkMode mode) =>
        mode == OdNetworkMode.Lockstep
            ? [GameplayMachineBuildOutput.LockstepRelayServer,
                GameplayMachineBuildOutput.LockstepCommandLineGame,
                GameplayMachineBuildOutput.LockstepExportedGame]
            : [GameplayMachineBuildOutput.StateSyncCommandLineGame,
                GameplayMachineBuildOutput.StateSyncExportedGame];

    public static async Task<GameplayMachineBuildResult> PublishAsync(
        OdProject project,
        GameplayMachineBuildOutput output,
        string outputDirectory,
        string referenceDirectory,
        IOdExportTarget? exportedTarget = null,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        progress?.Report($"Validating {project.Name}...");
        ValidateOutput(project.Runtime.NetworkMode, output);
        IReadOnlyList<OdIssue> issues = OdProjectValidator.Validate(project);
        if (issues.Any(item => item.Severity == OdIssueSeverity.Error))
        {
            progress?.Report("Validation failed.");
            return new GameplayMachineBuildResult(outputDirectory, issues, Array.Empty<string>());
        }

        string fullOutput = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutput);
        progress?.Report($"Output: {fullOutput}");
        if (output is GameplayMachineBuildOutput.StateSyncExportedGame or
            GameplayMachineBuildOutput.LockstepExportedGame)
        {
            exportedTarget ??= new PlainCSharpExportTarget();
            return await ExportGameAsync(project, fullOutput, exportedTarget, progress, cancellationToken);
        }

        string temporaryRoot = Path.Combine(Path.GetTempPath(), "GameplayMachineEditor", "Build",
            project.Id.ToString("N"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            if (output != GameplayMachineBuildOutput.LockstepRelayServer)
            {
                progress?.Report("Generating GameplayMachine source...");
                OdCodeGenerationResult generated = await OdCSharpGenerator.GenerateAsync(
                    project, temporaryRoot, cancellationToken);
                if (!generated.Succeeded)
                {
                    progress?.Report("Source generation failed.");
                    return new GameplayMachineBuildResult(fullOutput, generated.Issues, Array.Empty<string>());
                }
                progress?.Report($"Generated {generated.Files.Count} source files.");
            }

            progress?.Report("Preparing build project...");
            await File.WriteAllTextAsync(Path.Combine(temporaryRoot, "Program.cs"),
                ProgramSource(project, output), new UTF8Encoding(false), cancellationToken);
            string projectFile = Path.Combine(temporaryRoot, "GameplayMachine.Game.csproj");
            await File.WriteAllTextAsync(projectFile, ProjectFile(project.Name, referenceDirectory),
                new UTF8Encoding(false), cancellationToken);
            progress?.Report("Publishing Release build...");
            await RunAsync("dotnet", ["publish", projectFile, "-c", "Release", "-o", fullOutput,
                "--nologo", "-v:minimal"], temporaryRoot, progress, cancellationToken);
            string[] files = Directory.EnumerateFiles(fullOutput, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(fullOutput, path)).ToArray();
            progress?.Report($"Published {files.Length} files.");
            return new GameplayMachineBuildResult(fullOutput, issues, files);
        }
        finally
        {
            try { if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<GameplayMachineBuildResult> ExportGameAsync(
        OdProject project,
        string outputDirectory,
        IOdExportTarget target,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "GameplayMachineEditor", "Export",
            project.Id.ToString("N"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        string packagePath = Path.Combine(temporaryDirectory, "game.odpkg");
        try
        {
            progress?.Report("Building OD package...");
            OdPackageBuildResult package = await OdPackageCompiler.BuildAsync(project, packagePath, cancellationToken);
            if (package.Size == 0)
            {
                progress?.Report("OD package build failed.");
                return new GameplayMachineBuildResult(outputDirectory, package.Issues, Array.Empty<string>());
            }

            progress?.Report($"Exporting {target.DisplayName} runtime...");
            var profile = new OdExportProfile { Name = "build", Target = target.Id };
            OdExportResult exported = await OdExportPipeline.ExportAsync(target,
                new OdExportRequest(packagePath, outputDirectory, profile), cancellationToken);
            if (!exported.Succeeded)
            {
                progress?.Report("Runtime export failed.");
                return new GameplayMachineBuildResult(outputDirectory, exported.Plan.Issues, Array.Empty<string>());
            }

            if (target is GodotExportTarget)
            {
                string? godotProject = GodotExportTarget.FindGodotProjectFile(outputDirectory);
                if (godotProject is null)
                {
                    progress?.Report(
                        "Godot C# build skipped: the export directory is not inside a Godot project.");
                }
                else
                {
                    progress?.Report("Building Godot C# project...");
                    await RunAsync("dotnet",
                        ["build", godotProject, "--nologo", "-v:minimal"],
                        Path.GetDirectoryName(godotProject)!, progress, cancellationToken);
                    progress?.Report("Godot C# project built successfully.");
                }
            }

            progress?.Report("Writing integration guide...");
            string guide = Path.Combine(outputDirectory, "GAMEPLAY_MACHINE_INTEGRATION.md");
            await File.WriteAllTextAsync(guide, IntegrationGuide(project, target),
                new UTF8Encoding(false), cancellationToken);
            string[] files = exported.WrittenFiles.Append(Path.GetFileName(guide)).ToArray();
            progress?.Report($"Exported {files.Length} files.");
            return new GameplayMachineBuildResult(outputDirectory, exported.Plan.Issues, files);
        }
        finally
        {
            try { if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static string ProgramSource(OdProject project, GameplayMachineBuildOutput output) => output switch
    {
        GameplayMachineBuildOutput.StateSyncCommandLineGame => StateSyncProgram(),
        GameplayMachineBuildOutput.LockstepRelayServer => RelayProgram(project.Runtime.TickIntervalMilliseconds,
            project.Runtime.LockstepTickMode),
        GameplayMachineBuildOutput.LockstepCommandLineGame => LockstepGameProgram(project.Runtime.LockstepTickMode),
        _ => throw new ArgumentOutOfRangeException(nameof(output)),
    };

    private static void ValidateOutput(OdNetworkMode mode, GameplayMachineBuildOutput output)
    {
        if (!OutputsFor(mode).Contains(output))
            throw new InvalidOperationException($"Build output {output} is not valid for {mode} projects.");
    }

    private static string ProjectFile(string projectName, string referenceDirectory)
    {
        string references = string.Join(Environment.NewLine,
            Directory.EnumerateFiles(referenceDirectory, "*.dll")
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .Select((path, index) =>
                    $"    <Reference Include=\"GameplayMachineReference{index}\"><HintPath>{SecurityElement.Escape(path)}</HintPath><Private>true</Private></Reference>"));
        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
                <AssemblyName>{{SecurityElement.Escape(projectName)}}</AssemblyName>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
            {{references}}
              </ItemGroup>
            </Project>
            """;
    }

    private static async Task RunAsync(string fileName, IEnumerable<string> arguments,
        string workingDirectory, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        string[] argumentList = arguments.ToArray();
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in argumentList)
            info.ArgumentList.Add(argument);
        progress?.Report($"> {fileName} {string.Join(' ', argumentList)}");
        using Process process = Process.Start(info) ??
            throw new InvalidOperationException($"Could not start {fileName}.");
        var output = new StringBuilder();
        var error = new StringBuilder();
        Task outputTask = PumpOutputAsync(process.StandardOutput, output, progress, null);
        Task errorTask = PumpOutputAsync(process.StandardError, error, progress, "[stderr] ");
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(outputTask, errorTask);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) { }
            try { await Task.WhenAll(outputTask, errorTask); }
            catch (IOException) { }
            throw;
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException((output + Environment.NewLine + error).Trim());
    }

    private static async Task PumpOutputAsync(StreamReader reader, StringBuilder capture,
        IProgress<string>? progress, string? prefix)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            capture.AppendLine(line);
            progress?.Report((prefix ?? string.Empty) + line);
        }
    }

    private static string IntegrationGuide(OdProject project, IOdExportTarget target) => $$"""
        # {{project.Name}} exported GameplayMachine

        Platform: {{target.DisplayName}}

        Create the generated `{{OdProjectCodeManager.SafeProjectName(project)}}GameHost` during platform startup.
        Call `Update(deltaSeconds)` exactly once from the game engine update loop. This pumps fixed gameplay ticks,
        networking, partition interest, and the debug server. Use `Save()` to obtain save bytes and `Dispose()`
        when the platform shuts down. Pass save bytes back through `GameOptions.SaveData` to load a game.
        {{(project.Runtime.NetworkMode == OdNetworkMode.Lockstep
            ? "Set `StartRelayServer = true` to run the bundled Relay Server. In that mode the local machine always connects through loopback; `RelayAddress` cannot point to a remote host."
            : string.Empty)}}
        """;

    private static string StateSyncProgram() => """"
        using System.Diagnostics;
        using System.Net;
        using System.Reflection;
        using GMCore;

        if (Args.Has(args, "help") || Args.Has(args, "h")) { Help(); return; }
        Dictionary<string, string?> options = Args.Parse(args);
        string mode = Args.Get(options, "mode", "offline").ToLowerInvariant();
        if (mode is not ("offline" or "host" or "dedicated-server"))
            throw new ArgumentException("--mode must be offline, host, or dedicated-server.");
        using var logger = new GameplayLogger(new GameplayLoggerOptions
        {
            DirectoryPath = Args.Get(options, "log-directory", "logs"),
            FilePrefix = "state-sync-" + mode,
        });
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            logger.Critical(LogCatagories.Runner, eventArgs.ExceptionObject);
        IODGameplayRuntime runtime = Assembly.GetExecutingAssembly().GetTypes()
            .Where(type => !type.IsAbstract && typeof(IODGameplayRuntime).IsAssignableFrom(type))
            .Select(type => (IODGameplayRuntime)Activator.CreateInstance(type)!).Single();
        GameplayMachineSettings settings = runtime.CreateSettings();
        settings.Logger = logger;
        settings.LocalPlayerMode = mode == "dedicated-server"
            ? GameplayLocalPlayerMode.Disabled : GameplayLocalPlayerMode.Enabled;
        IODModule[] modules = runtime.CreateModules().ToArray();
        string? loadFile = Args.Optional(options, "load");
        GameplayMachine machine = loadFile is not null
            ? GameplayMachineBase.LoadGameplayMachine(new MachineKey { ID = Environment.ProcessId }, settings,
                File.ReadAllBytes(loadFile), modules)
            : GameplayMachineBase.SpawnGameplayMachine(null, settings, modules);
        machine.SetUseDelayedCallback(true);
        if (mode != "offline")
        {
            IPAddress address = Args.Address(Args.Get(options, "address", "0.0.0.0"), true);
            int port = Args.Int(options, "port", 7777);
            machine.EnableNetworking(LiteNetLibGameplayNetworkTransport.CreateServer(new IPEndPoint(address, port)));
            logger.Important(LogCatagories.Runner, $"{mode} listening on {address}:{port}");
        }
        else logger.Important(LogCatagories.Runner, "Offline GameplayMachine started");
        StartDebug(machine, options);
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
        var clock = Stopwatch.StartNew(); double previous = clock.Elapsed.TotalSeconds;
        while (!shutdown.IsCancellationRequested)
        {
            double now = clock.Elapsed.TotalSeconds;
            machine.Update(now - previous); previous = now;
            Thread.Sleep(1);
        }
        string? saveFile = Args.Optional(options, "save");
        if (saveFile is not null) File.WriteAllBytes(saveFile, GameplayMachineBase.SaveGameplayMachine(machine));
        machine.StopDebugServer(); machine.DisableNetworking();

        static void StartDebug(GameplayMachine machine, IReadOnlyDictionary<string, string?> options)
        {
            if (!Args.Has(options, "debug")) return;
            machine.StartDebugServer(new GameplayDebugOptions { Address = Args.Get(options, "debug-address", "0.0.0.0"),
                Port = Args.Int(options, "debug-port", 7788), Token = Args.Get(options, "debug-token", ""),
                Access = GameplayDebugAccess.Control });
        }
        static void Help() => Console.WriteLine("""
        GameplayMachine State Sync command-line game
          --mode <offline|host|dedicated-server>  Startup type (default: offline)
          --address <ip>                          Listen address (default: 0.0.0.0)
          --port <number>                         Listen port (default: 7777)
          --debug                                 Enable debug server
          --debug-address <ip>                    Debug listen address (default: 0.0.0.0)
          --debug-port <number>                   Debug port (default: 7788)
          --debug-token <token>                   Optional debug access token
          --load <file>                           Load save bytes at startup
          --save <file>                           Save on shutdown
          --log-directory <directory>             Full log directory (default: logs)
          --help                                  Show this help
        """);
        """" + ArgumentHelpers();

    private static string LockstepGameProgram(OdLockstepTickMode tickMode) => $$""""
        using System.Diagnostics;
        using System.Net;
        using System.Reflection;
        using GMCore;

        if (Args.Has(args, "help") || Args.Has(args, "h")) { Help(); return; }
        Dictionary<string, string?> options = Args.Parse(args);
        using var logger = new GameplayLogger(new GameplayLoggerOptions
        {
            DirectoryPath = Args.Get(options, "log-directory", "logs"),
            FilePrefix = "lockstep-game",
        });
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            logger.Critical(LogCatagories.Runner, eventArgs.ExceptionObject);
        IODGameplayRuntime runtime = Assembly.GetExecutingAssembly().GetTypes()
            .Where(type => !type.IsAbstract && typeof(IODGameplayRuntime).IsAssignableFrom(type))
            .Select(type => (IODGameplayRuntime)Activator.CreateInstance(type)!).Single();
        GameplayMachineSettings settings = runtime.CreateSettings();
        settings.Logger = logger;
        GameplayMachine machine = GameplayMachineBase.SpawnGameplayMachine(null, settings,
            runtime.CreateModules().ToArray());
        machine.SetUseDelayedCallback(true);
        bool startRelay = Args.Has(options, "start-relay");
        if (startRelay && options.ContainsKey("relay-address"))
        {
            logger.Error(LogCatagories.Runner,
                "--relay-address cannot be used with --start-relay. The local game always connects to its embedded relay.");
            Environment.ExitCode = 2;
            return;
        }
        string relayAddress = startRelay ? "127.0.0.1" : Args.Get(options, "relay-address", "127.0.0.1");
        int relayPort = Args.Int(options, "relay-port", 7777);
        var lockstep = new GameplayLockstepOptions { TickIntervalMilliseconds = settings.TickIntervalMilliseconds,
            TickMode = GameplayLockstepTickMode.{{tickMode}},
            InputDelayTicks = {{(tickMode == OdLockstepTickMode.InputDriven ? 0 : 2)}},
            RequestedSlot = Args.Int(options, "requested-slot", 0),
            RecordingDirectory = Args.Optional(options, "record-directory"), Logger = logger };
        GameplayLockstepRelayServer? relay = null;
        if (startRelay)
        {
            IPAddress listenAddress = Args.Address(Args.Get(options, "relay-listen-address", "0.0.0.0"), true);
            lockstep.ExpectedPlayerCount = Args.Int(options, "players", 2);
            relay = new GameplayLockstepRelayServer(
                LiteNetLibGameplayNetworkTransport.CreateServer(new IPEndPoint(listenAddress, relayPort)), lockstep);
            relay.Start();
            logger.Important(LogCatagories.Runner,
                $"Embedded Lockstep relay listening on {listenAddress}:{relayPort}");
        }
        machine.ConnectLockstepNetworking(
            LiteNetLibGameplayNetworkTransport.CreateClient(relayAddress, relayPort), lockstep);
        if (Args.Has(options, "debug"))
            machine.StartDebugServer(new GameplayDebugOptions { Address = Args.Get(options, "debug-address", "0.0.0.0"),
                Port = Args.Int(options, "debug-port", 7788), Token = Args.Get(options, "debug-token", ""),
                Access = GameplayDebugAccess.Control });
        logger.Important(LogCatagories.Runner, $"Connecting to Lockstep relay {relayAddress}:{relayPort}");
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
        while (!shutdown.IsCancellationRequested)
        {
            relay?.Update();
            machine.UpdateNetwork();
            relay?.Update();
            Thread.Sleep(1);
        }
        machine.StopDebugServer(); machine.DisableNetworking();
        relay?.Dispose();

        static void Help() => Console.WriteLine("""
        GameplayMachine Lockstep command-line game
          --start-relay                           Start the bundled Relay Server
          --relay-listen-address <ip>             Embedded Relay listen address (default: 0.0.0.0)
          --relay-address <host>                  Relay address (default: 127.0.0.1)
          --relay-port <number>                   Relay port (default: 7777)
          --players <number>                      Embedded Relay player count (default: 2)
          --requested-slot <number>               Zero-based player slot requested from the Relay (default: 0)
          --record-directory <directory>          Write deterministic input recordings
          --debug                                 Enable debug server
          --debug-address <ip>                    Debug listen address (default: 0.0.0.0)
          --debug-port <number>                   Debug port (default: 7788)
          --debug-token <token>                   Optional debug access token
          --log-directory <directory>             Full log directory (default: logs)
          --help                                  Show this help
        """);
        """" + ArgumentHelpers();

    private static string RelayProgram(int tickIntervalMilliseconds, OdLockstepTickMode tickMode) => $$""""
        using System.Net;
        using GMCore;

        if (Args.Has(args, "help") || Args.Has(args, "h")) { Help(); return; }
        Dictionary<string, string?> options = Args.Parse(args);
        using var logger = new GameplayLogger(new GameplayLoggerOptions
        {
            DirectoryPath = Args.Get(options, "log-directory", "logs"),
            FilePrefix = "lockstep-relay",
        });
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            logger.Critical(LogCatagories.Runner, eventArgs.ExceptionObject);
        IPAddress address = Args.Address(Args.Get(options, "address", "0.0.0.0"), true);
        int port = Args.Int(options, "port", 7777);
        var relayOptions = new GameplayLockstepOptions
        {
            ExpectedPlayerCount = Args.Int(options, "players", 2),
            TickIntervalMilliseconds = {{tickIntervalMilliseconds}},
            TickMode = GameplayLockstepTickMode.{{tickMode}},
            InputDelayTicks = {{(tickMode == OdLockstepTickMode.InputDriven ? 0 : 2)}},
            RecordingDirectory = Args.Optional(options, "record-directory"),
            Logger = logger,
        };
        using var relay = new GameplayLockstepRelayServer(
            LiteNetLibGameplayNetworkTransport.CreateServer(new IPEndPoint(address, port)), relayOptions);
        relay.Start();
        logger.Important(LogCatagories.Runner, $"Lockstep relay listening on {address}:{port}");
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
        while (!shutdown.IsCancellationRequested) { relay.Update(); Thread.Sleep(1); }

        static void Help() => Console.WriteLine("""
        GameplayMachine Lockstep relay server
          --address <ip>                          Listen address (default: 0.0.0.0)
          --port <number>                         Listen port (default: 7777)
          --players <number>                      Expected players (default: 2)
          --record-directory <directory>          Write authoritative input recordings
          --log-directory <directory>             Full log directory (default: logs)
          --help                                  Show this help
        """);
        """" + ArgumentHelpers();

    private static string ArgumentHelpers() => """

        static class Args
        {
            public static Dictionary<string, string?> Parse(string[] values)
            {
                var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (int index = 0; index < values.Length; index++)
                {
                    string token = values[index];
                    if (!token.StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"Unexpected argument '{token}'. Use --help for usage.");
                    string name = token[2..];
                    string? value = index + 1 < values.Length && !values[index + 1].StartsWith("--", StringComparison.Ordinal)
                        ? values[++index] : null;
                    result[name] = value;
                }
                return result;
            }
            public static bool Has(string[] values, string name) => values.Any(value =>
                string.Equals(value, "--" + name, StringComparison.OrdinalIgnoreCase));
            public static bool Has(IReadOnlyDictionary<string, string?> values, string name) => values.ContainsKey(name);
            public static string Get(IReadOnlyDictionary<string, string?> values, string name, string fallback) =>
                values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
            public static string? Optional(IReadOnlyDictionary<string, string?> values, string name) =>
                values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : null;
            public static int Int(IReadOnlyDictionary<string, string?> values, string name, int fallback) =>
                int.TryParse(Get(values, name, fallback.ToString()), out int value) ? value :
                    throw new ArgumentException($"--{name} must be an integer.");
            public static IPAddress Address(string value, bool allowAny)
            {
                if (allowAny && (string.IsNullOrWhiteSpace(value) || value == "0.0.0.0")) return IPAddress.Any;
                if (IPAddress.TryParse(value, out IPAddress? parsed)) return parsed;
                return Dns.GetHostAddresses(value).First(item => item.AddressFamily ==
                    System.Net.Sockets.AddressFamily.InterNetwork);
            }
        }
        """;
}
