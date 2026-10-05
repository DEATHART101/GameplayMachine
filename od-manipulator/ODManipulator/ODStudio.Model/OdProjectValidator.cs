using System.Text.RegularExpressions;

namespace ODStudio.Model;

public enum OdIssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record OdIssue(OdIssueSeverity Severity, string Code, string Message, Guid? TargetId = null);

public static partial class OdProjectValidator
{
    public static IReadOnlyList<OdIssue> Validate(OdProject project, IEnumerable<OdDefinition>? referencedDefinitions = null)
    {
        var issues = new List<OdIssue>();
        List<OdDefinition> externalDefinitions = (referencedDefinitions ?? Array.Empty<OdDefinition>())
            .Concat(OdNetworkPackage.CreateProject(project.Runtime.NetworkMode).Definitions)
            .GroupBy(item => item.Id)
            .Select(group => group.First())
            .ToList();
        if (project.FormatVersion != OdProject.CurrentFormatVersion)
            issues.Add(new(OdIssueSeverity.Error, "OD0001", $"Unsupported project format {project.FormatVersion}."));
        if (string.IsNullOrWhiteSpace(project.Name))
            issues.Add(new(OdIssueSeverity.Error, "OD0002", "Project name is required."));
        if (project.Ods.Count == 0)
            issues.Add(new(OdIssueSeverity.Error, "OD0003", "Project must contain at least one OD."));

        ValidateIds(project, issues);
        ValidateOds(project, issues);
        ValidatePackageReferences(project, issues);
        ValidateDefinitions(project, externalDefinitions, issues);
        ValidateData(project, externalDefinitions, issues);
        ValidateScenes(project, externalDefinitions, issues);
        ValidateRuntimeSettings(project, issues);
        ValidateCodeFiles(project, issues);
        return issues;
    }

    private static void ValidateCodeFiles(OdProject project, List<OdIssue> issues)
    {
        foreach (OdCodeFile file in project.CodeFiles.Where(item =>
                     item.Kind != OdCodeFileKind.Interface &&
                     !item.RelativePath.StartsWith(".gameplaymachine-editor/", StringComparison.OrdinalIgnoreCase) &&
                     !item.RelativePath.StartsWith(".odstudio/", StringComparison.OrdinalIgnoreCase)))
        {
            if (!ExternalInterfaceDeclarationRegex().IsMatch(file.Content ?? string.Empty))
                continue;
            issues.Add(new OdIssue(
                OdIssueSeverity.Error,
                "OD0700",
                $"Code file '{file.RelativePath}' declares a GameplayMachine interface outside INTERFACES. Register the interface in GameplayMachine Editor and move its implementation to the generated interface file.",
                file.OwnerId ?? project.Id));
        }
    }

    private static void ValidateRuntimeSettings(OdProject project, List<OdIssue> issues)
    {
        OdRuntimeSettings settings = project.Runtime ?? new OdRuntimeSettings();
        if (!Enum.IsDefined(settings.NetworkMode))
            issues.Add(new OdIssue(OdIssueSeverity.Error, "OD0711", "Runtime network mode is invalid.", project.Id));
        if (!Enum.IsDefined(settings.LockstepTickMode))
            issues.Add(new OdIssue(OdIssueSeverity.Error, "OD0713", "Lockstep tick mode is invalid.", project.Id));
        if (!Enum.IsDefined(settings.StateSyncTickMode))
            issues.Add(new OdIssue(OdIssueSeverity.Error, "OD0715", "State Sync tick mode is invalid.", project.Id));
        bool usesFixedRateTick = settings.NetworkMode == OdNetworkMode.Lockstep
            ? settings.LockstepTickMode == OdLockstepTickMode.FixedRate
            : settings.StateSyncTickMode == OdStateSyncTickMode.FixedRate;
        if (usesFixedRateTick && settings.TickIntervalMilliseconds <= 0)
            issues.Add(new OdIssue(OdIssueSeverity.Error, "OD0712", "Tick interval must be greater than zero milliseconds.", project.Id));
        if (settings.NetworkMode == OdNetworkMode.Lockstep)
            return;
        if (!Enum.IsDefined(settings.PartitionResolver))
            issues.Add(new OdIssue(OdIssueSeverity.Error, "OD0701", "Runtime partition resolver mode is invalid.", project.Id));
        if (!float.IsFinite(settings.SpatialCellSize) || settings.SpatialCellSize <= 0)
            issues.Add(new OdIssue(OdIssueSeverity.Error, "OD0702", "Spatial cell size must be greater than zero.", project.Id));
        if (settings.PartitionResolver != OdPartitionResolverKind.None &&
            (!float.IsFinite(settings.PartitionCellSize) || settings.PartitionCellSize <= 0))
            issues.Add(new OdIssue(OdIssueSeverity.Error, "OD0703", "Partition cell size must be greater than zero.", project.Id));
        if (!float.IsFinite(settings.LoadMargin) || settings.LoadMargin < 0)
            issues.Add(new OdIssue(OdIssueSeverity.Error, "OD0704", "Partition load margin cannot be negative.", project.Id));
        if (!float.IsFinite(settings.UnloadDelaySeconds) || settings.UnloadDelaySeconds < 0)
            issues.Add(new OdIssue(OdIssueSeverity.Error, "OD0705", "Partition unload delay cannot be negative.", project.Id));
        if (settings.MaxLoadsPerUpdate < 1)
            issues.Add(new OdIssue(OdIssueSeverity.Error, "OD0706", "Max partition loads per update must be at least one.", project.Id));
        if (settings.ProvisionPartitions && settings.PartitionResolver == OdPartitionResolverKind.None)
            issues.Add(new OdIssue(OdIssueSeverity.Error, "OD0707", "Dynamic partition provisioning requires a partition resolver.", project.Id));
        if (settings.ProvisionPartitions && !HasGameplayMachineFile(project, "OnProvisionPartitions.cs"))
            issues.Add(new OdIssue(OdIssueSeverity.Error, "OD0708", "Dynamic partition provisioning requires an OnProvisionPartitions implementation.", project.Id));
        if (settings.PartitionResolver == OdPartitionResolverKind.Custom)
        {
            if (!HasGameplayMachineFile(project, "OnQueryPartitions.cs"))
                issues.Add(new OdIssue(OdIssueSeverity.Error, "OD0709", "Custom partition resolving requires an OnQueryPartitions implementation.", project.Id));
            if (!HasGameplayMachineFile(project, "TryResolvePartition.cs"))
                issues.Add(new OdIssue(OdIssueSeverity.Error, "OD0710", "Custom partition resolving requires a TryResolvePartition implementation.", project.Id));
        }
    }

    private static bool HasGameplayMachineFile(OdProject project, string fileName) =>
        project.CodeFiles.Any(file => file.Kind == OdCodeFileKind.GameplayMachine &&
            file.RelativePath.EndsWith("/GameplayMachine/" + fileName, StringComparison.OrdinalIgnoreCase));

    private static void ValidateIds(OdProject project, List<OdIssue> issues)
    {
        var ids = new HashSet<Guid>();
        void Add(Guid id, string label)
        {
            if (id == Guid.Empty)
                issues.Add(new(OdIssueSeverity.Error, "OD0100", $"{label} has an empty permanent ID."));
            else if (!ids.Add(id))
                issues.Add(new(OdIssueSeverity.Error, "OD0101", $"Duplicate permanent ID {id} on {label}.", id));
        }

        Add(project.Id, "project");
        foreach (OdScope od in project.Ods)
            Add(od.Id, $"OD {od.Name}");
        foreach (OdPackageReference packageReference in project.PackageReferences)
            Add(packageReference.Id, $"package reference {packageReference.Name}");
        foreach (var definition in project.Definitions)
        {
            Add(definition.Id, definition.Name);
            foreach (var child in OdProject.GetDefinitionChildren(definition))
                Add(child.Id, $"{definition.Name}.{child.Name}");
        }
        foreach (var dataSet in project.DataSets)
        {
            Add(dataSet.Id, dataSet.Name);
            foreach (var record in dataSet.Records)
                Add(record.Id, $"{dataSet.Name}.{record.Name}");
        }
        foreach (OdScene scene in project.Scenes)
        {
            Add(scene.Id, $"scene {scene.Name}");
            foreach (OdSceneObject sceneObject in scene.Objects)
                Add(sceneObject.Id, $"{scene.Name}.{sceneObject.Name}");
        }
    }

    private static void ValidatePackageReferences(OdProject project, List<OdIssue> issues)
    {
        foreach (OdPackageReference packageReference in project.PackageReferences)
        {
            if (packageReference.PackageId == Guid.Empty)
                issues.Add(new(OdIssueSeverity.Error, "OD0600",
                    $"Package reference '{packageReference.Name}' has an empty package ID.", packageReference.Id));
            if (string.IsNullOrWhiteSpace(packageReference.PackagePath))
                issues.Add(new(OdIssueSeverity.Error, "OD0601",
                    $"Package reference '{packageReference.Name}' has no package path.", packageReference.Id));
        }

        foreach (var duplicate in project.PackageReferences.GroupBy(item => item.PackageId)
                     .Where(group => group.Key != Guid.Empty && group.Count() > 1))
            issues.Add(new(OdIssueSeverity.Error, "OD0602",
                $"Package {duplicate.Key} is referenced more than once.", duplicate.First().Id));
    }

    private static void ValidateOds(OdProject project, List<OdIssue> issues)
    {
        foreach (OdScope od in project.Ods)
        {
            ValidateIdentifier(od.Name, "OD", od.Id, issues);
            ValidateNamespace(od.Namespace, od.Id, issues);
        }
        foreach (var duplicate in project.Ods.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
            issues.Add(new(OdIssueSeverity.Error, "OD0004", $"Duplicate OD name '{duplicate.Key}'.", duplicate.First().Id));
        foreach (var duplicate in project.Ods.GroupBy(item => item.Namespace, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
            issues.Add(new(OdIssueSeverity.Error, "OD0005", $"Duplicate OD namespace '{duplicate.Key}'.", duplicate.First().Id));
    }

    private static void ValidateDefinitions(OdProject project, IReadOnlyCollection<OdDefinition> referencedDefinitions,
        List<OdIssue> issues)
    {
        List<OdDefinition> allDefinitions = project.Definitions.Concat(referencedDefinitions).ToList();
        var definitions = allDefinitions.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
        var odNamespaces = project.Ods.Select(item => item.Namespace).ToHashSet(StringComparer.Ordinal);
        foreach (var duplicate in project.Definitions.GroupBy(item => $"{item.Namespace}.{item.Name}", StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
            issues.Add(new(OdIssueSeverity.Error, "OD0200", $"Duplicate definition name '{duplicate.Key}'.", duplicate.First().Id));

        foreach (var definition in project.Definitions)
        {
            ValidateIdentifier(definition.Name, "definition", definition.Id, issues);
            ValidateNamespace(definition.Namespace, definition.Id, issues);
            if (!odNamespaces.Contains(definition.Namespace))
                issues.Add(new(OdIssueSeverity.Error, "OD0006",
                    $"Definition '{definition.Name}' does not belong to an OD namespace.", definition.Id));
            IEnumerable<OdFieldDefinition> fields = definition switch
            {
                OdFieldContainerDefinition container => container.Fields,
                OdInterfaceDefinition interfaceDefinition => interfaceDefinition.Inputs.Concat(interfaceDefinition.Outputs),
                _ => Array.Empty<OdFieldDefinition>(),
            };
            foreach (var duplicate in fields.GroupBy(field => field.Name, StringComparer.Ordinal).Where(group => group.Count() > 1))
                issues.Add(new(OdIssueSeverity.Error, "OD0201", $"Duplicate field '{definition.Name}.{duplicate.Key}'.", duplicate.First().Id));
            foreach (var field in fields)
            {
                ValidateIdentifier(field.Name, "field", field.Id, issues);
                ValidateType(field.Type, definitions, field.Id, issues);
                if (field.Type.DefinitionId is { } fieldDefinitionId &&
                    definitions.TryGetValue(fieldDefinitionId, out OdDefinition? fieldDefinition) &&
                    fieldDefinition is OdInterfaceDefinition)
                {
                    if (definition is not OdResourceDefinition)
                        issues.Add(new(OdIssueSeverity.Error, "OD0270",
                            $"Interface-call field '{definition.Name}.{field.Name}' is currently supported only on Resource definitions.", field.Id));
                    if (field.Type.Container != OdContainerKind.Single)
                        issues.Add(new(OdIssueSeverity.Error, "OD0271",
                            $"Interface-call field '{definition.Name}.{field.Name}' must use the Single collection mode.", field.Id));
                }
                if (project.Runtime.NetworkMode == OdNetworkMode.Lockstep)
                    ValidateLockstepCollectionType(field.Type, definitions, field.Id, issues);
                if (definition is OdResourceDefinition && HasLiveGameplayObjectReference(field.Type, definitions))
                    issues.Add(new(OdIssueSeverity.Error, "OD0238",
                        $"Resource field '{definition.Name}.{field.Name}' cannot reference a live class. Use ClassResource instead.",
                        field.Id));
                if (field.NotNull &&
                    (definition is not OdInterfaceDefinition interfaceDefinition ||
                     !interfaceDefinition.Inputs.Contains(field) ||
                     field.Type.IsResourceClass ||
                     field.Type.Container != OdContainerKind.Single ||
                     (field.Type.DefinitionId is not { } referencedId ||
                      !definitions.TryGetValue(referencedId, out var referencedDefinition) ||
                      referencedDefinition is not OdClassDefinition)))
                {
                    issues.Add(new(OdIssueSeverity.Error, "OD0227",
                        $"NotNull is only supported on single class interface inputs ('{definition.Name}.{field.Name}').",
                        field.Id));
                }
                if (!Enum.IsDefined(field.Mode))
                    issues.Add(new(OdIssueSeverity.Error, "OD0220", $"Field '{definition.Name}.{field.Name}' has an invalid field mode.", field.Id));
            }

            if (definition is OdSwitchStructDefinition switchStructDefinition)
            {
                if (switchStructDefinition.Fields.Count == 0)
                    issues.Add(new(OdIssueSeverity.Error, "OD0218",
                        $"Switch struct '{definition.Name}' must contain at least one subtype.", definition.Id));
                string[] reservedNames = { "SwitchTypes", "SwitchType", "Value", "StructName" };
                foreach (OdFieldDefinition field in switchStructDefinition.Fields
                             .Where(item => reservedNames.Contains(item.Name, StringComparer.Ordinal)))
                    issues.Add(new(OdIssueSeverity.Error, "OD0219",
                        $"Switch struct subtype '{field.Name}' conflicts with a generated member.", field.Id));
            }

            if (definition is OdStructDefinition structDefinition)
            {
                if (structDefinition.IsStructOf is { } aliasType)
                {
                    ValidateType(aliasType, definitions, definition.Id, issues);
                    if (aliasType.Container != OdContainerKind.Single || aliasType.Nullable)
                        issues.Add(new(OdIssueSeverity.Error, "OD0230",
                            $"IsStruct '{definition.Name}' must wrap one non-nullable value.", definition.Id));
                    if (structDefinition.Fields.Count != 0)
                        issues.Add(new(OdIssueSeverity.Error, "OD0231",
                            $"IsStruct '{definition.Name}' cannot declare fields.", definition.Id));
                    if (aliasType.DefinitionId is { } aliasDefinitionId &&
                        (!definitions.TryGetValue(aliasDefinitionId, out OdDefinition? aliasDefinition) ||
                         aliasDefinition is not (OdStructDefinition or OdEnumDefinition)))
                        issues.Add(new(OdIssueSeverity.Error, "OD0233",
                            $"IsStruct '{definition.Name}' must wrap a built-in, enum, or struct type.", definition.Id));
                    else if (aliasType.DefinitionId is { } valueDefinitionId &&
                             definitions.TryGetValue(valueDefinitionId, out OdDefinition? valueDefinition) &&
                             valueDefinition is OdResourceDefinition)
                        issues.Add(new(OdIssueSeverity.Error, "OD0235",
                            $"IsStruct '{definition.Name}' cannot wrap a resource.", definition.Id));
                    if (HasIsStructCycle(structDefinition, definitions))
                        issues.Add(new(OdIssueSeverity.Error, "OD0234",
                            $"IsStruct cycle starts at '{definition.Name}'.", definition.Id));
                }
            }

            if (definition is OdClassDefinition classDefinition)
            {
                if (!Enum.IsDefined(classDefinition.Relevancy))
                    issues.Add(new(OdIssueSeverity.Error, "OD0241",
                        $"Class '{classDefinition.Name}' has an invalid relevancy mode.", classDefinition.Id));
                if (project.Runtime.NetworkMode == OdNetworkMode.StateSync &&
                    !Enum.IsDefined(classDefinition.Partition))
                    issues.Add(new(OdIssueSeverity.Error, "OD0242",
                        $"Class '{classDefinition.Name}' has an invalid partition mode.", classDefinition.Id));
                if ((classDefinition.Relevancy == OdObjectRelevancyMode.Spatial ||
                     project.Runtime.NetworkMode == OdNetworkMode.StateSync &&
                     classDefinition.Partition == OdObjectPartitionMode.Spatial) &&
                    !IsOrInherits(classDefinition, OdNetworkPackage.ActorClassId, definitions, new HashSet<Guid>()))
                    issues.Add(new(OdIssueSeverity.Error, "OD0243",
                        $"Spatial class '{classDefinition.Name}' must inherit Actor.", classDefinition.Id));
                foreach (Guid duplicate in classDefinition.IsClasses.GroupBy(item => item).Where(group => group.Count() > 1).Select(group => group.Key))
                    issues.Add(new(OdIssueSeverity.Error, "OD0202", $"'{classDefinition.Name}' contains duplicate IsClass reference {duplicate}.", classDefinition.Id));
                foreach (Guid isClass in classDefinition.IsClasses.Distinct())
                {
                    if (!definitions.TryGetValue(isClass, out var isDefinition) || isDefinition is not OdClassDefinition)
                        issues.Add(new(OdIssueSeverity.Error, "OD0202", $"IsClass reference {isClass} of '{classDefinition.Name}' is not a class.", classDefinition.Id));
                    else if (isDefinition is OdClassDefinition { PreventInheritance: true } sealedClass)
                        issues.Add(new(OdIssueSeverity.Error, "OD0240",
                            $"Class '{classDefinition.Name}' cannot inherit '{sealedClass.Name}' because it prevents inheritance.",
                            classDefinition.Id));
                }
                if (HasInheritanceCycle(classDefinition, definitions))
                    issues.Add(new(OdIssueSeverity.Error, "OD0203", $"IsClass cycle starts at '{classDefinition.Name}'.", classDefinition.Id));
                foreach (OdClassDefinition repeatedAncestor in FindDiamondAncestors(classDefinition, definitions))
                    issues.Add(new(OdIssueSeverity.Error, "OD0217",
                        $"Diamond inheritance on '{classDefinition.Name}' reaches '{repeatedAncestor.Name}' through multiple IsClass paths.",
                        classDefinition.Id));
                foreach (var conflict in AllClassFields(classDefinition, definitions)
                             .GroupBy(field => field.Name, StringComparer.Ordinal)
                             .Where(group => group.Select(field => field.Id).Distinct().Count() > 1))
                    issues.Add(new(OdIssueSeverity.Error, "OD0208", $"Inherited field '{conflict.Key}' is ambiguous on '{classDefinition.Name}'.", classDefinition.Id));

                ValidateDrivenFields(classDefinition, definitions, issues);
            }

            if (definition is OdEnumDefinition enumDefinition)
            {
                foreach (var member in enumDefinition.Members)
                    ValidateIdentifier(member.Name, "enum member", member.Id, issues);
                foreach (var duplicate in enumDefinition.Members.GroupBy(item => item.Name, StringComparer.Ordinal).Where(group => group.Count() > 1))
                    issues.Add(new(OdIssueSeverity.Error, "OD0204", $"Duplicate enum member '{enumDefinition.Name}.{duplicate.Key}'.", duplicate.First().Id));
            }

            if (definition is OdInterfaceDefinition validatedInterface)
            {
                if (!Enum.IsDefined(validatedInterface.InterfaceType))
                    issues.Add(new(OdIssueSeverity.Error, "OD0212", $"Interface '{validatedInterface.Name}' has an invalid interface type.", validatedInterface.Id));
                if (!Enum.IsDefined(validatedInterface.Prediction))
                    issues.Add(new(OdIssueSeverity.Error, "OD0205", $"Interface '{validatedInterface.Name}' has an invalid prediction type.", validatedInterface.Id));
                if (validatedInterface.InterfaceType == OdInterfaceType.Gameplay &&
                    validatedInterface.Prediction != OdPredictionMode.None)
                    issues.Add(new(OdIssueSeverity.Error, "OD0213",
                        $"Gameplay interface '{validatedInterface.Name}' cannot use prediction.", validatedInterface.Id));
                if (validatedInterface.InterfaceType == OdInterfaceType.PlayerInput &&
                    validatedInterface.Outputs.Count != 0)
                    issues.Add(new(OdIssueSeverity.Error, "OD0206",
                        $"PlayerInput interface '{validatedInterface.Name}' cannot have output fields.", validatedInterface.Id));
                if (validatedInterface.InterfaceType == OdInterfaceType.PlayerInput && validatedInterface.IsRoutine)
                    issues.Add(new(OdIssueSeverity.Error, "OD0207",
                        $"PlayerInput interface '{validatedInterface.Name}' cannot be a routine.", validatedInterface.Id));
                if (project.Runtime.NetworkMode == OdNetworkMode.Lockstep && validatedInterface.IsRoutine)
                    issues.Add(new(OdIssueSeverity.Error, "OD0260",
                        $"Lockstep interface '{validatedInterface.Name}' cannot be a routine.", validatedInterface.Id));
                if (project.Runtime.NetworkMode == OdNetworkMode.Lockstep &&
                    validatedInterface.InterfaceType == OdInterfaceType.PlayerInput &&
                    validatedInterface.Prediction != OdPredictionMode.None)
                    issues.Add(new(OdIssueSeverity.Error, "OD0261",
                        $"Lockstep PlayerInput '{validatedInterface.Name}' cannot use StateSync prediction.", validatedInterface.Id));
                if (validatedInterface.BaseInterfaceId is { } baseId)
                {
                    if (!definitions.TryGetValue(baseId, out OdDefinition? baseDefinition) ||
                        baseDefinition is not OdInterfaceDefinition baseInterface)
                        issues.Add(new(OdIssueSeverity.Error, "OD0272",
                            $"Interface '{validatedInterface.Name}' references a missing base interface.", validatedInterface.Id));
                    else
                    {
                        if (baseInterface.InterfaceType != validatedInterface.InterfaceType ||
                            baseInterface.IsRoutine != validatedInterface.IsRoutine)
                            issues.Add(new(OdIssueSeverity.Error, "OD0273",
                                $"Interface '{validatedInterface.Name}' must use the same interface and routine type as '{baseInterface.Name}'.", validatedInterface.Id));
                        if (baseInterface.Outputs.Count != 0 || validatedInterface.Outputs.Count != 0)
                            issues.Add(new(OdIssueSeverity.Error, "OD0274",
                                $"Inherited interface '{validatedInterface.Name}' cannot declare outputs in the first interface-call version.", validatedInterface.Id));
                    }
                }
                if (HasInterfaceInheritanceCycle(validatedInterface, definitions))
                    issues.Add(new(OdIssueSeverity.Error, "OD0275",
                        $"Interface inheritance cycle starts at '{validatedInterface.Name}'.", validatedInterface.Id));
                foreach (var conflict in AllInterfaceInputs(validatedInterface, definitions)
                             .GroupBy(field => field.Name, StringComparer.Ordinal)
                             .Where(group => group.Select(field => field.Id).Distinct().Count() > 1))
                    issues.Add(new(OdIssueSeverity.Error, "OD0276",
                        $"Inherited interface input '{validatedInterface.Name}.{conflict.Key}' is ambiguous.", validatedInterface.Id));
                if (validatedInterface.Abstract && validatedInterface.InterfaceType != OdInterfaceType.Gameplay)
                    issues.Add(new(OdIssueSeverity.Error, "OD0277",
                        $"Abstract interface '{validatedInterface.Name}' must be an ordinary Gameplay interface.", validatedInterface.Id));
            }

            if (project.Runtime.NetworkMode == OdNetworkMode.Lockstep &&
                definition is OdEventDefinition { Multicast: true } lockstepEvent)
                issues.Add(new(OdIssueSeverity.Error, "OD0262",
                    $"Lockstep event '{lockstepEvent.Name}' cannot use StateSync multicast.", lockstepEvent.Id));

            if (project.Runtime.NetworkMode == OdNetworkMode.Lockstep &&
                definition is OdClassDefinition lockstepClass &&
                lockstepClass.IsClasses.Any(id => id == OdNetworkPackage.ActorClassId ||
                                                   id == OdNetworkPackage.PawnClassId))
                issues.Add(new(OdIssueSeverity.Error, "OD0263",
                    $"Lockstep class '{lockstepClass.Name}' cannot inherit Actor or Pawn because Transform is unavailable.", lockstepClass.Id));
        }
    }

    private static bool IsOrInherits(OdClassDefinition definition, Guid target,
        IReadOnlyDictionary<Guid, OdDefinition> definitions, HashSet<Guid> visited)
    {
        if (definition.Id == target)
            return true;
        if (!visited.Add(definition.Id))
            return false;
        return definition.IsClasses.Any(id => id == target ||
            definitions.TryGetValue(id, out OdDefinition? candidate) && candidate is OdClassDefinition parent &&
            IsOrInherits(parent, target, definitions, visited));
    }

    private static void ValidateDrivenFields(
        OdClassDefinition classDefinition,
        IReadOnlyDictionary<Guid, OdDefinition> definitions,
        List<OdIssue> issues)
    {
        List<OdFieldDefinition> availableFields = AllClassFields(classDefinition, definitions).ToList();
        Dictionary<Guid, OdFieldDefinition> availableById = availableFields
            .GroupBy(field => field.Id)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (OdFieldDefinition field in classDefinition.Fields.Where(item => item.Mode == OdFieldMode.Driven))
        {
            if (field.Replication != OdFieldReplicationMode.None)
                issues.Add(new(OdIssueSeverity.Error, "OD0221",
                    $"Driven field '{classDefinition.Name}.{field.Name}' must use None networking.", field.Id));
            if (field.Type.Container != OdContainerKind.Single)
                issues.Add(new(OdIssueSeverity.Error, "OD0222",
                    $"Driven field '{classDefinition.Name}.{field.Name}' must use the Single collection mode.", field.Id));
            foreach (Guid duplicate in field.DrivenByFields.GroupBy(item => item)
                         .Where(group => group.Count() > 1).Select(group => group.Key))
                issues.Add(new(OdIssueSeverity.Error, "OD0223",
                    $"Driven field '{classDefinition.Name}.{field.Name}' contains duplicate dependency {duplicate}.", field.Id));
            foreach (Guid drivenBy in field.DrivenByFields.Distinct())
            {
                if (drivenBy == field.Id)
                    issues.Add(new(OdIssueSeverity.Error, "OD0224",
                        $"Driven field '{classDefinition.Name}.{field.Name}' cannot depend on itself.", field.Id));
                else if (!availableById.ContainsKey(drivenBy))
                    issues.Add(new(OdIssueSeverity.Error, "OD0225",
                        $"Driven field '{classDefinition.Name}.{field.Name}' references a field outside its class hierarchy.", field.Id));
            }
        }

        var visited = new HashSet<Guid>();
        var visiting = new HashSet<Guid>();
        foreach (OdFieldDefinition field in availableFields.Where(item => item.Mode == OdFieldMode.Driven))
        {
            if (HasDrivenCycle(field))
            {
                issues.Add(new(OdIssueSeverity.Error, "OD0226",
                    $"Driven dependency cycle is reachable from '{classDefinition.Name}.{field.Name}'.", field.Id));
                break;
            }
        }

        bool HasDrivenCycle(OdFieldDefinition field)
        {
            if (visited.Contains(field.Id))
                return false;
            if (!visiting.Add(field.Id))
                return true;
            foreach (Guid dependencyId in field.DrivenByFields)
                if (availableById.TryGetValue(dependencyId, out OdFieldDefinition? dependency) &&
                    dependency.Mode == OdFieldMode.Driven && HasDrivenCycle(dependency))
                    return true;
            visiting.Remove(field.Id);
            visited.Add(field.Id);
            return false;
        }
    }

    private static void ValidateData(OdProject project, IReadOnlyCollection<OdDefinition> referencedDefinitions,
        List<OdIssue> issues)
    {
        var definitions = project.Definitions.Concat(referencedDefinitions).GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var dataSet in project.DataSets)
        {
            if (!definitions.TryGetValue(dataSet.DefinitionId, out var definition) ||
                definition is not OdFieldContainerDefinition container ||
                definition is not (OdClassDefinition or OdStructDefinition or OdResourceDefinition))
                issues.Add(new(OdIssueSeverity.Error, "OD0400", $"Resource folder '{dataSet.Name}' references a missing resource-capable type.", dataSet.Id));
            else
            {
                var fields = definition is OdClassDefinition classDefinition
                    ? GetAllClassFields(classDefinition, definitions)
                    : container.Fields;
                var fieldIds = fields.Select(item => item.Id).ToHashSet();
                foreach (var record in dataSet.Records)
                {
                    foreach (var fieldId in record.Values.Keys.Where(id => !fieldIds.Contains(id)))
                        issues.Add(new(OdIssueSeverity.Error, "OD0401", $"Record '{record.Name}' contains unknown field {fieldId}.", record.Id));
                    foreach (OdFieldDefinition field in fields)
                        if (record.Values.TryGetValue(field.Id, out var value))
                            ValidateConfiguredValue(project, definitions, field.Type, value, record.Id, null, issues);
                }
            }
        }
    }

    private static void ValidateScenes(OdProject project, IReadOnlyCollection<OdDefinition> referencedDefinitions,
        List<OdIssue> issues)
    {
        var definitions = project.Definitions.Concat(referencedDefinitions).GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var odIds = project.Ods.Select(item => item.Id).ToHashSet();
        List<OdClassDefinition> localClasses = project.Definitions.OfType<OdClassDefinition>().ToList();
        if (localClasses.Count > 0 && project.RootClassId is null)
            issues.Add(new(OdIssueSeverity.Error, "OD0508", "A root gameplay object class must be configured.", project.Id));
        else if (project.RootClassId is { } rootClassId && localClasses.All(item => item.Id != rootClassId))
            issues.Add(new(OdIssueSeverity.Error, "OD0509", "The configured root gameplay object class is missing or belongs to another package.", project.Id));

        if (project.Scenes.Count > 0 && project.InitialSceneId is null)
            issues.Add(new(OdIssueSeverity.Error, "OD0510", "An initial scene must be configured.", project.Id));
        else if (project.InitialSceneId is { } initialSceneId && project.Scenes.All(item => item.Id != initialSceneId))
            issues.Add(new(OdIssueSeverity.Error, "OD0511", "The configured initial scene is missing.", project.Id));

        foreach (OdScene scene in project.Scenes)
        {
            if (!odIds.Contains(scene.OdId))
                issues.Add(new(OdIssueSeverity.Error, "OD0500", $"Scene '{scene.Name}' belongs to a missing OD.", scene.Id));
            foreach (var duplicate in scene.Objects.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1))
                issues.Add(new(OdIssueSeverity.Error, "OD0501",
                    $"Scene '{scene.Name}' contains duplicate object name '{duplicate.Key}'.", duplicate.First().Id));

            foreach (OdSceneObject sceneObject in scene.Objects)
            {
                if (sceneObject.ClassDefinitionId == project.RootClassId)
                    issues.Add(new(OdIssueSeverity.Error, "OD0512",
                        $"Scene '{scene.Name}' cannot create the engine-managed root class.", sceneObject.Id));
                if (!definitions.TryGetValue(sceneObject.ClassDefinitionId, out OdDefinition? definition) ||
                    definition is not OdClassDefinition classDefinition)
                {
                    issues.Add(new(OdIssueSeverity.Error, "OD0502",
                        $"Scene object '{sceneObject.Name}' references a missing class.", sceneObject.Id));
                    continue;
                }

                IReadOnlyList<OdFieldDefinition> fields = GetAllClassFields(classDefinition, definitions);
                var fieldIds = fields.Select(item => item.Id).ToHashSet();
                foreach (Guid fieldId in sceneObject.Values.Keys.Where(id => !fieldIds.Contains(id)))
                    issues.Add(new(OdIssueSeverity.Error, "OD0503",
                        $"Scene object '{sceneObject.Name}' contains unknown field {fieldId}.", sceneObject.Id));
                foreach (OdFieldDefinition field in fields)
                    if (sceneObject.Values.TryGetValue(field.Id, out var value))
                        ValidateConfiguredValue(project, definitions, field.Type, value, sceneObject.Id, scene, issues);
            }
        }
    }

    private static IReadOnlyList<OdFieldDefinition> GetAllClassFields(OdClassDefinition definition,
        IReadOnlyDictionary<Guid, OdDefinition> definitions)
    {
        var result = new List<OdFieldDefinition>();
        var visited = new HashSet<Guid>();
        void Add(OdClassDefinition current)
        {
            if (!visited.Add(current.Id))
                return;
            foreach (Guid inheritedId in current.IsClasses)
                if (definitions.TryGetValue(inheritedId, out OdDefinition? inherited) && inherited is OdClassDefinition inheritedClass)
                    Add(inheritedClass);
            result.AddRange(current.Fields);
        }
        Add(definition);
        return result;
    }

    private static void ValidateConfiguredValue(OdProject project,
        IReadOnlyDictionary<Guid, OdDefinition> definitions, OdTypeReference type, System.Text.Json.Nodes.JsonNode? value,
        Guid targetId, OdScene? scene, List<OdIssue> issues)
    {
        if (value is null)
            return;
        var elementType = new OdTypeReference
        {
            BuiltIn = type.BuiltIn,
            DefinitionId = type.DefinitionId,
            IsResourceClass = type.IsResourceClass,
        };
        if (type.Container is OdContainerKind.List or OdContainerKind.Set && value is System.Text.Json.Nodes.JsonArray array)
        {
            foreach (var item in array)
                ValidateConfiguredValue(project, definitions, elementType, item, targetId, scene, issues);
            return;
        }
        if (type.Container == OdContainerKind.Dictionary && value is System.Text.Json.Nodes.JsonObject map)
        {
            foreach (var item in map)
                ValidateConfiguredValue(project, definitions, elementType, item.Value, targetId, scene, issues);
            return;
        }
        if (type.Container != OdContainerKind.Single || type.DefinitionId is not { } definitionId ||
            !definitions.TryGetValue(definitionId, out OdDefinition? definition))
            return;

        if (definition is OdInterfaceDefinition declaredInterface)
        {
            ValidateInterfaceCallValue(project, definitions, declaredInterface, value, targetId, issues);
            return;
        }

        bool isResourceReference = definition is OdResourceDefinition ||
                                   definition is OdClassDefinition && type.IsResourceClass;
        bool isSceneReference = definition is OdClassDefinition && !type.IsResourceClass;
        if (!isResourceReference && !isSceneReference)
            return;
        if (value is not System.Text.Json.Nodes.JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(out string? text) || !Guid.TryParse(text, out Guid referenceId))
        {
            issues.Add(new(OdIssueSeverity.Error, "OD0504", "Configured object reference is not a valid resource or scene object ID.", targetId));
            return;
        }

        if (isResourceReference)
        {
            bool exists = project.DataSets.Any(folder => folder.DefinitionId == definitionId &&
                                                        folder.Records.Any(record => record.Id == referenceId));
            if (!exists)
                issues.Add(new(OdIssueSeverity.Error, "OD0505", $"Configured resource reference {referenceId} does not exist.", targetId));
        }
        else if (scene is null)
        {
            issues.Add(new(OdIssueSeverity.Error, "OD0506", "Resource items cannot reference live gameplay objects.", targetId));
        }
        else
        {
            OdSceneObject? referencedObject = scene.Objects.FirstOrDefault(item => item.Id == referenceId);
            if (referencedObject is null || !IsClassAssignableTo(referencedObject.ClassDefinitionId, definitionId, definitions))
                issues.Add(new(OdIssueSeverity.Error, "OD0507", $"Scene object reference {referenceId} is missing or has an incompatible class.", targetId));
        }
    }

    private static void ValidateInterfaceCallValue(OdProject project,
        IReadOnlyDictionary<Guid, OdDefinition> definitions, OdInterfaceDefinition declaredInterface,
        System.Text.Json.Nodes.JsonNode value, Guid targetId, List<OdIssue> issues)
    {
        if (!OdInterfaceCallValue.TryGetInterfaceId(value, out Guid concreteId) ||
            !definitions.TryGetValue(concreteId, out OdDefinition? concreteDefinition) ||
            concreteDefinition is not OdInterfaceDefinition concreteInterface)
        {
            issues.Add(new(OdIssueSeverity.Error, "OD0520",
                "Configured interface call does not select a valid concrete interface.", targetId));
            return;
        }
        if (!IsInterfaceAssignableTo(concreteInterface, declaredInterface, definitions))
            issues.Add(new(OdIssueSeverity.Error, "OD0521",
                $"Interface '{concreteInterface.Name}' is not assignable to '{declaredInterface.Name}'.", targetId));
        if (concreteInterface.Abstract)
            issues.Add(new(OdIssueSeverity.Error, "OD0522",
                $"Abstract interface '{concreteInterface.Name}' cannot be stored as an interface call.", targetId));
        if (concreteInterface.InterfaceType != OdInterfaceType.Gameplay || concreteInterface.IsRoutine)
            issues.Add(new(OdIssueSeverity.Error, "OD0523",
                $"Interface call '{concreteInterface.Name}' must be an ordinary, non-routine Gameplay interface.", targetId));
        if (InterfaceHierarchy(concreteInterface, definitions).Any(item => item.Outputs.Count != 0))
            issues.Add(new(OdIssueSeverity.Error, "OD0524",
                $"Interface call '{concreteInterface.Name}' cannot have output fields in this version.", targetId));

        System.Text.Json.Nodes.JsonObject? configured = OdInterfaceCallValue.GetValues(value);
        if (configured is null)
        {
            issues.Add(new(OdIssueSeverity.Error, "OD0525",
                "Configured interface call has no parameter object.", targetId));
            return;
        }
        IReadOnlyList<OdFieldDefinition> inputs = AllInterfaceInputs(concreteInterface, definitions);
        var knownKeys = inputs.SelectMany(field => new[] { field.Id.ToString("D"), field.Name })
            .ToHashSet(StringComparer.Ordinal);
        foreach (string unknown in configured.Select(item => item.Key).Where(key => !knownKeys.Contains(key)))
            issues.Add(new(OdIssueSeverity.Warning, "OD0526",
                $"Configured interface call contains orphan parameter '{unknown}'.", targetId));
        foreach (OdFieldDefinition input in inputs)
        {
            System.Text.Json.Nodes.JsonNode? configuredValue =
                configured[input.Id.ToString("D")] ?? configured[input.Name];
            if (configuredValue is not null)
                ValidateConfiguredValue(project, definitions, input.Type, configuredValue, targetId, null, issues);
        }
    }

    private static IReadOnlyList<OdInterfaceDefinition> InterfaceHierarchy(OdInterfaceDefinition definition,
        IReadOnlyDictionary<Guid, OdDefinition> definitions)
    {
        var result = new List<OdInterfaceDefinition>();
        var visited = new HashSet<Guid>();
        void Add(OdInterfaceDefinition current)
        {
            if (!visited.Add(current.Id))
                return;
            if (current.BaseInterfaceId is { } baseId &&
                definitions.TryGetValue(baseId, out OdDefinition? baseDefinition) &&
                baseDefinition is OdInterfaceDefinition baseInterface)
                Add(baseInterface);
            result.Add(current);
        }
        Add(definition);
        return result;
    }

    private static IReadOnlyList<OdFieldDefinition> AllInterfaceInputs(OdInterfaceDefinition definition,
        IReadOnlyDictionary<Guid, OdDefinition> definitions) =>
        InterfaceHierarchy(definition, definitions).SelectMany(item => item.Inputs).ToList();

    private static bool IsInterfaceAssignableTo(OdInterfaceDefinition actual, OdInterfaceDefinition expected,
        IReadOnlyDictionary<Guid, OdDefinition> definitions) =>
        InterfaceHierarchy(actual, definitions).Any(item => item.Id == expected.Id);

    private static bool HasInterfaceInheritanceCycle(OdInterfaceDefinition start,
        IReadOnlyDictionary<Guid, OdDefinition> definitions)
    {
        var visited = new HashSet<Guid>();
        OdInterfaceDefinition? current = start;
        while (current is not null)
        {
            if (!visited.Add(current.Id))
                return true;
            if (current.BaseInterfaceId is not { } baseId ||
                !definitions.TryGetValue(baseId, out OdDefinition? baseDefinition) ||
                baseDefinition is not OdInterfaceDefinition baseInterface)
                return false;
            current = baseInterface;
        }
        return false;
    }

    private static bool IsClassAssignableTo(Guid actualId, Guid expectedId,
        IReadOnlyDictionary<Guid, OdDefinition> definitions)
    {
        if (actualId == expectedId)
            return true;
        if (!definitions.TryGetValue(actualId, out OdDefinition? definition) || definition is not OdClassDefinition actual)
            return false;
        return actual.IsClasses.Any(parent => IsClassAssignableTo(parent, expectedId, definitions));
    }

    private static bool HasIsStructCycle(OdStructDefinition start,
        IReadOnlyDictionary<Guid, OdDefinition> definitions)
    {
        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        return Visit(start);

        bool Visit(OdStructDefinition current)
        {
            if (visited.Contains(current.Id))
                return false;
            if (!visiting.Add(current.Id))
                return true;
            if (current.IsStructOf?.DefinitionId is { } targetId &&
                definitions.TryGetValue(targetId, out OdDefinition? target) &&
                target is OdStructDefinition targetStruct && Visit(targetStruct))
                return true;
            visiting.Remove(current.Id);
            visited.Add(current.Id);
            return false;
        }
    }

    private static void ValidateType(OdTypeReference type, IReadOnlyDictionary<Guid, OdDefinition> definitions,
        Guid targetId, List<OdIssue> issues)
    {
        if (!Enum.IsDefined(type.BuiltIn))
            issues.Add(new(OdIssueSeverity.Error, "OD0209", "Field has an invalid type source.", targetId));
        int sourceCount = (type.BuiltIn != OdBuiltInType.None ? 1 : 0) +
                          (type.DefinitionId.HasValue ? 1 : 0);
        if (sourceCount == 0)
            issues.Add(new(OdIssueSeverity.Error, "OD0210", "Field type is not configured.", targetId));
        else if (sourceCount > 1)
            issues.Add(new(OdIssueSeverity.Error, "OD0214", "Field has more than one type source.", targetId));
        if (type.DefinitionId is { } definitionId && !definitions.ContainsKey(definitionId))
            issues.Add(new(OdIssueSeverity.Error, "OD0211", $"Field references missing type {definitionId}.", targetId));
        if (type.IsResourceClass &&
            (type.DefinitionId is not { } resourceId || !definitions.TryGetValue(resourceId, out var resourceDefinition) ||
             resourceDefinition is not OdClassDefinition))
            issues.Add(new(OdIssueSeverity.Error, "OD0236",
                "ResourceClass type must reference a class definition.", targetId));
        if (type.Container == OdContainerKind.Dictionary)
        {
            if (!Enum.IsDefined(type.DictionaryKeyBuiltIn))
                issues.Add(new(OdIssueSeverity.Error, "OD0215", "Map has an invalid key type source.", targetId));
            int keySourceCount = (type.DictionaryKeyBuiltIn != OdBuiltInType.None ? 1 : 0) +
                                 (type.DictionaryKeyDefinitionId.HasValue ? 1 : 0);
            if (keySourceCount == 0)
                issues.Add(new(OdIssueSeverity.Error, "OD0212", "Map key type is not configured.", targetId));
            else if (keySourceCount > 1)
                issues.Add(new(OdIssueSeverity.Error, "OD0216", "Map has more than one key type source.", targetId));
            if (type.DictionaryKeyDefinitionId is { } keyDefinitionId &&
                (!definitions.TryGetValue(keyDefinitionId, out var keyDefinition) ||
                 keyDefinition is not (OdClassDefinition or OdStructDefinition or OdEnumDefinition or
                     OdSwitchStructDefinition or OdResourceDefinition)))
                issues.Add(new(OdIssueSeverity.Error, "OD0213",
                    $"Map key references missing or unsupported type {keyDefinitionId}.", targetId));
            if (type.DictionaryKeyIsResourceClass &&
                (type.DictionaryKeyDefinitionId is not { } keyResourceId ||
                 !definitions.TryGetValue(keyResourceId, out var keyResourceDefinition) ||
                 keyResourceDefinition is not OdClassDefinition))
                issues.Add(new(OdIssueSeverity.Error, "OD0237",
                    "Map ResourceClass key must reference a class definition.", targetId));
        }
    }

    private static void ValidateLockstepCollectionType(
        OdTypeReference type,
        IReadOnlyDictionary<Guid, OdDefinition> definitions,
        Guid targetId,
        List<OdIssue> issues)
    {
        if (type.Container is not (OdContainerKind.Set or OdContainerKind.Dictionary))
            return;
        OdBuiltInType builtIn = type.Container == OdContainerKind.Set
            ? type.BuiltIn
            : type.DictionaryKeyBuiltIn;
        Guid? definitionId = type.Container == OdContainerKind.Set
            ? type.DefinitionId
            : type.DictionaryKeyDefinitionId;
        bool resourceClass = type.Container == OdContainerKind.Set
            ? type.IsResourceClass
            : type.DictionaryKeyIsResourceClass;
        bool orderable = IsLockstepOrderable(
            builtIn,
            definitionId,
            resourceClass,
            definitions,
            new HashSet<Guid>());
        if (!orderable)
            issues.Add(new(OdIssueSeverity.Error, "OD0264",
                "Lockstep Set values and Map keys must have a deterministic ordering. Struct fields must also be orderable scalar, enum, struct, or live gameplay class values.",
                targetId));
    }

    private static bool IsLockstepOrderable(
        OdBuiltInType builtIn,
        Guid? definitionId,
        bool resourceClass,
        IReadOnlyDictionary<Guid, OdDefinition> definitions,
        HashSet<Guid> visiting)
    {
        if (builtIn != OdBuiltInType.None)
            return true;
        if (resourceClass || definitionId is not { } id ||
            !definitions.TryGetValue(id, out OdDefinition? definition))
            return false;
        if (definition is OdEnumDefinition or OdClassDefinition)
            return true;
        if (definition is OdSwitchStructDefinition switchStructure)
        {
            if (!visiting.Add(id))
                return false;
            bool switchResult = switchStructure.Fields.All(field =>
                field.Type.Container == OdContainerKind.Single &&
                IsLockstepOrderable(field.Type.BuiltIn, field.Type.DefinitionId,
                    field.Type.IsResourceClass, definitions, visiting));
            visiting.Remove(id);
            return switchResult;
        }
        if (definition is not OdStructDefinition structure || !visiting.Add(id))
            return false;

        bool result;
        if (structure.IsStructOf is { } alias)
        {
            result = alias.Container == OdContainerKind.Single &&
                     IsLockstepOrderable(alias.BuiltIn, alias.DefinitionId, alias.IsResourceClass,
                         definitions, visiting);
        }
        else
        {
            result = structure.Fields.All(field =>
                field.Type.Container == OdContainerKind.Single &&
                IsLockstepOrderable(field.Type.BuiltIn, field.Type.DefinitionId,
                    field.Type.IsResourceClass, definitions, visiting));
        }

        visiting.Remove(id);
        return result;
    }

    private static bool HasLiveGameplayObjectReference(OdTypeReference type,
        IReadOnlyDictionary<Guid, OdDefinition> definitions)
    {
        bool liveValue = !type.IsResourceClass && type.DefinitionId is { } valueId &&
                         definitions.TryGetValue(valueId, out OdDefinition? valueDefinition) &&
                         valueDefinition is OdClassDefinition;
        if (liveValue)
            return true;
        return type.Container == OdContainerKind.Dictionary &&
               (!type.DictionaryKeyIsResourceClass && type.DictionaryKeyDefinitionId is { } keyId &&
                definitions.TryGetValue(keyId, out OdDefinition? keyDefinition) &&
                keyDefinition is OdClassDefinition);
    }

    private static bool HasInheritanceCycle(OdClassDefinition start, IReadOnlyDictionary<Guid, OdDefinition> definitions)
    {
        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        return Visit(start);

        bool Visit(OdClassDefinition current)
        {
            if (visited.Contains(current.Id))
                return false;
            if (!visiting.Add(current.Id))
                return true;
            foreach (Guid isClass in current.IsClasses)
                if (definitions.TryGetValue(isClass, out var definition) && definition is OdClassDefinition parent && Visit(parent))
                    return true;
            visiting.Remove(current.Id);
            visited.Add(current.Id);
            return false;
        }
    }

    private static IReadOnlyList<OdClassDefinition> FindDiamondAncestors(
        OdClassDefinition start,
        IReadOnlyDictionary<Guid, OdDefinition> definitions)
    {
        var reached = new HashSet<Guid>();
        var repeated = new List<OdClassDefinition>();
        var repeatedIds = new HashSet<Guid>();

        foreach (Guid isClass in start.IsClasses.Distinct())
            if (definitions.TryGetValue(isClass, out var definition) && definition is OdClassDefinition parent)
                Visit(parent, new HashSet<Guid> { start.Id });

        return repeated;

        void Visit(OdClassDefinition current, HashSet<Guid> path)
        {
            if (!path.Add(current.Id))
                return;
            if (!reached.Add(current.Id))
            {
                if (repeatedIds.Add(current.Id))
                    repeated.Add(current);
                return;
            }

            foreach (Guid isClass in current.IsClasses.Distinct())
                if (definitions.TryGetValue(isClass, out var definition) && definition is OdClassDefinition parent)
                    Visit(parent, new HashSet<Guid>(path));
        }
    }

    private static IEnumerable<OdFieldDefinition> AllClassFields(
        OdClassDefinition start,
        IReadOnlyDictionary<Guid, OdDefinition> definitions)
    {
        var visited = new HashSet<Guid>();
        return Visit(start);

        IEnumerable<OdFieldDefinition> Visit(OdClassDefinition current)
        {
            if (!visited.Add(current.Id))
                yield break;
            foreach (Guid isClass in current.IsClasses)
                if (definitions.TryGetValue(isClass, out var definition) && definition is OdClassDefinition parent)
                    foreach (OdFieldDefinition field in Visit(parent))
                        yield return field;
            foreach (OdFieldDefinition field in current.Fields)
                yield return field;
        }
    }

    private static void ValidateIdentifier(string value, string kind, Guid id, List<OdIssue> issues)
    {
        if (!IdentifierRegex().IsMatch(value ?? string.Empty))
            issues.Add(new(OdIssueSeverity.Error, "OD0500", $"Invalid {kind} identifier '{value}'.", id));
    }

    private static void ValidateNamespace(string value, Guid id, List<OdIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Split('.').Any(part => !IdentifierRegex().IsMatch(part)))
            issues.Add(new(OdIssueSeverity.Error, "OD0501", $"Invalid namespace '{value}'.", id));
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"\b(?:class|struct)\s+[A-Za-z_][A-Za-z0-9_]*(?:\s*<[^>{}]+>)?\s*:\s*[^{}]*\b(?:ICheckableInterface|IReplicatedInterface|IPredictableInterface|ICheckableRoutine)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ExternalInterfaceDeclarationRegex();
}
