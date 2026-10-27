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
