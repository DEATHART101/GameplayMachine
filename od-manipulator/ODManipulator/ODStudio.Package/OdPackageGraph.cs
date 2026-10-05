using ODStudio.Model;

namespace ODStudio.Package;

public sealed class OdPackageGraph
{
    private OdPackageGraph(OdProject root, IReadOnlyList<OdProject> projects, IReadOnlyList<OdIssue> issues)
    {
        Root = root;
        Projects = projects;
        Issues = issues;
    }

    public OdProject Root { get; }
    public IReadOnlyList<OdProject> Projects { get; }
    public IReadOnlyList<OdIssue> Issues { get; }

    public IReadOnlyList<OdProject> GetUsedProjects()
    {
        var owners = Projects.SelectMany(project => project.Definitions.Select(definition => (definition.Id, project)))
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First().project);
        var used = new List<OdProject> { Root };
        var usedIds = new HashSet<Guid> { Root.Id };

        for (int index = 0; index < used.Count; index++)
        {
            OdProject project = used[index];
            foreach (Guid referencedId in ReferencedDefinitionIds(project))
            {
                if (!owners.TryGetValue(referencedId, out OdProject? owner) || !usedIds.Add(owner.Id))
                    continue;
                used.Add(owner);
            }
        }

        return used;
    }

    public OdProject ComposeUsedProject()
    {
        IReadOnlyList<OdProject> used = GetUsedProjects();
        IEnumerable<OdProject> emittedProjects = used.Where(project => !OdNetworkPackage.IsPackage(project));
        return new OdProject
        {
            FormatVersion = Root.FormatVersion,
            Id = Root.Id,
            Name = Root.Name,
            DefaultNamespace = Root.DefaultNamespace,
            Description = Root.Description,
            CreatedUtc = Root.CreatedUtc,
            ModifiedUtc = Root.ModifiedUtc,
            Ods = emittedProjects.SelectMany(project => project.Ods).ToList(),
            PackageReferences = Root.PackageReferences,
            Definitions = emittedProjects.SelectMany(project => project.Definitions).ToList(),
            DataSets = emittedProjects.SelectMany(project => project.DataSets).ToList(),
            Scenes = emittedProjects.SelectMany(project => project.Scenes).ToList(),
            CodeFiles = emittedProjects.SelectMany(project => project.CodeFiles).ToList(),
            RootClassId = Root.RootClassId,
            InitialSceneId = Root.InitialSceneId,
            Runtime = Root.Runtime,
            History = Root.History,
            EditorState = Root.EditorState,
            SourceDirectory = Root.SourceDirectory,
        };
    }

    public static Task<OdPackageGraph> LoadAsync(string rootPackagePath,
        CancellationToken cancellationToken = default) =>
        LoadRootAsync(rootPackagePath, cancellationToken);

    public static async Task<OdPackageGraph> LoadForProjectAsync(OdProject root,
        CancellationToken cancellationToken = default)
    {
        OdProject networkPackage = OdNetworkPackage.CreateProject(root.Runtime.NetworkMode);
        var projects = new List<OdProject> { root, networkPackage };
        var issues = new List<OdIssue>();
        var loaded = new Dictionary<Guid, OdProject>
        {
            [root.Id] = root,
            [networkPackage.Id] = networkPackage,
        };
        var active = new HashSet<Guid> { root.Id };
        await LoadReferencesAsync(root, projects, loaded, active, issues, cancellationToken);
        foreach (var duplicate in projects.SelectMany(package => package.Definitions
                     .Select(definition => (definition.Id, Package: package, Definition: definition)))
                     .GroupBy(item => item.Id).Where(group => group.Count() > 1))
            issues.Add(new(OdIssueSeverity.Error, "OD0606",
                $"Permanent definition ID {duplicate.Key} occurs in multiple referenced packages: " +
                string.Join(", ", duplicate.Select(item => item.Package.Name).Distinct(StringComparer.Ordinal)),
                duplicate.Key));
        return new OdPackageGraph(root, projects, issues);
    }

    private static async Task<OdPackageGraph> LoadRootAsync(string rootPackagePath,
        CancellationToken cancellationToken)
    {
        OdProject root = await OdPackageCompiler.LoadAsync(rootPackagePath, cancellationToken);
        return await LoadForProjectAsync(root, cancellationToken);
    }

    private static async Task LoadReferencesAsync(OdProject project, List<OdProject> projects,
        Dictionary<Guid, OdProject> loaded, HashSet<Guid> active, List<OdIssue> issues,
        CancellationToken cancellationToken)
    {
        foreach (OdPackageReference packageReference in project.PackageReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (active.Contains(packageReference.PackageId))
            {
                issues.Add(new(OdIssueSeverity.Error, "OD0603",
                    $"Package reference cycle reaches '{packageReference.Name}' ({packageReference.PackageId}).",
                    packageReference.Id));
                continue;
            }
            string baseDirectory = string.IsNullOrWhiteSpace(project.SourceDirectory)
                ? Environment.CurrentDirectory
                : project.SourceDirectory;
            string packagePath;
            try
            {
                packagePath = Path.IsPathRooted(packageReference.PackagePath)
                    ? Path.GetFullPath(packageReference.PackagePath)
                    : Path.GetFullPath(Path.Combine(baseDirectory, packageReference.PackagePath));
                if (!File.Exists(packagePath))
                    throw new FileNotFoundException("Referenced package does not exist.", packagePath);
                OdProject dependency = await OdPackageCompiler.LoadAsync(packagePath, cancellationToken);
                if (dependency.Id != packageReference.PackageId)
                {
                    issues.Add(new(OdIssueSeverity.Error, "OD0604",
                        $"Reference '{packageReference.Name}' expects package {packageReference.PackageId}, but the file contains {dependency.Id}.",
                        packageReference.Id));
                    continue;
                }

                if (loaded.ContainsKey(dependency.Id))
                    continue;

                loaded.Add(dependency.Id, dependency);
                projects.Add(dependency);
                active.Add(dependency.Id);
                await LoadReferencesAsync(dependency, projects, loaded, active, issues, cancellationToken);
                active.Remove(dependency.Id);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                issues.Add(new(OdIssueSeverity.Error, "OD0605",
                    $"Cannot load referenced package '{packageReference.Name}': {exception.Message}", packageReference.Id));
            }
        }
    }

    private static IEnumerable<Guid> ReferencedDefinitionIds(OdProject project)
    {
        foreach (OdDefinition definition in project.Definitions)
        {
            if (definition is OdClassDefinition classDefinition)
                foreach (Guid inheritedId in classDefinition.IsClasses)
                    yield return inheritedId;
            if (definition is OdStructDefinition { IsStructOf.DefinitionId: { } aliasId })
                yield return aliasId;
            if (definition is OdInterfaceDefinition { BaseInterfaceId: { } baseInterfaceId })
                yield return baseInterfaceId;

            IEnumerable<OdFieldDefinition> fields = definition switch
            {
                OdFieldContainerDefinition container => container.Fields,
                OdInterfaceDefinition interfaceDefinition => interfaceDefinition.Inputs.Concat(interfaceDefinition.Outputs),
                _ => Array.Empty<OdFieldDefinition>(),
            };
            foreach (OdFieldDefinition field in fields)
            {
                if (field.Type.DefinitionId is { } fieldTypeId)
                    yield return fieldTypeId;
                if (field.Type.DictionaryKeyDefinitionId is { } keyTypeId)
                    yield return keyTypeId;
            }
        }

        foreach (OdDataSet dataSet in project.DataSets)
            yield return dataSet.DefinitionId;
        foreach (OdScene scene in project.Scenes)
            foreach (OdSceneObject sceneObject in scene.Objects)
                yield return sceneObject.ClassDefinitionId;
    }
}
