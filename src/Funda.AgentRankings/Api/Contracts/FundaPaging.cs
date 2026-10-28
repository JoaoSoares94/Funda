using System.Text.Json.Serialization;

namespace Funda.AgentRankings.Api.Contracts;

public sealed record FundaPaging
{
    [JsonPropertyName("AantalPaginas")]
    public required int TotalPages { get; init; }

    [JsonPropertyName("HuidigePagina")]
    public required int CurrentPage { get; init; }
}
