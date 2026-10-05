using ODStudio.Model;
using ODStudio.Package;
using ODStudio.Generator;
using ODStudio.Export;
using ODStudio.Export.Unity;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    try
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "new" when args.Length >= 3:
            {
                var project = OdProjectStore.Create(args[2], args.Length >= 4 ? args[3] : null);
                await OdProjectStore.SaveAsync(args[1], project);
                Console.WriteLine($"Created {project.Name} ({project.Id}) at {Path.GetFullPath(args[1])}");
                return 0;
            }
            case "validate" when args.Length >= 2:
            {
                var project = await OdProjectStore.LoadAsync(args[1]);
                return PrintIssues(OdProjectValidator.Validate(project));
            }
            case "build" when args.Length >= 3:
            {
                var project = await OdProjectStore.LoadAsync(args[1]);
                var result = await OdPackageCompiler.BuildAsync(project, args[2]);
                var exitCode = PrintIssues(result.Issues);
                if (exitCode == 0)
                    Console.WriteLine($"Built {result.OutputPath} ({result.Size} bytes, package {result.PackageId})");
                return exitCode;
            }
            case "inspect" when args.Length >= 2:
            {
                var project = await OdPackageCompiler.LoadAsync(args[1]);
                Console.WriteLine($"{project.Name} ({project.Id})");
                Console.WriteLine($"Definitions: {project.Definitions.Count}, Data sets: {project.DataSets.Count}");
                return PrintIssues(OdProjectValidator.Validate(project));
            }
            case "generate" when args.Length >= 3:
            {
                var result = await OdCSharpGenerator.GeneratePackageAsync(args[1], args[2]);
                var exitCode = PrintIssues(result.Issues);
                if (exitCode == 0)
                    Console.WriteLine($"Generated {result.Files.Count} C# file(s) in {Path.GetFullPath(args[2])}");
                return exitCode;
            }
            case "export" when args.Length >= 3:
            {
                string targetId = Option(args, "--target") ?? "unity";
                IOdExportTarget target = targetId.ToLowerInvariant() switch
                {
                    "csharp" => new PlainCSharpExportTarget(),
                    "unity" => new UnityExportTarget(),
                    "godot" => new GodotExportTarget(),
                    _ => throw new ArgumentException($"Unknown export target '{targetId}'."),
                };
                var profile = new OdExportProfile { Name = "cli", Target = target.Id };
                bool dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
                var result = await OdExportPipeline.ExportAsync(target,
                    new OdExportRequest(args[1], args[2], profile, dryRun));
                int exitCode = PrintIssues(result.Plan.Issues);
                if (exitCode == 0)
                    Console.WriteLine(dryRun
                        ? $"Export plan: {result.Plan.Artifacts.Count} file(s) for {target.DisplayName}."
                        : $"Exported {result.WrittenFiles.Count} file(s), removed {result.DeletedFiles.Count} stale file(s) in {Path.GetFullPath(args[2])}");
                return exitCode;
            }
            case "publish" when args.Length >= 3:
            {
                OdProject project = await OdProjectStore.LoadAsync(args[1]);
                string type = Option(args, "--type") ?? throw new ArgumentException("publish requires --type.");
                GameplayMachineBuildOutput output = type.ToLowerInvariant() switch
                {
                    "state-commandline" => GameplayMachineBuildOutput.StateSyncCommandLineGame,
                    "state-exported" => GameplayMachineBuildOutput.StateSyncExportedGame,
                    "lockstep-relay" => GameplayMachineBuildOutput.LockstepRelayServer,
                    "lockstep-commandline" => GameplayMachineBuildOutput.LockstepCommandLineGame,
                    "lockstep-exported" => GameplayMachineBuildOutput.LockstepExportedGame,
                    _ => throw new ArgumentException($"Unknown publish type '{type}'. Use help for valid types."),
                };
                bool exported = output is GameplayMachineBuildOutput.StateSyncExportedGame or
                    GameplayMachineBuildOutput.LockstepExportedGame;
                string? platform = Option(args, "--platform");
                if (!exported && platform is not null)
                    throw new ArgumentException("--platform is only valid for exported game builds.");
                IOdExportTarget? exportTarget = exported ? CreateExportTarget(platform ?? "csharp") : null;
                GameplayMachineBuildResult result = await GameplayMachineBuildPublisher.PublishAsync(
                    project, output, args[2], AppContext.BaseDirectory, exportTarget);
                int exitCode = PrintIssues(result.Issues);
                if (exitCode == 0)
                    Console.WriteLine($"Published {output} ({result.Files.Count} files) to {result.OutputDirectory}");
                return exitCode;
            }
            default:
                Console.Error.WriteLine("Invalid command. Use 'help' for usage.");
                return 2;
        }
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
}

static IOdExportTarget CreateExportTarget(string targetId) => targetId.ToLowerInvariant() switch
{
    "csharp" => new PlainCSharpExportTarget(),
    "unity" => new UnityExportTarget(),
    "godot" => new GodotExportTarget(),
    _ => throw new ArgumentException($"Unknown export platform '{targetId}'."),
};

static string? Option(string[] args, string name)
{
    for (int index = 0; index < args.Length - 1; index++)
        if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            return args[index + 1];
    return null;
}

static int PrintIssues(IReadOnlyList<OdIssue> issues)
{
    foreach (var issue in issues)
        Console.WriteLine($"{issue.Severity,-7} {issue.Code}: {issue.Message}");
    Console.WriteLine($"Validation: {issues.Count(issue => issue.Severity == OdIssueSeverity.Error)} error(s), " +
                      $"{issues.Count(issue => issue.Severity == OdIssueSeverity.Warning)} warning(s)");
    return issues.Any(issue => issue.Severity == OdIssueSeverity.Error) ? 1 : 0;
}

static void PrintHelp()
{
    Console.WriteLine("GameplayMachine Editor CLI");
    Console.WriteLine("  gameplaymachine-editor new <project-directory> <name> [namespace]");
    Console.WriteLine("  gameplaymachine-editor validate <project-directory>");
    Console.WriteLine("  gameplaymachine-editor build <project-directory> <output.odpkg>");
    Console.WriteLine("  gameplaymachine-editor inspect <package.odpkg>");
    Console.WriteLine("  gameplaymachine-editor generate <package.odpkg> <output-directory>");
    Console.WriteLine("  gameplaymachine-editor export <package.odpkg> <output-directory> [--target csharp|unity|godot] [--dry-run]");
    Console.WriteLine("  gameplaymachine-editor publish <project-directory> <output-directory> --type <state-commandline|state-exported|lockstep-relay|lockstep-commandline|lockstep-exported> [--platform csharp|unity|godot]");
}
