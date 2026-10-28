using System.Net;

namespace Funda.AgentRankings.Api;

/// <summary>A safe API failure that never contains a request URL, response body, or key.</summary>
public sealed class FundaApiException(string message, HttpStatusCode? statusCode = null)
    : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
