using System.Security;
using System.Text;
using ODStudio.Generator;
using ODStudio.Model;

namespace ODStudio.Editor;

internal sealed record OdCodeWorkspace(string LaunchPath, string SolutionPath);

internal static class OdCodeWorkspaceService
{
    public static async Task<OdCodeWorkspace> PrepareAsync(OdProject project,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(project.SourceDirectory))
            throw new InvalidOperationException("Save the OD project before editing code.");

        string codeRoot = Path.Combine(project.SourceDirectory, "code");
        string workspaceRoot = Path.Combine(codeRoot, ".gameplaymachine-editor");
        string generatedRoot = Path.Combine(workspaceRoot, "Generated");
        Directory.CreateDirectory(codeRoot);
        OdCodeGenerationResult generation = await OdCSharpGenerator.GenerateAsync(
            project, generatedRoot, cancellationToken);
        if (!generation.Succeeded)
            throw new InvalidOperationException(string.Join(Environment.NewLine,
                generation.Issues.Where(item => item.Severity == OdIssueSeverity.Error)
                    .Select(item => $"{item.Code}: {item.Message}")));

        string projectName = SafeIdentifier(project.Name);
        string projectFile = Path.Combine(workspaceRoot, projectName + ".csproj");
        string solutionFile = Path.Combine(workspaceRoot, projectName + ".sln");
        string workspaceFile = Path.Combine(workspaceRoot, projectName + ".code-workspace");
        await File.WriteAllTextAsync(projectFile, BuildProjectFile(projectName),
            new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(solutionFile, BuildSolutionFile(projectName, project.Id),
            new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(workspaceFile, BuildWorkspaceFile(projectName),
            new UTF8Encoding(false), cancellationToken);
        return new OdCodeWorkspace(workspaceFile, solutionFile);
    }

    private static string BuildProjectFile(string projectName)
    {
        string references = string.Join(Environment.NewLine,
            OdRuntimeReferences.Resolve(AppContext.BaseDirectory)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .Select((path, index) =>
                    $"    <Reference Include=\"GameplayMachineEditorReference{index}\"><HintPath>{SecurityElement.Escape(path)}</HintPath><Private>false</Private></Reference>"));
        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <AssemblyName>{{SecurityElement.Escape(projectName)}}.OD.Authoring</AssemblyName>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="..\**\*.cs" Exclude="..\.gameplaymachine-editor\**\*;..\.odstudio\**\*;..\bin\**\*;..\obj\**\*" />
                <Compile Include="Generated\**\*.g.cs" />
            {{references}}
              </ItemGroup>
            </Project>
            """;
    }

    private static string SafeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Game";
        char[] characters = value.Select((character, index) =>
            char.IsLetter(character) || character == '_' || (index > 0 && char.IsDigit(character))
                ? character
                : '_').ToArray();
        return new string(characters);
    }

    private static string BuildSolutionFile(string projectName, Guid projectId)
    {
        string projectGuid = projectId.ToString("B").ToUpperInvariant();
        const string csharpProjectType = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";
        return $$"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            VisualStudioVersion = 17.0.31903.59
            MinimumVisualStudioVersion = 10.0.40219.1
            Project("{{csharpProjectType}}") = "{{projectName}}", "{{projectName}}.csproj", "{{projectGuid}}"
            EndProject
            Global
              GlobalSection(SolutionConfigurationPlatforms) = preSolution
                Debug|Any CPU = Debug|Any CPU
              EndGlobalSection
              GlobalSection(ProjectConfigurationPlatforms) = postSolution
                {{projectGuid}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                {{projectGuid}}.Debug|Any CPU.Build.0 = Debug|Any CPU
              EndGlobalSection
            EndGlobal
            """;
    }

    private static string BuildWorkspaceFile(string projectName) => $$"""
        {
          "folders": [
            { "path": ".." }
          ],
          "settings": {
            "dotnet.defaultSolution": ".gameplaymachine-editor/{{projectName}}.sln"
          }
        }
        """;
}
