using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Funda.AgentRankings.Tests.ObjectMothers;

internal static class FundaHttpResponseMother
{
    public static HttpResponseMessage CreateDefault() => CreateWithPage(1, 1);

    public static HttpResponseMessage CreateWithPage(int currentPage, int totalPages)
    {
        var json = JsonNode.Parse(FundaResponseJsonMother.CreateDefault())!;
        json["Paging"]!["HuidigePagina"] = currentPage;
        json["Paging"]!["AantalPaginas"] = totalPages;
        json["Objects"]![0]!["Id"] = $"listing-{currentPage}";
        return CreateWithJson(json.ToJsonString());
    }

    public static HttpResponseMessage CreateWithInvalidField(string path, string value)
    {
        var json = JsonNode.Parse(FundaResponseJsonMother.CreateDefault())!;
        json["Paging"]!["AantalPaginas"] = 1;
        var segments = path.Split('.');
        var parent = json;
        foreach (var segment in segments[..^1])
        {
            parent = int.TryParse(segment, out var index) ? parent![index] : parent![segment];
        }
        if (int.TryParse(segments[^1], out var itemIndex))
        {
            parent![itemIndex] = JsonNode.Parse(value);
        }
        else
        {
            parent![segments[^1]] = JsonNode.Parse(value);
        }
        return CreateWithJson(json.ToJsonString());
    }

    public static HttpResponseMessage CreateWithRepeatedListing(int page)
    {
        var response = JsonNode.Parse(FundaResponseJsonMother.CreateDefault())!;
        response["Paging"]!["HuidigePagina"] = page;
        response["Paging"]!["AantalPaginas"] = 2;
        return CreateWithJson(response.ToJsonString());
    }

    public static HttpResponseMessage CreateWithGroupedPage(int page, bool explicitNull)
    {
        var json = JsonNode.Parse(FundaResponseJsonMother.CreateDefault())!;
        json["Paging"]!["HuidigePagina"] = page;
        json["Paging"]!["AantalPaginas"] = 2;
        json["Objects"]![0]!["Id"] = $"listing-{page}";
        var listing = json["Objects"]![0]!.AsObject();
        if (explicitNull)
        {
            listing["MakelaarId"] = null;
        }
        else
        {
            listing.Remove("MakelaarId");
        }
        return CreateWithJson(json.ToJsonString());
    }

    public static HttpResponseMessage CreateWithUnresolvableAgent(string children)
    {
        var json = JsonNode.Parse(FundaResponseJsonMother.CreateDefault())!;
        json["Paging"]!["AantalPaginas"] = 1;
        json["Objects"]![0]!.AsObject().Remove("MakelaarId");
        json["Objects"]![0]!["ChildrenObjects"] = JsonNode.Parse(children);
        return CreateWithJson(json.ToJsonString());
    }
    public static HttpResponseMessage CreateWithEmptyResults() =>
        CreateWithJson(FundaResponseJsonMother.CreateWithEmptyResults());

    public static HttpResponseMessage CreateWithJson(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage CreateWithStatus(int status) =>
        new((HttpStatusCode)status) { Content = new StringContent(FundaEnvironmentMother.TestKey) };

    public static HttpRequestException CreateWithNetworkFailure() =>
        new($"Network failure for https://example.com/{FundaEnvironmentMother.TestKey}/");

    public static TaskCanceledException CreateWithTimeout() =>
        new($"Request timed out for {FundaEnvironmentMother.TestKey}");

    public static CancellationTokenSource CreateWithCanceledToken()
    {
        var source = new CancellationTokenSource();
        source.Cancel();
        return source;
    }
}
