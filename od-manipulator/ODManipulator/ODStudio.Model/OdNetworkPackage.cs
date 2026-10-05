namespace ODStudio.Model;

public static class OdNetworkPackage
{
    public static readonly Guid PackageId = Guid.Parse("6f701951-49a3-4d87-a15c-64eb65ff1000");
    public static readonly Guid ScopeId = Guid.Parse("6f701951-49a3-4d87-a15c-64eb65ff1001");
    public static readonly Guid PlayerControllerClassId = Guid.Parse("6f701951-49a3-4d87-a15c-64eb65ff1010");
    public static readonly Guid LockstepPlayerControllerClassId = Guid.Parse("6f701951-49a3-4d87-a15c-64eb65ff1014");
    public static readonly Guid PawnClassId = Guid.Parse("6f701951-49a3-4d87-a15c-64eb65ff1020");
    public static readonly Guid ActorClassId = Guid.Parse("6f701951-49a3-4d87-a15c-64eb65ff1030");
    public static readonly Guid PlayerControllerPawnFieldId = Guid.Parse("6f701951-49a3-4d87-a15c-64eb65ff1011");
    public static readonly Guid PlayerControllerViewRangeFieldId = Guid.Parse("6f701951-49a3-4d87-a15c-64eb65ff1012");
    public static readonly Guid LockstepPlayerControllerSlotIdFieldId = Guid.Parse("6f701951-49a3-4d87-a15c-64eb65ff1013");
    public static readonly Guid PawnPlayerControllerFieldId = Guid.Parse("6f701951-49a3-4d87-a15c-64eb65ff1021");
    public static readonly Guid ActorTransformFieldId = Guid.Parse("6f701951-49a3-4d87-a15c-64eb65ff1031");
    public static readonly Guid Float3StructId = Guid.Parse("6f701951-49a3-4d87-a15c-64eb65ff1040");
    public static readonly Guid PositionStructId = Guid.Parse("6f701951-49a3-4d87-a15c-64eb65ff1050");
    public static readonly Guid RotationStructId = Guid.Parse("6f701951-49a3-4d87-a15c-64eb65ff1060");
    public static readonly Guid TransformStructId = Guid.Parse("6f701951-49a3-4d87-a15c-64eb65ff1070");

    private const int LegacyPlayerControllerBuiltInValue = 9;
    private const int LegacyPawnBuiltInValue = 10;

    public static OdProject CreateProject(OdNetworkMode mode = OdNetworkMode.StateSync)
    {
        OdProject project = CreateStateSyncProject();
        if (mode == OdNetworkMode.Lockstep)
        {
            project.Description = "Built-in GameplayMachine lockstep types (Transform is intentionally unavailable).";
            project.Definitions.RemoveAll(definition => definition.Id != PlayerControllerClassId);
            var playerController = (OdClassDefinition)project.Definitions[0];
            playerController.Id = LockstepPlayerControllerClassId;
            playerController.Name = "PlayerController";
            playerController.Description = "The engine-managed controller for one deterministic player slot.";
            playerController.Replication = OdObjectReplicationMode.None;
            playerController.Fields.Clear();
            playerController.Fields.Add(new OdFieldDefinition
            {
                Id = LockstepPlayerControllerSlotIdFieldId,
                Name = "SlotID",
                Description = "The zero-based deterministic player slot assigned by the relay.",
                Type = OdTypeReference.BuiltInType(OdBuiltInType.Int32),
                DefaultValue = System.Text.Json.Nodes.JsonValue.Create(-1),
                Replication = OdFieldReplicationMode.None,
            });
        }
        return project;
    }

    private static OdProject CreateStateSyncProject() => new()
    {
        Id = PackageId,
        Name = "Network",
        DefaultNamespace = "GMCore",
        Description = "Built-in GameplayMachine networking types.",
        Ods =
        {
            new OdScope { Id = ScopeId, Name = "Network", Namespace = "GMCore" },
        },
        Definitions =
        {
            new OdClassDefinition
            {
                Id = PlayerControllerClassId,
                Name = "PlayerController",
                Namespace = "GMCore",
                Description = "The engine-managed controller for one connected player.",
                Replication = OdObjectReplicationMode.OwnerOnly,
                PreventInheritance = true,
                Fields =
                {
                    new OdFieldDefinition
                    {
                        Id = PlayerControllerPawnFieldId,
                        Name = "Pawn",
                        Description = "The Pawn currently possessed by this player.",
                        Type = new OdTypeReference
                        {
                            DefinitionId = PawnClassId,
                            Nullable = true,
                        },
                        Replication = OdFieldReplicationMode.OwnerOnly,
                    },
                    new OdFieldDefinition
                    {
                        Id = PlayerControllerViewRangeFieldId,
                        Name = "ViewRange",
                        Description = "The world-space interest radius of this player.",
                        Type = OdTypeReference.BuiltInType(OdBuiltInType.Single),
                        DefaultValue = System.Text.Json.Nodes.JsonValue.Create(64f),
                        Replication = OdFieldReplicationMode.OwnerOnly,
                    },
                },
            },
            new OdClassDefinition
            {
                Id = ActorClassId,
                Name = "Actor",
                Namespace = "GMCore",
                Description = "A gameplay object with an engine-independent transform.",
                Replication = OdObjectReplicationMode.AllClients,
                Fields =
                {
                    new OdFieldDefinition
                    {
                        Id = ActorTransformFieldId,
                        Name = "Transform",
                        Description = "The actor position, rotation, and scale.",
                        Type = new OdTypeReference { DefinitionId = TransformStructId },
                        DefaultValue = TransformDefault(),
                        Replication = OdFieldReplicationMode.AllClients,
                    },
                },
            },
            new OdClassDefinition
            {
                Id = PawnClassId,
                Name = "Pawn",
                Namespace = "GMCore",
                Description = "A gameplay object that can be possessed by a PlayerController.",
                Replication = OdObjectReplicationMode.AllClients,
                IsClasses = { ActorClassId },
                Fields =
                {
                    new OdFieldDefinition
                    {
                        Id = PawnPlayerControllerFieldId,
                        Name = "PlayerController",
                        Description = "The PlayerController currently possessing this Pawn.",
                        Type = new OdTypeReference
                        {
                            DefinitionId = PlayerControllerClassId,
                            Nullable = true,
                        },
                        Replication = OdFieldReplicationMode.OwnerOnly,
                    },
                },
            },
            Float3Definition(),
            PositionDefinition(),
            RotationDefinition(),
            TransformDefinition(),
        },
    };

    public static bool IsPackage(OdProject project) => project.Id == PackageId;

    public static string? RuntimeTypeName(
        Guid definitionId,
        OdNetworkMode mode = OdNetworkMode.StateSync) => definitionId switch
    {
        var id when id == PlayerControllerClassId => "global::GMCore.StateSync.PlayerController",
        var id when id == LockstepPlayerControllerClassId => "global::GMCore.Lockstep.PlayerController",
        var id when id == ActorClassId => "global::GMCore.Actor",
        var id when id == PawnClassId => "global::GMCore.Pawn",
        var id when id == Float3StructId => "global::GMCore.Float3",
        var id when id == PositionStructId => "global::GMCore.Position",
        var id when id == RotationStructId => "global::GMCore.Rotation",
        var id when id == TransformStructId => "global::GMCore.Transform",
        _ => null,
    };

    public static string? RuntimeStableName(
        Guid definitionId,
        OdNetworkMode mode = OdNetworkMode.StateSync) => definitionId switch
    {
        var id when id == PlayerControllerClassId => "GMCore.StateSync.PlayerController",
        var id when id == LockstepPlayerControllerClassId => "GMCore.Lockstep.PlayerController",
        var id when id == ActorClassId => "GMCore.Actor",
        var id when id == PawnClassId => "GMCore.Pawn",
        _ => null,
    };

    public static string? RuntimeFieldStableName(
        Guid fieldId,
        OdNetworkMode mode = OdNetworkMode.StateSync) => fieldId switch
    {
        var id when id == PlayerControllerPawnFieldId => "GMCore.StateSync.PlayerController.Pawn",
        var id when id == PlayerControllerViewRangeFieldId => "GMCore.StateSync.PlayerController.ViewRange",
        var id when id == ActorTransformFieldId => "GMCore.Actor.Transform",
        var id when id == PawnPlayerControllerFieldId => "GMCore.Pawn.PlayerController",
        var id when id == LockstepPlayerControllerSlotIdFieldId =>
            "GMCore.Lockstep.PlayerController.SlotID",
        _ => null,
    };

    public static string PlayerControllerRuntimeTypeName(OdNetworkMode mode) =>
        mode == OdNetworkMode.Lockstep
            ? "global::GMCore.Lockstep.PlayerController"
            : "global::GMCore.StateSync.PlayerController";

    public static string PlayerControllerNamespace(OdNetworkMode mode) =>
        mode == OdNetworkMode.Lockstep ? "GMCore.Lockstep" : "GMCore.StateSync";

    public static void MigrateLegacyDefinitions(
        IEnumerable<OdDefinition> definitions,
        OdNetworkMode mode = OdNetworkMode.StateSync)
    {
        foreach (OdDefinition definition in definitions)
        {
            IEnumerable<OdFieldDefinition> fields = definition switch
            {
                OdFieldContainerDefinition container => container.Fields,
                OdInterfaceDefinition interfaceDefinition => interfaceDefinition.Inputs.Concat(interfaceDefinition.Outputs),
                _ => Array.Empty<OdFieldDefinition>(),
            };
            foreach (OdFieldDefinition field in fields)
                MigrateLegacyType(field.Type, mode);
            if (definition is OdStructDefinition { IsStructOf: { } isStructOf })
                MigrateLegacyType(isStructOf, mode);
        }
    }

    public static void MigrateLegacyType(
        OdTypeReference type,
        OdNetworkMode mode = OdNetworkMode.StateSync)
    {
        if ((int)type.BuiltIn == LegacyPlayerControllerBuiltInValue)
        {
            type.BuiltIn = OdBuiltInType.None;
            type.DefinitionId = PlayerControllerClassId;
        }
        else if ((int)type.BuiltIn == LegacyPawnBuiltInValue)
        {
            type.BuiltIn = OdBuiltInType.None;
            type.DefinitionId = PawnClassId;
        }
        if (mode == OdNetworkMode.Lockstep && type.DefinitionId == PlayerControllerClassId)
            type.DefinitionId = LockstepPlayerControllerClassId;

        if ((int)type.DictionaryKeyBuiltIn == LegacyPlayerControllerBuiltInValue)
        {
            type.DictionaryKeyBuiltIn = OdBuiltInType.None;
            type.DictionaryKeyDefinitionId = PlayerControllerClassId;
        }
        else if ((int)type.DictionaryKeyBuiltIn == LegacyPawnBuiltInValue)
        {
            type.DictionaryKeyBuiltIn = OdBuiltInType.None;
            type.DictionaryKeyDefinitionId = PawnClassId;
        }
        if (mode == OdNetworkMode.Lockstep &&
            type.DictionaryKeyDefinitionId == PlayerControllerClassId)
            type.DictionaryKeyDefinitionId = LockstepPlayerControllerClassId;
    }

    private static OdStructDefinition Float3Definition() => new()
    {
        Id = Float3StructId, Name = "Float3", Namespace = "GMCore",
        Fields =
        {
            FloatField("Float3.X", "X"), FloatField("Float3.Y", "Y"), FloatField("Float3.Z", "Z"),
        },
    };

    private static OdStructDefinition PositionDefinition() => new()
    {
        Id = PositionStructId, Name = "Position", Namespace = "GMCore",
        Fields =
        {
            FloatField("Position.X", "X"), FloatField("Position.Y", "Y"), FloatField("Position.Z", "Z"),
        },
    };

    private static OdStructDefinition RotationDefinition() => new()
    {
        Id = RotationStructId, Name = "Rotation", Namespace = "GMCore",
        Fields =
        {
            FloatField("Rotation.X", "X"), FloatField("Rotation.Y", "Y"), FloatField("Rotation.Z", "Z"),
            new OdFieldDefinition
            {
                Id = StableGuid("Rotation.W"), Name = "W",
                Type = OdTypeReference.BuiltInType(OdBuiltInType.Single),
                DefaultValue = System.Text.Json.Nodes.JsonValue.Create(1f),
            },
        },
    };

    private static OdStructDefinition TransformDefinition() => new()
    {
        Id = TransformStructId, Name = "Transform", Namespace = "GMCore",
        Fields =
        {
            new OdFieldDefinition { Id = StableGuid("Transform.Position"), Name = "Position", Type = new OdTypeReference { DefinitionId = PositionStructId } },
            new OdFieldDefinition { Id = StableGuid("Transform.Rotation"), Name = "Rotation", Type = new OdTypeReference { DefinitionId = RotationStructId } },
            new OdFieldDefinition { Id = StableGuid("Transform.Scale"), Name = "Scale", Type = new OdTypeReference { DefinitionId = Float3StructId } },
        },
    };

    private static OdFieldDefinition FloatField(string stableName, string name) => new()
    {
        Id = StableGuid(stableName), Name = name, Type = OdTypeReference.BuiltInType(OdBuiltInType.Single),
    };

    private static Guid StableGuid(string value)
    {
        byte[] hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("GMCore.Network." + value));
        return new Guid(hash);
    }

    private static System.Text.Json.Nodes.JsonObject TransformDefault() => new()
    {
        ["Position"] = new System.Text.Json.Nodes.JsonObject { ["X"] = 0f, ["Y"] = 0f, ["Z"] = 0f },
        ["Rotation"] = new System.Text.Json.Nodes.JsonObject { ["X"] = 0f, ["Y"] = 0f, ["Z"] = 0f, ["W"] = 1f },
        ["Scale"] = new System.Text.Json.Nodes.JsonObject { ["X"] = 1f, ["Y"] = 1f, ["Z"] = 1f },
    };
}
