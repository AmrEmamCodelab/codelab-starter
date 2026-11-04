using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Codelab.Observability.Tests;

/// <summary>Deployment settings: defaults, binding and fail-fast validation naming the key.</summary>
public sealed class ObservabilitySettingsTests
{
    [Fact]
    public void Defaults_apply_when_the_section_is_missing()
    {
        var settings = ObservabilitySettings.From(new ConfigurationBuilder().Build());

        Assert.True(settings.LogFile.Enabled);
        Assert.Equal(LogFileSettings.DefaultRetainedFileCountLimit, settings.LogFile.RetainedFileCountLimit);
        Assert.Equal(LogFileSettings.DefaultFileSizeLimitBytes, settings.LogFile.FileSizeLimitBytes);
        Assert.EndsWith(LogFileSettings.DefaultDirectoryName, settings.LogFile.ResolvedDirectory, StringComparison.Ordinal);
        Assert.False(settings.Seq.Enabled);
        Assert.False(settings.Tracing.Enabled);
        settings.Validate(isDevelopment: false);
    }

    [Fact]
    public void Values_bind_from_the_observability_section()
    {
        var settings = ObservabilitySettings.From(Configuration(new()
        {
            ["Observability:LogFile:Directory"] = "D:/logs",
            ["Observability:Seq:ServerUrl"] = "https://seq.example.test",
            ["Observability:Seq:QueueSizeLimit"] = "500",
            ["Observability:Tracing:Enabled"] = "true",
            ["Observability:Tracing:OtlpEndpoint"] = "https://otel.example.test/v1/traces",
            ["Observability:DeploymentEnvironment"] = "staging",
        }));

        Assert.Equal("D:/logs", settings.LogFile.ResolvedDirectory);
        Assert.True(settings.Seq.Enabled);
        Assert.Equal(500, settings.Seq.QueueSizeLimit);
        Assert.True(settings.Tracing.Enabled);
        Assert.Equal("staging", settings.DeploymentEnvironment);
        settings.Validate(isDevelopment: false);
    }

    [Theory]
    [InlineData("http://seq.example.test", false, "Observability:Seq:ServerUrl")]
    [InlineData("not a url", true, "Observability:Seq:ServerUrl")]
    public void An_unsafe_seq_address_is_refused(string url, bool isDevelopment, string key)
    {
        var settings = new ObservabilitySettings { Seq = new SeqSettings { ServerUrl = url } };

        var error = Assert.Throws<InvalidOperationException>(() => settings.Validate(isDevelopment));

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plain_http_seq_is_allowed_in_Development_only()
    {
        new ObservabilitySettings { Seq = new SeqSettings { ServerUrl = "http://localhost:5341" } }.Validate(isDevelopment: true);
    }

    [Fact]
    public void Non_positive_limits_are_refused()
    {
        Assert.Throws<InvalidOperationException>(
            () => new ObservabilitySettings { Seq = new SeqSettings { QueueSizeLimit = 0 } }.Validate(isDevelopment: false));
        Assert.Throws<InvalidOperationException>(
            () => new ObservabilitySettings { LogFile = new LogFileSettings { RetainedFileCountLimit = 0 } }.Validate(isDevelopment: false));
    }

    [Fact]
    public void Tracing_on_without_an_endpoint_is_refused()
    {
        var settings = new ObservabilitySettings { Tracing = new TracingSettings { Enabled = true } };

        var error = Assert.Throws<InvalidOperationException>(() => settings.Validate(isDevelopment: false));

        Assert.Contains("Observability:Tracing:OtlpEndpoint", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "Production", "production")]
    [InlineData("", "Development", "development")]
    [InlineData("preproduction", "Production", "preproduction")]
    public void Deployment_environment_defaults_to_the_lowercase_environment(string? configured, string environment, string expected)
    {
        Assert.Equal(expected, DeploymentEnvironments.Resolve(configured, UserIdHashTests.Env(environment)));
    }

    [Fact]
    public void An_unknown_deployment_environment_is_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => DeploymentEnvironments.Resolve("prod", UserIdHashTests.Env(Environments.Production)));

        Assert.Contains(ObservabilitySettings.DeploymentEnvironmentKey, error.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
