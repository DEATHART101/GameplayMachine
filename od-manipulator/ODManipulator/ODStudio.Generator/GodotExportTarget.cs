using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using ODStudio.Export;
using ODStudio.Model;

namespace ODStudio.Generator;

public sealed class GodotExportTarget : IOdExportTarget, IOdExportPostProcessor
{
    public string Id => "godot";
    public string DisplayName => "Godot (.NET)";
    public string Version => "1.0.0";

    public OdExportPlan BuildPlan(OdExportModel model, OdExportProfile profile)
    {
        OdExportPlan source = new PlainCSharpExportTarget().BuildPlan(model, profile);
        var artifacts = source.Artifacts
            .Select(artifact => artifact with
            {
                OverwriteExisting = true,
                LegacyRelativePaths = null,
            })
            .ToList();
        artifacts.Add(new OdExportArtifact(
            $"Godot/{OdProjectCodeManager.SafeProjectName(model.RootProject)}Runner.cs",
            RunnerSource(model.RootProject)));
        return new OdExportPlan
        {
            TargetId = Id,
            TargetVersion = Version,
            Artifacts = artifacts,
            Issues = source.Issues,
        };
    }

    public Task PostProcessAsync(OdExportModel model, OdExportRequest request,
        CancellationToken cancellationToken = default)
    {
        string? projectRoot = FindGodotProjectRoot(request.OutputDirectory);
        if (projectRoot is null)
            return Task.CompletedTask;

        string runtimeProject = ResolveRuntimeProject(projectRoot, request.Profile);
        string assemblyName = ReadGodotAssemblyName(projectRoot) ??
            OdProjectCodeManager.SafeProjectName(model.RootProject);
        string projectFile = Path.Combine(projectRoot, assemblyName + ".csproj");
        if (!File.Exists(projectFile))
            File.WriteAllText(projectFile, NewProjectFile(assemblyName));

        XDocument document = XDocument.Load(projectFile, LoadOptions.PreserveWhitespace);
        XElement root = document.Root ?? throw new InvalidDataException($"Invalid project file '{projectFile}'");
        string relativeRuntime = Path.GetRelativePath(projectRoot, runtimeProject).Replace('\\', '/');
        bool exists = root.Descendants().Any(element => element.Name.LocalName == "ProjectReference" &&
            string.Equals(NormalizePath((string?)element.Attribute("Include")), NormalizePath(relativeRuntime),
                StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            XElement? group = root.Elements().FirstOrDefault(element => element.Name.LocalName == "ItemGroup" &&
                element.Elements().Any(child => child.Name.LocalName == "ProjectReference"));
            if (group is null)
            {
                group = new XElement(root.Name.Namespace + "ItemGroup");
                root.Add(group);
            }
            group.Add(new XElement(root.Name.Namespace + "ProjectReference",
                new XAttribute("Include", relativeRuntime)));
            document.Save(projectFile);
        }
        EnsureSolution(projectRoot, assemblyName, projectFile);
        return Task.CompletedTask;
    }

    private static void EnsureSolution(string projectRoot, string assemblyName, string projectFile)
    {
        string solutionFile = Path.Combine(projectRoot, assemblyName + ".sln");
        if (File.Exists(solutionFile))
            return;

        string projectName = Path.GetFileNameWithoutExtension(projectFile);
        string relativeProject = Path.GetRelativePath(projectRoot, projectFile).Replace('/', '\\');
        string projectGuid = StableProjectGuid(assemblyName).ToString("B").ToUpperInvariant();
        File.WriteAllText(solutionFile, $$"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            VisualStudioVersion = 17.0.31903.59
            MinimumVisualStudioVersion = 10.0.40219.1
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "{{projectName}}", "{{relativeProject}}", "{{projectGuid}}"
            EndProject
            Global
                GlobalSection(SolutionConfigurationPlatforms) = preSolution
                    Debug|Any CPU = Debug|Any CPU
                    ExportDebug|Any CPU = ExportDebug|Any CPU
                    ExportRelease|Any CPU = ExportRelease|Any CPU
                EndGlobalSection
                GlobalSection(ProjectConfigurationPlatforms) = postSolution
                    {{projectGuid}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                    {{projectGuid}}.Debug|Any CPU.Build.0 = Debug|Any CPU
                    {{projectGuid}}.ExportDebug|Any CPU.ActiveCfg = ExportDebug|Any CPU
                    {{projectGuid}}.ExportDebug|Any CPU.Build.0 = ExportDebug|Any CPU
                    {{projectGuid}}.ExportRelease|Any CPU.ActiveCfg = ExportRelease|Any CPU
                    {{projectGuid}}.ExportRelease|Any CPU.Build.0 = ExportRelease|Any CPU
                EndGlobalSection
                GlobalSection(SolutionProperties) = preSolution
                    HideSolutionNode = FALSE
                EndGlobalSection
            EndGlobal
            """, new UTF8Encoding(false));
    }

    private static Guid StableProjectGuid(string assemblyName)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("GameplayMachine.Godot:" + assemblyName));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string RunnerSource(OdProject project)
    {
        string ns = OdGenerationContext.NamespaceIdentifier(project.DefaultNamespace);
        string projectName = OdProjectCodeManager.SafeProjectName(project);
        bool isLockstep = project.Runtime.NetworkMode == OdNetworkMode.Lockstep;
        string tickMode = project.Runtime.LockstepTickMode.ToString();
        int inputDelay = project.Runtime.LockstepTickMode == OdLockstepTickMode.InputDriven ? 0 : 2;
        string lockstepProperties = isLockstep
            ? """
                [global::Godot.ExportGroup("Lockstep")]
                [global::Godot.Export(global::Godot.PropertyHint.Range, "1,64,1")] public int LockstepPlayerCount { get; set; } = 2;
                [global::Godot.Export(global::Godot.PropertyHint.Range, "0,63,1")] public int RequestedSlot { get; set; }
                [global::Godot.Export] public string LockstepRecordingDirectory { get; set; } = string.Empty;

                """
            : string.Empty;
        string lockstepAssignments = isLockstep
            ? """
                    _core.LockstepPlayerCount = LockstepPlayerCount;
                    _core.RequestedSlot = RequestedSlot;
                    _core.LockstepRecordingDirectory = LockstepRecordingDirectory;
                """
            : string.Empty;
        string lockstepOptions = isLockstep
            ? $$"""

                    protected override global::GMCore.GameplayLockstepOptions CreateLockstepOptions() => new()
                    {
                        TickMode = global::GMCore.GameplayLockstepTickMode.{{tickMode}},
                        InputDelayTicks = {{inputDelay}},
                    };
                """
            : string.Empty;
        return $$"""
            // <auto-generated />
            #nullable enable

            namespace {{ns}};

            [global::Godot.GlobalClass]
            public partial class {{projectName}}Runner : global::Godot.Node, global::ODStudio.Godot.Runtime.IGameplayMachineRunner
            {
                [global::Godot.Signal] public delegate void MachineStartedEventHandler();
                [global::Godot.Signal] public delegate void MachineStoppedEventHandler();
                [global::Godot.Signal] public delegate void MachineStoppingEventHandler();
                [global::Godot.Signal] public delegate void MachineFailedEventHandler(string message);
                [global::Godot.Signal] public delegate void NetworkStateChangedEventHandler(string oldState, string newState);
                [global::Godot.Signal] public delegate void GameplayObjectCreatedEventHandler(int objectId, string className);
                [global::Godot.Signal] public delegate void GameplayObjectDeletedEventHandler(int objectId);
                [global::Godot.Signal] public delegate void GameplayObjectFieldChangedEventHandler(int objectId, string fieldName, global::Godot.Variant value);

                [global::Godot.ExportGroup("Startup")]
                [global::Godot.Export] public bool StartOnReady { get; set; } = true;
                [global::Godot.Export] public global::ODStudio.Godot.Runtime.GameplayMachineStartupMode StartupMode { get; set; } = global::ODStudio.Godot.Runtime.GameplayMachineStartupMode.Offline;
                [global::Godot.Export] public bool ModifyDataInsideExecutionOnly { get; set; } = true;
                [global::Godot.Export] public bool UseDelayedCallbacks { get; set; } = true;

                [global::Godot.ExportGroup("Save")]
                [global::Godot.Export] public bool EnableSave { get; set; } = true;
                [global::Godot.Export] public bool LoadExistingSave { get; set; } = true;
                [global::Godot.Export] public string SaveFileName { get; set; } = "game.sav";
                [global::Godot.Export] public bool UsePartitionedSave { get; set; }
                [global::Godot.Export] public bool EmitObjectFieldSignals { get; set; }

                [global::Godot.ExportGroup("Network")]
                [global::Godot.Export] public global::ODStudio.Godot.Runtime.GameplayMachineTransport NetworkTransport { get; set; } = global::ODStudio.Godot.Runtime.GameplayMachineTransport.Udp;
                [global::Godot.Export] public string AuthorityAddress { get; set; } = "127.0.0.1";
                [global::Godot.Export(global::Godot.PropertyHint.Range, "1,65535,1")] public int Port { get; set; } = 7777;

            {{lockstepProperties}}
                [global::Godot.ExportGroup("Debug")]
                [global::Godot.Export] public bool EnableDebugServer { get; set; }
                [global::Godot.Export(global::Godot.PropertyHint.Range, "1,65535,1")] public int DebugPort { get; set; } = 7788;

                private RunnerCore? _core;
                private event global::System.Action? RuntimeStarted;
                private event global::System.Action? RuntimeStopping;

                public global::GMCore.GameplayMachine? Machine => _core?.Machine;

                event global::System.Action? global::ODStudio.Godot.Runtime.IGameplayMachineRunner.Started
                {
                    add => RuntimeStarted += value;
                    remove => RuntimeStarted -= value;
                }

                event global::System.Action? global::ODStudio.Godot.Runtime.IGameplayMachineRunner.Stopping
                {
                    add => RuntimeStopping += value;
                    remove => RuntimeStopping -= value;
                }

                public override void _Ready()
                {
                    ConfigureCore();
                    _core!.Ready();
                }

                public override void _Process(double delta) => _core?.Process(delta);

                public override void _ExitTree()
                {
                    _core?.ExitTree();
                    _core = null;
                }

                public bool IsRunning() => _core?.IsRunning() == true;
                public bool IsReady() => _core?.IsReady() == true;
                public string GetMachineRole() => _core?.GetMachineRole() ?? "Disabled";
                public string GetNetworkState() => _core?.GetNetworkState() ?? "Disabled";
                public int GetRootId() => _core?.GetRootId() ?? 0;
                public int GetLocalPlayerControllerId() => _core?.GetLocalPlayerControllerId() ?? 0;

                public void StartOffline()
                {
                    StartupMode = global::ODStudio.Godot.Runtime.GameplayMachineStartupMode.Offline;
                    ConfigureCore();
                    _core!.StartOffline();
                }

                public void StartHost(int port)
                {
                    StartupMode = global::ODStudio.Godot.Runtime.GameplayMachineStartupMode.Authority;
                    Port = port;
                    ConfigureCore();
                    _core!.StartHost(port);
                }

                public void StartProxy(string address, int port)
                {
                    StartupMode = global::ODStudio.Godot.Runtime.GameplayMachineStartupMode.ClientProxy;
                    AuthorityAddress = address;
                    Port = port;
                    ConfigureCore();
                    _core!.StartProxy(address, port);
                }

                public void StartMachine()
                {
                    ConfigureCore();
                    _core!.StartMachine();
                }

                public void StopMachine() => _core?.StopMachine();
                public bool SaveMachine() => _core?.SaveMachine() == true;
                public bool HasSave() => ConfigureCore().HasSave();
                public bool DeleteSave() => ConfigureCore().DeleteSave();
                public global::Godot.Collections.Array GetGameplayObjects() => ConfigureCore().GetGameplayObjects();
                public global::Godot.Collections.Dictionary GetGameplayObject(int objectId) => ConfigureCore().GetGameplayObject(objectId);
                public global::Godot.Collections.Array GetInterfaces() => ConfigureCore().GetInterfaces();
                public global::Godot.Collections.Dictionary ExecuteInterface(string interfaceName, global::Godot.Collections.Dictionary inputs) =>
                    ConfigureCore().ExecuteInterface(interfaceName, inputs);

                private RunnerCore ConfigureCore()
                {
                    if (_core == null)
                    {
                        _core = new RunnerCore(Name.ToString(), unchecked((int)GetInstanceId()));
                        _core.MachineStarted += () =>
                        {
                            RuntimeStarted?.Invoke();
                            EmitSignal(SignalName.MachineStarted);
                        };
                        _core.MachineStopped += () => EmitSignal(SignalName.MachineStopped);
                        _core.MachineStopping += () =>
                        {
                            RuntimeStopping?.Invoke();
                            EmitSignal(SignalName.MachineStopping);
                        };
                        _core.MachineFailed += message => EmitSignal(SignalName.MachineFailed, message);
                        _core.NetworkStateChanged += (oldState, newState) => EmitSignal(SignalName.NetworkStateChanged, oldState, newState);
                        _core.GameplayObjectCreated += (objectId, className) => EmitSignal(SignalName.GameplayObjectCreated, objectId, className);
                        _core.GameplayObjectDeleted += objectId => EmitSignal(SignalName.GameplayObjectDeleted, objectId);
                        _core.GameplayObjectFieldChanged += (objectId, fieldName, value) => EmitSignal(SignalName.GameplayObjectFieldChanged, objectId, fieldName, value);
                    }

                    _core.StartOnReady = StartOnReady;
                    _core.StartupMode = StartupMode;
                    _core.ModifyDataInsideExecutionOnly = ModifyDataInsideExecutionOnly;
                    _core.UseDelayedCallbacks = UseDelayedCallbacks;
                    _core.EnableSave = EnableSave;
                    _core.LoadExistingSave = LoadExistingSave;
                    _core.SaveFileName = SaveFileName;
                    _core.UsePartitionedSave = UsePartitionedSave;
                    _core.EmitObjectFieldSignals = EmitObjectFieldSignals;
                    _core.NetworkTransport = NetworkTransport;
                    _core.AuthorityAddress = AuthorityAddress;
                    _core.Port = Port;
            {{lockstepAssignments}}
                    _core.EnableDebugServer = EnableDebugServer;
                    _core.DebugPort = DebugPort;
                    return _core;
                }

                private sealed class RunnerCore : global::ODStudio.Godot.Runtime.GameplayMachineRunnerCore
                {
                    private readonly {{projectName}}Runtime _runtime = new();

                    public RunnerCore(string runnerName, int instanceId) : base(runnerName, instanceId) { }

                    protected override global::GMCore.IODModule[] CreateModules() =>
                        global::System.Linq.Enumerable.ToArray(_runtime.CreateModules());

                    protected override global::GMCore.GameplayMachineSettings CreateSettings() =>
                        _runtime.CreateSettings();
            {{lockstepOptions}}
                }
            }
            """;
    }

    private static string? FindGodotProjectRoot(string outputDirectory)
    {
        for (DirectoryInfo? directory = new(Path.GetFullPath(outputDirectory)); directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "project.godot")))
                return directory.FullName;
        }
        return null;
    }

    internal static string? FindGodotProjectFile(string outputDirectory)
    {
        string? projectRoot = FindGodotProjectRoot(outputDirectory);
        if (projectRoot is null)
            return null;
        string assemblyName = ReadGodotAssemblyName(projectRoot) ??
            new DirectoryInfo(projectRoot).Name;
        string projectFile = Path.Combine(projectRoot,
            OdGenerationContext.SafeIdentifier(assemblyName) + ".csproj");
        return File.Exists(projectFile) ? projectFile : null;
    }

    private static string ResolveRuntimeProject(string projectRoot, OdExportProfile profile)
    {
        string configured = profile.GetOption("godotRuntimeProject", string.Empty);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);
        string? environment = Environment.GetEnvironmentVariable("GAMEPLAYMACHINE_GODOT_PROJECT");
        if (!string.IsNullOrWhiteSpace(environment) && File.Exists(environment))
            return Path.GetFullPath(environment);

        for (DirectoryInfo? directory = new(projectRoot); directory is not null; directory = directory.Parent)
        {
            string? candidate = RuntimeProjectAt(directory);
            if (candidate is not null) return candidate;
        }
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
        {
            string? candidate = RuntimeProjectAt(directory);
            if (candidate is not null) return candidate;
        }
        throw new FileNotFoundException(
            "GameplayMachineGodot.csproj was not found. Set the godotRuntimeProject export option or GAMEPLAYMACHINE_GODOT_PROJECT.");
    }

    private static string? RuntimeProjectAt(DirectoryInfo directory)
    {
        string direct = Path.Combine(directory.FullName, "gameplaymachine_core", "main",
            "GameplayMachineGodot", "GameplayMachineGodot.csproj");
        if (File.Exists(direct)) return direct;
        if (string.Equals(directory.Name, "od-manipulator", StringComparison.OrdinalIgnoreCase) &&
            directory.Parent is not null)
        {
            string sibling = Path.Combine(directory.Parent.FullName, "gameplaymachine_core", "main",
                "GameplayMachineGodot", "GameplayMachineGodot.csproj");
            if (File.Exists(sibling)) return sibling;
        }
        return null;
    }

    private static string NormalizePath(string? path) => (path ?? string.Empty).Replace('\\', '/');

    private static string? ReadGodotAssemblyName(string projectRoot)
    {
        string projectFile = Path.Combine(projectRoot, "project.godot");
        foreach (string line in File.ReadLines(projectFile))
        {
            const string prefix = "project/assembly_name=";
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string value = line[prefix.Length..].Trim().Trim('"');
            return string.IsNullOrWhiteSpace(value) ? null : OdGenerationContext.SafeIdentifier(value);
        }
        return null;
    }

    private static string NewProjectFile(string assemblyName) => $$"""
        <Project Sdk="Godot.NET.Sdk/4.7.2">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <EnableDynamicLoading>true</EnableDynamicLoading>
            <RootNamespace>{{assemblyName}}</RootNamespace>
          </PropertyGroup>
        </Project>
        """;
}
