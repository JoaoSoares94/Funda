using System.Text.Json.Serialization;

namespace Funda.AgentRankings.Api.Contracts;

public sealed record FundaAgentReference
{
    [JsonPropertyName("MakelaarId")]
    public int? AgentId { get; init; }

    [JsonPropertyName("MakelaarNaam")]
    public string? AgentName { get; init; }
}
