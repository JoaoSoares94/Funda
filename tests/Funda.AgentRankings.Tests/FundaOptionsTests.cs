using Funda.AgentRankings.Configuration;
using Funda.AgentRankings.Tests.ObjectMothers;

namespace Funda.AgentRankings.Tests;

public sealed class FundaOptionsTests
{

    [Fact]
    public void Load_When_OptionalSettingsAreAbsent_Then_ReturnsDefaultApiKey()
    {
        // Arrange
        var readVariable = FundaEnvironmentMother.CreateDefault();

        // Act
        var result = FundaOptions.Load(readVariable);

        // Assert
        Assert.Equal(FundaEnvironmentMother.TestKey, result.ApiKey);
    }

    [Fact]
    public void Load_When_OptionalSettingsAreAbsent_Then_ReturnsDefaultBaseUrl()
    {
        // Arrange
        var readVariable = FundaEnvironmentMother.CreateDefault();

        // Act
        var result = FundaOptions.Load(readVariable);

        // Assert
        Assert.Equal("https://candidate-excercise.funda.io/feeds/Aanbod.svc/json/", result.BaseUrl.AbsoluteUri);
    }

    [Fact]
    public void Load_When_OptionalSettingsAreAbsent_Then_ReturnsDefaultPageSize()
    {
        // Arrange
        var readVariable = FundaEnvironmentMother.CreateDefault();

        // Act
        var result = FundaOptions.Load(readVariable);

        // Assert
        Assert.Equal(25, result.PageSize);
    }

    [Fact]
    public void Load_When_OptionalSettingsAreAbsent_Then_ReturnsDefaultRequestTimeout()
    {
        // Arrange
        var readVariable = FundaEnvironmentMother.CreateDefault();

        // Act
        var result = FundaOptions.Load(readVariable);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(30), result.RequestTimeout);
    }

    [Fact]
    public void Load_When_OptionalSettingsAreAbsent_Then_ReturnsDefaultRequestInterval()
    {
        // Arrange
        var readVariable = FundaEnvironmentMother.CreateDefault();

        // Act
        var result = FundaOptions.Load(readVariable);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1), result.RequestInterval);
    }

    [Fact]
    public void Load_When_OptionalSettingsAreAbsent_Then_ReturnsDefaultMaxRetries()
    {
        // Arrange
        var readVariable = FundaEnvironmentMother.CreateDefault();

        // Act
        var result = FundaOptions.Load(readVariable);

        // Assert
        Assert.Equal(3, result.MaxRetries);
    }

    [Fact]
    public void Load_When_BaseUrlIsOverridden_Then_ReturnsConfiguredBaseUrl()
    {
        // Arrange
        var readVariable = FundaEnvironmentMother.CreateWithOverride("FUNDA_BASE_URL", "https://example.com/api/");

        // Act
        var result = FundaOptions.Load(readVariable);

        // Assert
        Assert.Equal("https://example.com/api/", result.BaseUrl.AbsoluteUri);
    }

    [Fact]
    public void Load_When_PageSizeIsOverridden_Then_ReturnsConfiguredPageSize()
    {
        // Arrange
        var readVariable = FundaEnvironmentMother.CreateWithOverride("FUNDA_PAGE_SIZE", "10");

        // Act
        var result = FundaOptions.Load(readVariable);

        // Assert
        Assert.Equal(10, result.PageSize);
    }

    [Fact]
    public void Load_When_RequestTimeoutIsOverridden_Then_ReturnsConfiguredRequestTimeout()
    {
        // Arrange
        var readVariable = FundaEnvironmentMother.CreateWithOverride("FUNDA_TIMEOUT_SECONDS", "60");

        // Act
        var result = FundaOptions.Load(readVariable);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(60), result.RequestTimeout);
    }

    [Fact]
    public void Load_When_RequestIntervalIsOverridden_Then_ReturnsConfiguredRequestInterval()
    {
        // Arrange
        var readVariable = FundaEnvironmentMother.CreateWithOverride("FUNDA_REQUEST_INTERVAL_MS", "2000");

        // Act
        var result = FundaOptions.Load(readVariable);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(2), result.RequestInterval);
    }

    [Fact]
    public void Load_When_MaxRetriesIsOverridden_Then_ReturnsConfiguredMaxRetries()
    {
        // Arrange
        var readVariable = FundaEnvironmentMother.CreateWithOverride("FUNDA_MAX_RETRIES", "0");

        // Act
        var result = FundaOptions.Load(readVariable);

        // Assert
        Assert.Equal(0, result.MaxRetries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("key with spaces")]
    [InlineData("key\n")]
    public void Load_If_KeyIsMissingOrMalformed_Then_ThrowsInvalidOperationException(string? value)
    {
        // Arrange
        var readVariable = FundaEnvironmentMother.CreateWithOverride("FUNDA_API_KEY", value);

        // Act
        var act = () => FundaOptions.Load(readVariable);

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }

    [Theory]
    [InlineData("http://example.com/api/")]
    [InlineData("/relative/")]
    [InlineData("https://example.com/api")]
    [InlineData("https://user:password@example.com/api/")]
    [InlineData("https://example.com/api/?key=secret")]
    [InlineData("https://example.com/api/#secret")]
    [InlineData("")]
    public void Load_If_BaseUrlIsInvalid_Then_ThrowsInvalidOperationException(string value)
    {
        // Arrange
        var readVariable = FundaEnvironmentMother.CreateWithOverride("FUNDA_BASE_URL", value);

        // Act
        var act = () => FundaOptions.Load(readVariable);

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }

    [Theory]
    [InlineData("FUNDA_PAGE_SIZE", "0")]
    [InlineData("FUNDA_PAGE_SIZE", "26")]
    [InlineData("FUNDA_TIMEOUT_SECONDS", "0")]
    [InlineData("FUNDA_TIMEOUT_SECONDS", "301")]
    [InlineData("FUNDA_REQUEST_INTERVAL_MS", "999")]
    [InlineData("FUNDA_REQUEST_INTERVAL_MS", "60001")]
    [InlineData("FUNDA_MAX_RETRIES", "-1")]
    [InlineData("FUNDA_MAX_RETRIES", "6")]
    [InlineData("FUNDA_MAX_RETRIES", "test-key-never-a-real-secret")]
    [InlineData("FUNDA_MAX_RETRIES", "")]
    [InlineData("FUNDA_MAX_RETRIES", "999999999999999999999")]
    public void Load_If_NumericSettingIsInvalid_Then_ThrowsInvalidOperationException(string name, string value)
    {
        // Arrange
        var readVariable = FundaEnvironmentMother.CreateWithOverride(name, value);

        // Act
        var act = () => FundaOptions.Load(readVariable);

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }

    [Theory]
    [InlineData("FUNDA_API_KEY", "private key", "Set FUNDA_API_KEY to a non-empty API key without whitespace or control characters.")]
    [InlineData("FUNDA_BASE_URL", "https://user:password@example.com/api/?key=secret", "FUNDA_BASE_URL must be an absolute HTTPS URL ending in '/', without credentials, query, or fragment. Do not include the API key.")]
    [InlineData("FUNDA_MAX_RETRIES", FundaEnvironmentMother.TestKey, "FUNDA_MAX_RETRIES must be an integer from 0 to 5.")]
    public void Load_If_SettingContainsSensitiveInvalidValue_Then_ReturnsActionableErrorWithoutValue(
        string name, string value, string expectedMessage)
    {
        // Arrange
        var readVariable = FundaEnvironmentMother.CreateWithOverride(name, value);

        // Act
        var result = Record.Exception(() => FundaOptions.Load(readVariable));

        // Assert
        Assert.Equal(expectedMessage, result?.Message);
    }

    [Fact]
    public void ToString_When_OptionsContainApiKey_Then_OmitsApiKey()
    {
        // Arrange
        var options = FundaOptions.Load(FundaEnvironmentMother.CreateDefault());

        // Act
        var result = options.ToString();

        // Assert
        Assert.DoesNotContain(FundaEnvironmentMother.TestKey, result);
    }
}
