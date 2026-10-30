using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ash.Modules.Cases.Infrastructure.History;

/// <summary>Builds the details JSON {"old":{...},"new":{...}} containing only the changed fields.</summary>
public static class HistoryDetails
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Returns null when both sides are null. A null <paramref name="old"/> (creation) gives "old":null and all new fields.</summary>
    public static string? Build(object? old, object? @new)
    {
        if (old is null && @new is null)
        {
            return null;
        }

        var oldFields = ToObject(old);
        var newFields = ToObject(@new);

        JsonNode? oldResult;
        JsonNode? newResult;

        if (oldFields is null || newFields is null)
        {
            oldResult = oldFields;
            newResult = newFields;
        }
        else
        {
            var oldChanged = new JsonObject();
            var newChanged = new JsonObject();
            foreach (var key in oldFields.Select(p => p.Key).Union(newFields.Select(p => p.Key)))
            {
                oldFields.TryGetPropertyValue(key, out var before);
                newFields.TryGetPropertyValue(key, out var after);
                if (!JsonNode.DeepEquals(before, after))
                {
                    oldChanged[key] = before?.DeepClone();
                    newChanged[key] = after?.DeepClone();
                }
            }

            oldResult = oldChanged;
            newResult = newChanged;
        }

        var root = new JsonObject { ["old"] = oldResult, ["new"] = newResult };
        return root.ToJsonString();
    }

    /// <summary>
    /// The old/new parts of a stored details JSON for the API, with every internal numeric id removed (any "id" or
    /// "…Id" property holding a number, at any depth). Writers no longer store such ids (task 020); rows written before
    /// that still contain courtId/judgeId/headUserId. Unreadable JSON gives (null, null).
    /// </summary>
    public static (JsonNode? Old, JsonNode? New) Read(string? details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return (null, null);
        }

        try
        {
            if (JsonNode.Parse(details) is not JsonObject root)
            {
                return (null, null);
            }

            return (StripInternalIds(root["old"]?.DeepClone()), StripInternalIds(root["new"]?.DeepClone()));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    public static JsonNode? StripInternalIds(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (IsIdName(key) && obj[key] is JsonValue value && value.GetValueKind() == JsonValueKind.Number)
                    {
                        obj.Remove(key);
                    }
                    else
                    {
                        StripInternalIds(obj[key]);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    StripInternalIds(item);
                }

                break;
        }

        return node;
    }

    private static bool IsIdName(string key) =>
        key.Equals("id", StringComparison.OrdinalIgnoreCase) || key.EndsWith("Id", StringComparison.Ordinal)
                                                             || key.EndsWith("_id", StringComparison.OrdinalIgnoreCase);

    private static JsonObject? ToObject(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return JsonSerializer.SerializeToNode(value, Options) as JsonObject
            ?? throw new ArgumentException("History old/new values must serialize to a JSON object.", nameof(value));
    }
}
