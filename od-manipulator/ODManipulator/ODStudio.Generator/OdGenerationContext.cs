using System.Globalization;
using System.Text.Json.Nodes;
using ODStudio.Model;

namespace ODStudio.Generator;

public sealed class OdGenerationContext
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while", "record", "init", "required", "file", "scoped",
    };

    public OdGenerationContext(OdProject project, OdScope scope, List<OdIssue> issues,
        OdCSharpGenerationOptions? options = null)
    {
        Project = project;
        Scope = scope;
        Issues = issues;
        Definitions = project.Definitions.Concat(OdNetworkPackage.CreateProject(project.Runtime.NetworkMode).Definitions)
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());
        OwnedDefinitions = project.Definitions
            .Where(item => string.Equals(item.Namespace, scope.Namespace, StringComparison.Ordinal))
            .ToList();
        var ownedDefinitionIds = OwnedDefinitions.Select(item => item.Id).ToHashSet();
        OwnedDataSets = project.DataSets.Where(item => ownedDefinitionIds.Contains(item.DefinitionId)).ToList();
        OwnedScenes = project.Scenes.Where(item => item.OdId == scope.Id).ToList();
        Options = options ?? new OdCSharpGenerationOptions();
    }

    public OdProject Project { get; }
    public OdScope Scope { get; }
    public List<OdIssue> Issues { get; }
    public OdCSharpGenerationOptions Options { get; }
    public IReadOnlyDictionary<Guid, OdDefinition> Definitions { get; }
    public IReadOnlyList<OdDefinition> OwnedDefinitions { get; }
    public IReadOnlyList<OdDataSet> OwnedDataSets { get; }
    public IReadOnlyList<OdScene> OwnedScenes { get; }
    public string RootNamespace => NamespaceIdentifier(string.IsNullOrWhiteSpace(Scope.Namespace) ? "Generated.OD" : Scope.Namespace);
    public string ModuleTypeName => SafeIdentifier(Scope.Name) + "Module";
    public string ModuleStableName => $"od:{Scope.Id:N}";

    public static string Identifier(string value) => Keywords.Contains(value) ? "@" + value : value;
    public static string SafeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Generated";
        var characters = value.Select((character, index) =>
            char.IsLetter(character) || character == '_' || (index > 0 && char.IsDigit(character)) ? character : '_').ToArray();
        return Identifier(new string(characters));
    }
    public static string NamespaceIdentifier(string value) =>
        string.Join('.', value.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(SafeIdentifier));
    public static string NamespaceDirectory(string value) =>
        string.Join('/', value.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(SafeIdentifier));
    public static string StringLiteral(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

    public string Namespace(OdDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.Namespace) ? RootNamespace : NamespaceIdentifier(definition.Namespace);

    public string FullName(OdDefinition definition) =>
        OdNetworkPackage.RuntimeTypeName(definition.Id, Project.Runtime.NetworkMode) ??
        Options.TypeNameOverride?.Invoke(definition) ??
        $"global::{Namespace(definition)}.{Identifier(definition.Name)}";

    public bool ShouldEmitTypeDefinition(OdDefinition definition) =>
        Options.EmitTypeDefinition?.Invoke(definition) ?? true;

    public bool ShouldEmitStructMetadata(OdStructDefinition definition) =>
        Options.EmitStructMetadata?.Invoke(definition) ?? true;

    public bool UsesCustomSerialization(OdDefinition definition) =>
        Options.UseCustomSerialization?.Invoke(definition) ?? false;

    public string? CustomWriteStatement(OdDefinition definition, string expression) =>
        Options.CustomWriteStatement?.Invoke(definition, expression);

    public string? CustomReadExpression(OdDefinition definition) =>
        Options.CustomReadExpression?.Invoke(definition);

    public bool IsReferenceType(OdDefinition definition) =>
        definition is OdResourceDefinition or OdInterfaceDefinition ||
        Options.IsReferenceType?.Invoke(definition) == true;

    public string InterfaceContractTypeName(OdInterfaceDefinition definition) =>
        $"global::{Namespace(definition)}.I{SafeIdentifier(definition.Name).TrimStart('@')}";

    public string InterfaceCallTypeName(OdInterfaceDefinition definition) =>
        $"global::{Namespace(definition)}.{SafeIdentifier(definition.Name)}Call";

    public string ClassResourceTypeName(OdClassDefinition definition) =>
        Options.ClassResourceTypeName?.Invoke(definition) ?? FullName(definition) + "Resource";

    public string ResourceReferenceExpression(OdDefinition definition, Guid resourceId)
    {
        if (Options.ResourceReferenceExpression is not null)
            return Options.ResourceReferenceExpression(definition, resourceId);
        OdDataSet? folder = Project.DataSets.FirstOrDefault(item => item.DefinitionId == definition.Id &&
                                                                    item.Records.Any(record => record.Id == resourceId));
        OdDataRecord? record = folder?.Records.FirstOrDefault(item => item.Id == resourceId);
        if (folder is null || record is null)
            return $"default({(definition is OdClassDefinition classDefinition ? ClassResourceTypeName(classDefinition) : FullName(definition))})";
        return $"global::{Namespace(definition)}.GeneratedResources.{Identifier(folder.Name)}_{Identifier(record.Name)}";
    }

    public string? ClassResourceWriteStatement(OdClassDefinition definition, string expression) =>
        Options.ClassResourceWriteStatement?.Invoke(definition, expression);

    public string? ClassResourceReadExpression(OdClassDefinition definition) =>
        Options.ClassResourceReadExpression?.Invoke(definition);

    public string ClassStableName(OdClassDefinition definition) =>
        OdNetworkPackage.RuntimeStableName(definition.Id, Project.Runtime.NetworkMode) ??
        $"{ModuleStableName}:class:{definition.Id:N}";
    public string StructStableName(OdDefinition definition) => $"{ModuleStableName}:struct:{definition.Id:N}";
    public string EventStableName(OdDefinition definition) => $"{ModuleStableName}:event:{definition.Id:N}";
    public string FieldStableName(OdClassDefinition owner, OdFieldDefinition field) =>
        OdNetworkPackage.RuntimeFieldStableName(field.Id, Project.Runtime.NetworkMode) ??
        $"{ModuleStableName}:class:{owner.Id:N}:field:{field.Id:N}";

    public string TypeName(OdTypeReference type, bool includeNullable = true)
    {
        bool classReference = IsClassReference(type);
        bool referenceDefinition = type.Container == OdContainerKind.Single &&
                                   type.DefinitionId is { } referenceId &&
                                   Definitions.TryGetValue(referenceId, out OdDefinition? referencedDefinition) &&
                                   IsReferenceType(referencedDefinition);
        string element = CollectionElementTypeName(type);

        bool lockstep = Project.Runtime.NetworkMode == OdNetworkMode.Lockstep;
        string result = type.Container switch
        {
            OdContainerKind.List => $"global::System.Collections.Generic.List<{element}>",
            OdContainerKind.Set when lockstep => $"global::XLockstep.DeterministicSet<{element}>",
            OdContainerKind.Set => $"global::System.Collections.Generic.HashSet<{element}>",
            OdContainerKind.Dictionary when lockstep => $"global::XLockstep.DeterministicMap<{DictionaryKeyTypeName(type)}, {element}>",
            OdContainerKind.Dictionary => $"global::System.Collections.Generic.Dictionary<{DictionaryKeyTypeName(type)}, {element}>",
            _ => element,
        };

        if (includeNullable && (type.Nullable || classReference || IsResourceClass(type) || referenceDefinition) && CanBeNullable(type))
            result += "?";
        return result;
    }

    public bool IsResourceClass(OdTypeReference type) => type.IsResourceClass &&
        type.DefinitionId is { } id && Definitions.TryGetValue(id, out OdDefinition? definition) &&
        definition is OdClassDefinition;

    public OdClassDefinition? ResourceClassDefinition(OdTypeReference type) =>
        IsResourceClass(type) && type.DefinitionId is { } id
            ? (OdClassDefinition)Definitions[id]
            : null;

    public OdClassDefinition? ClassResourceDefinition(OdFieldDefinition field) => ResourceClassDefinition(field.Type);

    public string FieldTypeName(OdFieldDefinition field, bool gameplayCollection = false, bool includeNullable = true)
    {
        return gameplayCollection ? GameplayFieldTypeName(field.Type) : TypeName(field.Type, includeNullable);
    }

    public string GameplayFieldTypeName(OdTypeReference type)
    {
        string element = CollectionElementTypeName(type);
        return type.Container switch
        {
            OdContainerKind.List => $"global::GMCore.Collections.List<{element}>",
            OdContainerKind.Set => $"global::GMCore.Collections.Set<{element}>",
            OdContainerKind.Dictionary => $"global::GMCore.Collections.Map<{DictionaryKeyTypeName(type)}, {element}>",
            _ => TypeName(type),
        };
    }

    public string CollectionStorageTypeName(OdTypeReference type)
    {
        string element = CollectionElementTypeName(type);
        return type.Container switch
        {
            OdContainerKind.List => $"global::GMCore.Collections.ListCollection<{element}>",
            OdContainerKind.Set => $"global::GMCore.Collections.SetCollection<{element}>",
            OdContainerKind.Dictionary => $"global::GMCore.Collections.MapCollection<{DictionaryKeyTypeName(type)}, {element}>",
            _ => throw new ArgumentException("A collection type is required.", nameof(type)),
        };
    }

    public string CollectionStorageTypeName(OdFieldDefinition field)
    {
        return CollectionStorageTypeName(field.Type);
    }

    public string CollectionBinderTypeName(OdTypeReference type)
    {
        string element = CollectionElementTypeName(type);
        return type.Container switch
        {
            OdContainerKind.List => "global::GMCore.FieldChangeEventBinder",
            OdContainerKind.Set => $"global::GMCore.CollectionBinder<{element}>",
            OdContainerKind.Dictionary => $"global::GMCore.CollectionBinder<{DictionaryKeyTypeName(type)}, {element}>",
            _ => throw new ArgumentException("A collection type is required.", nameof(type)),
        };
    }

    public string CollectionBinderTypeName(OdFieldDefinition field)
    {
        return CollectionBinderTypeName(field.Type);
    }

    public string CollectionItemTypeName(OdTypeReference type)
    {
        return type.Container == OdContainerKind.Dictionary
            ? DictionaryKeyTypeName(type)
            : CollectionElementTypeName(type);
    }

    public string CollectionStorageExpression(OdFieldDefinition field)
    {
        string storageType = CollectionStorageTypeName(field);
        return field.DefaultValue is null
            ? $"new {storageType}()"
            : CollectionLiteral(field.Type, field.DefaultValue, field.Id, storageType);
    }

    public string DictionaryKeyTypeName(OdTypeReference type)
    {
        if (type.DictionaryKeyDefinitionId is { } id && Definitions.TryGetValue(id, out var definition))
            return definition is OdClassDefinition resourceClass && type.DictionaryKeyIsResourceClass
                ? ClassResourceTypeName(resourceClass)
                : FullName(definition);
        return BuiltInTypeName(type.DictionaryKeyBuiltIn);
    }

    public bool IsClassReference(OdTypeReference type) =>
        type.Container == OdContainerKind.Single &&
        (!type.IsResourceClass &&
         type.DefinitionId is { } id &&
         Definitions.TryGetValue(id, out var definition) &&
         definition is OdClassDefinition);

    public bool IsLiveClassReference(OdFieldDefinition field) =>
        IsClassReference(field.Type);

    public IEnumerable<(OdClassDefinition Owner, OdFieldDefinition Field)> AllFields(OdClassDefinition definition)
    {
        var visitedClasses = new HashSet<Guid>();
        var visitedFields = new HashSet<Guid>();
        return Visit(definition);

        IEnumerable<(OdClassDefinition Owner, OdFieldDefinition Field)> Visit(OdClassDefinition current)
        {
            if (!visitedClasses.Add(current.Id))
                yield break;
            foreach (Guid isClass in current.IsClasses)
                if (Definitions.TryGetValue(isClass, out var parent) && parent is OdClassDefinition parentClass)
                    foreach (var item in Visit(parentClass))
                        yield return item;
            foreach (OdFieldDefinition field in current.Fields)
                if (visitedFields.Add(field.Id))
                    yield return (current, field);
        }
    }

    public IEnumerable<OdClassDefinition> AllAssignableClasses(OdClassDefinition definition)
    {
        var visited = new HashSet<Guid>();
        return Visit(definition);

        IEnumerable<OdClassDefinition> Visit(OdClassDefinition current)
        {
            if (!visited.Add(current.Id))
                yield break;
            yield return current;
            foreach (Guid isClass in current.IsClasses)
                if (Definitions.TryGetValue(isClass, out OdDefinition? parent) && parent is OdClassDefinition parentClass)
                    foreach (OdClassDefinition item in Visit(parentClass))
                        yield return item;
        }
    }

    public IReadOnlyList<OdInterfaceDefinition> InterfaceHierarchy(OdInterfaceDefinition definition)
    {
        var result = new List<OdInterfaceDefinition>();
        var visited = new HashSet<Guid>();
        void Add(OdInterfaceDefinition current)
        {
            if (!visited.Add(current.Id))
                return;
            if (current.BaseInterfaceId is { } baseId &&
                Definitions.TryGetValue(baseId, out OdDefinition? baseDefinition) &&
                baseDefinition is OdInterfaceDefinition baseInterface)
                Add(baseInterface);
            result.Add(current);
        }
        Add(definition);
        return result;
    }

    public IReadOnlyList<OdFieldDefinition> AllInterfaceInputs(OdInterfaceDefinition definition) =>
        InterfaceHierarchy(definition).SelectMany(item => item.Inputs)
            .GroupBy(item => item.Id).Select(group => group.First()).ToList();

    public bool IsInterfaceAssignableTo(OdInterfaceDefinition actual, OdInterfaceDefinition expected) =>
        InterfaceHierarchy(actual).Any(item => item.Id == expected.Id);

    public IReadOnlyList<(OdClassDefinition Owner, OdFieldDefinition Field)> DirectDrivenByFields(
        OdClassDefinition owner,
        OdFieldDefinition field)
    {
        Dictionary<Guid, (OdClassDefinition Owner, OdFieldDefinition Field)> available =
            AllFields(owner).ToDictionary(item => item.Field.Id);
        return field.DrivenByFields
            .Where(available.ContainsKey)
            .Select(id => available[id])
            .ToList();
    }

    public IReadOnlyList<(OdClassDefinition Owner, OdFieldDefinition Field)> BaseDrivenByFields(
        OdClassDefinition owner,
        OdFieldDefinition field)
    {
        var result = new Dictionary<Guid, (OdClassDefinition Owner, OdFieldDefinition Field)>();
        var visiting = new HashSet<Guid>();
        Gather(owner, field);
        return result.Values.OrderBy(item => item.Field.Id).ToList();

        void Gather(OdClassDefinition currentOwner, OdFieldDefinition current)
        {
            if (!visiting.Add(current.Id))
                return;
            foreach (var dependency in DirectDrivenByFields(currentOwner, current))
            {
                if (dependency.Field.Mode == OdFieldMode.Driven)
                    Gather(dependency.Owner, dependency.Field);
                else
                    result.TryAdd(dependency.Field.Id, dependency);
            }
            visiting.Remove(current.Id);
        }
    }

    public IReadOnlyList<(OdClassDefinition Owner, OdFieldDefinition Field)> FinalDrivingFields(
        OdFieldDefinition source)
    {
        var reverse = new Dictionary<Guid, List<(OdClassDefinition Owner, OdFieldDefinition Field)>>();
        foreach (OdClassDefinition owner in Project.Definitions.OfType<OdClassDefinition>())
        foreach (OdFieldDefinition field in owner.Fields.Where(item => item.Mode == OdFieldMode.Driven))
        foreach (Guid dependency in field.DrivenByFields)
        {
            if (!reverse.TryGetValue(dependency, out var dependents))
                reverse.Add(dependency, dependents = new());
            dependents.Add((owner, field));
        }

        var result = new Dictionary<Guid, (OdClassDefinition Owner, OdFieldDefinition Field)>();
        Gather(source.Id);
        return result.Values.OrderBy(item => item.Field.Id).ToList();

        void Gather(Guid fieldId)
        {
            if (!reverse.TryGetValue(fieldId, out var dependents))
                return;
            foreach (var dependent in dependents)
            {
                if (!result.TryAdd(dependent.Field.Id, dependent))
                    continue;
                Gather(dependent.Field.Id);
            }
        }
    }

    public string DefaultExpression(OdFieldDefinition field)
    {
        if (field.DefaultValue is null)
        {
            if (field.Type.Container != OdContainerKind.Single)
                return $"new {FieldTypeName(field, includeNullable: false)}()";
            if (field.Type.DefinitionId is null &&
                field.Type.BuiltIn == OdBuiltInType.String &&
                !field.Type.Nullable)
                return "global::System.String.Empty";
            return $"default({FieldTypeName(field)})";
        }
        return JsonLiteral(field.Type, field.DefaultValue, field.Id);
    }

    private string CollectionElementTypeName(OdTypeReference type)
    {
        if (type.DefinitionId is not { } definitionId || !Definitions.TryGetValue(definitionId, out var definition))
        {
            string builtIn = BuiltInTypeName(type.BuiltIn);
            return builtIn;
        }

        string result = definition switch
        {
            OdClassDefinition resourceClass when type.IsResourceClass => ClassResourceTypeName(resourceClass),
            OdInterfaceDefinition interfaceDefinition => InterfaceContractTypeName(interfaceDefinition),
            _ => FullName(definition),
        };
        return type.Container != OdContainerKind.Single &&
               (definition is OdClassDefinition || IsReferenceType(definition))
            ? result + "?"
            : result;
    }

    public string JsonLiteral(OdTypeReference type, JsonNode? value, Guid targetId)
    {
        if (value is null)
            return "null";
        if (type.Container != OdContainerKind.Single)
            return CollectionLiteral(type, value, targetId);
        if (type.DefinitionId is { } definitionId && Definitions.TryGetValue(definitionId, out var definition))
        {
            if (definition is OdInterfaceDefinition declaredInterface)
            {
                if (!OdInterfaceCallValue.TryGetInterfaceId(value, out Guid actualId) ||
                    !Definitions.TryGetValue(actualId, out OdDefinition? actualDefinition) ||
                    actualDefinition is not OdInterfaceDefinition actualInterface ||
                    !IsInterfaceAssignableTo(actualInterface, declaredInterface))
                    return "null";
                JsonObject configured = OdInterfaceCallValue.GetValues(value) ?? new JsonObject();
                IEnumerable<string> assignments = AllInterfaceInputs(actualInterface).Select(field =>
                {
                    JsonNode? child = configured[field.Id.ToString("D")] ?? configured[field.Name];
                    string expression = child is null ? DefaultExpression(field) : JsonLiteral(field.Type, child, targetId);
                    return $"{Identifier(field.Name)} = {expression}";
                });
                return $"new {InterfaceCallTypeName(actualInterface)} {{ {string.Join(", ", assignments)} }}";
            }
            if ((definition is OdResourceDefinition || definition is OdClassDefinition && type.IsResourceClass) &&
                value is JsonValue resourceValue && resourceValue.TryGetValue<string>(out string? resourceText) &&
                Guid.TryParse(resourceText, out Guid resourceId))
                return ResourceReferenceExpression(definition, resourceId);
            if (definition is OdEnumDefinition)
            {
                if (value is JsonValue enumValue && enumValue.TryGetValue<long>(out var number))
                    return $"({FullName(definition)}){checked((int)number).ToString(CultureInfo.InvariantCulture)}";
                if (value is JsonValue namedValue && namedValue.TryGetValue<string>(out var name))
                    return $"{FullName(definition)}.{Identifier(name)}";
            }
            if (definition is OdStructDefinition { IsStructOf: { } directAliasType })
            {
                JsonNode? aliasValue = value is JsonObject aliasObject
                    ? aliasObject["Value"] ?? aliasObject["value"]
                    : value;
                return $"new {FullName(definition)}({JsonLiteral(directAliasType, aliasValue, targetId)})";
            }
            if (definition is OdSwitchStructDefinition switchDefinition && value is JsonObject switchValue)
            {
                OdFieldDefinition? activeField = switchDefinition.Fields.FirstOrDefault(field =>
                    switchValue.ContainsKey(field.Id.ToString("D")) || switchValue.ContainsKey(field.Name));
                activeField ??= switchDefinition.Fields.FirstOrDefault();
                if (activeField is null)
                    return $"default({FullName(definition)})";
                JsonNode? child = switchValue[activeField.Id.ToString("D")] ?? switchValue[activeField.Name];
                string expression = child is null
                    ? DefaultExpression(activeField)
                    : JsonLiteral(activeField.Type, child, targetId);
                return $"new {FullName(definition)} {{ {Identifier(activeField.Name)} = {expression} }}";
            }
            if (definition is OdFieldContainerDefinition containerDefinition &&
                definition is OdStructDefinition or OdResourceDefinition && value is JsonObject structure)
            {
                var assignments = containerDefinition.Fields.Select(field =>
                {
                    JsonNode? child = structure[field.Name] ?? structure[field.Id.ToString("D")];
                    return $"{Identifier(field.Name)} = {(child is null ? DefaultExpression(field) : JsonLiteral(field.Type, child, targetId))}";
                });
                return $"new {FullName(definition)} {{ {string.Join(", ", assignments)} }}";
            }
            Issues.Add(new(OdIssueSeverity.Warning, "ODG0102", $"Complex data value for '{definition.Name}' uses its generated default.", targetId));
            return $"default({TypeName(type)})";
        }

        try
        {
            if (Project.Runtime.NetworkMode == OdNetworkMode.Lockstep &&
                type.BuiltIn is OdBuiltInType.Single or OdBuiltInType.Double or OdBuiltInType.Decimal)
            {
                string number = type.BuiltIn switch
                {
                    OdBuiltInType.Single => value.GetValue<float>().ToString("R", CultureInfo.InvariantCulture),
                    OdBuiltInType.Double => value.GetValue<double>().ToString("R", CultureInfo.InvariantCulture),
                    _ => value.GetValue<decimal>().ToString(CultureInfo.InvariantCulture),
                };
                return $"global::XLockstep.Fixed64.Parse({StringLiteral(number)})";
            }
            return type.BuiltIn switch
            {
                OdBuiltInType.Boolean => value.GetValue<bool>() ? "true" : "false",
                OdBuiltInType.Int32 => value.GetValue<int>().ToString(CultureInfo.InvariantCulture),
                OdBuiltInType.Int64 => value.GetValue<long>().ToString(CultureInfo.InvariantCulture) + "L",
                OdBuiltInType.Single => value.GetValue<float>().ToString("R", CultureInfo.InvariantCulture) + "f",
                OdBuiltInType.Double => value.GetValue<double>().ToString("R", CultureInfo.InvariantCulture) + "d",
                OdBuiltInType.Decimal => value.GetValue<decimal>().ToString(CultureInfo.InvariantCulture) + "m",
                OdBuiltInType.String => StringLiteral(value.GetValue<string>()),
                OdBuiltInType.DateTime => $"global::System.DateTime.Parse({StringLiteral(value.GetValue<string>())}, global::System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.RoundtripKind)",
                _ => $"default({TypeName(type)})",
            };
        }
        catch (Exception)
        {
            Issues.Add(new(OdIssueSeverity.Error, "ODG0103", $"Value '{value}' is invalid for {TypeName(type)}.", targetId));
            return $"default({TypeName(type)})";
        }
    }

    private string CollectionLiteral(OdTypeReference type, JsonNode value, Guid targetId, string? collectionType = null)
    {
        collectionType ??= TypeName(type, false);
        var elementType = new OdTypeReference
        {
            BuiltIn = type.BuiltIn,
            DefinitionId = type.DefinitionId,
            IsResourceClass = type.IsResourceClass,
        };
        if (type.Container is OdContainerKind.List or OdContainerKind.Set && value is JsonArray array)
        {
            string values = string.Join(", ", array.Select(item => JsonLiteral(elementType, item, targetId)));
            return $"new {collectionType} {{ {values} }}";
        }
        if (type.Container == OdContainerKind.Dictionary && value is JsonObject dictionary)
        {
            var keyType = new OdTypeReference
            {
                BuiltIn = type.DictionaryKeyBuiltIn,
                DefinitionId = type.DictionaryKeyDefinitionId,
                IsResourceClass = type.DictionaryKeyIsResourceClass,
            };
            string entries = string.Join(", ", dictionary.Select(item =>
                $"[{DictionaryKeyLiteral(keyType, item.Key, targetId)}] = {JsonLiteral(elementType, item.Value, targetId)}"));
            return $"new {collectionType} {{ {entries} }}";
        }
        Issues.Add(new(OdIssueSeverity.Error, "ODG0101", $"Collection value '{value}' does not match {type.Container} JSON syntax.", targetId));
        return $"default({TypeName(type)})";
    }

    private string DictionaryKeyLiteral(OdTypeReference type, string value, Guid targetId)
    {
        if (type.DefinitionId is { } definitionId && Definitions.TryGetValue(definitionId, out var definition) && definition is OdEnumDefinition enumDefinition)
        {
            var member = enumDefinition.Members.FirstOrDefault(item => item.Name == value);
            if (member is not null)
                return $"{FullName(enumDefinition)}.{Identifier(member.Name)}";
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int enumValue))
                return $"({FullName(enumDefinition)}){enumValue}";
        }
        try
        {
            return type.BuiltIn switch
            {
                OdBuiltInType.String => StringLiteral(value),
                OdBuiltInType.Int32 => int.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                OdBuiltInType.Int64 => long.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "L",
                OdBuiltInType.Single or OdBuiltInType.Double or OdBuiltInType.Decimal
                    when Project.Runtime.NetworkMode == OdNetworkMode.Lockstep =>
                    $"global::XLockstep.Fixed64.Parse({StringLiteral(value)})",
                _ => throw new FormatException(),
            };
        }
        catch (Exception)
        {
            Issues.Add(new(OdIssueSeverity.Error, "ODG0104", $"Map key '{value}' is invalid for {TypeName(type)}.", targetId));
            return $"default({TypeName(type)})";
        }
    }

    private bool CanBeNullable(OdTypeReference type)
    {
        if (type.Container != OdContainerKind.Single)
            return true;
        if (type.DefinitionId is { } id && Definitions.TryGetValue(id, out var definition))
            return definition is OdClassDefinition or OdStructDefinition or OdResourceDefinition or OdSwitchStructDefinition or OdEnumDefinition ||
                   IsReferenceType(definition);
        return type.BuiltIn != OdBuiltInType.None;
    }

    private string BuiltInTypeName(OdBuiltInType type)
    {
        if (Project.Runtime.NetworkMode == OdNetworkMode.Lockstep &&
            type is OdBuiltInType.Single or OdBuiltInType.Double or OdBuiltInType.Decimal)
            return "global::XLockstep.Fixed64";
        return type switch
        {
            OdBuiltInType.Boolean => "global::System.Boolean",
            OdBuiltInType.Int32 => "global::System.Int32",
            OdBuiltInType.Int64 => "global::System.Int64",
            OdBuiltInType.Single => "global::System.Single",
            OdBuiltInType.Double => "global::System.Double",
            OdBuiltInType.Decimal => "global::System.Decimal",
            OdBuiltInType.String => "global::System.String",
            OdBuiltInType.DateTime => "global::System.DateTime",
            _ => "global::System.Object",
        };
    }

}
