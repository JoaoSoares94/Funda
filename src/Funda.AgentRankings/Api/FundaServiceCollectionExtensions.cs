using System.Net.Http.Headers;
using Funda.AgentRankings.Configuration;
using Funda.AgentRankings.Listings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Funda.AgentRankings.Api;

public static class FundaServiceCollectionExtensions
{
    public static IHttpClientBuilder AddFundaApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddFundaOptions(configuration);
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<FundaOptions>>().Value);
        // Typed clients are transient; the request budget must survive client recreation.
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<FundaRequestPolicy>();

        return services.AddHttpClient<IListingSource, FundaClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<FundaOptions>();
                client.BaseAddress = options.BaseUrl;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            // The API key is in the path, so header redaction alone cannot protect it.
            .RemoveAllLoggers();
    }
}
