using System.Text.Json;
using Codelab.Integration.Gateway;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Codelab.Integration.Tests.Gateway;

/// <summary>The wire contract: route and code constants, JSON shapes (snapshots) and the problem shape.</summary>
public sealed class GatewayContractTests
{
    [Fact]
    public void Relay_and_health_routes_are_the_agreed_paths()
    {
        Assert.Equal("/relay/sso/token", GatewayRoutes.SsoToken);
        Assert.Equal("/relay/sso/jwks", GatewayRoutes.SsoJwks);
        Assert.Equal("/relay/sms", GatewayRoutes.Sms);
        Assert.Equal("/health/live", GatewayRoutes.HealthLive);
        Assert.Equal("/health/ready", GatewayRoutes.HealthReady);
        Assert.Equal("/relay/sms", GatewayRoutes.ProfilePrefix(GatewayProfileKey.Sms));
        Assert.Equal("relay/sso/token", GatewayRoutes.Relative(GatewayRoutes.SsoToken));
        Assert.Equal("sso", GatewayProfileKey.Sso);
        Assert.Equal("sms", GatewayProfileKey.Sms);
        Assert.Equal("Codelab:Integration:Gateway", GatewayOptions.SectionName);
    }

    [Theory]
    [InlineData("SMS")]
    [InlineData("1sms")]
    [InlineData("sms/x")]
    [InlineData("")]
    public void A_malformed_key_has_no_profile_prefix(string key) =>
        Assert.Throws<ArgumentException>(() => GatewayRoutes.ProfilePrefix(key));

    [Theory]
    [InlineData("sso", true)]
    [InlineData("national-registry-2", true)]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345", true)]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456", false)]
    [InlineData("Sso", false)]
    [InlineData("-sso", false)]
    [InlineData("s so", false)]
    [InlineData(null, false)]
    public void Profile_keys_are_lower_case_kebab_up_to_the_limit(string? key, bool valid) =>
        Assert.Equal(valid, GatewayProfileKey.IsValid(key));

    [Theory]
    [InlineData(GatewayFailureCodes.UpstreamUnavailable, "GATEWAY_UPSTREAM_UNAVAILABLE", StatusCodes.Status503ServiceUnavailable)]
    [InlineData(GatewayFailureCodes.UpstreamRejected, "GATEWAY_UPSTREAM_REJECTED", StatusCodes.Status502BadGateway)]
    [InlineData(GatewayFailureCodes.ProfileDisabled, "GATEWAY_PROFILE_DISABLED", StatusCodes.Status404NotFound)]
    public void Each_failure_code_has_its_value_status_and_a_title(string code, string value, int status)
    {
        Assert.Equal(value, code);
        Assert.Equal(status, GatewayFailureCodes.StatusOf(code));
        Assert.False(string.IsNullOrWhiteSpace(GatewayFailureCodes.TitleOf(code)));
        Assert.True(GatewayFailureCodes.IsKnown(code));
    }

    [Fact]
    public void An_unknown_code_has_no_status_or_title()
    {
        Assert.False(GatewayFailureCodes.IsKnown("OTHER"));
        Assert.False(GatewayFailureCodes.IsKnown(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => GatewayFailureCodes.StatusOf("OTHER"));
        Assert.Throws<ArgumentOutOfRangeException>(() => GatewayFailureCodes.TitleOf("OTHER"));
    }

    [Fact]
    public void The_problem_result_carries_status_title_and_code()
    {
        var result = Assert.IsType<ProblemHttpResult>(GatewayProblems.For(GatewayFailureCodes.ProfileDisabled));

        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal(GatewayFailureCodes.TitleOf(GatewayFailureCodes.ProfileDisabled), result.ProblemDetails.Title);
        Assert.Equal(GatewayFailureCodes.ProfileDisabled, result.ProblemDetails.Extensions[GatewayProblemMembers.Code]);
    }

    [Fact]
    public void Sms_request_snapshot()
    {
        var json = JsonSerializer.Serialize(new SmsRelayRequest { To = "05XXXXXXXX", Text = "Hello", Language = GatewayLanguages.English }, GatewayJson.Options);

        Assert.Equal("{\"to\":\"05XXXXXXXX\",\"text\":\"Hello\",\"language\":2}", json);
    }

    [Fact]
    public void Sms_result_snapshot_omits_absent_members()
    {
        var full = JsonSerializer.Serialize(new SmsRelayResult { Accepted = true, ProviderMessageId = "p-1", ReferenceCode = "GW-1" }, GatewayJson.Options);
        var bare = JsonSerializer.Serialize(new SmsRelayResult { Accepted = false });

        Assert.Equal("{\"accepted\":true,\"providerMessageId\":\"p-1\",\"referenceCode\":\"GW-1\"}", full);
        Assert.Equal("{\"Accepted\":false}", bare);
    }

    [Fact]
    public void Arabic_text_round_trips_and_the_language_stays_an_integer()
    {
        var request = new SmsRelayRequest { To = "+9665XXXXXXXX", Text = "رمز التحقق 1234", Language = GatewayLanguages.Arabic };

        var json = JsonSerializer.Serialize(request, GatewayJson.Options);
        var back = JsonSerializer.Deserialize<SmsRelayRequest>(json, GatewayJson.Options)!;

        Assert.Equal(request.Text, back.Text);
        Assert.Equal(request.To, back.To);
        Assert.Equal(JsonValueKind.Number, JsonDocument.Parse(json).RootElement.GetProperty("language").ValueKind);
    }

    [Fact]
    public void The_shared_json_options_are_read_only() =>
        Assert.True(GatewayJson.Options.IsReadOnly);

    [Fact]
    public void Languages_are_one_and_two()
    {
        Assert.Equal(1, GatewayLanguages.Arabic);
        Assert.Equal(2, GatewayLanguages.English);
        Assert.False(GatewayLanguages.IsKnown(0));
        Assert.False(GatewayLanguages.IsKnown(3));
    }
}
