using ODStudio.Model;

namespace ODStudio.Export;

public sealed record OdExportArtifact(
    string RelativePath,
    string Content,
    bool OverwriteExisting = true,
    string? StableId = null,
    IReadOnlyList<string>? LegacyRelativePaths = null);

public sealed class OdExportProfile
{
    public string Name { get; set; } = "default";
    public string Target { get; set; } = string.Empty;
    public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string GetOption(string name, string defaultValue) =>
        Options.TryGetValue(name, out string? value) ? value : defaultValue;
}

public sealed record OdExportRequest(
    string PackagePath,
    string OutputDirectory,
    OdExportProfile Profile,
    bool DryRun = false);

public sealed class OdExportModel
{
    public OdExportModel(OdProject project, OdProject? rootProject = null,
        IReadOnlyList<OdProject>? dependencyProjects = null)
    {
        Project = project;
        RootProject = rootProject ?? project;
        DependencyProjects = dependencyProjects ?? Array.Empty<OdProject>();
        Definitions = project.Definitions.ToDictionary(item => item.Id);
    }

    public OdProject Project { get; }
    public OdProject RootProject { get; }
    public IReadOnlyList<OdProject> DependencyProjects { get; }
    public IReadOnlyDictionary<Guid, OdDefinition> Definitions { get; }
}

public sealed class OdExportPlan
{
    public required string TargetId { get; init; }
    public required string TargetVersion { get; init; }
    public required IReadOnlyList<OdExportArtifact> Artifacts { get; init; }
    public required IReadOnlyList<OdIssue> Issues { get; init; }
    public bool Succeeded => Issues.All(item => item.Severity != OdIssueSeverity.Error);
}

public sealed class OdExportResult
{
    public required OdExportPlan Plan { get; init; }
    public required IReadOnlyList<string> WrittenFiles { get; init; }
    public required IReadOnlyList<string> DeletedFiles { get; init; }
    public bool Succeeded => Plan.Succeeded;
}

public interface IOdExportTarget
{
    string Id { get; }
    string DisplayName { get; }
    string Version { get; }
    OdExportPlan BuildPlan(OdExportModel model, OdExportProfile profile);
}

public interface IOdExportPostProcessor
{
    Task PostProcessAsync(OdExportModel model, OdExportRequest request,
        CancellationToken cancellationToken = default);
}
