using System.Text.Json.Nodes;
using ODStudio.Model;

namespace ODStudio.Generator;

public static class OdResourceCatalog
{
    private const string SignatureVersion = "od-resource-catalog:2";

    public static string BuildSignature(OdProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        Dictionary<Guid, OdDefinition> definitions = project.Definitions
            .Concat(OdNetworkPackage.CreateProject(project.Runtime.NetworkMode).Definitions)
            .GroupBy(definition => definition.Id)
            .ToDictionary(group => group.Key, group => group.First());
        Dictionary<Guid, OdDefinition> resourceDefinitions = project.Definitions
            .Where(definition => definition is OdClassDefinition or OdResourceDefinition)
            .ToDictionary(definition => definition.Id);
        var parts = new List<string> { SignatureVersion };

        foreach (OdDataSet dataSet in project.DataSets
                     .Where(dataSet => resourceDefinitions.ContainsKey(dataSet.DefinitionId))
                     .OrderBy(dataSet => dataSet.DefinitionId)
                     .ThenBy(dataSet => dataSet.Id))
        {
            OdDefinition definition = resourceDefinitions[dataSet.DefinitionId];
            IReadOnlyList<OdFieldDefinition> fields = definition is OdClassDefinition classDefinition
                ? AllFields(classDefinition, definitions).ToList()
                : ((OdResourceDefinition)definition).Fields;

            foreach (OdDataRecord record in dataSet.Records.OrderBy(record => record.Id))
            {
                parts.Add($"record:{dataSet.DefinitionId:N}:{record.Id:N}");
                foreach (OdFieldDefinition field in fields.OrderBy(field => field.Id))
                {
                    JsonNode? value = record.Values.TryGetValue(field.Id, out JsonNode? configuredValue)
                        ? configuredValue
                        : field.DefaultValue;
                    parts.Add($"value:{field.Id:N}:{CanonicalJson(value)}");
                }
            }
        }

        return string.Join("|", parts);
    }

    private static IEnumerable<OdFieldDefinition> AllFields(
        OdClassDefinition definition,
        IReadOnlyDictionary<Guid, OdDefinition> definitions)
    {
        var visitedClasses = new HashSet<Guid>();
        var visitedFields = new HashSet<Guid>();
        return Visit(definition);

        IEnumerable<OdFieldDefinition> Visit(OdClassDefinition current)
        {
            if (!visitedClasses.Add(current.Id))
                yield break;

            foreach (Guid isClass in current.IsClasses)
            {
                if (!definitions.TryGetValue(isClass, out OdDefinition? parent) || parent is not OdClassDefinition parentClass)
                    continue;
                foreach (OdFieldDefinition field in Visit(parentClass))
                    yield return field;
            }

            foreach (OdFieldDefinition field in current.Fields)
                if (visitedFields.Add(field.Id))
                    yield return field;
        }
    }

    private static string CanonicalJson(JsonNode? value)
    {
        if (value is null)
            return "null";
        if (value is JsonArray array)
            return "[" + string.Join(",", array.Select(CanonicalJson)) + "]";
        if (value is JsonObject obj)
        {
            return "{" + string.Join(",", obj.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => System.Text.Json.JsonSerializer.Serialize(item.Key) + ":" + CanonicalJson(item.Value))) + "}";
        }
        return value.ToJsonString();
    }
}
