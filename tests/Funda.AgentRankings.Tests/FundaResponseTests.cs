using System.Text.Json;
using Funda.AgentRankings.Api.Contracts;
using Funda.AgentRankings.Listings;
using Funda.AgentRankings.Tests.ObjectMothers;

namespace Funda.AgentRankings.Tests;

public sealed class FundaResponseTests
{

    [Fact]
    public void Deserialize_When_ResponseContainsTotalObjectCount_Then_ReturnsMappedValue()
    {
        // Arrange
        var json = FundaResponseJsonMother.CreateDefault();

        // Act
        var result = JsonSerializer.Deserialize<FundaResponse>(json)!;

        // Assert
        Assert.Equal(5631, result.TotalObjectCount);
    }

    [Fact]
    public void Deserialize_When_ResponseContainsTotalPages_Then_ReturnsMappedValue()
    {
        // Arrange
        var json = FundaResponseJsonMother.CreateDefault();

        // Act
        var result = JsonSerializer.Deserialize<FundaResponse>(json)!;

        // Assert
        Assert.Equal(226, result.Paging.TotalPages);
    }

    [Fact]
    public void Deserialize_When_ResponseContainsCurrentPage_Then_ReturnsMappedValue()
    {
        // Arrange
        var json = FundaResponseJsonMother.CreateDefault();

        // Act
        var result = JsonSerializer.Deserialize<FundaResponse>(json)!;

        // Assert
        Assert.Equal(1, result.Paging.CurrentPage);
    }

    [Fact]
    public void Deserialize_When_ResponseContainsAccountStatus_Then_ReturnsMappedValue()
    {
        // Arrange
        var json = FundaResponseJsonMother.CreateDefault();

        // Act
        var result = JsonSerializer.Deserialize<FundaResponse>(json)!;

        // Assert
        Assert.Equal(0, result.AccountStatus);
    }

    [Fact]
    public void Deserialize_When_ResponseContainsValidationFlag_Then_ReturnsMappedValue()
    {
        // Arrange
        var json = FundaResponseJsonMother.CreateDefault();

        // Act
        var result = JsonSerializer.Deserialize<FundaResponse>(json)!;

        // Assert
        Assert.False(result.ValidationFailed);
    }

    [Fact]
    public void Deserialize_When_ResponseContainsEmailFlag_Then_ReturnsMappedValue()
    {
        // Arrange
        var json = FundaResponseJsonMother.CreateDefault();

        // Act
        var result = JsonSerializer.Deserialize<FundaResponse>(json)!;

        // Assert
        Assert.False(result.EmailNotConfirmed);
    }

    [Fact]
    public void Deserialize_When_ResponseContainsNestedChildren_Then_ReturnsMappedValue()
    {
        // Arrange
        var json = FundaResponseJsonMother.CreateDefault();

        // Act
        var result = JsonSerializer.Deserialize<FundaResponse>(json)!;

        // Assert
        Assert.Single(result.Objects);
    }

    [Fact]
    public void ToListing_When_ObjectContainsAgentIdentity_Then_ReturnsApplicationListing()
    {
        // Arrange
        var response = JsonSerializer.Deserialize<FundaResponse>(FundaResponseJsonMother.CreateDefault())!;

        // Act
        var result = response.Objects[0].ToListing();

        // Assert
        Assert.Equal(CreateDefaultListing(), result);
    }

    [Fact]
    public void Deserialize_If_ObjectsPropertyIsMissing_Then_ThrowsJsonException()
    {
        // Arrange
        var json = FundaResponseJsonMother.CreateWithMissingObjects();

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

    private static Listing CreateDefaultListing() => new("listing-1", 42, "Example Agent");
}
