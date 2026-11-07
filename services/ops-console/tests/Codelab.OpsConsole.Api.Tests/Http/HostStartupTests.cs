using System.Net;
using System.Text.Json;
using Codelab.OpsConsole.Api.Tests.TestSupport;
using Codelab.Security.ServiceAuth;

namespace Codelab.OpsConsole.Api.Tests.Http;

/// <summary>How the real host starts: the dev operator only in Development, Windows sign-in elsewhere, refusals named.</summary>
public sealed class HostStartupTests
{
    // WebApplicationFactory's deferred host can report a refused start as ObjectDisposedException (it disposes the
    // provider while it still reads from it). That race hides the reason, never the refusal, so the start is retried
    // on a fresh host until the real reason surfaces.
    private const int StartAttempts = 5;

    [Fact]
    public async Task In_development_the_dev_operator_signs_in_and_passes_the_group_check()
    {
        using var host = new OpsConsoleHost(
            settings =>
            {
                settings["Codelab:OpsConsole:DevOperator:Enabled"] = "true";
                settings["Codelab:OpsConsole:DevOperator:Name"] = "DEV\\ops-operator";
                settings["Codelab:OpsConsole:DevOperator:Groups:0"] = OpsConsoleHost.OperatorGroupSid;
            },
            environment: "Development",
            replaceSignIn: false);
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/ops-api/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DEV\\ops-operator", body.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task In_development_a_dev_operator_outside_the_group_gets_403()
    {
        using var host = new OpsConsoleHost(
            settings =>
            {
                settings["Codelab:OpsConsole:DevOperator:Enabled"] = "true";
                settings["Codelab:OpsConsole:DevOperator:Name"] = "DEV\\visitor";
                settings["Codelab:OpsConsole:DevOperator:Groups:0"] = "SomeOtherGroup";
            },
            environment: "Development",
            replaceSignIn: false);
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/ops-api/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Outside_development_an_anonymous_caller_is_challenged_for_windows_sign_in()
    {
        using var host = new OpsConsoleHost(replaceSignIn: false);
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/ops-api/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, challenge => challenge.Scheme == "Negotiate");
    }

    [Theory]
    [InlineData("Codelab:OpsConsole:DevOperator:Enabled", "true", "DevOperator:Enabled")]
    [InlineData("Codelab:OpsConsole:OperatorGroup", "", "OperatorGroup")]
    [InlineData("Codelab:OpsConsole:Api:BaseUrl", "http://api.example.test/", "Api:BaseUrl")]
    [InlineData("Codelab:OpsConsole:Seq:BaseUrl", "http://seq.example.test/", "Seq:BaseUrl")]
    [InlineData("Codelab:OpsConsole:Seq:MaxEvents", "0", "Seq:MaxEvents")]
    [InlineData(ServiceAssertionClientOptions.SectionName + ":PrivateKeyPem", "", "PrivateKeyPem")]
    [InlineData("Codelab:Security:PublicIds:ActiveKeyId", "api", "Codelab:Security:PublicIds")]
    public void A_refused_setting_stops_the_start_and_names_the_key(string key, string value, string named)
    {
        var reason = RefusedStart(settings => settings[key] = value);

        Assert.Contains(named, reason, StringComparison.Ordinal);
    }

    private static string RefusedStart(Action<IDictionary<string, string?>> configure)
    {
        var reason = string.Empty;
        for (var attempt = 0; attempt < StartAttempts; attempt++)
        {
            using var host = new OpsConsoleHost(configure);
            var failure = Assert.ThrowsAny<Exception>(() => host.CreateClient());
            reason = failure.ToString();
            if (failure is not ObjectDisposedException)
            {
                break;
            }
        }

        return reason;
    }
}
