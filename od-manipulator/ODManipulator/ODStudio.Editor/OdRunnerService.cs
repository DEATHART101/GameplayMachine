using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;
using ODStudio.Generator;
using ODStudio.Model;

namespace ODStudio.Editor;

internal enum RunnerMode
{
    Offline,
    AuthorityHost,
    ClientProxy,
}

internal sealed record RunnerLaunchOptions(
    RunnerMode Mode,
    string Address,
    int Port,
    int RequestedSlot,
    int LockstepPlayerCount,
    OdLockstepTickMode LockstepTickMode);

internal sealed record RunnerLaunchResult(Process Process, int DebugPort, string DebugToken);

internal enum RunnerOutputStream
{
    Output,
    Error,
}

internal sealed record RunnerOutputLine(DateTimeOffset Timestamp, RunnerOutputStream Stream, string Message);

internal static class OdRunnerService
{
    public static async Task<RunnerLaunchResult> BuildAndStartAsync(
        OdProject project, RunnerLaunchOptions options, Action<RunnerOutputLine>? output = null,
        CancellationToken cancellationToken = default)
    {
        string cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameplayMachineEditor", "Runner", project.Id.ToString("N"));
        string sourceRoot = Path.Combine(cacheRoot, "src");
        Directory.CreateDirectory(sourceRoot);
        OdCodeGenerationResult generation = await OdCSharpGenerator.GenerateAsync(project, sourceRoot, cancellationToken);
        if (!generation.Succeeded)
            throw new InvalidOperationException(string.Join(Environment.NewLine,
                generation.Issues.Select(item => $"{item.Code}: {item.Message}")));

        string projectFile = Path.Combine(sourceRoot, "GameplayMachine.Runtime.csproj");
        await File.WriteAllTextAsync(projectFile, BuildProjectFile(project.Name), new UTF8Encoding(false), cancellationToken);
        string buildOutput = await RunProcessAsync("dotnet", new[] { "build", projectFile, "--nologo", "-v:minimal" },
            sourceRoot, cancellationToken);
        string assemblyPath = Path.Combine(sourceRoot, "bin", "Debug", "net8.0", "GameplayMachine.Runtime.dll");
        if (!File.Exists(assemblyPath))
            throw new InvalidOperationException("The OD runtime build did not produce an assembly.\n" + buildOutput);

        int debugPort = FindFreeTcpPort();
        string debugToken = Guid.NewGuid().ToString("N");
        ProcessStartInfo startInfo = CreateSelfStartInfo();
        startInfo.ArgumentList.Add("--od-runner-host");
        AddArgument(startInfo, "--assembly", assemblyPath);
        AddArgument(startInfo, "--mode", options.Mode.ToString());
        AddArgument(startInfo, "--address", options.Address);
        AddArgument(startInfo, "--port", options.Port.ToString());
        AddArgument(startInfo, "--requested-slot", options.RequestedSlot.ToString());
        AddArgument(startInfo, "--lockstep-player-count", options.LockstepPlayerCount.ToString());
        AddArgument(startInfo, "--lockstep-tick-mode", options.LockstepTickMode.ToString());
        AddArgument(startInfo, "--debug-port", debugPort.ToString());
        AddArgument(startInfo, "--debug-token", debugToken);
        AddArgument(startInfo, "--log-directory", Path.Combine(cacheRoot, "logs"));
        startInfo.WorkingDirectory = sourceRoot;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the OD runtime process.");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data?.StartsWith("ODRUNNER_READY", StringComparison.Ordinal) == true)
            {
                ready.TrySetResult();
                return;
            }
            if (!string.IsNullOrWhiteSpace(args.Data))
                output?.Invoke(new RunnerOutputLine(DateTimeOffset.Now, RunnerOutputStream.Output, args.Data));
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                lock (errors) errors.AppendLine(args.Data);
                output?.Invoke(new RunnerOutputLine(DateTimeOffset.Now, RunnerOutputStream.Error, args.Data));
            }
        };
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            if (!ready.Task.IsCompleted)
                ready.TrySetException(new InvalidOperationException(
                    "The OD runtime exited before it became ready.\n" + errors));
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return new RunnerLaunchResult(process, debugPort, debugToken);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            throw;
        }
    }

    private static string BuildProjectFile(string projectName)
    {
        string references = string.Join(Environment.NewLine,
            Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .Select((path, index) =>
                    $"    <Reference Include=\"ODRuntimeReference{index}\"><HintPath>{SecurityElement.Escape(path)}</HintPath><Private>false</Private></Reference>"));
        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <AssemblyName>GameplayMachine.Runtime</AssemblyName>
                <RootNamespace>{{SecurityElement.Escape(projectName)}}</RootNamespace>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
            {{references}}
              </ItemGroup>
            </Project>
            """;
    }

    private static async Task<string> RunProcessAsync(string fileName, IEnumerable<string> arguments,
        string workingDirectory, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string output = await outputTask;
        string error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"OD runtime compilation failed.\n{output}\n{error}".Trim());
        return output;
    }

    private static ProcessStartInfo CreateSelfStartInfo()
    {
        string executable = Environment.ProcessPath
                            ?? throw new InvalidOperationException("Cannot locate the GameplayMachine Editor executable.");
        var result = new ProcessStartInfo(executable);
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            result.ArgumentList.Add(typeof(Program).Assembly.Location);
        return result;
    }

    private static void AddArgument(ProcessStartInfo info, string name, string value)
    {
        info.ArgumentList.Add(name);
        info.ArgumentList.Add(value);
    }

    private static int FindFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
