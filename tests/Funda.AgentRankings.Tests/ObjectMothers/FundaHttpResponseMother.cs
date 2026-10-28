using System.Net;
using System.Text;
using System.Text.Json;
using Funda.AgentRankings.Api.Contracts;

namespace Funda.AgentRankings.Tests.ObjectMothers;

internal static class FundaHttpResponseMother
{
    public static HttpResponseMessage CreateDefault() => CreateWithPage(1, 1);

    public static HttpResponseMessage CreateWithPage(int currentPage, int totalPages) =>
        CreateWithProperties(currentPage, totalPages, new FundaObject
        {
            Id = $"listing-{currentPage}", AgentId = 42, AgentName = "Example Agent", ChildrenObjects = []
        });

    public static HttpResponseMessage CreateWithProperties(
        int currentPage, int totalPages, params FundaObject[] properties) =>
        CreateWithJson(JsonSerializer.Serialize(new FundaResponse
        {
            AccountStatus = 0, EmailNotConfirmed = false, ValidationFailed = false,
            Objects = properties,
            Paging = new FundaPaging { CurrentPage = currentPage, TotalPages = totalPages }
        }));

    public static HttpResponseMessage CreateWithJson(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage CreateWithStatus(int status) =>
        new((HttpStatusCode)status) { Content = new StringContent(FundaConfigurationMother.TestKey) };

    public static HttpRequestException CreateWithNetworkFailure() =>
        new($"Network failure for https://example.com/{FundaConfigurationMother.TestKey}/");

    public static TaskCanceledException CreateWithTimeout() =>
        new($"Request timed out for {FundaConfigurationMother.TestKey}");

    public static CancellationTokenSource CreateWithCanceledToken()
    {
        var source = new CancellationTokenSource();
        source.Cancel();
        return source;
    }
}
