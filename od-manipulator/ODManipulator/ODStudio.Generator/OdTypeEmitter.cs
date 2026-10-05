using ODStudio.Model;

namespace ODStudio.Generator;

internal static class OdTypeEmitter
{
    public static string Emit(OdGenerationContext context)
    {
        var writer = new CodeWriter();
        Header(writer);
        foreach (var namespaceGroup in context.OwnedDefinitions
                     .Where(item => context.ShouldEmitTypeDefinition(item) &&
                                    item is OdClassDefinition or OdStructDefinition or OdResourceDefinition or OdSwitchStructDefinition or OdEnumDefinition or OdEventDefinition)
                     .GroupBy(context.Namespace).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            using (writer.Block($"namespace {namespaceGroup.Key}"))
            {
                foreach (var definition in namespaceGroup.OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    switch (definition)
                    {
                        case OdClassDefinition classDefinition:
                            EmitClass(writer, context, classDefinition);
                            if (context.Options.EmitClassResourceDefinitions)
                            {
                                writer.Line();
                                EmitClassResource(writer, context, classDefinition);
                            }
                            break;
                        case OdStructDefinition structDefinition:
                            EmitStruct(writer, context, structDefinition);
                            break;
                        case OdResourceDefinition resourceDefinition:
                            EmitResource(writer, context, resourceDefinition);
                            break;
                        case OdSwitchStructDefinition switchStructDefinition:
                            EmitSwitchStruct(writer, context, switchStructDefinition);
                            break;
                        case OdEnumDefinition enumDefinition:
                            EmitEnum(writer, enumDefinition);
                            break;
                        case OdEventDefinition eventDefinition:
                            EmitEvent(writer, context, eventDefinition);
                            break;
                    }
                    writer.Line();
                }
            }
        }
        return writer.ToString();
    }

    private static void EmitClass(CodeWriter writer, OdGenerationContext context, OdClassDefinition definition)
    {
        string name = OdGenerationContext.Identifier(definition.Name);
        writer.Line("[global::System.Serializable]");
        using (writer.Block($"public partial struct {name} : global::GMCore.IGameplayObjectOperator, global::GMCore.IODClass, global::System.IEquatable<{name}>, global::System.IComparable<{name}>, global::System.IComparable"))
        {
            writer.Line($"public static readonly global::GMCore.ODClassName ClassName = new global::GMCore.ODClassName {{ NameObject = {OdGenerationContext.StringLiteral(context.ClassStableName(definition))} }};");
            writer.Line("public global::GMCore.GameplayMachine Machine { get; set; }");
            writer.Line("public global::GMCore.GObjectID ObjectID { get; set; }");
            writer.Line("public global::GMCore.ODClassName ObjectClass => Machine.GetGameplayObjectClass(ObjectID);");
            writer.Line("public global::GMCore.ODClassName GetODClassName() => ClassName;");
            writer.Line("public void Delete() => Machine.DeleteGameplayObject(ObjectID);");
            writer.Line("public void Return() => Delete();");
            writer.Line("public bool IsA<T>() where T : struct, global::GMCore.IODClass => global::GMCore.GameplayMachineBase.CanCastTo(ObjectClass, new T().GetODClassName());");
            writer.Line("public T? Cast<T>() where T : struct, global::GMCore.IGameplayObjectOperator, global::GMCore.IODClass => Machine.Cast<T>(this);");
            writer.Line($"public bool Equals({name} other) => global::System.Object.ReferenceEquals(Machine, other.Machine) && ObjectID.Equals(other.ObjectID);");
            writer.Line($"public int CompareTo({name} other) => ObjectID.CompareTo(other.ObjectID);");
            writer.Line($"int global::System.IComparable.CompareTo(object? other) => other is {name} value ? CompareTo(value) : throw new global::System.ArgumentException(\"Object is not a {name}\", nameof(other));");
            writer.Line($"public override bool Equals(object? obj) => obj is {name} other && Equals(other);");
            writer.Line("public override int GetHashCode() => global::System.HashCode.Combine(Machine, ObjectID);");
            writer.Line($"public static bool operator ==({name} left, {name} right) => left.Equals(right);");
            writer.Line($"public static bool operator !=({name} left, {name} right) => !left.Equals(right);");

            foreach (var (owner, field) in context.AllFields(definition))
                EmitClassField(writer, context, owner, field);

            foreach (OdClassDefinition baseDefinition in context.AllAssignableClasses(definition).Skip(1))
                writer.Line($"public static implicit operator {context.FullName(baseDefinition)}({name} value) => new {context.FullName(baseDefinition)} {{ Machine = value.Machine, ObjectID = value.ObjectID }};");

            using (writer.Block("public static class Fields"))
            {
                foreach (var field in definition.Fields)
                    writer.Line($"public static readonly global::GMCore.ODFieldName {OdGenerationContext.Identifier(field.Name)} = new global::GMCore.ODFieldName {{ NameObject = {OdGenerationContext.StringLiteral(context.FieldStableName(definition, field))} }};");
            }

            writer.Line("[global::System.Serializable]");
            using (writer.Block("public sealed class InnerLayout : global::GMCore.IGameplayObjectCreateParam"))
            {
                foreach (var (_, field) in context.AllFields(definition))
                {
                    string initializer = field.Type.Container == OdContainerKind.Single
                        ? context.DefaultExpression(field)
                        : $"new {context.FieldTypeName(field, includeNullable: false)}()";
                    writer.Line($"public {context.FieldTypeName(field)} {OdGenerationContext.Identifier(field.Name)} = {initializer};");
                }
                using (writer.Block("public void Set(global::GMCore.IGameplayObjectOperator gameplayObject)"))
                {
                    foreach (var (owner, field) in context.AllFields(definition).Where(item => item.Field.Mode != OdFieldMode.Driven))
                    {
                        string fieldName = OdGenerationContext.Identifier(field.Name);
                        string fieldRef = $"{context.FullName(owner)}.Fields.{fieldName}";
                        if (field.Type.Container == OdContainerKind.Single)
                        {
                            writer.Line($"gameplayObject.Machine.SetGameplayObjectValue(gameplayObject.ObjectID, {fieldRef}, {fieldName});");
                            continue;
                        }

                        string proxyType = context.FieldTypeName(field, gameplayCollection: true);
                        string target = $"target_{field.Id:N}";
                        writer.Line($"var {target} = new {proxyType} {{ Machine = gameplayObject.Machine, ObjectID = gameplayObject.ObjectID, FieldName = {fieldRef} }};");
                        writer.Line($"{target}.Clear();");
                        using (writer.Block($"if ({fieldName} != null)"))
                            writer.Line($"{target}.AddRange({fieldName});");
                    }
                }
            }
        }
    }

    private static void EmitClassField(CodeWriter writer, OdGenerationContext context, OdClassDefinition owner, OdFieldDefinition field)
    {
        string name = OdGenerationContext.Identifier(field.Name);
        string type = field.Type.Container == OdContainerKind.Single
            ? context.FieldTypeName(field)
            : context.FieldTypeName(field, gameplayCollection: true);
        string fieldName = $"{context.FullName(owner)}.Fields.{name}";
        if (field.Type.Container != OdContainerKind.Single)
        {
            writer.Line($"public {type} {name} => new {type} {{ Machine = Machine, ObjectID = ObjectID, FieldName = {fieldName} }};");
            string binderType = context.CollectionBinderTypeName(field);
            writer.Line($"public {binderType} {name}Binder => new {binderType} {{ Machine = Machine, ObjectID = ObjectID, FieldName = {fieldName} }};");
        }
        else if (field.Mode == OdFieldMode.Driven)
        {
            string implementation = $"global::{context.Namespace(owner)}.DrivenValueImplementations.Get_{OdGenerationContext.SafeIdentifier(owner.Name)}_{OdGenerationContext.SafeIdentifier(field.Name)}";
            string arguments = string.Join(", ", context.DirectDrivenByFields(owner, field)
                .Select(item => OdGenerationContext.Identifier(item.Field.Name)));
            writer.Line($"public {type} {name} => {implementation}({arguments});");
        }
        else
        {
            string value = $"Machine.GetGameplayObjectValue<{type}>(ObjectID, {fieldName})";
            if (context.IsLiveClassReference(field))
                value = $"global::GMCore.GameplayMachine.ResolveGameplayObjectReference({value})";
            writer.Line($"public {type} {name} {{ get => {value}; set => Machine.SetGameplayObjectValue(ObjectID, {fieldName}, value); }}");
        }
        writer.Line($"public global::GMCore.FieldChangeEventBinder After{OdGenerationContext.Identifier(field.Name)}Changed => new global::GMCore.FieldChangeEventBinder {{ Machine = Machine, ObjectID = ObjectID, FieldName = {fieldName} }};");
    }

    private static void EmitClassResource(CodeWriter writer, OdGenerationContext context, OdClassDefinition definition)
    {
        string name = OdGenerationContext.Identifier(definition.Name) + "Resource";
        writer.Line("[global::System.Serializable]");
        using (writer.Block($"public sealed partial class {name} : global::GMCore.IODClassResource"))
        {
            foreach (var (_, field) in context.AllFields(definition)
                         .Where(item => item.Field.Mode != OdFieldMode.Driven && !context.IsLiveClassReference(item.Field)))
            {
                string initializer = field.Type.Container != OdContainerKind.Single
                    ? $" = new {context.FieldTypeName(field, includeNullable: false)}();"
                    : field.Type.BuiltIn == OdBuiltInType.String && field.Type.DefinitionId is null && !field.Type.Nullable
                        ? " = global::System.String.Empty;"
                        : $" = {context.DefaultExpression(field)};";
                writer.Line($"public {context.FieldTypeName(field)} {OdGenerationContext.Identifier(field.Name)}{initializer}");
            }
            writer.Line($"public global::GMCore.ODClassName ODClassName => {context.FullName(definition)}.ClassName;");
            using (writer.Block("public void Set(global::GMCore.IGameplayObjectOperator gameplayObject)"))
            {
                foreach (var (owner, field) in context.AllFields(definition)
                             .Where(item => item.Field.Mode != OdFieldMode.Driven && !context.IsLiveClassReference(item.Field)))
                {
                    string member = OdGenerationContext.Identifier(field.Name);
                    string fieldRef = $"{context.FullName(owner)}.Fields.{member}";
                    if (field.Type.Container == OdContainerKind.Single)
                    {
                        writer.Line($"gameplayObject.Machine.SetGameplayObjectValue(gameplayObject.ObjectID, {fieldRef}, {member});");
                        continue;
                    }
                    string target = $"target_{field.Id:N}";
                    writer.Line($"var {target} = new {context.FieldTypeName(field, gameplayCollection: true)} {{ Machine = gameplayObject.Machine, ObjectID = gameplayObject.ObjectID, FieldName = {fieldRef} }};");
                    writer.Line($"{target}.Clear();");
                    using (writer.Block($"if ({member} != null)"))
                        writer.Line($"{target}.AddRange({member});");
                }
            }
        }
    }

    private static void EmitStruct(CodeWriter writer, OdGenerationContext context, OdStructDefinition definition)
    {
        string name = OdGenerationContext.Identifier(definition.Name);
        writer.Line("[global::System.Serializable]");
        string interfaces = $"global::GMCore.IODStruct, global::System.IEquatable<{name}>, global::System.IComparable<{name}>, global::System.IComparable";
        using (writer.Block($"public partial struct {name} : {interfaces}"))
        {
            writer.Line($"public static readonly global::GMCore.ODStructName StructName = new global::GMCore.ODStructName {{ NameObject = {OdGenerationContext.StringLiteral(context.StructStableName(definition))} }};");
            if (definition.IsStructOf is { } aliasType)
                EmitIsStructMembers(writer, context, definition, aliasType);
            else
            {
                foreach (var field in definition.Fields)
                    EmitValueField(writer, context, field);
                EmitStructEqualityMembers(writer, context, definition);
            }
            writer.Line("public global::GMCore.ODStructName GetODStructName() => StructName;");
        }
    }

    private static void EmitStructEqualityMembers(CodeWriter writer, OdGenerationContext context,
        OdStructDefinition definition)
    {
        string name = OdGenerationContext.Identifier(definition.Name);
        string[] comparisons = definition.Fields.Select(field =>
        {
            string type = context.FieldTypeName(field);
            string value = StructFieldStorageName(context, field);
            return $"global::System.Collections.Generic.EqualityComparer<{type}>.Default.Equals({value}, other.{value})";
        }).ToArray();
        writer.Line($"public bool Equals({name} other) => {(comparisons.Length == 0 ? "true" : string.Join(" && ", comparisons))};");
        using (writer.Block($"public int CompareTo({name} other)"))
        {
            for (int index = 0; index < definition.Fields.Count; index++)
            {
                OdFieldDefinition field = definition.Fields[index];
                string type = context.FieldTypeName(field);
                string value = StructFieldStorageName(context, field);
                writer.Line($"int comparison{index} = global::XLockstep.DeterministicComparer<{type}>.Default.Compare({value}, other.{value});");
                writer.Line($"if (comparison{index} != 0) return comparison{index};");
            }
            writer.Line("return 0;");
        }
        writer.Line($"int global::System.IComparable.CompareTo(object? other) => other is {name} value ? CompareTo(value) : throw new global::System.ArgumentException(\"Object is not a {name}\", nameof(other));");
        writer.Line($"public override bool Equals(object? obj) => obj is {name} other && Equals(other);");
        if (definition.Fields.Count == 0)
        {
            writer.Line("public override int GetHashCode() => 0;");
        }
        else
        {
            using (writer.Block("public override int GetHashCode()"))
            {
                writer.Line("var hash = new global::System.HashCode();");
                foreach (OdFieldDefinition field in definition.Fields)
                    writer.Line($"hash.Add({StructFieldStorageName(context, field)});");
                writer.Line("return hash.ToHashCode();");
            }
        }
        writer.Line($"public static bool operator ==({name} left, {name} right) => left.Equals(right);");
        writer.Line($"public static bool operator !=({name} left, {name} right) => !left.Equals(right);");
    }

    private static string StructFieldStorageName(OdGenerationContext context, OdFieldDefinition field) =>
        context.IsLiveClassReference(field)
            ? $"__reference_{field.Id:N}"
            : OdGenerationContext.Identifier(field.Name);

    private static void EmitResource(CodeWriter writer, OdGenerationContext context,
        OdResourceDefinition definition)
    {
        string name = OdGenerationContext.Identifier(definition.Name);
        writer.Line("[global::System.Serializable]");
        using (writer.Block($"public partial class {name} : global::GMCore.IODStruct"))
        {
            writer.Line($"public static readonly global::GMCore.ODStructName StructName = new global::GMCore.ODStructName {{ NameObject = {OdGenerationContext.StringLiteral(context.StructStableName(definition))} }};");
            foreach (OdFieldDefinition field in definition.Fields)
                EmitResourceValueField(writer, context, field);
            writer.Line("public global::GMCore.ODStructName GetODStructName() => StructName;");
        }
    }

    private static void EmitResourceValueField(CodeWriter writer, OdGenerationContext context, OdFieldDefinition field)
    {
        if (field.Type.Container == OdContainerKind.Single && field.Type.DefinitionId is { } interfaceId &&
            context.Definitions.TryGetValue(interfaceId, out OdDefinition? interfaceDefinition) &&
            interfaceDefinition is OdInterfaceDefinition)
        {
            string interfaceType = context.FieldTypeName(field);
            string interfaceMember = OdGenerationContext.Identifier(field.Name);
            string backing = $"__interfaceTemplate_{field.Id:N}";
            writer.Line($"private {interfaceType} {backing};");
            writer.Line($"public {interfaceType} {interfaceMember} {{ get => {backing} is null ? null : ({interfaceType.TrimEnd('?')}){backing}.CopyUntyped(); set => {backing} = value; }}");
            return;
        }
        if (context.IsLiveClassReference(field))
        {
            EmitValueField(writer, context, field);
            return;
        }

        string type = context.FieldTypeName(field);
        string name = OdGenerationContext.Identifier(field.Name);
        string initializer = field.Type.Container != OdContainerKind.Single
            ? $" = new {context.FieldTypeName(field, includeNullable: false)}()"
            : field.Type.BuiltIn == OdBuiltInType.String && field.Type.DefinitionId is null && !field.Type.Nullable
                ? " = global::System.String.Empty"
                : string.Empty;
        writer.Line($"public {type} {name}{initializer};");
    }

    private static void EmitIsStructMembers(CodeWriter writer, OdGenerationContext context,
        OdStructDefinition definition, OdTypeReference aliasType)
    {
        string name = OdGenerationContext.Identifier(definition.Name);
        string valueType = context.TypeName(aliasType, false);
        writer.Line($"public {valueType} Value;");
        writer.Line($"public {name}({valueType} value) => Value = value;");
        writer.Line($"public bool Equals({name} other) => global::System.Collections.Generic.EqualityComparer<{valueType}>.Default.Equals(Value, other.Value);");
        writer.Line($"public int CompareTo({name} other) => global::XLockstep.DeterministicComparer<{valueType}>.Default.Compare(Value, other.Value);");
        writer.Line($"int global::System.IComparable.CompareTo(object? other) => other is {name} value ? CompareTo(value) : throw new global::System.ArgumentException(\"Object is not a {name}\", nameof(other));");
        writer.Line($"public override bool Equals(object? obj) => obj is {name} other && Equals(other);");
        writer.Line("public override int GetHashCode() => global::System.Collections.Generic.EqualityComparer<" + valueType + ">.Default.GetHashCode(Value);");
        writer.Line("public override string ToString() => global::System.Convert.ToString(Value, global::System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;");
        writer.Line($"public static implicit operator {name}({valueType} value) => new {name}(value);");
        writer.Line($"public static explicit operator {valueType}({name} value) => value.Value;");
        writer.Line($"public static bool operator ==({name} left, {name} right) => left.Equals(right);");
        writer.Line($"public static bool operator !=({name} left, {name} right) => !left.Equals(right);");

        if (!IsNumericAliasTarget(context, aliasType, new HashSet<Guid>()))
            return;
        writer.Line($"public static {name} operator +({name} left, {name} right) => new {name}(left.Value + right.Value);");
        writer.Line($"public static {name} operator -({name} left, {name} right) => new {name}(left.Value - right.Value);");
        writer.Line($"public static {name} operator *({name} left, {name} right) => new {name}(left.Value * right.Value);");
        writer.Line($"public static {name} operator /({name} left, {name} right) => new {name}(left.Value / right.Value);");
        writer.Line($"public static {name} operator %({name} left, {name} right) => new {name}(left.Value % right.Value);");
        writer.Line($"public static {name} operator +({name} value) => value;");
        writer.Line($"public static {name} operator -({name} value) => new {name}(-value.Value);");
        writer.Line($"public static {name} operator ++({name} value) => new {name}(value.Value + ({valueType})1);");
        writer.Line($"public static {name} operator --({name} value) => new {name}(value.Value - ({valueType})1);");
        writer.Line($"public static bool operator <({name} left, {name} right) => left.Value < right.Value;");
        writer.Line($"public static bool operator >({name} left, {name} right) => left.Value > right.Value;");
        writer.Line($"public static bool operator <=({name} left, {name} right) => left.Value <= right.Value;");
        writer.Line($"public static bool operator >=({name} left, {name} right) => left.Value >= right.Value;");
    }

    private static bool IsNumericAliasTarget(OdGenerationContext context, OdTypeReference type, HashSet<Guid> visited)
    {
        if (type.DefinitionId is not { } definitionId)
            return type.BuiltIn is OdBuiltInType.Int32 or OdBuiltInType.Int64 or OdBuiltInType.Single or
                OdBuiltInType.Double or OdBuiltInType.Decimal;
        if (!visited.Add(definitionId) || !context.Definitions.TryGetValue(definitionId, out OdDefinition? definition))
            return false;
        return definition is OdStructDefinition { IsStructOf: { } nested } &&
               IsNumericAliasTarget(context, nested, visited);
    }

    private static void EmitEnum(CodeWriter writer, OdEnumDefinition definition)
    {
        using (writer.Block($"public enum {OdGenerationContext.Identifier(definition.Name)} : int"))
        {
            foreach (var member in definition.Members.OrderBy(item => item.Value))
                writer.Line($"{OdGenerationContext.Identifier(member.Name)} = {member.Value},");
        }
    }

    private static void EmitSwitchStruct(CodeWriter writer, OdGenerationContext context,
        OdSwitchStructDefinition definition)
    {
        string name = OdGenerationContext.Identifier(definition.Name);
        writer.Line("[global::System.Serializable]");
        using (writer.Block($"public partial struct {name} : global::GMCore.IODStruct, global::System.IEquatable<{name}>, global::System.IComparable<{name}>, global::System.IComparable"))
        {
            writer.Line($"public static readonly global::GMCore.ODStructName StructName = new global::GMCore.ODStructName {{ NameObject = {OdGenerationContext.StringLiteral(context.StructStableName(definition))} }};");
            using (writer.Block("public enum SwitchTypes : int"))
                foreach (OdFieldDefinition field in definition.Fields)
                    writer.Line($"{OdGenerationContext.Identifier(field.Name)},");
            if (!string.IsNullOrWhiteSpace(context.Options.PrivateSerializedFieldAttribute))
                writer.Line(context.Options.PrivateSerializedFieldAttribute!);
            writer.Line("private SwitchTypes __switchType;");
            writer.Line("public SwitchTypes SwitchType { get => __switchType; private set => __switchType = value; }");
            foreach (OdFieldDefinition field in definition.Fields)
            {
                if (!string.IsNullOrWhiteSpace(context.Options.PrivateSerializedFieldAttribute))
                    writer.Line(context.Options.PrivateSerializedFieldAttribute!);
                writer.Line($"private {context.FieldTypeName(field)} __value_{field.Id:N};");
            }
            writer.Line();
            foreach (OdFieldDefinition field in definition.Fields)
            {
                string fieldName = OdGenerationContext.Identifier(field.Name);
                string backingName = $"__value_{field.Id:N}";
                using (writer.Block($"public {context.FieldTypeName(field)} {fieldName}"))
                {
                    using (writer.Block("get"))
                    {
                        writer.Line($"if (SwitchType != SwitchTypes.{fieldName})");
                        writer.Line($"    throw new global::System.InvalidOperationException(\"Cannot read {definition.Name}.{field.Name} while the active subtype is \" + SwitchType);");
                        string value = context.IsLiveClassReference(field)
                            ? $"global::GMCore.GameplayMachine.ResolveGameplayObjectReference({backingName})"
                            : backingName;
                        writer.Line($"return {value};");
                    }
                    using (writer.Block("set"))
                    {
                        writer.Line($"SwitchType = SwitchTypes.{fieldName};");
                        writer.Line($"{backingName} = value;");
                    }
                }
            }
            using (writer.Block("public object? Value"))
            using (writer.Block("get"))
            using (writer.Block("switch (SwitchType)"))
            {
                foreach (OdFieldDefinition field in definition.Fields)
                    writer.Line($"case SwitchTypes.{OdGenerationContext.Identifier(field.Name)}: return {OdGenerationContext.Identifier(field.Name)};");
                writer.Line("default: throw new global::System.InvalidOperationException(\"Unknown switch struct subtype\");");
            }
            writer.Line("public global::GMCore.ODStructName GetODStructName() => StructName;");
            writer.Line($"public bool Equals({name} other) => SwitchType == other.SwitchType && global::System.Object.Equals(Value, other.Value);");
            using (writer.Block($"public int CompareTo({name} other)"))
            {
                writer.Line("int typeComparison = ((int)SwitchType).CompareTo((int)other.SwitchType);");
                writer.Line("if (typeComparison != 0) return typeComparison;");
                using (writer.Block("switch (SwitchType)"))
                {
                    foreach (OdFieldDefinition field in definition.Fields)
                    {
                        string fieldName = OdGenerationContext.Identifier(field.Name);
                        string type = context.FieldTypeName(field);
                        writer.Line($"case SwitchTypes.{fieldName}: return global::XLockstep.DeterministicComparer<{type}>.Default.Compare(__value_{field.Id:N}, other.__value_{field.Id:N});");
                    }
                    writer.Line("default: throw new global::System.InvalidOperationException(\"Unknown switch struct subtype\");");
                }
            }
            writer.Line($"int global::System.IComparable.CompareTo(object? other) => other is {name} value ? CompareTo(value) : throw new global::System.ArgumentException(\"Object is not a {name}\", nameof(other));");
            writer.Line($"public override bool Equals(object? obj) => obj is {name} other && Equals(other);");
            writer.Line("public override int GetHashCode() => global::System.HashCode.Combine(SwitchType, Value);");
            writer.Line($"public static bool operator ==({name} left, {name} right) => left.Equals(right);");
            writer.Line($"public static bool operator !=({name} left, {name} right) => !left.Equals(right);");
        }
    }

    private static void EmitEvent(CodeWriter writer, OdGenerationContext context, OdEventDefinition definition)
    {
        string name = OdGenerationContext.Identifier(definition.Name);
        writer.Line("[global::System.Serializable]");
        string multicast = definition.Multicast ? ", global::GMCore.IMulticastEvent" : string.Empty;
        using (writer.Block($"public partial struct {name} : global::GMCore.IODEvent{multicast}"))
        {
            writer.Line($"public static readonly global::GMCore.ODEventName EventName = new global::GMCore.ODEventName {{ NameObject = {OdGenerationContext.StringLiteral(context.EventStableName(definition))} }};");
            foreach (var field in definition.Fields)
                EmitValueField(writer, context, field);
            writer.Line("public global::GMCore.ODEventName GetODEventName() => EventName;");
        }
    }

    private static void EmitValueField(CodeWriter writer, OdGenerationContext context, OdFieldDefinition field)
    {
        string type = context.FieldTypeName(field);
        string name = OdGenerationContext.Identifier(field.Name);
        if (!context.IsLiveClassReference(field))
        {
            writer.Line($"public {type} {name};");
            return;
        }

        string backingName = $"__reference_{field.Id:N}";
        writer.Line($"private {type} {backingName};");
        writer.Line($"public {type} {name} {{ get => global::GMCore.GameplayMachine.ResolveGameplayObjectReference({backingName}); set => {backingName} = value; }}");
    }

    private static void Header(CodeWriter writer)
    {
        writer.Line("// <auto-generated />");
        writer.Line("#nullable enable");
        writer.Line();
    }
}
