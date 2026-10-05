using System.Text;
using System.Text.Json.Nodes;
using ODStudio.Model;

namespace ODStudio.Package;

internal static class OdBinaryModelCodec
{
    public static byte[] WriteCodeFiles(IReadOnlyCollection<OdCodeFile> files)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(files.Count);
        foreach (OdCodeFile file in files.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            WriteGuid(writer, file.Id);
            WriteNullableGuid(writer, file.OwnerId);
            writer.Write((byte)file.Kind);
            WriteString(writer, file.RelativePath);
            WriteString(writer, file.Content);
        }
        return stream.ToArray();
    }

    public static List<OdCodeFile> ReadCodeFiles(ReadOnlySpan<byte> bytes)
    {
        using var reader = Reader(bytes);
        var result = new List<OdCodeFile>();
        int count = ReadCount(reader);
        for (int index = 0; index < count; index++)
        {
            result.Add(new OdCodeFile
            {
                Id = ReadGuid(reader),
                OwnerId = ReadNullableGuid(reader),
                Kind = (OdCodeFileKind)reader.ReadByte(),
                RelativePath = ReadString(reader),
                Content = ReadString(reader),
            });
        }
        return result;
    }

    public static byte[] WriteManifest(OdProject project)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        WriteGuid(writer, project.Id);
        WriteString(writer, project.Name);
        WriteString(writer, project.DefaultNamespace);
        WriteString(writer, project.Description);
        writer.Write(project.CreatedUtc.ToBinary());
        writer.Write(project.ModifiedUtc.ToBinary());
        WriteNullableGuid(writer, project.RootClassId);
        WriteNullableGuid(writer, project.InitialSceneId);
        writer.Write((byte)project.Runtime.PartitionResolver);
        writer.Write(project.Runtime.SpatialCellSize);
        writer.Write(project.Runtime.PartitionCellSize);
        writer.Write(project.Runtime.PartitionIncludeY);
        writer.Write(project.Runtime.ProvisionPartitions);
        writer.Write(project.Runtime.LoadMargin);
        writer.Write(project.Runtime.UnloadDelaySeconds);
        writer.Write(project.Runtime.MaxLoadsPerUpdate);
        writer.Write(project.Runtime.ManagePartitionLoading);
        writer.Write((byte)project.Runtime.NetworkMode);
        writer.Write(project.Runtime.TickIntervalMilliseconds);
        writer.Write((byte)project.Runtime.LockstepTickMode);
        writer.Write((byte)project.Runtime.StateSyncTickMode);
        return stream.ToArray();
    }

    public static void ReadManifest(ReadOnlySpan<byte> bytes, OdProject project, bool hasStartupConfiguration,
        bool hasRuntimeSettings = false, bool hasNetworkMode = false, bool hasTickSettings = false,
        bool hasLockstepTickMode = false, bool hasStateSyncTickMode = false)
    {
        using var reader = Reader(bytes);
        project.Id = ReadGuid(reader);
        project.Name = ReadString(reader);
        project.DefaultNamespace = ReadString(reader);
        project.Description = ReadString(reader);
        project.CreatedUtc = DateTime.FromBinary(reader.ReadInt64());
        project.ModifiedUtc = DateTime.FromBinary(reader.ReadInt64());
        if (hasStartupConfiguration)
        {
            project.RootClassId = ReadNullableGuid(reader);
            project.InitialSceneId = ReadNullableGuid(reader);
        }
        if (hasRuntimeSettings)
        {
            project.Runtime = new OdRuntimeSettings
            {
                PartitionResolver = (OdPartitionResolverKind)reader.ReadByte(),
                SpatialCellSize = reader.ReadSingle(),
                PartitionCellSize = reader.ReadSingle(),
                PartitionIncludeY = reader.ReadBoolean(),
                ProvisionPartitions = reader.ReadBoolean(),
                LoadMargin = reader.ReadSingle(),
                UnloadDelaySeconds = reader.ReadSingle(),
                MaxLoadsPerUpdate = reader.ReadInt32(),
                ManagePartitionLoading = reader.ReadBoolean(),
            };
            if (hasNetworkMode)
                project.Runtime.NetworkMode = (OdNetworkMode)reader.ReadByte();
            if (hasTickSettings)
                project.Runtime.TickIntervalMilliseconds = reader.ReadInt32();
            if (hasLockstepTickMode)
                project.Runtime.LockstepTickMode = (OdLockstepTickMode)reader.ReadByte();
            if (hasStateSyncTickMode)
                project.Runtime.StateSyncTickMode = (OdStateSyncTickMode)reader.ReadByte();
        }
    }

    public static byte[] WriteScopes(IReadOnlyCollection<OdScope> scopes)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(scopes.Count);
        foreach (OdScope scope in scopes)
        {
            WriteEntity(writer, scope);
            WriteString(writer, scope.Namespace);
        }
        return stream.ToArray();
    }

    public static List<OdScope> ReadScopes(ReadOnlySpan<byte> bytes)
    {
        using var reader = Reader(bytes);
        var result = new List<OdScope>();
        int count = ReadCount(reader);
        for (int index = 0; index < count; index++)
        {
            var scope = new OdScope();
            ReadEntity(reader, scope);
            scope.Namespace = ReadString(reader);
            result.Add(scope);
        }
        return result;
    }

    public static byte[] WriteDefinitions(IReadOnlyCollection<OdDefinition> definitions)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(definitions.Count);
        foreach (var definition in definitions)
        {
            writer.Write((byte)definition.Kind);
            WriteEntity(writer, definition);
            WriteString(writer, definition.Namespace);
            switch (definition)
            {
                case OdClassDefinition classDefinition:
                    writer.Write((byte)classDefinition.Replication);
                    writer.Write(classDefinition.PreventInheritance);
                    writer.Write((byte)classDefinition.Relevancy);
                    writer.Write((byte)classDefinition.Partition);
                    writer.Write(classDefinition.IsClasses.Count);
                    foreach (Guid isClass in classDefinition.IsClasses)
                        WriteGuid(writer, isClass);
                    WriteFields(writer, classDefinition.Fields);
                    break;
                case OdStructDefinition structDefinition:
                    writer.Write(false);
                    WriteString(writer, string.Empty);
                    writer.Write(structDefinition.IsStructOf is not null);
                    if (structDefinition.IsStructOf is not null)
                        WriteType(writer, structDefinition.IsStructOf);
                    WriteFields(writer, structDefinition.Fields);
                    break;
                case OdResourceDefinition resourceDefinition:
                    WriteFields(writer, resourceDefinition.Fields);
                    break;
                case OdSwitchStructDefinition switchStructDefinition:
                    WriteFields(writer, switchStructDefinition.Fields);
                    break;
                case OdEnumDefinition enumDefinition:
                    writer.Write(enumDefinition.Members.Count);
                    foreach (var member in enumDefinition.Members)
                    {
                        WriteEntity(writer, member);
                        writer.Write(member.Value);
                    }
                    break;
                case OdInterfaceDefinition interfaceDefinition:
                    writer.Write(interfaceDefinition.IsRoutine);
                    writer.Write((byte)interfaceDefinition.InterfaceType);
                    writer.Write((byte)interfaceDefinition.Prediction);
                    writer.Write(interfaceDefinition.Abstract);
                    WriteNullableGuid(writer, interfaceDefinition.BaseInterfaceId);
                    WriteFields(writer, interfaceDefinition.Inputs);
                    WriteFields(writer, interfaceDefinition.Outputs);
                    break;
                case OdEventDefinition eventDefinition:
                    writer.Write(eventDefinition.Multicast);
                    WriteFields(writer, eventDefinition.Fields);
                    break;
                default:
                    throw new InvalidDataException($"Unsupported definition {definition.GetType().Name}.");
            }
        }
        return stream.ToArray();
    }

    public static List<OdDefinition> ReadDefinitions(ReadOnlySpan<byte> bytes, bool includesLegacyStructResourceKind = true,
        bool includesIsStruct = true, bool includesLegacyClassResource = false, bool includesFieldResource = true,
        bool enumValuesAreInt32 = true, bool includesClassReplication = true, bool includesPawnClass = true,
        bool includesPreventInheritance = true, bool includesSpatialRelevancy = true,
        bool includesPartitionMode = true, bool includesInterfaceType = true,
        bool includesPredictionAndEventMulticast = true, bool includesInterfaceInheritance = true)
    {
        using var reader = Reader(bytes);
        var result = new List<OdDefinition>();
        var count = ReadCount(reader);
        for (var index = 0; index < count; index++)
        {
            var kind = (OdDefinitionKind)reader.ReadByte();
            OdDefinition definition = kind switch
            {
                OdDefinitionKind.Class => new OdClassDefinition(),
                OdDefinitionKind.Struct => new OdStructDefinition(),
                OdDefinitionKind.SwitchStruct => new OdSwitchStructDefinition(),
                OdDefinitionKind.Enum => new OdEnumDefinition(),
                OdDefinitionKind.Interface => new OdInterfaceDefinition(),
                OdDefinitionKind.Event => new OdEventDefinition(),
                OdDefinitionKind.Resource => new OdResourceDefinition(),
                _ => throw new InvalidDataException($"Unknown definition kind {kind}."),
            };
            ReadEntity(reader, definition);
            definition.Namespace = ReadString(reader);
            switch (definition)
            {
                case OdClassDefinition classDefinition:
                    if (includesClassReplication)
                        classDefinition.Replication = (OdObjectReplicationMode)reader.ReadByte();
                    bool legacyPawn = includesPawnClass && !includesPreventInheritance && reader.ReadBoolean();
                    if (includesPreventInheritance)
                        classDefinition.PreventInheritance = reader.ReadBoolean();
                    if (includesSpatialRelevancy)
                    {
                        classDefinition.Relevancy = (OdObjectRelevancyMode)reader.ReadByte();
                        if (includesPartitionMode)
                            classDefinition.Partition = (OdObjectPartitionMode)reader.ReadByte();
                        else
                        {
                            var dependencyCount = ReadCount(reader);
                            for (var dependencyIndex = 0; dependencyIndex < dependencyCount; dependencyIndex++)
                                _ = ReadGuid(reader);
                        }
                    }
                    var isClassCount = ReadCount(reader);
                    for (var isClassIndex = 0; isClassIndex < isClassCount; isClassIndex++)
                        classDefinition.IsClasses.Add(ReadGuid(reader));
                    if (legacyPawn && !classDefinition.IsClasses.Contains(OdNetworkPackage.PawnClassId))
                        classDefinition.IsClasses.Add(OdNetworkPackage.PawnClassId);
                    if (includesLegacyClassResource)
                        _ = reader.ReadBoolean();
                    classDefinition.Fields = ReadFields(reader, includesFieldResource);
                    break;
                case OdStructDefinition structDefinition:
                    bool wasLegacyResource = reader.ReadBoolean();
                    if (includesLegacyStructResourceKind)
                        _ = ReadString(reader);
                    if (includesIsStruct && reader.ReadBoolean())
                        structDefinition.IsStructOf = ReadType(reader, includesFieldResource);
                    structDefinition.Fields = ReadFields(reader, includesFieldResource);
                    if (wasLegacyResource)
                    {
                        definition = new OdResourceDefinition
                        {
                            Id = structDefinition.Id,
                            Name = structDefinition.Name,
                            Description = structDefinition.Description,
                            Namespace = structDefinition.Namespace,
                            Fields = structDefinition.Fields,
                        };
                    }
                    break;
                case OdResourceDefinition resourceDefinition:
                    resourceDefinition.Fields = ReadFields(reader, includesFieldResource);
                    break;
                case OdSwitchStructDefinition switchStructDefinition:
                    switchStructDefinition.Fields = ReadFields(reader, includesFieldResource);
                    break;
                case OdEnumDefinition enumDefinition:
                    var memberCount = ReadCount(reader);
                    for (var memberIndex = 0; memberIndex < memberCount; memberIndex++)
                    {
                        var member = new OdEnumMember();
                        ReadEntity(reader, member);
                        if (enumValuesAreInt32)
                        {
                            member.Value = reader.ReadInt32();
                        }
                        else
                        {
                            long legacyValue = reader.ReadInt64();
                            if (legacyValue is < int.MinValue or > int.MaxValue)
                                throw new InvalidDataException($"Legacy enum value {legacyValue} is outside the supported Int32 range.");
                            member.Value = (int)legacyValue;
                        }
                        enumDefinition.Members.Add(member);
                    }
                    break;
                case OdInterfaceDefinition interfaceDefinition:
                    interfaceDefinition.IsRoutine = reader.ReadBoolean();
                    if (includesInterfaceType)
                        interfaceDefinition.InterfaceType = (OdInterfaceType)reader.ReadByte();
                    byte interfaceMode = reader.ReadByte();
                    if (includesPredictionAndEventMulticast)
                    {
                        interfaceDefinition.Prediction = (OdPredictionMode)interfaceMode;
                    }
                    else if (interfaceMode == 3)
                    {
                        interfaceDefinition.InterfaceType = OdInterfaceType.PlayerInput;
                        interfaceDefinition.Prediction = OdPredictionMode.Both;
                    }
                    if (includesInterfaceInheritance)
                    {
                        interfaceDefinition.Abstract = reader.ReadBoolean();
                        interfaceDefinition.BaseInterfaceId = ReadNullableGuid(reader);
                    }
                    interfaceDefinition.Inputs = ReadFields(reader, includesFieldResource);
                    interfaceDefinition.Outputs = ReadFields(reader, includesFieldResource);
                    break;
                case OdEventDefinition eventDefinition:
                    if (includesPredictionAndEventMulticast)
                        eventDefinition.Multicast = reader.ReadBoolean();
                    eventDefinition.Fields = ReadFields(reader, includesFieldResource);
                    break;
            }
            result.Add(definition);
        }
        return result;
    }

    public static byte[] WritePackageReferences(IReadOnlyCollection<OdPackageReference> references)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(references.Count);
        foreach (OdPackageReference packageReference in references)
        {
            WriteEntity(writer, packageReference);
            WriteGuid(writer, packageReference.PackageId);
            WriteString(writer, packageReference.PackagePath);
        }
        return stream.ToArray();
    }

    public static List<OdPackageReference> ReadPackageReferences(ReadOnlySpan<byte> bytes)
    {
        using var reader = Reader(bytes);
        var result = new List<OdPackageReference>();
        int count = ReadCount(reader);
        for (int index = 0; index < count; index++)
        {
            var packageReference = new OdPackageReference();
            ReadEntity(reader, packageReference);
            packageReference.PackageId = ReadGuid(reader);
            packageReference.PackagePath = ReadString(reader);
            result.Add(packageReference);
        }
        return result;
    }

    public static byte[] WriteDataSets(IReadOnlyCollection<OdDataSet> dataSets)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(dataSets.Count);
        foreach (var dataSet in dataSets)
        {
            WriteEntity(writer, dataSet);
            WriteGuid(writer, dataSet.DefinitionId);
            writer.Write(dataSet.Records.Count);
            foreach (var record in dataSet.Records)
            {
                WriteEntity(writer, record);
                writer.Write(record.Values.Count);
                foreach (var value in record.Values.OrderBy(item => item.Key))
                {
                    WriteGuid(writer, value.Key);
                    WriteJson(writer, value.Value);
                }
            }
        }
        return stream.ToArray();
    }

    public static List<OdDataSet> ReadDataSets(ReadOnlySpan<byte> bytes)
    {
        using var reader = Reader(bytes);
        var result = new List<OdDataSet>();
        var count = ReadCount(reader);
        for (var index = 0; index < count; index++)
        {
            var dataSet = new OdDataSet();
            ReadEntity(reader, dataSet);
            dataSet.DefinitionId = ReadGuid(reader);
            var recordCount = ReadCount(reader);
            for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
            {
                var record = new OdDataRecord();
                ReadEntity(reader, record);
                var valueCount = ReadCount(reader);
                for (var valueIndex = 0; valueIndex < valueCount; valueIndex++)
                    record.Values.Add(ReadGuid(reader), ReadJson(reader));
                dataSet.Records.Add(record);
            }
            result.Add(dataSet);
        }
        return result;
    }

    public static byte[] WriteScenes(IReadOnlyCollection<OdScene> scenes)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(scenes.Count);
        foreach (OdScene scene in scenes)
        {
            WriteEntity(writer, scene);
            WriteGuid(writer, scene.OdId);
            writer.Write(scene.Objects.Count);
            foreach (OdSceneObject sceneObject in scene.Objects)
            {
                WriteEntity(writer, sceneObject);
                WriteGuid(writer, sceneObject.ClassDefinitionId);
                writer.Write(sceneObject.Values.Count);
                foreach (var value in sceneObject.Values.OrderBy(item => item.Key))
                {
                    WriteGuid(writer, value.Key);
                    WriteJson(writer, value.Value);
                }
            }
        }
        return stream.ToArray();
    }

    public static List<OdScene> ReadScenes(ReadOnlySpan<byte> bytes, bool hasLegacyRootObjectId)
    {
        using var reader = Reader(bytes);
        var result = new List<OdScene>();
        int count = ReadCount(reader);
        for (int index = 0; index < count; index++)
        {
            var scene = new OdScene();
            ReadEntity(reader, scene);
            scene.OdId = ReadGuid(reader);
            if (hasLegacyRootObjectId && reader.ReadBoolean())
                _ = ReadGuid(reader);
            int objectCount = ReadCount(reader);
            for (int objectIndex = 0; objectIndex < objectCount; objectIndex++)
            {
                var sceneObject = new OdSceneObject();
                ReadEntity(reader, sceneObject);
                sceneObject.ClassDefinitionId = ReadGuid(reader);
                int valueCount = ReadCount(reader);
                for (int valueIndex = 0; valueIndex < valueCount; valueIndex++)
                    sceneObject.Values.Add(ReadGuid(reader), ReadJson(reader));
                scene.Objects.Add(sceneObject);
            }
            result.Add(scene);
        }
        return result;
    }

    private static void WriteFields(BinaryWriter writer, IReadOnlyCollection<OdFieldDefinition> fields)
    {
        writer.Write(fields.Count);
        foreach (var field in fields)
        {
            WriteEntity(writer, field);
            WriteType(writer, field.Type);
            writer.Write(field.NotNull);
            writer.Write((byte)field.Mode);
            writer.Write((byte)field.Replication);
            writer.Write(field.DrivenByFields.Count);
            foreach (Guid drivenBy in field.DrivenByFields)
                writer.Write(drivenBy.ToByteArray());
            WriteJson(writer, field.DefaultValue);
            writer.Write(field.Metadata.Count);
            foreach (var metadata in field.Metadata.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                WriteString(writer, metadata.Key);
                WriteString(writer, metadata.Value);
            }
        }
    }

    private static List<OdFieldDefinition> ReadFields(BinaryReader reader, bool includesFieldResource)
    {
        var result = new List<OdFieldDefinition>();
        var count = ReadCount(reader);
        for (var index = 0; index < count; index++)
        {
            var field = new OdFieldDefinition();
            ReadEntity(reader, field);
            field.Type = ReadType(reader, includesFieldResource);
            field.NotNull = reader.ReadBoolean();
            field.Mode = (OdFieldMode)reader.ReadByte();
            field.Replication = (OdFieldReplicationMode)reader.ReadByte();
            var drivenByCount = ReadCount(reader);
            for (var drivenByIndex = 0; drivenByIndex < drivenByCount; drivenByIndex++)
                field.DrivenByFields.Add(new Guid(reader.ReadBytes(16)));
            field.DefaultValue = ReadJson(reader);
            var metadataCount = ReadCount(reader);
            for (var metadataIndex = 0; metadataIndex < metadataCount; metadataIndex++)
                field.Metadata.Add(ReadString(reader), ReadString(reader));
            result.Add(field);
        }
        return result;
    }

    private static void WriteType(BinaryWriter writer, OdTypeReference type)
    {
        writer.Write((byte)type.BuiltIn);
        WriteNullableGuid(writer, type.DefinitionId);
        writer.Write(type.IsResourceClass);
        writer.Write((byte)type.Container);
        writer.Write((byte)type.DictionaryKeyBuiltIn);
        WriteNullableGuid(writer, type.DictionaryKeyDefinitionId);
        writer.Write(type.DictionaryKeyIsResourceClass);
        writer.Write(type.Nullable);
    }

    private static OdTypeReference ReadType(BinaryReader reader, bool includesResourceClasses) => new()
    {
        BuiltIn = (OdBuiltInType)reader.ReadByte(),
        DefinitionId = ReadNullableGuid(reader),
        IsResourceClass = includesResourceClasses && reader.ReadBoolean(),
        Container = (OdContainerKind)reader.ReadByte(),
        DictionaryKeyBuiltIn = (OdBuiltInType)reader.ReadByte(),
        DictionaryKeyDefinitionId = ReadNullableGuid(reader),
        DictionaryKeyIsResourceClass = includesResourceClasses && reader.ReadBoolean(),
        Nullable = reader.ReadBoolean(),
    };

    private static void WriteEntity(BinaryWriter writer, OdEntity entity)
    {
        WriteGuid(writer, entity.Id);
        WriteString(writer, entity.Name);
        WriteString(writer, entity.Description);
    }

    private static void ReadEntity(BinaryReader reader, OdEntity entity)
    {
        entity.Id = ReadGuid(reader);
        entity.Name = ReadString(reader);
        entity.Description = ReadString(reader);
    }

    private static void WriteJson(BinaryWriter writer, JsonNode? value) =>
        WriteNullableString(writer, value?.ToJsonString());

    private static JsonNode? ReadJson(BinaryReader reader)
    {
        var json = ReadNullableString(reader);
        return json is null ? null : JsonNode.Parse(json);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        var count = ReadCount(reader);
        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
            throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteNullableString(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null)
            WriteString(writer, value);
    }

    private static string? ReadNullableString(BinaryReader reader) => reader.ReadBoolean() ? ReadString(reader) : null;

    private static void WriteGuid(BinaryWriter writer, Guid value) => writer.Write(value.ToByteArray());

    private static Guid ReadGuid(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(16);
        if (bytes.Length != 16)
            throw new EndOfStreamException();
        return new Guid(bytes);
    }

    private static void WriteNullableGuid(BinaryWriter writer, Guid? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
            WriteGuid(writer, value.Value);
    }

    private static Guid? ReadNullableGuid(BinaryReader reader) => reader.ReadBoolean() ? ReadGuid(reader) : null;

    private static int ReadCount(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > 10_000_000)
            throw new InvalidDataException($"Invalid collection or string length {count}.");
        return count;
    }

    private static BinaryReader Reader(ReadOnlySpan<byte> bytes) =>
        new(new MemoryStream(bytes.ToArray(), false), Encoding.UTF8, false);
}
