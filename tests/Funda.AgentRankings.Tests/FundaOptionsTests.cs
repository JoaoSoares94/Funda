using Funda.AgentRankings.Configuration;
using Funda.AgentRankings.Tests.ObjectMothers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Funda.AgentRankings.Tests;

public sealed class FundaOptionsTests
{
    [Fact]
    public void Validate_When_RequestSettingsAreAbsent_Then_PreservesDefaults()
    {
        // Arrange
        var configuration = FundaConfigurationMother.CreateDefault();
        using var provider = CreateDefaultProvider(configuration);

        // Act
        provider.GetRequiredService<IStartupValidator>().Validate();
        var result = provider.GetRequiredService<IOptions<FundaOptions>>().Value;

        // Assert
        Assert.Equal(25, result.PageSize);
        Assert.Equal(TimeSpan.FromSeconds(30), result.RequestTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(650), result.RequestInterval);
        Assert.Equal(3, result.MaxRetries);
    }

    [Fact]
    public void Validate_When_EndpointIsAbsent_Then_UsesDefaultEndpointAndSuppliedKey()
    {
        // Arrange
        var configuration = FundaConfigurationMother.CreateDefault();
        using var provider = CreateDefaultProvider(configuration);

        // Act
        provider.GetRequiredService<IStartupValidator>().Validate();
        var result = provider.GetRequiredService<IOptions<FundaOptions>>().Value;

        // Assert
        Assert.Equal("https://candidate-excercise.funda.io/feeds/Aanbod.svc/json/", result.BaseUrl.AbsoluteUri);
        Assert.Equal(FundaConfigurationMother.TestKey, result.ApiKey);
    }

    [Fact]
    public void Validate_When_RequestSettingsAreConfigured_Then_PreservesConfiguredValues()
    {
        // Arrange
        var configuration = CreateWithRequestOverrides();
        using var provider = CreateDefaultProvider(configuration);

        // Act
        provider.GetRequiredService<IStartupValidator>().Validate();
        var result = provider.GetRequiredService<IOptions<FundaOptions>>().Value;

        // Assert
        Assert.Equal(10, result.PageSize);
        Assert.Equal(TimeSpan.FromSeconds(60), result.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), result.RequestInterval);
        Assert.Equal(0, result.MaxRetries);
    }

    [Fact]
    public void Validate_When_BaseUrlIsConfigured_Then_PreservesConfiguredValue()
    {
        // Arrange
        var configuration = FundaConfigurationMother.CreateWithOverride("Funda:BaseUrl", "https://example.com/api/");
        using var provider = CreateDefaultProvider(configuration);

        // Act
        provider.GetRequiredService<IStartupValidator>().Validate();
        var result = provider.GetRequiredService<IOptions<FundaOptions>>().Value;

        // Assert
        Assert.Equal("https://example.com/api/", result.BaseUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("FUNDA_API_KEY", null)]
    [InlineData("FUNDA_API_KEY", "")]
    [InlineData("FUNDA_API_KEY", " ")]
    [InlineData("FUNDA_API_KEY", "key with spaces")]
    [InlineData("FUNDA_API_KEY", "key\n")]
    [InlineData("Funda:BaseUrl", "http://example.com/api/")]
    [InlineData("Funda:BaseUrl", "/relative/")]
    [InlineData("Funda:BaseUrl", "https://example.com/api")]
    [InlineData("Funda:BaseUrl", "https://user:password@example.com/api/")]
    [InlineData("Funda:BaseUrl", "https://example.com/api/?key=secret")]
    [InlineData("Funda:BaseUrl", "https://example.com/api/#secret")]
    [InlineData("Funda:BaseUrl", "")]
    [InlineData("Funda:PageSize", "0")]
    [InlineData("Funda:PageSize", "26")]
    [InlineData("Funda:TimeoutSeconds", "0")]
    [InlineData("Funda:TimeoutSeconds", "301")]
    [InlineData("Funda:RequestIntervalMs", "649")]
    [InlineData("Funda:RequestIntervalMs", "60001")]
    [InlineData("Funda:MaxRetries", "-1")]
    [InlineData("Funda:MaxRetries", "6")]
    [InlineData("Funda:MaxRetries", "test-key-never-a-real-secret")]
    [InlineData("Funda:MaxRetries", "")]
    [InlineData("Funda:MaxRetries", "999999999999999999999")]
    public void Validate_If_SettingIsInvalid_Then_ThrowsOptionsValidationException(string name, string? value)
    {
        // Arrange
        var configuration = FundaConfigurationMother.CreateWithOverride(name, value);
        using var provider = CreateDefaultProvider(configuration);

        // Act
        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        // Assert
        Assert.Throws<OptionsValidationException>(act);
    }

    [Theory]
    [InlineData("FUNDA_API_KEY", "private key")]
    [InlineData("Funda:BaseUrl", "https://user:password@example.com/api/?key=secret")]
    [InlineData("Funda:MaxRetries", FundaConfigurationMother.TestKey)]
    public void Validate_If_InvalidSettingContainsSensitiveValue_Then_OmitsValueFromError(string name, string value)
    {
        // Arrange
        var configuration = FundaConfigurationMother.CreateWithOverride(name, value);
        using var provider = CreateDefaultProvider(configuration);

        // Act
        var result = Record.Exception(() => provider.GetRequiredService<IStartupValidator>().Validate());

        // Assert
        Assert.DoesNotContain(value, Assert.IsType<OptionsValidationException>(result).ToString());
    }

    [Fact]
    public void ToString_When_OptionsContainApiKey_Then_OmitsApiKey()
    {
        // Arrange
        var options = FundaConfigurationMother.CreateDefaultOptions();

        // Act
        var result = options.ToString();

        // Assert
        Assert.DoesNotContain(FundaConfigurationMother.TestKey, result);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("true", true)]
    public void Validate_When_NameFallbackFlagIsConfigured_Then_PreservesExpectedValue(string? configuredValue, bool expected)
    {
        // Arrange
        var configuration = FundaConfigurationMother.CreateWithOverride("Funda:EnableAgentNameFallback", configuredValue);
        using var provider = CreateDefaultProvider(configuration);

        // Act
        provider.GetRequiredService<IStartupValidator>().Validate();
        var result = provider.GetRequiredService<IOptions<FundaOptions>>().Value;

        // Assert
        Assert.Equal(expected, result.EnableAgentNameFallback);
    }

    [Theory]
    [InlineData(null, "section-key")]
    [InlineData("legacy-key", "legacy-key")]
    public void Validate_When_KeySourcesAreConfigured_Then_PrefersLegacyKeyOtherwiseSectionKey(
        string? legacyKey, string expected)
    {
        // Arrange
        var configuration = CreateWithKeySources(legacyKey);
        using var provider = CreateDefaultProvider(configuration);

        // Act
        provider.GetRequiredService<IStartupValidator>().Validate();
        var result = provider.GetRequiredService<IOptions<FundaOptions>>().Value;

        // Assert
        Assert.Equal(expected, result.ApiKey);
    }

    private static ServiceProvider CreateDefaultProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddFundaOptions(configuration);
        return services.BuildServiceProvider();
    }

    private static IConfigurationRoot CreateWithKeySources(string? legacyKey) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Funda:ApiKey"] = "section-key",
            ["FUNDA_API_KEY"] = legacyKey
        }).Build();

    private static IConfigurationRoot CreateWithRequestOverrides() =>
        new ConfigurationBuilder()
            .AddConfiguration(FundaConfigurationMother.CreateDefault())
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Funda:PageSize"] = "10",
                ["Funda:TimeoutSeconds"] = "60",
                ["Funda:RequestIntervalMs"] = "2000",
                ["Funda:MaxRetries"] = "0"
            }).Build();

}
