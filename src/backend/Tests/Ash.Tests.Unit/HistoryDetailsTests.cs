using System.Text.Json.Nodes;
using Ash.Modules.Cases.Infrastructure.History;
using Shouldly;

namespace Ash.Tests.Unit;

public class HistoryDetailsTests
{
    private static JsonObject Parse(string? json) => (JsonObject)JsonNode.Parse(json!)!;

    [Fact]
    public void Only_changed_fields_are_stored()
    {
        var json = HistoryDetails.Build(
            new { judge = "A", court = "Tiranë", priority = 1 },
            new { judge = "B", court = "Tiranë", priority = 1 });

        var root = Parse(json);
        root["old"]!.AsObject().ToJsonString().ShouldBe("""{"judge":"A"}""");
        root["new"]!.AsObject().ToJsonString().ShouldBe("""{"judge":"B"}""");
    }

    [Fact]
    public void Creation_has_null_old_and_all_new_fields()
    {
        var root = Parse(HistoryDetails.Build(null, new { lawyer = "Ermal", role = "PRIMARY" }));

        root["old"].ShouldBeNull();
        root["new"]!["lawyer"]!.GetValue<string>().ShouldBe("Ermal");
        root["new"]!["role"]!.GetValue<string>().ShouldBe("PRIMARY");
    }

    [Fact]
    public void Field_present_on_one_side_only_counts_as_changed()
    {
        var root = Parse(HistoryDetails.Build(
            new Dictionary<string, object?> { ["a"] = 1 },
            new Dictionary<string, object?> { ["a"] = 1, ["b"] = "x" }));

        root["old"]!.AsObject().ToJsonString().ShouldBe("""{"b":null}""");
        root["new"]!.AsObject().ToJsonString().ShouldBe("""{"b":"x"}""");
    }

    [Fact]
    public void Removal_has_null_new()
    {
        var root = Parse(HistoryDetails.Build(new { party = "X" }, null));

        root["old"]!["party"]!.GetValue<string>().ShouldBe("X");
        root["new"].ShouldBeNull();
    }

    [Fact]
    public void No_values_gives_null_details()
    {
        HistoryDetails.Build(null, null).ShouldBeNull();
    }

    [Fact]
    public void Non_object_values_are_rejected()
    {
        Should.Throw<ArgumentException>(() => HistoryDetails.Build(null, "text"));
    }
}
