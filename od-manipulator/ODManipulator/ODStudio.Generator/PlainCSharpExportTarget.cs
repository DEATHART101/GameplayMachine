using ODStudio.Export;
using ODStudio.Model;

namespace ODStudio.Generator;

public sealed class PlainCSharpExportTarget : IOdExportTarget
{
    public string Id => "csharp";
    public string DisplayName => "Plain C#";
    public string Version => "1.2.0";

    public OdExportPlan BuildPlan(OdExportModel model, OdExportProfile profile)
    {
        OdCodeGenerationResult result = OdCSharpGenerator.GenerateFiles(model.Project);
        return new OdExportPlan
        {
            TargetId = Id,
            TargetVersion = Version,
            Issues = result.Issues,
            Artifacts = result.Files.Select(file => new OdExportArtifact(
                file.RelativePath,
                file.Content,
                file.OverwriteExisting,
                LegacyRelativePaths: file.OverwriteExisting ? null : new[] { LegacyUserPath(file.RelativePath) })).ToList(),
        };
    }

    private static string LegacyUserPath(string relativePath) =>
        relativePath[(OdCSharpGenerator.UserCodeDirectory.Length + 1)..];
}
