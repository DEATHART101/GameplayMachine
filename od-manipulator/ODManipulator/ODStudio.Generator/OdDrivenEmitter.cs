using ODStudio.Model;

namespace ODStudio.Generator;

internal static class OdDrivenEmitter
{
    public static IEnumerable<OdGeneratedFile> EmitImplementationFiles(OdGenerationContext context)
    {
        foreach (OdClassDefinition owner in context.OwnedDefinitions.OfType<OdClassDefinition>()
                     .OrderBy(item => item.Name, StringComparer.Ordinal))
        foreach (OdFieldDefinition field in owner.Fields.Where(item => item.Mode == OdFieldMode.Driven)
                     .OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            yield return new OdGeneratedFile(
                $"DrivenValues/{OdGenerationContext.SafeIdentifier(owner.Name)}_{OdGenerationContext.SafeIdentifier(field.Name)}.cs",
                EmitImplementation(context, owner, field),
                OverwriteExisting: false);
        }
    }

    internal static string EmitImplementation(
        OdGenerationContext context,
        OdClassDefinition owner,
        OdFieldDefinition field)
    {
        var writer = new CodeWriter();
        writer.Line("#nullable enable");
        writer.Line();
        writer.Line("using System;");
        writer.Line();
        using (writer.Block($"namespace {context.Namespace(owner)}"))
        using (writer.Block("public static partial class DrivenValueImplementations"))
        {
            string parameters = string.Join(", ", context.DirectDrivenByFields(owner, field).Select(item =>
                $"{context.FieldTypeName(item.Field, gameplayCollection: item.Field.Type.Container != OdContainerKind.Single)} {OdGenerationContext.Identifier(item.Field.Name)}"));
            using (writer.Block($"public static {context.FieldTypeName(field)} Get_{OdGenerationContext.SafeIdentifier(owner.Name)}_{OdGenerationContext.SafeIdentifier(field.Name)}({parameters})"))
                writer.Line("throw new NotImplementedException();");
        }
        return writer.ToString();
    }
}
