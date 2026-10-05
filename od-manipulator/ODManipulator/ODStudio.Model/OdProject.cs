using System.Text.Json.Serialization;

namespace ODStudio.Model;

public sealed class OdProject
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "NewODProject";
    public string DefaultNamespace { get; set; } = "Game.OD";
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public List<OdScope> Ods { get; set; } = new();
    public List<OdPackageReference> PackageReferences { get; set; } = new();
    public List<OdDefinition> Definitions { get; set; } = new();
    public List<OdDataSet> DataSets { get; set; } = new();
    public List<OdScene> Scenes { get; set; } = new();
    public List<OdCodeFile> CodeFiles { get; set; } = new();
    public Guid? RootClassId { get; set; }
    public Guid? InitialSceneId { get; set; }
    public OdRuntimeSettings Runtime { get; set; } = new();
    public List<OdChangeRecord> History { get; set; } = new();
    public OdEditorState EditorState { get; set; } = new();

    [JsonIgnore]
    public string SourceDirectory { get; set; } = string.Empty;

    public OdEntity? FindEntity(Guid id)
    {
        OdScope? od = Ods.FirstOrDefault(item => item.Id == id);
        if (od is not null)
            return od;

        OdPackageReference? packageReference = PackageReferences.FirstOrDefault(item => item.Id == id);
        if (packageReference is not null)
            return packageReference;

        foreach (var definition in Definitions)
        {
            if (definition.Id == id)
                return definition;

            foreach (var child in GetDefinitionChildren(definition))
                if (child.Id == id)
                    return child;
        }

        foreach (var dataSet in DataSets)
        {
            if (dataSet.Id == id)
                return dataSet;
            foreach (var record in dataSet.Records)
                if (record.Id == id)
                    return record;
        }

        foreach (var scene in Scenes)
        {
            if (scene.Id == id)
                return scene;
            foreach (var sceneObject in scene.Objects)
                if (sceneObject.Id == id)
                    return sceneObject;
        }

        return null;
    }

    public OdScope? FindOd(OdDefinition definition) =>
        Ods.FirstOrDefault(item => string.Equals(item.Namespace, definition.Namespace, StringComparison.Ordinal));

    public OdScope? FindOd(OdDataSet dataSet) =>
        Definitions.FirstOrDefault(item => item.Id == dataSet.DefinitionId) is { } definition
            ? FindOd(definition)
            : null;

    public OdScope? FindOd(OdScene scene) => Ods.FirstOrDefault(item => item.Id == scene.OdId);

    public void EnsureOdScopes()
    {
        if (Ods.Count != 0)
            return;

        var namespaces = Definitions.Select(item => item.Namespace)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (namespaces.Count == 0)
            namespaces.Add(string.IsNullOrWhiteSpace(DefaultNamespace) ? "Game.OD" : DefaultNamespace);

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string odNamespace in namespaces)
        {
            string preferredName = string.Equals(odNamespace, DefaultNamespace, StringComparison.Ordinal)
                ? Name
                : odNamespace.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "OD";
            string name = UniqueOdName(SafeIdentifier(preferredName), usedNames);
            Ods.Add(new OdScope { Name = name, Namespace = odNamespace });
        }
        DefaultNamespace = Ods[0].Namespace;
    }

    private static string UniqueOdName(string preferredName, HashSet<string> usedNames)
    {
        if (usedNames.Add(preferredName))
            return preferredName;
        for (int index = 2; ; index++)
        {
            string candidate = preferredName + index;
            if (usedNames.Add(candidate))
                return candidate;
        }
    }

    private static string SafeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "OD";
        char[] characters = value.Select((character, index) =>
            char.IsLetter(character) || character == '_' || (index > 0 && char.IsDigit(character)) ? character : '_').ToArray();
        return new string(characters);
    }

    public IReadOnlyList<OdClassDefinition> GetInheritedClasses(OdClassDefinition definition,
        IEnumerable<OdDefinition>? referencedDefinitions = null)
    {
        var classesById = Definitions.Concat(referencedDefinitions ?? Array.Empty<OdDefinition>())
            .Concat(OdNetworkPackage.CreateProject(Runtime.NetworkMode).Definitions)
            .OfType<OdClassDefinition>()
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var result = new List<OdClassDefinition>();
        var visited = new HashSet<Guid> { definition.Id };

        void AddClassAndAncestors(Guid classId)
        {
            if (!visited.Add(classId) || !classesById.TryGetValue(classId, out var inheritedClass))
                return;

            result.Add(inheritedClass);
            foreach (Guid inheritedClassId in inheritedClass.IsClasses)
                AddClassAndAncestors(inheritedClassId);
        }

        foreach (Guid inheritedClassId in definition.IsClasses)
            AddClassAndAncestors(inheritedClassId);

        return result;
    }

    public IReadOnlyList<OdInterfaceDefinition> GetInheritedInterfaces(OdInterfaceDefinition definition,
        IEnumerable<OdDefinition>? referencedDefinitions = null)
    {
        var interfacesById = Definitions.Concat(referencedDefinitions ?? Array.Empty<OdDefinition>())
            .OfType<OdInterfaceDefinition>()
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var result = new List<OdInterfaceDefinition>();
        var visited = new HashSet<Guid> { definition.Id };
        Guid? current = definition.BaseInterfaceId;
        while (current is { } id && visited.Add(id) && interfacesById.TryGetValue(id, out var inherited))
        {
            result.Add(inherited);
            current = inherited.BaseInterfaceId;
        }
        result.Reverse();
        return result;
    }

    public static IEnumerable<OdEntity> GetDefinitionChildren(OdDefinition definition) => definition switch
    {
        OdFieldContainerDefinition fields => fields.Fields,
        OdEnumDefinition enumDefinition => enumDefinition.Members,
        OdInterfaceDefinition interfaceDefinition => interfaceDefinition.Inputs.Concat<OdEntity>(interfaceDefinition.Outputs),
        _ => Array.Empty<OdEntity>(),
    };
}

public enum OdPartitionResolverKind
{
    None,
    Grid,
    Custom,
}

public enum OdNetworkMode
{
    StateSync = 0,
    Lockstep = 1,
}

public enum OdLockstepTickMode
{
    FixedRate = 0,
    InputDriven = 1,
}

public enum OdStateSyncTickMode
{
    None = 0,
    FixedRate = 1,
}

public sealed class OdRuntimeSettings
{
    public OdNetworkMode NetworkMode { get; set; }
    public OdStateSyncTickMode StateSyncTickMode { get; set; } = OdStateSyncTickMode.FixedRate;
    public OdLockstepTickMode LockstepTickMode { get; set; } = OdLockstepTickMode.FixedRate;
    public int TickIntervalMilliseconds { get; set; } = 50;
    public float SpatialCellSize { get; set; } = 32f;
    public OdPartitionResolverKind PartitionResolver { get; set; }
    public float PartitionCellSize { get; set; } = 32f;
    public bool PartitionIncludeY { get; set; }
    public bool ProvisionPartitions { get; set; }
    public float LoadMargin { get; set; }
    public float UnloadDelaySeconds { get; set; } = 5f;
    public int MaxLoadsPerUpdate { get; set; } = 8;
    public bool ManagePartitionLoading { get; set; } = true;
}

public enum OdCodeFileKind
{
    Interface,
    DrivenValue,
    GameplayMachine,
    Runtime,
    Resolver,
    Other,
}

public sealed class OdCodeFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OwnerId { get; set; }
    public OdCodeFileKind Kind { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public enum OdChangeKind
{
    Create,
    Delete,
    Rename,
    Update,
    Undo,
    Redo,
}

public sealed class OdChangeRecord
{
    public long Sequence { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public OdChangeKind Kind { get; set; }
    public Guid? TargetId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public Dictionary<string, string?> Details { get; set; } = new(StringComparer.Ordinal);
}
