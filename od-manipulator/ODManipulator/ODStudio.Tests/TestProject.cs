using System.Text.Json.Nodes;
using ODStudio.Model;

namespace ODStudio.Tests;

internal static class TestProject
{
    public static OdProject Create()
    {
        var health = new OdFieldDefinition
        {
            Name = "Health",
            Type = OdTypeReference.BuiltInType(OdBuiltInType.Int32),
            Replication = OdFieldReplicationMode.AllClients,
            DefaultValue = JsonValue.Create(100),
        };
        var actor = new OdClassDefinition
        {
            Name = "Actor",
            Namespace = "Tests.OD",
            Fields = { health },
        };
        var damage = new OdInterfaceDefinition
        {
            Name = "Damage",
            Namespace = "Tests.OD",
            Inputs =
            {
                new OdFieldDefinition
                {
                    Name = "Target",
                    Type = new OdTypeReference { DefinitionId = actor.Id },
                    NotNull = true,
                },
                new OdFieldDefinition { Name = "Amount", Type = OdTypeReference.BuiltInType(OdBuiltInType.Int32) },
            },
        };
        var dataSet = new OdDataSet
        {
            Name = "Actors",
            DefinitionId = actor.Id,
            Records =
            {
                new OdDataRecord
                {
                    Name = "DefaultActor",
                    Values = { [health.Id] = JsonValue.Create(100) },
                },
            },
        };
        var project = new OdProject
        {
            Name = "TestGame",
            DefaultNamespace = "Tests.OD",
            RootClassId = actor.Id,
            Ods = { new OdScope { Name = "TestGame", Namespace = "Tests.OD" } },
            Definitions = { actor, damage },
            DataSets = { dataSet },
            History =
            {
                new OdChangeRecord { Sequence = 1, Kind = OdChangeKind.Create, Summary = "Create test project" },
            },
        };
        return project;
    }
}
