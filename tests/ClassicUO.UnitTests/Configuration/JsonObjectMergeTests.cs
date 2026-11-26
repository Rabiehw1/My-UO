#nullable enable

using System.Text.Json.Nodes;
using ClassicUO.Configuration;
using Xunit;

namespace ClassicUO.UnitTests.Configuration;

public class JsonObjectMergeTests
{
    [Fact]
    public void CombinesDifferentKeysAndConvergentEdits()
    {
        AssertMerge(
            """{"autorun":{"A":"old"},"groups":{"old":true},"order":["old"]}""",
            """{"autorun":{"A":"next"},"groups":{"old":true,"local":false},"order":[],"new":{"A":1}}""",
            """{"autorun":{"A":"old","B":"other"},"groups":{},"order":[],"new":{"B":2}}""",
            """{"autorun":{"A":"next","B":"other"},"groups":{"local":false},"order":[],"new":{"A":1,"B":2}}""",
            """{"autorun":{"A":"next","B":"other"},"groups":{"local":false},"order":[],"new":{"A":1,"B":2}}"""
        );
    }

    [Fact]
    public void ChoicesPreserveIndependentEditsAroundAConflictingArray()
    {
        AssertMerge(
            """{"sequence":{"A":["one","two"]}}""",
            """{"sequence":{"A":["replaced","two"],"B":["local"]}}""",
            """{"sequence":{"A":["one","replaced"],"C":["saved"]}}""",
            """{"sequence":{"A":["replaced","two"],"B":["local"],"C":["saved"]}}""",
            """{"sequence":{"A":["one","replaced"],"B":["local"],"C":["saved"]}}"""
        );
    }

    [Fact]
    public void DeletingAPropertyConflictsWithExplicitlySettingItToNull()
    {
        AssertMerge(
            """{"entry":"original","nullable":null}""",
            """{"nullable":null}""",
            """{"entry":null,"nullable":"saved"}""",
            """{"nullable":"saved"}""",
            """{"entry":null,"nullable":"saved"}"""
        );
    }

    [Fact]
    public void ResultsDoNotShareMutableNodesWithTheirInputsOrEachOther()
    {
        JsonObject session = Parse("""{"array":["original"]}""");
        (JsonObject keepSession, JsonObject keepDisk) = JsonObjectMerge.Merge(new JsonObject(), session, new JsonObject());

        keepSession["array"]![0] = "changed";

        Assert.Equal("original", keepDisk["array"]![0]!.GetValue<string>());
        Assert.Equal("original", session["array"]![0]!.GetValue<string>());
    }

    private static void AssertMerge(string baseline, string session, string disk, string expectedSession, string expectedDisk)
    {
        (JsonObject keepSession, JsonObject keepDisk) = JsonObjectMerge.Merge(Parse(baseline), Parse(session), Parse(disk));

        Assert.True(JsonNode.DeepEquals(Parse(expectedSession), keepSession), keepSession.ToJsonString());
        Assert.True(JsonNode.DeepEquals(Parse(expectedDisk), keepDisk), keepDisk.ToJsonString());
    }

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();
}
