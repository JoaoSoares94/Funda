using System.Globalization;

namespace Funda.AgentRankings.Configuration;

public sealed class FundaOptions
{
    public Uri BaseUrl { get; }
    public string ApiKey { get; }
    public int PageSize { get; }
    public TimeSpan RequestTimeout { get; }
    public TimeSpan RequestInterval { get; }
    public int MaxRetries { get; }

    private FundaOptions(Uri baseUrl, string apiKey, int pageSize,
        TimeSpan requestTimeout, TimeSpan requestInterval, int maxRetries)
    {
        BaseUrl = baseUrl;
        ApiKey = apiKey;
        PageSize = pageSize;
        RequestTimeout = requestTimeout;
        RequestInterval = requestInterval;
        MaxRetries = maxRetries;
    }

    public static FundaOptions FromEnvironment() => Load(Environment.GetEnvironmentVariable);

    // The reader allows tests to supply configuration without changing process-wide variables.
    public static FundaOptions Load(Func<string, string?> readVariable)
    {
        ArgumentNullException.ThrowIfNull(readVariable);

        var apiKey = "AAAAAGsALUchuA87aeHp4A.ZVhStXkbaFE8nG48GmuAnKQ1iVlbDR3KsmsS85yyl8I";
        //var apiKey = readVariable("FUNDA_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(char.IsWhiteSpace) ||
            apiKey.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "Set FUNDA_API_KEY to a non-empty API key without whitespace or control characters.");
        }

        var baseUrlText = readVariable("FUNDA_BASE_URL") ??
            "https://candidate-excercise.funda.io/feeds/Aanbod.svc/json/";
        if (!Uri.TryCreate(baseUrlText, UriKind.Absolute, out var baseUrl) ||
            baseUrl.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(baseUrl.Host) ||
            !string.IsNullOrEmpty(baseUrl.UserInfo) || !string.IsNullOrEmpty(baseUrl.Query) ||
            !string.IsNullOrEmpty(baseUrl.Fragment) ||
            !baseUrl.AbsolutePath.EndsWith('/'))
        {
            throw new InvalidOperationException(
                "FUNDA_BASE_URL must be an absolute HTTPS URL ending in '/', without credentials, query, or fragment. Do not include the API key.");
        }

        return new FundaOptions(baseUrl, apiKey,
            ReadInteger(readVariable, "FUNDA_PAGE_SIZE", 25, 1, 25),
            TimeSpan.FromSeconds(ReadInteger(readVariable, "FUNDA_TIMEOUT_SECONDS", 30, 1, 300)),
            TimeSpan.FromMilliseconds(ReadInteger(readVariable, "FUNDA_REQUEST_INTERVAL_MS", 1000, 1000, 60000)),
            ReadInteger(readVariable, "FUNDA_MAX_RETRIES", 3, 0, 5));
    }

    private static int ReadInteger(Func<string, string?> readVariable,
        string name, int defaultValue, int minimum, int maximum)
    {
        var text = readVariable(name);
        if (text is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value < minimum || value > maximum)
        {
            // Never echo configuration values: a user could accidentally paste a key here.
            throw new InvalidOperationException($"{name} must be an integer from {minimum} to {maximum}.");
        }

        return value;
    }

    public override string ToString() => "FundaOptions (credentials omitted)";
}
