using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Funda.AgentRankings.Api.Contracts;
using Funda.AgentRankings.Configuration;
using Funda.AgentRankings.Listings;

namespace Funda.AgentRankings.Api;

public sealed class FundaClient(HttpClient httpClient, FundaOptions options,
    FundaRequestPolicy requestPolicy) : IListingSource
{
    private readonly FundaRequestPolicy _requestPolicy = requestPolicy;

    public async IAsyncEnumerable<Listing> GetListingsAsync(
        ListingSearch search,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = search switch
        {
            ListingSearch.Amsterdam => "/amsterdam/",
            ListingSearch.AmsterdamWithGarden => "/amsterdam/tuin/",
            _ => throw new ArgumentOutOfRangeException(nameof(search))
        };

        int? totalPages = null;
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        for (var page = 1; ; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await FetchPageAsync(query, page, cancellationToken);
            ValidateResponse(response, page);

            if (totalPages is not null && totalPages != response.Paging.TotalPages)
            {
                throw new FundaApiException("The number of pages changed during the search. Run the search again.");
            }
            totalPages ??= response.Paging.TotalPages;

            if (response.Objects.Length == 0)
            {
                if (page == 1 && response.TotalObjectCount == 0 && totalPages <= 1)
                {
                    yield break;
                }
                throw new FundaApiException("The API returned an unexpected empty page.");
            }

            // Detect repeated content even when the server increments its page metadata.
            if (!response.Objects.Any(item => !seenIds.Contains(item.Id)))
            {
                throw new FundaApiException("The API repeated a page without any new listings.");
            }

            foreach (var item in response.Objects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                seenIds.Add(item.Id);
                yield return item.ToListing();
            }

            if (page == totalPages)
            {
                yield break;
            }
        }
    }

    private async Task<FundaResponse> FetchPageAsync(string query, int page, CancellationToken cancellationToken)
    {
        // Append the escaped key as a path segment, not a relative URI that can replace the base path.
            var url = options.BaseUrl.AbsoluteUri + Uri.EscapeDataString(options.ApiKey) +
            "/?type=koop&zo=" + Uri.EscapeDataString(query) +
            "&page=" + page.ToString(CultureInfo.InvariantCulture) +
            "&pagesize=" + options.PageSize.ToString(CultureInfo.InvariantCulture);

        try
        {
            using var response = await _requestPolicy.ExecuteAsync(async attemptToken =>
            {
                // Each retry gets a fresh request; HttpClient messages cannot be reused.
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return await httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseContentRead, attemptToken);
            }, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new FundaApiException(
                    $"The API returned HTTP {(int)response.StatusCode}.", response.StatusCode);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<FundaResponse>(json)
                ?? throw new FundaApiException("The API returned a null response.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("The search was canceled.", cancellationToken);
        }
        catch (OperationCanceledException)
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
            // Zero is the only successful account status verified in the supplied sample.
            throw new FundaApiException("The API reported an account or validation failure.");
        }

        if (response.Objects is null || response.Paging is null ||
            response.TotalObjectCount < 0 || response.Paging.TotalPages < 0 ||
            response.Paging.CurrentPage != requestedPage ||
            (response.Paging.TotalPages == 0 && response.TotalObjectCount != 0) ||
            (response.Paging.TotalPages > 0 && requestedPage > response.Paging.TotalPages) ||
            (response.Objects.Length > 0 && (response.TotalObjectCount == 0 || response.Paging.TotalPages == 0)))
        {
            throw new FundaApiException("The API returned invalid result or pagination data.");
        }

        for (var index = 0; index < response.Objects.Length; index++)
        {
            try
            {
                var item = response.Objects[index]
                    ?? throw new InvalidDataException("Null listing.");
                _ = item.ToListing();
            }
            catch (InvalidDataException)
            {
                throw new FundaApiException(
                    $"The API returned an invalid or ambiguous listing identity on page {requestedPage}, object {index + 1}.");
            }
        }
    }
}
