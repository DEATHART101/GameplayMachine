using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ODStudio.Package;

namespace ODStudio.Export;

public static class OdExportPipeline
{
    private const string ManifestFileName = ".gameplaymachine-export.json";
    private const string LegacyManifestFileName = ".odstudio-export.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<OdExportResult> ExportAsync(IOdExportTarget target, OdExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        OdPackageGraph graph = await OdPackageGraph.LoadAsync(request.PackagePath, cancellationToken);
        IReadOnlyList<ODStudio.Model.OdProject> usedProjects = graph.GetUsedProjects();
        var model = new OdExportModel(graph.ComposeUsedProject(), graph.Root,
            usedProjects.Skip(1).ToList());
        var plan = target.BuildPlan(model, request.Profile);
        if (graph.Issues.Count != 0)
            plan = new OdExportPlan
            {
                TargetId = plan.TargetId,
                TargetVersion = plan.TargetVersion,
                Artifacts = plan.Artifacts,
                Issues = graph.Issues.Concat(plan.Issues).ToList(),
            };
        if (!plan.Succeeded || request.DryRun)
            return new OdExportResult { Plan = plan, WrittenFiles = Array.Empty<string>(), DeletedFiles = Array.Empty<string>() };

        await using var packageStream = new FileStream(request.PackagePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, true);
        string packageHash = Convert.ToHexString(await SHA256.HashDataAsync(packageStream, cancellationToken))
            .ToLowerInvariant();
        OdExportResult result = await PublishAsync(plan, request.OutputDirectory, packageHash, cancellationToken);
        if (target is IOdExportPostProcessor postProcessor)
            await postProcessor.PostProcessAsync(model, request, cancellationToken);
        return result;
    }

    public static async Task<OdExportResult> PublishAsync(OdExportPlan plan, string outputDirectory,
        string packageHash = "in-memory", CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (!plan.Succeeded)
            return new OdExportResult { Plan = plan, WrittenFiles = Array.Empty<string>(), DeletedFiles = Array.Empty<string>() };

        string outputRoot = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputRoot);
        string manifestPath = Path.Combine(outputRoot, ManifestFileName);
        string previousManifestPath = File.Exists(manifestPath)
            ? manifestPath
            : Path.Combine(outputRoot, LegacyManifestFileName);
        ExportManifest? previous = await ReadManifestAsync(previousManifestPath, cancellationToken);
        string[] artifactPaths = plan.Artifacts.Select(item => NormalizeRelativePath(item.RelativePath)).ToArray();
        if (artifactPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != artifactPaths.Length)
            throw new InvalidDataException("The export plan contains duplicate artifact paths.");
        var ownedPaths = plan.Artifacts.Where(item => item.OverwriteExisting)
            .Select(item => NormalizeRelativePath(item.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deleted = new List<string>();
        if (previous is not null && string.Equals(previous.Target, plan.TargetId, StringComparison.OrdinalIgnoreCase))
        {
            foreach (string stale in previous.Files.Select(item => item.Path).Where(path => !ownedPaths.Contains(path)))
            {
                string fullPath = SafeOutputPath(outputRoot, stale);
                if (!File.Exists(fullPath))
                    continue;
                File.Delete(fullPath);
                deleted.Add(stale);
            }
        }

        var written = new List<string>();
        var manifestFiles = new List<ExportManifestFile>();
        foreach (OdExportArtifact artifact in plan.Artifacts.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            string relativePath = NormalizeRelativePath(artifact.RelativePath);
            string fullPath = SafeOutputPath(outputRoot, relativePath);
            if (!artifact.OverwriteExisting && File.Exists(fullPath))
                continue;
            if (!artifact.OverwriteExisting && TryMigrateLegacyArtifact(outputRoot, fullPath, artifact.LegacyRelativePaths))
            {
                written.Add(relativePath);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            string temporaryPath = fullPath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, artifact.Content, new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryPath, fullPath, true);
            written.Add(relativePath);
            if (artifact.OverwriteExisting)
                manifestFiles.Add(new ExportManifestFile
                {
                    Path = relativePath,
                    StableId = artifact.StableId,
                    Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(artifact.Content))).ToLowerInvariant(),
                });
        }

        var manifest = new ExportManifest
        {
            FormatVersion = 1,
            Target = plan.TargetId,
            TargetVersion = plan.TargetVersion,
            PackageSha256 = packageHash,
            Files = manifestFiles,
        };
        string manifestTemporaryPath = manifestPath + ".tmp";
        await File.WriteAllTextAsync(manifestTemporaryPath,
            JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false), cancellationToken);
        File.Move(manifestTemporaryPath, manifestPath, true);
        if (!string.Equals(previousManifestPath, manifestPath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(previousManifestPath))
            File.Delete(previousManifestPath);
        return new OdExportResult { Plan = plan, WrittenFiles = written, DeletedFiles = deleted };
    }

    private static bool TryMigrateLegacyArtifact(
        string outputRoot,
        string destinationPath,
        IReadOnlyList<string>? legacyRelativePaths)
    {
        if (legacyRelativePaths is null)
            return false;

        foreach (string legacyRelativePath in legacyRelativePaths)
        {
            string normalizedLegacyPath = NormalizeRelativePath(legacyRelativePath);
            string legacyPath = SafeOutputPath(outputRoot, normalizedLegacyPath);
            if (string.Equals(legacyPath, destinationPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(legacyPath))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Move(legacyPath, destinationPath);
            MoveUnityMetaFile(legacyPath, destinationPath);
            DeleteEmptyLegacyDirectories(outputRoot, Path.GetDirectoryName(legacyPath));
            return true;
        }
        return false;
    }

    private static void MoveUnityMetaFile(string legacyPath, string destinationPath)
    {
        string legacyMetaPath = legacyPath + ".meta";
        string destinationMetaPath = destinationPath + ".meta";
        if (File.Exists(legacyMetaPath) && !File.Exists(destinationMetaPath))
            File.Move(legacyMetaPath, destinationMetaPath);
    }

    private static void DeleteEmptyLegacyDirectories(string outputRoot, string? directory)
    {
        while (directory is not null &&
               !string.Equals(directory, outputRoot, StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(directory) &&
               !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            string metaPath = directory + ".meta";
            if (File.Exists(metaPath))
                File.Delete(metaPath);
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static async Task<ExportManifest?> ReadManifestAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ExportManifest>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"Export artifact path '{relativePath}' is not relative.");
        return relativePath.Replace('\\', '/');
    }

    private static string SafeOutputPath(string outputRoot, string relativePath)
    {
        string result = Path.GetFullPath(Path.Combine(outputRoot, relativePath));
        string relativeResult = Path.GetRelativePath(outputRoot, result);
        if (Path.IsPathRooted(relativeResult) || relativeResult.Equals("..", StringComparison.Ordinal) ||
            relativeResult.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relativeResult.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"Export artifact path '{relativePath}' escapes the output directory.");
        return result;
    }

    private sealed class ExportManifest
    {
        public int FormatVersion { get; set; }
        public string Target { get; set; } = string.Empty;
        public string TargetVersion { get; set; } = string.Empty;
        public string PackageSha256 { get; set; } = string.Empty;
        public List<ExportManifestFile> Files { get; set; } = new();
    }

    private sealed class ExportManifestFile
    {
        public string Path { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public string? StableId { get; set; }
    }
}
