using Codelab.OpsConsole.Api.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Codelab.OpsConsole.Api.Tests.Hosting;

public sealed class OptionsUnitTests
{
    [Theory]
    [InlineData("https://api.example.test/", false, true)]
    [InlineData("http://localhost:5180/", false, true)]
    [InlineData("http://127.0.0.1:5341", false, true)]
    [InlineData("http://api.example.test/", false, false)]
    [InlineData("http://api.example.test/", true, true)]
    [InlineData("ftp://api.example.test/", true, false)]
    [InlineData("api.example.test", true, false)]
    [InlineData("", true, false)]
    [InlineData(null, true, false)]
    public void An_upstream_address_is_https_or_local(string? value, bool isDevelopment, bool acceptable) =>
        Assert.Equal(acceptable, UpstreamAddress.IsAcceptable(value, isDevelopment));

    [Theory]
    [InlineData("https://host/portal", "https://host/portal/")]
    [InlineData(" https://host/portal/ ", "https://host/portal/")]
    public void A_base_address_always_ends_with_a_slash(string value, string expected) =>
        Assert.Equal(new Uri(expected), UpstreamAddress.AsBaseAddress(value));

    [Fact]
    public void A_complete_production_configuration_is_valid()
    {
        var result = Validate(Valid(), Environments.Production);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void Seq_is_optional()
    {
        var options = Valid();
        options.Seq.BaseUrl = null;

        Assert.True(Validate(options, Environments.Production).Succeeded);
    }

    [Theory]
    [InlineData("OperatorGroup")]
    [InlineData("Api:BaseUrl")]
    [InlineData("Api:TimeoutSeconds")]
    [InlineData("Seq:BaseUrl")]
    [InlineData("Seq:TimeoutSeconds")]
    [InlineData("Seq:MaxEvents")]
    [InlineData("Seq:LookbackDays")]
    [InlineData("DevOperator:Enabled")]
    public void Each_broken_setting_is_named(string setting)
    {
        var options = Valid();
        Break(options, setting);

        var result = Validate(options, Environments.Production);

        Assert.False(result.Succeeded);
        Assert.Contains(OpsConsoleOptions.SectionName + ":" + setting, result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dev_operator_needs_a_name_even_in_development()
    {
        var options = Valid();
        options.DevOperator.Enabled = true;

        var result = Validate(options, Environments.Development);

        Assert.False(result.Succeeded);
        Assert.Contains("DevOperator:Name", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void The_service_name_comes_from_settings_or_the_default()
    {
        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection([new(OpsConsoleHostSettings.ServiceNameKey, " fork-ops ")]).Build();
        var empty = new ConfigurationBuilder().Build();

        Assert.Equal("fork-ops", OpsConsoleHostSettings.ServiceNameFrom(configured));
        Assert.Equal(OpsConsoleHostSettings.DefaultServiceName, OpsConsoleHostSettings.ServiceNameFrom(empty));
    }

    private static OpsConsoleOptions Valid() => new()
    {
        OperatorGroup = "S-1-5-21-1-2-3-1105",
        Api = { BaseUrl = "https://portal.example.test/" },
        Seq = { BaseUrl = "http://localhost:5341/" },
    };

    private static void Break(OpsConsoleOptions options, string setting)
    {
        switch (setting)
        {
            case "OperatorGroup": options.OperatorGroup = " "; break;
            case "Api:BaseUrl": options.Api.BaseUrl = "http://portal.example.test/"; break;
            case "Api:TimeoutSeconds": options.Api.TimeoutSeconds = OpsConsoleOptionsValidator.MaxTimeoutSeconds + 1; break;
            case "Seq:BaseUrl": options.Seq.BaseUrl = "http://seq.example.test/"; break;
            case "Seq:TimeoutSeconds": options.Seq.TimeoutSeconds = 0; break;
            case "Seq:MaxEvents": options.Seq.MaxEvents = OpsConsoleOptionsValidator.MaxEventsCeiling + 1; break;
            case "Seq:LookbackDays": options.Seq.LookbackDays = 0; break;
            default: options.DevOperator.Enabled = true; options.DevOperator.Name = "DEV\\op"; break;
        }
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(OpsConsoleOptions options, string environment) =>
        new OpsConsoleOptionsValidator(new FixedEnvironment(environment)).Validate(name: null, options);

    private sealed class FixedEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
