using Codelab.Attachments.Api.Tests.TestSupport;
using Codelab.Security.Cors;
using Codelab.Security.PublicIds;
using Codelab.Security.Tickets;
using Codelab.Security.Uploads;

namespace Codelab.Attachments.Api.Tests.Http;

/// <summary>Configurations the service refuses to start with (fail fast, never a half-secured service).</summary>
public sealed class StartupRefusalTests
{
    // WebApplicationFactory's deferred host can report a refused start as ObjectDisposedException (it disposes the
    // provider while it still reads from it). That race hides the reason, never the refusal, so the start is retried
    // on a fresh host until the real reason surfaces.
    private const int StartAttempts = 5;

    [Theory]
    [InlineData(SealedTicketOptions.SectionName + ":Enabled", "false", "Tickets:Enabled")]
    [InlineData(UploadSafetyOptions.SectionName + ":ReceiptsEnabled", "false", "Uploads:ReceiptsEnabled")]
    [InlineData(CodelabCorsOptions.SectionName + ":AllowedOrigins:0", AttachmentsServiceHost.AllowedOrigin, "Attachments:Cors")]
    [InlineData("Attachments:Cors:AllowedOrigins:0", "http://web.example.test", "Attachments:Cors:AllowedOrigins")]
    [InlineData("Attachments:Storage:RootPath", "relative-folder", "Attachments:Storage:RootPath")]
    [InlineData("Codelab:Security:ServiceAssertions:Server:TrustedServices:0:PublicKeyPem", "not a key", "PublicKeyPem")]
    [InlineData(PublicIdKeyringOptions.SectionName + ":ActiveKeyId", "api", PublicIdKeyringOptions.SectionName)]
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
            using var host = new AttachmentsServiceHost(configure);
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
