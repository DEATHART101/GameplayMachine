using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ODStudio.Model;

public enum OdDefinitionKind
{
    Class,
    Struct,
    Enum,
    Interface,
    Event,
    SwitchStruct,
    Resource,
}

public enum OdBuiltInType
{
    None = 0,
    Boolean = 1,
    Int32 = 2,
    Int64 = 3,
    Single = 4,
    Double = 5,
    Decimal = 6,
    String = 7,
    DateTime = 8,
}

public enum OdContainerKind
{
    Single,
    List,
    Set,
    Dictionary,
}

public enum OdFieldMode
{
    Stored = 0,
    Driven = 2,
}

public enum OdFieldReplicationMode
{
    None = 0,
    OwnerOnly = 1,
    AllClients = 2,
}

public enum OdObjectReplicationMode
{
    None = 0,
    OwnerOnly = 1,
    AllClients = 2,
}

public enum OdObjectRelevancyMode
{
    Always = 0,
    Spatial = 1,
}

public enum OdObjectPartitionMode
{
    Manual = 0,
    Persistent = 1,
    Spatial = 2,
}

public sealed class OdTypeReference
{
    public OdBuiltInType BuiltIn { get; set; }
    public Guid? DefinitionId { get; set; }
    public bool IsResourceClass { get; set; }
    public OdContainerKind Container { get; set; }
    public OdBuiltInType DictionaryKeyBuiltIn { get; set; } = OdBuiltInType.String;
    public Guid? DictionaryKeyDefinitionId { get; set; }
    public bool DictionaryKeyIsResourceClass { get; set; }
    public bool Nullable { get; set; }

    [JsonIgnore]
    public bool IsDefinition => DefinitionId.HasValue;

    public static OdTypeReference BuiltInType(OdBuiltInType type) => new() { BuiltIn = type };
}

public abstract class OdEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class OdScope : OdEntity
{
    public string Namespace { get; set; } = string.Empty;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(OdClassDefinition), "class")]
[JsonDerivedType(typeof(OdStructDefinition), "struct")]
[JsonDerivedType(typeof(OdSwitchStructDefinition), "switchStruct")]
[JsonDerivedType(typeof(OdEnumDefinition), "enum")]
[JsonDerivedType(typeof(OdInterfaceDefinition), "interface")]
[JsonDerivedType(typeof(OdEventDefinition), "event")]
[JsonDerivedType(typeof(OdResourceDefinition), "resource")]
public abstract class OdDefinition : OdEntity
{
    public string Namespace { get; set; } = string.Empty;
    public abstract OdDefinitionKind Kind { get; }
}

public abstract class OdFieldContainerDefinition : OdDefinition
{
    public List<OdFieldDefinition> Fields { get; set; } = new();
}

public sealed class OdClassDefinition : OdFieldContainerDefinition
{
    public override OdDefinitionKind Kind => OdDefinitionKind.Class;
    public OdObjectReplicationMode Replication { get; set; } = OdObjectReplicationMode.AllClients;
    public OdObjectRelevancyMode Relevancy { get; set; }
    public OdObjectPartitionMode Partition { get; set; }
    public List<Guid> IsClasses { get; set; } = new();
    public bool PreventInheritance { get; set; }
}

public sealed class OdStructDefinition : OdFieldContainerDefinition
{
    public override OdDefinitionKind Kind => OdDefinitionKind.Struct;
    public OdTypeReference? IsStructOf { get; set; }
}

public sealed class OdResourceDefinition : OdFieldContainerDefinition
{
    public override OdDefinitionKind Kind => OdDefinitionKind.Resource;
}

public sealed class OdPackageReference : OdEntity
{
    public Guid PackageId { get; set; }
    public string PackagePath { get; set; } = string.Empty;
}

public sealed class OdSwitchStructDefinition : OdFieldContainerDefinition
{
    public override OdDefinitionKind Kind => OdDefinitionKind.SwitchStruct;
}

public sealed class OdEnumDefinition : OdDefinition
{
    public override OdDefinitionKind Kind => OdDefinitionKind.Enum;
    public List<OdEnumMember> Members { get; set; } = new();
}

public sealed class OdInterfaceDefinition : OdDefinition
{
    public override OdDefinitionKind Kind => OdDefinitionKind.Interface;
    public List<OdFieldDefinition> Inputs { get; set; } = new();
    public List<OdFieldDefinition> Outputs { get; set; } = new();
    public Guid? BaseInterfaceId { get; set; }
    public bool Abstract { get; set; }
    public bool IsRoutine { get; set; }
    public OdInterfaceType InterfaceType { get; set; } = OdInterfaceType.Gameplay;
    public OdPredictionMode Prediction { get; set; }
}

public static class OdInterfaceCallValue
{
    public const string InterfaceProperty = "$interface";
    public const string ValuesProperty = "values";

    public static JsonObject Create(Guid interfaceId) => new()
    {
        [InterfaceProperty] = interfaceId.ToString("D"),
        [ValuesProperty] = new JsonObject(),
    };

    public static bool TryGetInterfaceId(JsonNode? node, out Guid interfaceId)
    {
        interfaceId = default;
        return node is JsonObject value &&
               value[InterfaceProperty] is JsonValue idValue &&
               idValue.TryGetValue<string>(out string? text) &&
               Guid.TryParse(text, out interfaceId);
    }

    public static JsonObject? GetValues(JsonNode? node) =>
        node is JsonObject value ? value[ValuesProperty] as JsonObject : null;
}

public enum OdInterfaceType
{
    Gameplay,
    PlayerInput,
}

public enum OdPredictionMode
{
    None,
    OnlySuccess,
    OnlyFailure,
    Both,
}

public sealed class OdEventDefinition : OdFieldContainerDefinition
{
    public override OdDefinitionKind Kind => OdDefinitionKind.Event;
    public bool Multicast { get; set; }
}

public sealed class OdFieldDefinition : OdEntity
{
    public OdTypeReference Type { get; set; } = OdTypeReference.BuiltInType(OdBuiltInType.Int32);
    public bool NotNull { get; set; }
    public OdFieldMode Mode { get; set; }
    public OdFieldReplicationMode Replication { get; set; } = OdFieldReplicationMode.AllClients;
    public List<Guid> DrivenByFields { get; set; } = new();
    public JsonNode? DefaultValue { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
}

public sealed class OdEnumMember : OdEntity
{
    public int Value { get; set; }
}

public sealed class OdDataSet : OdEntity
{
    public Guid DefinitionId { get; set; }
    public List<OdDataRecord> Records { get; set; } = new();
}

public sealed class OdDataRecord : OdEntity
{
    public Dictionary<Guid, JsonNode?> Values { get; set; } = new();
}

public sealed class OdScene : OdEntity
{
    public Guid OdId { get; set; }
    public List<OdSceneObject> Objects { get; set; } = new();
}

public sealed class OdSceneObject : OdEntity
{
    public Guid ClassDefinitionId { get; set; }
    public Dictionary<Guid, JsonNode?> Values { get; set; } = new();
}

public sealed class OdEditorState
{
    public Guid? SelectedEntityId { get; set; }
    public double ExplorerWidth { get; set; } = 280;
    public double InspectorWidth { get; set; } = 340;
    public double ResourceExplorerWidth { get; set; } = 300;
    public double SceneExplorerWidth { get; set; } = 300;
    public Guid? SelectedResourceEntityId { get; set; }
    public Guid? SelectedSceneEntityId { get; set; }
    public HashSet<string> CollapsedInheritedFieldSections { get; set; } = new(StringComparer.Ordinal);
}
