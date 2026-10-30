using System.Text.Json;
using Shouldly;

namespace Ash.Tests.Integration.Infrastructure;

public static class JsonAssert
{
    /// <summary>Every "id" / "…Id" / "…_id" property, at any depth, must not hold a number (internal ids never leave the API).</summary>
    public static void NoNumericIds(JsonElement element, string path = "$")
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var isId = property.Name.Equals("id", StringComparison.OrdinalIgnoreCase)
                               || property.Name.EndsWith("Id", StringComparison.Ordinal)
                               || property.Name.EndsWith("_id", StringComparison.OrdinalIgnoreCase);
                    (isId && property.Value.ValueKind == JsonValueKind.Number)
                        .ShouldBeFalse($"numeric id at {path}.{property.Name}");
                    NoNumericIds(property.Value, $"{path}.{property.Name}");
                }

                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    NoNumericIds(item, $"{path}[{i++}]");
                }

                break;
        }
    }
}
