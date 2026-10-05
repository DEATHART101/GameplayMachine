using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ODStudio.Export;
using ODStudio.Generator;
using ODStudio.Model;

namespace ODStudio.Export.Unity;

public sealed class UnityExportTarget : IOdExportTarget, IOdExportPostProcessor
{
    public const string NewtonsoftPackageName = "com.unity.nuget.newtonsoft-json";
    public const string NewtonsoftPackageVersion = "3.2.2";

    public string Id => "unity";
    public string DisplayName => "Unity";
    public string Version => "1.7.3";

    public OdExportPlan BuildPlan(OdExportModel model, OdExportProfile profile)
    {
        var issues = OdProjectValidator.Validate(model.Project).ToList();
        var mapper = new UnityTypeMapper(model.Project);
        var resources = model.Project.Definitions.OfType<OdResourceDefinition>().ToList();
        var options = new OdCSharpGenerationOptions
        {
            TypeNameOverride = mapper.DefinitionTypeName,
            EmitTypeDefinition = definition => definition is not OdResourceDefinition,
            EmitStructMetadata = _ => true,
            UseCustomSerialization = definition => definition is OdResourceDefinition,
            CustomWriteStatement = mapper.CustomWriteStatement,
            CustomReadExpression = mapper.CustomReadExpression,
            IsReferenceType = definition => definition is OdResourceDefinition,
            ClassResourceTypeName = mapper.ClassResourceTypeName,
            ClassResourceWriteStatement = mapper.ClassResourceWriteStatement,
            ClassResourceReadExpression = mapper.ClassResourceReadExpression,
            ResourceReferenceExpression = (definition, id) =>
                $"global::ODStudio.Unity.Runtime.ODUnityResourceRegistry.Resolve<{(definition is OdClassDefinition classDefinition ? mapper.ClassResourceTypeName(classDefinition) : mapper.DefinitionTypeName(definition) ?? $"global::{OdGenerationContext.NamespaceIdentifier(definition.Namespace)}.{OdGenerationContext.Identifier(definition.Name)}")}>(new global::GMCore.ODResourceID(new global::System.Guid(\"{id:N}\")))!",
            EmitClassResourceDefinitions = false,
            PrivateSerializedFieldAttribute = "[global::UnityEngine.SerializeField]",
        };
        OdCodeGenerationResult core = OdCSharpGenerator.GenerateFiles(model.Project, options);
        foreach (OdIssue issue in core.Issues)
            if (!issues.Contains(issue))
                issues.Add(issue);

        ValidateUnity(model.Project, issues);
        var artifacts = core.Files.Select(file => file.OverwriteExisting
            ? new OdExportArtifact(
                "Runtime/Generated/" + file.RelativePath,
                file.Content,
                StableId: "core:" + file.RelativePath)
            : new OdExportArtifact(
                file.RelativePath,
                file.Content,
                OverwriteExisting: false,
                LegacyRelativePaths: new[] { "Runtime/Generated/" + LegacyUserPath(file.RelativePath) })).ToList();

        foreach (OdScope scope in model.Project.Ods.OrderBy(item => item.Namespace, StringComparer.Ordinal))
        {
            var context = new OdGenerationContext(model.Project, scope, issues, options);
            string userNamespaceDirectory = $"{OdCSharpGenerator.UserCodeDirectory}/{OdGenerationContext.NamespaceDirectory(scope.Namespace)}";
            foreach (OdResourceDefinition definition in resources
                         .Where(item => string.Equals(item.Namespace, scope.Namespace, StringComparison.Ordinal))
                         .OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                artifacts.Add(new OdExportArtifact(
                    $"Runtime/Unity/Resources/{OdGenerationContext.SafeIdentifier(definition.Name)}.g.cs",
                    UnityResourceEmitter.EmitResource(context, definition, mapper),
                    StableId: $"resource:{definition.Id:N}"));
                artifacts.Add(new OdExportArtifact(
                    $"{userNamespaceDirectory}/Resources/{OdGenerationContext.SafeIdentifier(definition.Name)}.cs",
                    UnityResourceEmitter.EmitResourceExtension(context, definition),
                    OverwriteExisting: false,
                    LegacyRelativePaths: new[]
                    {
                        $"{userNamespaceDirectory}/Unity/Resources/{OdGenerationContext.SafeIdentifier(definition.Name)}.cs",
                        $"Runtime/Unity/Resources/{OdGenerationContext.SafeIdentifier(definition.Name)}.cs",
                    }));
            }

            foreach (OdClassDefinition definition in model.Project.Definitions.OfType<OdClassDefinition>()
                         .Where(item => string.Equals(item.Namespace, scope.Namespace, StringComparison.Ordinal))
                         .OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                artifacts.Add(new OdExportArtifact(
                    $"Runtime/Unity/ClassResources/{OdGenerationContext.SafeIdentifier(definition.Name)}Resource.g.cs",
                    UnityResourceEmitter.EmitClassResource(context, definition, mapper),
                    StableId: $"class-resource:{definition.Id:N}"));
                artifacts.Add(new OdExportArtifact(
                    $"Runtime/Unity/Displayers/{OdGenerationContext.SafeIdentifier(definition.Name)}DisplayerBase.g.cs",
                    UnityResourceEmitter.EmitDisplayerBase(context, definition),
                    StableId: $"displayer:{definition.Id:N}"));
            }

            foreach (OdScene scene in model.Project.Scenes
                         .Where(item => item.OdId == scope.Id)
                         .OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                artifacts.Add(new OdExportArtifact(
                    $"Runtime/Unity/Scenes/{OdGenerationContext.SafeIdentifier(scene.Name)}SceneAsset.g.cs",
                    UnityResourceEmitter.EmitSceneAsset(context, scene),
                    StableId: $"scene-asset:{scene.Id:N}"));
            }
        }

        artifacts.Add(new OdExportArtifact("Runtime/Unity/ODUnityRuntime.g.cs",
            UnityResourceEmitter.EmitRuntimeSupport(), StableId: "unity-runtime"));
        // Unity requires a ScriptableObject's source file name to match its class name.
        artifacts.Add(new OdExportArtifact("Runtime/Unity/ODUnityResourceRegistry.cs",
            UnityResourceEmitter.EmitResourceRegistry(), StableId: "unity-resource-registry"));
        artifacts.Add(new OdExportArtifact("Editor/ODUnityResourceEditor.g.cs",
            UnityResourceEmitter.EmitEditorSupport(model.Project, OdResourceCatalog.BuildSignature(model.Project)),
            StableId: "unity-editor"));
        artifacts.Add(new OdExportArtifact("Editor/ODUnitySwitchStructDrawers.g.cs",
            UnityResourceEmitter.EmitSwitchStructDrawers(model.Project), StableId: "unity-switch-drawers"));

        AddStableMetaFiles(model.Project.Id, artifacts);
        return new OdExportPlan
        {
            TargetId = Id,
            TargetVersion = Version,
            Artifacts = artifacts,
            Issues = issues,
        };
    }

    public static string? EnsureUnityProjectDependencies(string outputDirectory)
    {
        DirectoryInfo? directory = new(Path.GetFullPath(outputDirectory));
        string? manifestPath = null;
        while (directory is not null)
        {
            if (string.Equals(directory.Name, "Assets", StringComparison.OrdinalIgnoreCase) && directory.Parent is not null)
            {
                string candidate = Path.Combine(directory.Parent.FullName, "Packages", "manifest.json");
                if (File.Exists(candidate))
                {
                    manifestPath = candidate;
                    break;
                }
            }

            string projectCandidate = Path.Combine(directory.FullName, "Packages", "manifest.json");
            if (Directory.Exists(Path.Combine(directory.FullName, "Assets")) && File.Exists(projectCandidate))
            {
                manifestPath = projectCandidate;
                break;
            }
            directory = directory.Parent;
        }
        if (manifestPath is null)
            return null;

        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject() ??
            throw new InvalidDataException($"Unity package manifest '{manifestPath}' is invalid.");
        JsonObject dependencies = manifest["dependencies"]?.AsObject() ??
            throw new InvalidDataException($"Unity package manifest '{manifestPath}' has no dependencies object.");
        if (dependencies.ContainsKey(NewtonsoftPackageName))
            return manifestPath;

        dependencies[NewtonsoftPackageName] = NewtonsoftPackageVersion;
        string json = manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        File.WriteAllText(manifestPath, json, new UTF8Encoding(false));
        return manifestPath;
    }

    public Task PostProcessAsync(OdExportModel model, OdExportRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureUnityProjectDependencies(request.OutputDirectory);
        return Task.CompletedTask;
    }

    private static void ValidateUnity(OdProject project, ICollection<OdIssue> issues)
    {
        // Resource folders are authored by GameplayMachine Editor and materialized as Unity assets by the generated editor importer.
    }

    private static string LegacyUserPath(string relativePath) =>
        relativePath[(OdCSharpGenerator.UserCodeDirectory.Length + 1)..];

    private static void AddStableMetaFiles(Guid projectId, List<OdExportArtifact> artifacts)
    {
        var metaFiles = new List<OdExportArtifact>();
        foreach (OdExportArtifact artifact in artifacts.Where(item => item.OverwriteExisting && item.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            string stableId = artifact.StableId ?? artifact.RelativePath;
            string guid = StableGuid(projectId, stableId);
            metaFiles.Add(new OdExportArtifact(artifact.RelativePath + ".meta", UnityResourceEmitter.EmitMeta(guid),
                StableId: "meta:" + stableId));
        }
        artifacts.AddRange(metaFiles);
    }

    private static string StableGuid(Guid projectId, string stableId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"odstudio:unity:{projectId:N}:{stableId}"));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }
}

internal sealed class UnityTypeMapper
{
    private readonly OdProject _project;

    public UnityTypeMapper(OdProject project)
    {
        _project = project;
    }

    public string? DefinitionTypeName(OdDefinition definition)
        => null;

    public string ClassResourceTypeName(OdClassDefinition definition) =>
        $"global::{OdGenerationContext.NamespaceIdentifier(definition.Namespace)}.{OdGenerationContext.Identifier(definition.Name)}Resource";

    public string ClassResourceWriteStatement(OdClassDefinition definition, string expression) =>
        $"global::ODStudio.Unity.Runtime.ODUnityResourceRegistry.Write(writer, {expression});";

    public string ClassResourceReadExpression(OdClassDefinition definition) =>
        $"global::ODStudio.Unity.Runtime.ODUnityResourceRegistry.Read<{ClassResourceTypeName(definition)}>(reader)";

    public string? CustomWriteStatement(OdDefinition definition, string expression)
        => $"global::ODStudio.Unity.Runtime.ODUnityResourceRegistry.Write(writer, {expression});";

    public string? CustomReadExpression(OdDefinition definition)
    {
        string typeName = DefinitionTypeName(definition) ??
                          $"global::{OdGenerationContext.NamespaceIdentifier(definition.Namespace)}.{OdGenerationContext.Identifier(definition.Name)}";
        return $"global::ODStudio.Unity.Runtime.ODUnityResourceRegistry.Read<{typeName}>(reader)";
    }

    public string FieldTypeName(OdGenerationContext context, OdFieldDefinition field)
    {
        OdTypeReference type = field.Type;
        if (type.Container == OdContainerKind.Single)
            return context.FieldTypeName(field);
        var element = new OdTypeReference { BuiltIn = type.BuiltIn, DefinitionId = type.DefinitionId, Nullable = type.Nullable };
        element.IsResourceClass = type.IsResourceClass;
        string elementName = context.TypeName(element, includeNullable: false);
        return type.Container switch
        {
            OdContainerKind.List => $"global::System.Collections.Generic.List<{elementName}>",
            OdContainerKind.Set => $"global::ODStudio.Unity.Runtime.SerializableSet<{elementName}>",
            OdContainerKind.Dictionary => $"global::ODStudio.Unity.Runtime.SerializableDictionary<{context.DictionaryKeyTypeName(type)}, {elementName}>",
            _ => context.TypeName(type),
        };
    }

    public bool IsLiveClassReference(OdFieldDefinition field) =>
        !field.Type.IsResourceClass && field.Type.DefinitionId is { } id &&
        (_project.Definitions.FirstOrDefault(item => item.Id == id) is OdClassDefinition ||
         OdNetworkPackage.RuntimeTypeName(id) is not null);
}
