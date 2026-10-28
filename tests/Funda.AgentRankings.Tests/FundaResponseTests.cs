using System.Text.Json;
using Funda.AgentRankings.Api.Contracts;
using System.Text.Json.Nodes;
using Funda.AgentRankings.Tests.ObjectMothers;

namespace Funda.AgentRankings.Tests;

public sealed class FundaResponseTests
{
    [Fact]
    public void Deserialize_When_ResponseContainsPagination_Then_MapsPageMetadata()
    {
        // Arrange
        var json = FundaResponseJsonMother.CreateDefault();

        // Act
        var result = JsonSerializer.Deserialize<FundaResponse>(json)!;

        // Assert
        Assert.Equal(226, result.Paging.TotalPages);
        Assert.Equal(1, result.Paging.CurrentPage);
    }

    [Fact]
    public void Deserialize_When_ResponseContainsParentAndChild_Then_MapsListingIdentityAtBothLevels()
    {
        // Arrange
        var json = CreateWithParentAndChild();

        // Act
        var result = JsonSerializer.Deserialize<FundaResponse>(json)!;

        // Assert
        var parent = Assert.Single(result.Objects);
        Assert.Equal(("listing-1", 42, "Example Agent"), (parent.Id, parent.AgentId!.Value, parent.AgentName));
        var child = Assert.Single(parent.ChildrenObjects!);
        Assert.Equal(("child-1", 43, "Child Agent"), (child.Id, child.AgentId!.Value, child.AgentName));
    }

    [Fact]
    public void Deserialize_If_ObjectsPropertyIsMissing_Then_ThrowsJsonException()
    {
        // Arrange
        var json = CreateWithMissingObjects();

        // Act
        var act = () => JsonSerializer.Deserialize<FundaResponse>(json);

        // Assert
        Assert.Throws<JsonException>(act);
    }

    [Fact]
    public void Deserialize_When_SearchHasNoResults_Then_ReturnsEmptyObjects()
    {
        // Arrange
        var json = FundaResponseJsonMother.CreateWithEmptyResults();

        // Act
        var result = JsonSerializer.Deserialize<FundaResponse>(json)!;

        // Assert
        Assert.Empty(result.Objects);
    }

    private static string CreateWithParentAndChild()
    {
        var json = JsonNode.Parse(FundaResponseJsonMother.CreateDefault())!;
        json["Objects"]![0]!["ChildrenObjects"] = JsonNode.Parse("""[{"Id":"child-1","MakelaarId":43,"MakelaarNaam":"Child Agent"}]""");
        return json.ToJsonString();
    }

    private static string CreateWithMissingObjects() =>
        FundaResponseJsonMother.CreateDefault().Replace("\"Objects\":", "\"UnexpectedObjects\":", StringComparison.Ordinal);
}
