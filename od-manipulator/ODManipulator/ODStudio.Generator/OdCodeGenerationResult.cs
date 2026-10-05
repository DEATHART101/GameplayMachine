using ODStudio.Model;

namespace ODStudio.Generator;

public sealed record OdGeneratedFile(string RelativePath, string Content, bool OverwriteExisting = true);

public sealed class OdCodeGenerationResult
{
    public required IReadOnlyList<OdGeneratedFile> Files { get; init; }
    public required IReadOnlyList<OdIssue> Issues { get; init; }
    public bool Succeeded => Issues.All(item => item.Severity != OdIssueSeverity.Error);
}
