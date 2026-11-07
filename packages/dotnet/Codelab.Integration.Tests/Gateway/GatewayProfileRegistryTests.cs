using Codelab.Integration.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Codelab.Integration.Tests.Gateway;

/// <summary>The profile registry: enabled and disabled profiles, and the start-up failures.</summary>
public sealed class GatewayProfileRegistryTests
{
    [Fact]
    public void Registered_profiles_split_into_enabled_and_disabled()
    {
        var registry = Registry([new KeyedProfile(GatewayProfileKey.Sso), new KeyedProfile(GatewayProfileKey.Sms)], GatewayProfileKey.Sms);

        Assert.Equal([GatewayProfileKey.Sms], registry.Enabled.Select(profile => profile.Key));
        Assert.Equal([GatewayProfileKey.Sso], registry.Disabled.Select(profile => profile.Key));
        Assert.True(registry.IsEnabled(GatewayProfileKey.Sms));
        Assert.False(registry.IsEnabled(GatewayProfileKey.Sso));
        Assert.Equal(GatewayProfileKey.Sso, registry.Find(GatewayProfileKey.Sso)!.Key);
        Assert.Null(registry.Find("other"));
    }

    [Fact]
    public void Two_profiles_with_one_key_fail_fast()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Registry([new KeyedProfile(GatewayProfileKey.Sms), new OtherSmsProfile()]));

        Assert.Contains("'sms' is registered by more than one profile", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_key_and_an_enabled_key_without_a_profile_are_reported_together()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Registry([new KeyedProfile("Bad Key")], "national-registry"));

        Assert.Contains("malformed key 'Bad Key'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("enables 'national-registry'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registering_the_same_profile_type_twice_registers_it_once()
    {
        var services = new ServiceCollection().AddGatewayProfile<SmsProfile>().AddGatewayProfile<SmsProfile>();
        services.AddCodelabGatewayProfiles(Configuration(GatewayProfileKey.Sms));
        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetRequiredService<GatewayProfileRegistry>().Registered);
    }

    [Fact]
    public void A_duplicate_key_fails_when_the_profiles_are_mapped()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.AddInMemoryCollection(Settings(GatewayProfileKey.Sms));
        builder.Services.AddCodelabGatewayProfiles(builder.Configuration).AddGatewayProfile<SmsProfile>().AddGatewayProfile<OtherSmsProfile>();
        using var app = builder.Build();

        Assert.Throws<InvalidOperationException>(() => app.MapGatewayProfiles());
    }

    [Fact]
    public void Malformed_or_repeated_enabled_keys_fail_the_options()
    {
        var services = new ServiceCollection().AddCodelabGatewayProfiles(Configuration("sms", "sms"));
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<GatewayOptions>>().Value);
    }

    internal static Dictionary<string, string?> Settings(params string[] enabled) =>
        enabled.Select((key, index) => new KeyValuePair<string, string?>($"{GatewayOptions.SectionName}:Profiles:{index}", key)).ToDictionary();

    private static IConfiguration Configuration(params string[] enabled) => new ConfigurationBuilder().AddInMemoryCollection(Settings(enabled)).Build();

    private static GatewayProfileRegistry Registry(IGatewayProfile[] profiles, params string[] enabled) =>
        new(profiles, Options.Create(new GatewayOptions { Profiles = enabled }));

    private sealed class KeyedProfile(string key) : IGatewayProfile
    {
        public string Key => key;

        public void Map(IEndpointRouteBuilder endpoints)
        {
        }
    }

    private sealed class OtherSmsProfile : IGatewayProfile
    {
        public string Key => GatewayProfileKey.Sms;

        public void Map(IEndpointRouteBuilder endpoints)
        {
        }
    }
}
