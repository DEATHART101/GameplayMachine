using ODStudio.Model;

namespace ODStudio.Generator;

public sealed class OdCSharpGenerationOptions
{
    public Func<OdDefinition, string?>? TypeNameOverride { get; init; }
    public Func<OdDefinition, bool>? EmitTypeDefinition { get; init; }
    public Func<OdStructDefinition, bool>? EmitStructMetadata { get; init; }
    public Func<OdDefinition, bool>? UseCustomSerialization { get; init; }
    public Func<OdDefinition, string, string?>? CustomWriteStatement { get; init; }
    public Func<OdDefinition, string?>? CustomReadExpression { get; init; }
    public Func<OdDefinition, bool>? IsReferenceType { get; init; }
    public Func<OdClassDefinition, string>? ClassResourceTypeName { get; init; }
    public Func<OdClassDefinition, string, string?>? ClassResourceWriteStatement { get; init; }
    public Func<OdClassDefinition, string?>? ClassResourceReadExpression { get; init; }
    public Func<OdDefinition, Guid, string>? ResourceReferenceExpression { get; init; }
    public bool EmitClassResourceDefinitions { get; init; } = true;
    public string? ResourceCatalogIdExpression { get; init; }
    public string? PrivateSerializedFieldAttribute { get; init; }
}
