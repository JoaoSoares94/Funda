using Funda.AgentRankings.Configuration;
using Funda.AgentRankings.Listings;
using Microsoft.Extensions.DependencyInjection;

namespace Funda.AgentRankings.Api;

public static class FundaServiceCollectionExtensions
{
    public static IHttpClientBuilder AddFundaApi(this IServiceCollection services, FundaOptions options)
    {
        services.AddSingleton(options);
        // Typed clients are transient; the request budget must survive client recreation.
        services.AddSingleton(_ => new FundaRequestPolicy(
            options, SystemRequestTiming.Instance, Random.Shared.NextDouble));

        return services.AddHttpClient<IListingSource, FundaClient>(client =>
            {
                client.BaseAddress = options.BaseUrl;
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            // The API key is in the path, so header redaction alone cannot protect it.
            .RemoveAllLoggers();
    }
}
