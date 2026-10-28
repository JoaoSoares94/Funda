using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Funda.AgentRankings.Api.Contracts;
using Funda.AgentRankings.Configuration;
using Funda.AgentRankings.Listings;
using Polly.Timeout;

namespace Funda.AgentRankings.Api;

public sealed class FundaClient(HttpClient httpClient, FundaOptions options,
    FundaRequestPolicy requestPolicy, IProgress<ListingSearchProgress> progress) : IListingSource
{
    private const int CompletePercentage = 100;

    public async IAsyncEnumerable<Listing> GetListingsAsync(
        ListingSearch search,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = search switch
        {
            ListingSearch.Amsterdam => "/amsterdam/",
            ListingSearch.AmsterdamWithGarden => "/amsterdam/tuin/",
            _ => throw new ArgumentOutOfRangeException(nameof(search))
        };

        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(search, 0);
        var properties = new PropertyListingCollection();
        await foreach (var page in FetchPagesAsync(query, cancellationToken))
        {
            for (var index = 0; index < page.Objects.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = page.Objects[index]
                    ?? throw new FundaApiException($"The API returned a null listing on page {page.Number}, object {index + 1}.");
                try
                {
                    properties.Add(item, cancellationToken);
                }
                catch (InvalidDataException)
                {
                    throw new FundaApiException($"The API returned an invalid listing identity on page {page.Number}, object {index + 1}.");
                }
            }

            if (page.Number < page.TotalPages)
            {
                ReportProgress(search, (double)page.Number * CompletePercentage / page.TotalPages);
            }
        }

        var resolution = properties.Resolve(options.EnableAgentNameFallback, cancellationToken);
        foreach (var listing in resolution.Listings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return listing;
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new ListingSearchProgress(search, CompletePercentage, resolution.Summary));
    }

    private async IAsyncEnumerable<(int Number, int TotalPages, FundaObject[] Objects)> FetchPagesAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var totalPages = 1;
        for (var page = 1; page <= totalPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await FetchPageAsync(query, page, cancellationToken);
            ValidateResponse(response, page);

            if (page == 1)
            {
                totalPages = response.Paging.TotalPages;
            }

            yield return (page, totalPages, response.Objects);
        }
    }

    private void ReportProgress(ListingSearch search, double percentage) =>
        progress.Report(new ListingSearchProgress(search, percentage));

    private async Task<FundaResponse> FetchPageAsync(string query, int page, CancellationToken cancellationToken)
    {
        // No leading slash: preserve the configured BaseAddress path.
        var url = Uri.EscapeDataString(options.ApiKey) +
            "/?type=koop&zo=" + Uri.EscapeDataString(query) +
            "&page=" + page.ToString(CultureInfo.InvariantCulture) +
            "&pagesize=" + options.PageSize.ToString(CultureInfo.InvariantCulture);

        try
        {
            using var response = await requestPolicy.ExecuteAsync(
                async attemptToken =>
                {
                    // Each retry gets a fresh request; HttpClient messages cannot be reused.
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    return await httpClient.SendAsync(
                        request, HttpCompletionOption.ResponseContentRead, attemptToken);
                },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new FundaApiException(
                    $"The API returned HTTP {(int)response.StatusCode}.", response.StatusCode);
            }

            return await response.Content.ReadFromJsonAsync<FundaResponse>(
                JsonSerializerOptions.Default, cancellationToken)
                ?? throw new FundaApiException("The API returned a null response.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("The search was canceled.", cancellationToken);
        }
        catch (Exception exception) when (exception is TimeoutRejectedException or OperationCanceledException)
        {
            throw new FundaApiException("The API request timed out.");
        }
        catch (HttpRequestException)
        {
            throw new FundaApiException("The API request failed due to a network error.");
        }
        catch (IOException)
        {
            throw new FundaApiException("The API response could not be read.");
        }
        catch (JsonException)
        {
            // Parser and transport exceptions may contain response values or sensitive URLs.
            throw new FundaApiException("The API returned invalid JSON or missing required fields.");
        }
    }

    private static void ValidateResponse(FundaResponse response, int requestedPage)
    {
        if (response.AccountStatus != 0 || response.EmailNotConfirmed || response.ValidationFailed)
        {
            throw new FundaApiException("The API reported an account or validation failure.");
        }

        if (response.Objects is null || response.Paging is null || response.Paging.TotalPages < 0)
        {
            throw new FundaApiException("The API returned invalid result or pagination data.");
        }

        if (response.Paging.CurrentPage != requestedPage)
        {
            throw new FundaApiException($"The API returned page {response.Paging.CurrentPage} when page {requestedPage} was requested.");
        }
    }
}
