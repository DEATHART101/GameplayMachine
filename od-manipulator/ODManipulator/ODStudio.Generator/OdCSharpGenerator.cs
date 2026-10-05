using System.Text;
using ODStudio.Model;
using ODStudio.Package;

namespace ODStudio.Generator;

public static class OdCSharpGenerator
{
    private const string ManifestFileName = ".gameplaymachine-generated";
    private const string LegacyManifestFileName = ".odstudio-generated";
    public const string UserCodeDirectory = "User";

    public static async Task<OdCodeGenerationResult> GeneratePackageAsync(string packagePath, string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        OdPackageGraph graph = await OdPackageGraph.LoadAsync(packagePath, cancellationToken);
        if (graph.Issues.Any(issue => issue.Severity == OdIssueSeverity.Error))
            return new OdCodeGenerationResult { Files = Array.Empty<OdGeneratedFile>(), Issues = graph.Issues };
        return await GenerateAsync(graph.ComposeUsedProject(), outputDirectory, cancellationToken);
    }

    public static async Task<OdCodeGenerationResult> GeneratePackageAsync(string packagePath, string outputDirectory,
        OdCSharpGenerationOptions options, CancellationToken cancellationToken = default)
    {
        OdPackageGraph graph = await OdPackageGraph.LoadAsync(packagePath, cancellationToken);
        if (graph.Issues.Any(issue => issue.Severity == OdIssueSeverity.Error))
            return new OdCodeGenerationResult { Files = Array.Empty<OdGeneratedFile>(), Issues = graph.Issues };
        return await GenerateAsync(graph.ComposeUsedProject(), outputDirectory, options, cancellationToken);
    }

    public static async Task<OdCodeGenerationResult> GenerateAsync(OdProject project, string outputDirectory,
        CancellationToken cancellationToken = default)
        => await GenerateAsync(project, outputDirectory, new OdCSharpGenerationOptions(), cancellationToken);

    public static async Task<OdCodeGenerationResult> GenerateAsync(OdProject project, string outputDirectory,
        OdCSharpGenerationOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        project.EnsureOdScopes();

        var result = GenerateFiles(project, options);
        if (result.Succeeded)
            await WriteFilesAsync(outputDirectory, result.Files, cancellationToken);
        return result;
    }

    public static OdCodeGenerationResult GenerateFiles(OdProject project, OdCSharpGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.EnsureOdScopes();
        options ??= new OdCSharpGenerationOptions();
        OdProjectCodeManager.EnsureCodeFiles(project);
        var issues = OdProjectValidator.Validate(project).ToList();
        var files = new List<OdGeneratedFile>();
        if (issues.All(item => item.Severity != OdIssueSeverity.Error))
        {
            foreach (OdScope scope in project.Ods.OrderBy(item => item.Namespace, StringComparer.Ordinal))
            {
                var context = new OdGenerationContext(project, scope, issues, options);
                string directory = OdGenerationContext.NamespaceDirectory(scope.Namespace);
                files.Add(new($"{directory}/ODTypes.g.cs", OdTypeEmitter.Emit(context)));
                files.Add(new($"{directory}/ODBehaviors.g.cs", OdBehaviorEmitter.Emit(context)));
                files.Add(new($"{directory}/ODModule.g.cs", OdModuleEmitter.Emit(context)));
                files.Add(new($"{directory}/ODResources.g.cs", OdStaticDataEmitter.Emit(context)));
                files.Add(new($"{directory}/ODScenes.g.cs", OdSceneEmitter.Emit(context)));
            }
            files.Add(new($"{OdGenerationContext.NamespaceDirectory(project.DefaultNamespace)}/ODRuntime.g.cs",
                OdRuntimeEmitter.Emit(project)));
            var abstractInterfaceIds = project.Definitions.OfType<OdInterfaceDefinition>()
                .Where(item => item.Abstract).Select(item => item.Id).ToHashSet();
            files.AddRange(project.CodeFiles.Where(file => file.Kind != OdCodeFileKind.Runtime &&
                OdGameplayMachineFunctionCatalog.IsAvailable(project, file) &&
                !(file.Kind == OdCodeFileKind.Interface && file.OwnerId is { } ownerId && abstractInterfaceIds.Contains(ownerId))).Select(file =>
                new OdGeneratedFile(file.RelativePath, file.Content, OverwriteExisting: true)));
        }
        return new OdCodeGenerationResult { Files = files, Issues = issues };
    }

    private static async Task WriteFilesAsync(string outputDirectory, IReadOnlyList<OdGeneratedFile> files,
        CancellationToken cancellationToken)
    {
        string fullOutput = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutput);
        string manifestPath = Path.Combine(fullOutput, ManifestFileName);
        string previousManifestPath = File.Exists(manifestPath)
            ? manifestPath
            : Path.Combine(fullOutput, LegacyManifestFileName);
        if (File.Exists(previousManifestPath))
        {
            foreach (string relativePath in await File.ReadAllLinesAsync(previousManifestPath, cancellationToken))
            {
                string candidate = Path.GetFullPath(Path.Combine(fullOutput, relativePath));
                if (candidate.StartsWith(fullOutput + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                    File.Delete(candidate);
            }
        }

        foreach (var file in files)
        {
            string path = Path.GetFullPath(Path.Combine(fullOutput, file.RelativePath));
            if (!path.StartsWith(fullOutput + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Generated code file '{file.RelativePath}' escapes the output directory.");
            if (!file.OverwriteExisting && File.Exists(path))
                continue;
            if (!file.OverwriteExisting && TryMigrateLegacyUserFile(fullOutput, file.RelativePath, path))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, file.Content, new UTF8Encoding(false), cancellationToken);
            File.Move(temporary, path, true);
        }
        await File.WriteAllLinesAsync(manifestPath,
            files.Where(item => item.OverwriteExisting).Select(item => item.RelativePath),
            new UTF8Encoding(false), cancellationToken);
        if (!string.Equals(previousManifestPath, manifestPath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(previousManifestPath))
            File.Delete(previousManifestPath);
    }

    private static bool TryMigrateLegacyUserFile(string outputRoot, string relativePath, string destinationPath)
    {
        string prefix = UserCodeDirectory + "/";
        string normalizedPath = relativePath.Replace('\\', '/');
        if (!normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string legacyPath = Path.GetFullPath(Path.Combine(outputRoot, normalizedPath[prefix.Length..]));
        if (!legacyPath.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(legacyPath))
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Move(legacyPath, destinationPath);
        DeleteEmptyLegacyDirectories(outputRoot, Path.GetDirectoryName(legacyPath));
        return true;
    }

    private static void DeleteEmptyLegacyDirectories(string outputRoot, string? directory)
    {
        while (directory is not null &&
               !string.Equals(directory, outputRoot, StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(directory) &&
               !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }
}
