using System.Net;
using System.Text; // codelab:module Sms
using Codelab.Errors; // codelab:module Sms
using Codelab.Gateway.Api.Tests.TestSupport;
using Codelab.Integration.Gateway;
using Microsoft.Extensions.Options;

namespace Codelab.Gateway.Api.Tests.Http;

/// <summary>
/// Only the profiles listed in Codelab:Integration:Gateway:Profiles relay; the routes of a registered profile that is
/// not listed answer 404 GATEWAY_PROFILE_DISABLED to a signed caller (401 to anyone else). An enabled profile with an
/// incomplete upstream section stops the start; a disabled one needs no section at all.
/// </summary>
public sealed class ProfileSelectionTests
{
    // codelab:module-begin Sms
    private const string SmsJson = """{"to":"+05XXXXXX10","text":"hello","language":2}""";

    [Fact]
    public async Task A_disabled_profile_answers_404_profile_disabled_to_a_signed_caller()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var host = new GatewayHost(upstream, OnlySso);
        using var client = host.CreateClient();

        using var response = await client.SendAsync(host.SmsRequest(SmsJson));

        await Problems.AssertAsync(response, HttpStatusCode.NotFound, GatewayFailureCodes.ProfileDisabled);
        Assert.Empty(upstream.Messages);
    }

    [Fact]
    public async Task A_disabled_profile_still_refuses_an_unsigned_caller_with_401()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var host = new GatewayHost(upstream, OnlySso);
        using var client = host.CreateClient();

        using var response = await client.PostAsync(new Uri(GatewayRoutes.Sms, UriKind.Relative), new StringContent(SmsJson, Encoding.UTF8, GatewayMediaTypes.Json));

        await Problems.AssertAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task A_disabled_profile_needs_no_upstream_section()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var host = new GatewayHost(upstream, settings =>
        {
            OnlySso(settings);
            foreach (var key in settings.Keys.Where(key => key.StartsWith("Gateway:Sms:", StringComparison.Ordinal)).ToList())
            {
                settings.Remove(key);
            }
        });
        using var client = host.CreateClient();

        using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    // codelab:module-end Sms
    [Fact]
    public async Task No_enabled_profile_is_not_ready()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var host = new GatewayHost(upstream, settings =>
        {
            settings.Remove($"{GatewayOptions.SectionName}:Profiles:0");
            settings.Remove($"{GatewayOptions.SectionName}:Profiles:1");
        });
        using var client = host.CreateClient();

        using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Theory]
    [InlineData("Gateway:Sso:TokenEndpoint")]
    [InlineData("Gateway:Sms:UrlTemplate")] // codelab:module Sms
    [InlineData("Gateway:Sms:BodyTemplate")] // codelab:module Sms
    public async Task An_enabled_profile_with_a_missing_upstream_setting_stops_the_start(string setting)
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var host = new GatewayHost(upstream, settings => settings.Remove(setting));

        var failure = StartFailure(host);

        Assert.Contains(setting, failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plain_http_upstream_stops_the_start_outside_development()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var host = new GatewayHost(upstream, settings => settings["Gateway:Sso:TokenEndpoint"] = "http://127.0.0.1:9/token");

        var failure = StartFailure(host);

        Assert.Contains("https", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_enabled_profile_stops_the_start()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var host = new GatewayHost(upstream, settings => settings[$"{GatewayOptions.SectionName}:Profiles:2"] = "national-lookup");

        var failure = StartFailure(host);

        Assert.Contains("national-lookup", failure, StringComparison.Ordinal);
    }

    /// <summary>The start-up failure (options validation reports through an AggregateException) as one message.</summary>
    private static string StartFailure(GatewayHost host)
    {
        var failure = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        var all = failure is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [failure];
        Assert.All(all, inner => Assert.True(inner is OptionsValidationException or InvalidOperationException, inner.GetType().Name));
        return string.Join(" ", all.Select(inner => inner.Message));
    }
    // codelab:module-begin Sms

    private static void OnlySso(IDictionary<string, string?> settings) => settings.Remove($"{GatewayOptions.SectionName}:Profiles:1");
    // codelab:module-end Sms
}
