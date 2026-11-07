using Codelab.Integration.Gateway;
using Codelab.Integration.Outbound;
using Codelab.Integration.Tests.Gateway.Support;
using Codelab.Security.ServiceAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Codelab.Integration.Tests.Gateway;

/// <summary>The gateway options validators and their start-up wiring.</summary>
public sealed class GatewayOptionsValidatorTests
{
    private readonly ServiceKeyPair _keys = ServiceKeyPair.Create();

    [Theory]
    [InlineData("https://gateway.example.test/", false, true)]
    [InlineData("https://gateway.example.test/dmz", false, true)]
    [InlineData("http://gateway.example.test/", false, false)]
    [InlineData("http://localhost:5187/", true, true)]
    [InlineData("ftp://gateway.example.test/", true, false)]
    [InlineData("https://gateway.example.test/?a=1", false, false)]
    [InlineData("https://gateway.example.test/#f", false, false)]
    public void The_base_url_is_absolute_and_https_outside_development(string baseUrl, bool isDevelopment, bool valid)
    {
        var result = new GatewayClientOptionsValidator(isDevelopment).Validate(name: null, new GatewayOptions { BaseUrl = new Uri(baseUrl) });

        Assert.Equal(valid, result.Succeeded);
    }

    [Fact]
    public void A_missing_base_url_and_audience_are_both_reported()
    {
        var result = new GatewayClientOptionsValidator(isDevelopment: false).Validate(name: null, new GatewayOptions { Audience = " " });

        Assert.True(result.Failed);
        Assert.Equal(2, result.Failures!.Count());
    }

    [Fact]
    public void A_relative_base_url_is_refused() =>
        Assert.True(new GatewayClientOptionsValidator(isDevelopment: true).Validate(name: null, new GatewayOptions { BaseUrl = new Uri("relay", UriKind.Relative) }).Failed);

    [Fact]
    public void Profiles_must_be_well_formed_and_unique()
    {
        var result = new GatewayOptionsValidator().Validate(name: null, new GatewayOptions { Profiles = ["sso", "SMS", "sso"] });

        Assert.Equal(2, result.Failures!.Count());
        Assert.True(new GatewayOptionsValidator().Validate(name: null, new GatewayOptions { Profiles = ["sso", "sms"] }).Succeeded);
    }

    [Fact]
    public void An_http_gateway_fails_the_start_in_production()
    {
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys, new Uri("http://gateway.example.test/")));

        var failure = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<GatewayOptions>>().Value);

        Assert.Contains("https", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_http_gateway_is_allowed_in_development_and_the_outbound_options_follow_the_section()
    {
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys, new Uri("http://localhost:5187/gw")), environment: Environments.Development);

        var outbound = provider.GetRequiredService<IOptionsMonitor<OutboundClientOptions>>().Get(GatewayRelayClient.Name);

        Assert.Equal(new Uri("http://localhost:5187/gw/"), outbound.BaseAddress);
        Assert.True(outbound.AllowPlainHttp);
        Assert.Equal(1, outbound.Retry.MaxRetries);
    }

    [Fact]
    public void The_gateway_assertion_options_take_the_shared_identity_and_the_gateway_audience()
    {
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys));

        var assertion = provider.GetRequiredService<IOptionsMonitor<ServiceAssertionClientOptions>>().Get(GatewayRelayClient.Name);

        Assert.Equal(GatewayDefaults.Audience, assertion.Audience);
        Assert.Equal(GatewayClientHost.CallerName, assertion.ServiceName);
        Assert.Equal(GatewayClientHost.CallerKeyId, assertion.KeyId);
    }

    [Theory]
    [InlineData("not a key")]
    [InlineData("")]
    public void A_missing_or_broken_private_key_fails_the_start(string pem)
    {
        var settings = GatewayClientHost.Settings(_keys);
        settings[$"{ServiceAssertionClientOptions.SectionName}:PrivateKeyPem"] = pem;
        using var provider = GatewayClientHost.Build(settings);

        var failure = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptionsMonitor<ServiceAssertionClientOptions>>().Get(GatewayRelayClient.Name));

        Assert.Contains("PrivateKeyPem", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_public_key_is_not_a_signing_key()
    {
        var options = new ServiceAssertionClientOptions { ServiceName = "a", KeyId = "k", Audience = "g", PrivateKeyPem = _keys.PublicPem };

        Assert.True(new GatewayAssertionOptionsValidator().Validate(GatewayRelayClient.Name, options).Failed);
    }

    [Fact]
    public void Missing_identity_fields_and_a_bad_lifetime_are_reported_together()
    {
        var options = new ServiceAssertionClientOptions { PrivateKeyPem = _keys.PrivatePem, LifetimeSeconds = 0, Audience = "g" };

        Assert.Equal(2, new GatewayAssertionOptionsValidator().Validate(GatewayRelayClient.Name, options).Failures!.Count());
    }

    [Fact]
    public void Other_named_assertion_options_are_left_to_their_owner() =>
        Assert.True(new GatewayAssertionOptionsValidator().Validate(Options.DefaultName, new ServiceAssertionClientOptions()).Skipped);

    [Theory]
    [InlineData("https://g.example.test", "https://g.example.test/")]
    [InlineData("https://g.example.test/dmz", "https://g.example.test/dmz/")]
    [InlineData("https://g.example.test/dmz/", "https://g.example.test/dmz/")]
    public void The_base_address_always_ends_with_a_slash(string input, string expected) =>
        Assert.Equal(new Uri(expected), GatewayServiceCollectionExtensions.WithTrailingSlash(new Uri(input)));
}
