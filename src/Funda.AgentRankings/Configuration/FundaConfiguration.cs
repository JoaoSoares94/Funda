using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Funda.AgentRankings.Configuration;

public static class FundaConfiguration
{
    public static OptionsBuilder<FundaOptions> AddFundaOptions(
        this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddOptions<FundaOptions>()
            .Configure(options =>
            {
                try
                {
                    configuration.GetSection("Funda").Bind(options);
                }
                catch (InvalidOperationException)
                {
                    // Binder errors can contain secrets; omit their values and inner exceptions.
                    throw new OptionsValidationException(Options.DefaultName, typeof(FundaOptions),
                        ["Invalid Funda configuration. Check the types of values in appsettings.json and environment overrides."]);
                }
            })
            .PostConfigure(options => options.ApiKey = configuration["FUNDA_API_KEY"] ?? options.ApiKey)
            .ValidateDataAnnotations()
            .Validate(options => options.BaseUrl is { IsAbsoluteUri: true } url &&
                url.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(url.Host) &&
                url.AbsolutePath.EndsWith('/') && url.UserInfo.Length == 0 &&
                url.Query.Length == 0 && url.Fragment.Length == 0,
                "Funda:BaseUrl must be an absolute HTTPS URL ending in '/', without credentials, query, or fragment.")
            .ValidateOnStart();
    }
}
