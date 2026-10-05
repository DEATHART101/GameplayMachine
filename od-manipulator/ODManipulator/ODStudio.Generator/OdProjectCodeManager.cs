using ODStudio.Model;

namespace ODStudio.Generator;

public static class OdProjectCodeManager
{
    public static void EnsureCodeFiles(OdProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.EnsureOdScopes();
        foreach (OdScope scope in project.Ods)
        {
            var context = new OdGenerationContext(project, scope, new List<OdIssue>(), new OdCSharpGenerationOptions());
            string namespaceDirectory = OdGenerationContext.NamespaceDirectory(scope.Namespace);
            foreach (OdInterfaceDefinition definition in context.OwnedDefinitions.OfType<OdInterfaceDefinition>()
                         .Where(item => !item.Abstract))
                Ensure(project, definition.Id, OdCodeFileKind.Interface,
                    $"{namespaceDirectory}/{OdBehaviorEmitter.ImplementationPath(context, definition)}",
                    OdBehaviorEmitter.EmitImplementation(context, definition));

            foreach (OdClassDefinition owner in context.OwnedDefinitions.OfType<OdClassDefinition>())
            foreach (OdFieldDefinition field in owner.Fields.Where(item => item.Mode == OdFieldMode.Driven))
                Ensure(project, field.Id, OdCodeFileKind.DrivenValue,
                    $"{namespaceDirectory}/DrivenValues/{OdGenerationContext.SafeIdentifier(owner.Name)}_{OdGenerationContext.SafeIdentifier(field.Name)}.cs",
                    OdDrivenEmitter.EmitImplementation(context, owner, field));
        }
    }

    public static OdCodeFile GetOrCreateForEntity(OdProject project, Guid ownerId)
    {
        EnsureCodeFiles(project);
        return project.CodeFiles.FirstOrDefault(item => item.OwnerId == ownerId)
               ?? throw new InvalidOperationException("This item does not have an editable OD code file.");
    }

    public static OdCodeFile GetOrCreateResolverFile(OdProject project)
    {
        EnsureCodeFiles(project);
        OdCodeFile? existing = project.CodeFiles.FirstOrDefault(item => item.Kind == OdCodeFileKind.Resolver);
        if (existing is not null)
            return existing;

        string rootDirectory = OdGenerationContext.NamespaceDirectory(project.DefaultNamespace);
        var file = new OdCodeFile
        {
            OwnerId = project.Id,
            Kind = OdCodeFileKind.Resolver,
            RelativePath = $"{rootDirectory}/Resolvers/{SafeProjectName(project)}Resolvers.cs",
            Content = ResolverSource(project),
        };
        project.CodeFiles.Add(file);
        return file;
    }

    public static OdCodeFile GetOrCreateGameplayMachineFunctionFile(
        OdProject project,
        OdGameplayMachineFunction function)
    {
        EnsureCodeFiles(project);
        OdGameplayMachineFunctionDescriptor descriptor = OdGameplayMachineFunctionCatalog.Get(function);
        if (!OdGameplayMachineFunctionCatalog.IsAvailable(project, descriptor))
            throw new InvalidOperationException(
                $"GameplayMachine function '{descriptor.Name}' is unavailable in {project.Runtime.NetworkMode} mode.");
        string relativePath = OdGameplayMachineFunctionCatalog.RelativePath(project, descriptor);
        Ensure(project, descriptor.CodeOwnerId, OdCodeFileKind.GameplayMachine, relativePath,
            OdGameplayMachineFunctionCatalog.EmitImplementation(project, descriptor));
        return project.CodeFiles.Single(item =>
            item.Kind == OdCodeFileKind.GameplayMachine && item.OwnerId == descriptor.CodeOwnerId);
    }

    public static bool HasGameplayMachineFunctionFile(OdProject project, OdGameplayMachineFunction function)
    {
        OdGameplayMachineFunctionDescriptor descriptor = OdGameplayMachineFunctionCatalog.Get(function);
        string relativePath = OdGameplayMachineFunctionCatalog.RelativePath(project, descriptor);
        return project.CodeFiles.Any(item =>
            (item.Kind == OdCodeFileKind.GameplayMachine && item.OwnerId == descriptor.CodeOwnerId) ||
            string.Equals(item.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
    }

    private static void Ensure(OdProject project, Guid ownerId, OdCodeFileKind kind, string relativePath, string content)
    {
        string normalized = relativePath.Replace('\\', '/');
        OdCodeFile? existing = project.CodeFiles.FirstOrDefault(item => item.OwnerId == ownerId && item.Kind == kind)
                               ?? project.CodeFiles.FirstOrDefault(item =>
                                   string.Equals(item.RelativePath, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (!string.Equals(existing.RelativePath, normalized, StringComparison.OrdinalIgnoreCase))
            {
                TryMoveProjectFile(project, existing.RelativePath, normalized);
                existing.RelativePath = normalized;
            }
            existing.OwnerId ??= ownerId;
            if (existing.Kind == OdCodeFileKind.Other)
                existing.Kind = kind;
            return;
        }
        project.CodeFiles.Add(new OdCodeFile
        {
            OwnerId = ownerId,
            Kind = kind,
            RelativePath = normalized,
            Content = content,
        });
    }

    private static void TryMoveProjectFile(OdProject project, string oldRelativePath, string newRelativePath)
    {
        if (string.IsNullOrWhiteSpace(project.SourceDirectory) ||
            !File.Exists(Path.Combine(project.SourceDirectory, "project.json")))
            return;
        string codeRoot = Path.GetFullPath(Path.Combine(project.SourceDirectory, "code"));
        string oldPath = Path.GetFullPath(Path.Combine(codeRoot, oldRelativePath));
        string newPath = Path.GetFullPath(Path.Combine(codeRoot, newRelativePath));
        if (!oldPath.StartsWith(codeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !newPath.StartsWith(codeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(oldPath) || File.Exists(newPath))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.Move(oldPath, newPath);
    }

    internal static string SafeProjectName(OdProject project) =>
        OdGenerationContext.SafeIdentifier(string.IsNullOrWhiteSpace(project.Name) ? "Game" : project.Name);

    private static string ResolverSource(OdProject project)
    {
        string rootNamespace = OdGenerationContext.NamespaceIdentifier(project.DefaultNamespace);
        return $$"""
            #nullable enable

            namespace {{rootNamespace}}
            {
                // Project-owned partition and interest resolver implementations belong here.
            }
            """;
    }
}
