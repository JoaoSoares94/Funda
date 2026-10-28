using System.Text.Json.Serialization;

namespace Funda.AgentRankings.Api.Contracts;

public sealed record FundaObject
{
    public required string Id { get; init; }

    [JsonPropertyName("MakelaarId")]
    public int? AgentId { get; init; }

    [JsonPropertyName("MakelaarNaam")]
    public string? AgentName { get; init; }

    public FundaObject[]? ChildrenObjects { get; init; }
}
