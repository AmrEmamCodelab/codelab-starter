using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Starter.Api.Tests.Support;
using Starter.Infrastructure.Features;
using Starter.Infrastructure.Persistence;

namespace Starter.Api.Tests;

public sealed class FeatureSwitchValidatorTests
{
    [Fact]
    public void Declared_and_configured_flags_pass()
    {
        var switches = new FeatureSwitchDictionary { [Probe.Flag] = true };

        var result = new FeatureSwitchValidator([new ProbeFlags()]).Validate(name: null, switches);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void A_configured_switch_that_is_not_declared_fails()
    {
        var switches = new FeatureSwitchDictionary { ["unknownFlag"] = true };

        var result = new FeatureSwitchValidator([new ProbeFlags()]).Validate(name: null, switches);

        Assert.True(result.Failed);
        Assert.Contains("Features:unknownFlag", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NotCamel")]
    [InlineData("with-dash")]
    [InlineData("")]
    public void A_declared_flag_that_is_not_camel_case_fails(string flag)
    {
        var result = new FeatureSwitchValidator([new NamedFlags(flag)]).Validate(name: null, new FeatureSwitchDictionary());

        Assert.True(result.Failed);
    }

    [Fact]
    public void A_flag_declared_twice_fails()
    {
        var result = new FeatureSwitchValidator([new ProbeFlags(), new ProbeFlags()]).Validate(name: null, new FeatureSwitchDictionary());

        Assert.True(result.Failed);
        Assert.Contains("more than once", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Switch_names_are_case_insensitive()
    {
        var switches = new FeatureSwitchDictionary { ["PROBEFEATURE"] = true };

        Assert.True(switches.ContainsKey(Probe.Flag));
    }

    private sealed class NamedFlags(string name) : IFeatureFlagSource
    {
        public IReadOnlyCollection<FeatureFlag> Flags { get; } = [new FeatureFlag(name, DefaultValue: false, Description: "Test.")];
    }
}

public sealed class FeatureGateTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void The_effective_value_is_the_switch_or_the_default(bool? configured, bool expected)
    {
        var switches = new FeatureSwitchDictionary();
        if (configured is { } value)
        {
            switches[Probe.Flag] = value;
        }

        var gate = new FeatureGate([new ProbeFlags()], Options.Create(switches));

        Assert.Equal(expected, gate.IsEnabled(Probe.Flag));
        Assert.Equal(expected, gate.Flags[Probe.Flag]);
    }

    [Fact]
    public void An_undeclared_flag_is_a_programming_fault()
    {
        var gate = new FeatureGate([], Options.Create(new FeatureSwitchDictionary()));

        Assert.Throws<InvalidOperationException>(() => gate.IsEnabled("missing"));
    }
}

public sealed class PersistenceOptionsValidatorTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(" ", false)]
    [InlineData("Server=.;Database=x", true)]
    public void The_application_connection_string_is_required(string? connection, bool valid)
    {
        var result = new ConnectionStringsOptionsValidator().Validate(name: null, new ConnectionStringsOptions { Starter = connection });

        Assert.Equal(valid, result.Succeeded);
    }

    [Theory]
    [InlineData("Development", true, true)]
    [InlineData("Production", true, false)]
    [InlineData("Staging", true, false)]
    [InlineData("Production", false, true)]
    public void Migrations_on_start_are_allowed_in_Development_only(string environment, bool apply, bool valid)
    {
        var validator = new DatabaseOptionsValidator(new Environment(environment));

        var result = validator.Validate(name: null, new DatabaseOptions { ApplyMigrationsOnStartup = apply });

        Assert.Equal(valid, result.Succeeded);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
