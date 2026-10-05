namespace ODStudio.Model;

public static class OdTypePresentation
{
    public static string BuiltInDisplayName(OdBuiltInType type) => type switch
    {
        OdBuiltInType.Boolean => "bool",
        OdBuiltInType.Int32 => "int",
        OdBuiltInType.Int64 => "long",
        OdBuiltInType.Single => "float",
        OdBuiltInType.Double => "double",
        OdBuiltInType.Decimal => "decimal",
        OdBuiltInType.String => "string",
        OdBuiltInType.DateTime => "DateTime",
        _ => type.ToString(),
    };

    public static int FuzzyScore(string name, string detail, string category, string query)
    {
        query = query.Trim();
        if (query.Length == 0)
            return 0;
        string candidate = $"{name} {detail} {category}";
        int direct = candidate.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (direct >= 0)
            return 1000 - direct;

        int queryIndex = 0;
        int firstMatch = -1;
        int lastMatch = -1;
        for (int index = 0; index < candidate.Length && queryIndex < query.Length; index++)
        {
            if (char.ToUpperInvariant(candidate[index]) != char.ToUpperInvariant(query[queryIndex]))
                continue;
            if (firstMatch < 0)
                firstMatch = index;
            lastMatch = index;
            queryIndex++;
        }
        return queryIndex == query.Length ? 500 - (lastMatch - firstMatch) : -1;
    }
}
