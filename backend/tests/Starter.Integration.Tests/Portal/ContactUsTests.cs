using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Codelab.Security.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Starter.Application.Settings;
using Starter.Domain.Lookups;
using Starter.Infrastructure.Localization;
using Starter.Infrastructure.Numbering;
using Starter.Integration.Tests.Support;
using Starter.Modules.Portal.Application;

namespace Starter.Integration.Tests.Portal;

/// <summary>
/// The public contact form on real SQL: a submission answers 201 with a <c>CU-YYYY-NNN</c> number (the year in Riyadh
/// time), lands in <c>portal.ContactMessages</c> and is e-mailed to the inbox after the outbox drain; numbering restarts
/// at 001 when the Riyadh year rolls over (host clock shifted); the anonymous write is rate limited per client (429).
/// </summary>
[Collection(Phase5HostGroup.Name)]
public sealed partial class ContactUsTests(Phase5Host host)
{
    private const string ContactMessages = "/api/portal/contact-messages";
    private const string ReferenceMember = "referenceNumber";
    private const string FirstOfYear = "001";
    private const string MessageCount = "SELECT COUNT(*) FROM [portal].[ContactMessages] WHERE [ReferenceNumber] = @reference";
    private const string JsonMediaType = "application/json";
    private const string MessageCountByEmail = "SELECT COUNT(*) FROM [portal].[ContactMessages] WHERE [Email] = @email";
    private const string AdminSettings = "/api/admin/settings/";
    private const string ValueMember = "value";
    private const string CodeMember = "code";
    private const string BooleanTrue = "true";
    private const string BooleanFalse = "false";

    /// <summary>A TEST-NET-1 address (RFC 5737): a client of its own, so the rate-limit test never starves the others.</summary>
    private const string RateLimitedClient = "192.0.2.77";

    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan HalfMinute = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_submission_gets_a_CU_number_of_this_year_is_stored_and_reaches_the_inbox()
    {
        using var api = host.CreateApi();
        var year = RiyadhYear(host.Clock.GetUtcNow());

        var reference = await SubmitAsync(api, ContactBody(fullName: "Contact Sender", email: $"sender.{TestNames.Suffix()}@{PiiCanaries.EmailDomain}"));
        await host.DrainOutboxAsync();

        Assert.Matches(ReferenceShape(), reference);
        Assert.StartsWith($"{PortalNumberPrefixes.ContactUs}-{year}-", reference, StringComparison.Ordinal);
        Assert.Equal(1, await CountAsync(reference));
        Assert.Contains(host.Mail.To(host.InboxEmail), mail => mail.Body.Contains(reference, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Numbering_restarts_at_001_when_the_Riyadh_year_rolls_over()
    {
        using var api = host.CreateApi();
        var nextYear = RiyadhYear(host.Clock.GetUtcNow()) + 1; // a year no other test numbers in
        var riyadh = RiyadhZone();
        var lastMinute = new DateTime(nextYear + 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified) - OneMinute; // 23:59 on 31 December
        try
        {
            host.Clock.TravelTo(new DateTimeOffset(lastMinute, riyadh.GetUtcOffset(lastMinute)));
            var december = await SubmitAsync(api, ContactBody(fullName: "Year End", email: $"year.end@{PiiCanaries.EmailDomain}"));
            host.Clock.Advance(OneMinute + HalfMinute); // 00:00:30 on 1 January in Riyadh, still 31 December in UTC
            var january = await SubmitAsync(api, ContactBody(fullName: "New Year", email: $"new.year@{PiiCanaries.EmailDomain}"));

            Assert.Equal($"{PortalNumberPrefixes.ContactUs}-{nextYear}-{FirstOfYear}", december);
            Assert.Equal($"{PortalNumberPrefixes.ContactUs}-{nextYear + 1}-{FirstOfYear}", january);
            Assert.Equal(nextYear, host.Clock.GetUtcNow().UtcDateTime.Year); // the rollover followed Riyadh, not UTC
        }
        finally
        {
            host.Clock.Reset();
        }
    }

    [Fact]
    public async Task One_client_over_the_limit_gets_429_problem_json()
    {
        var limit = host.Factory.Services.GetRequiredService<IOptions<ClientRateLimitOptions>>().Value.PermitLimit;
        var refused = 0;
        for (var request = 0; request <= limit; request++)
        {
            var context = await PostAsClientAsync(RateLimitedClient, ContactBody(fullName: string.Empty, email: "not-an-email"));
            if (context.Response.StatusCode == StatusCodes.Status429TooManyRequests)
            {
                refused++;
                Assert.StartsWith("application/problem+json", context.Response.ContentType, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode); // within the limit: validated and refused
            }
        }

        Assert.Equal(1, refused); // exactly the request over PermitLimit
    }

    [Fact]
    public async Task A_form_switched_off_on_the_admin_settings_screen_is_refused_with_409_and_stores_nothing()
    {
        using var api = host.CreateApi();
        var adminToken = await host.AdminTokenAsync();
        var email = $"switched.off.{TestNames.Suffix()}@{PiiCanaries.EmailDomain}";
        try
        {
            await SetContactEnabledAsync(api, enabled: false, adminToken);

            using var response = await api.PostAsync(ContactMessages, ContactBody(fullName: "Switched Off", email: email), accessToken: null);
            var problem = await ApiClient.ReadAsync(response, HttpStatusCode.Conflict);

            Assert.Equal(PortalErrorCodes.ContactDisabled, problem.GetProperty(CodeMember).GetString());
            Assert.Equal(0, await CountByEmailAsync(email));
        }
        finally
        {
            await SetContactEnabledAsync(api, enabled: true, adminToken);
        }

        Assert.Matches(ReferenceShape(), await SubmitAsync(api, ContactBody(fullName: "Switched On", email: email)));
    }

    private static async Task SetContactEnabledAsync(ApiClient api, bool enabled, string adminToken)
    {
        var text = enabled ? BooleanTrue : BooleanFalse;
        using var response = await api.PutAsync(AdminSettings + SettingKeys.PortalContactEnabled, new { value = text }, adminToken);
        Assert.Equal(text, (await ApiClient.ReadAsync(response, HttpStatusCode.OK)).GetProperty(ValueMember).GetString());
    }

    private async Task<int> CountByEmailAsync(string email) =>
        Convert.ToInt32(
            await TestSqlServer.ScalarAsync(host.DatabaseName, MessageCountByEmail, new Dictionary<string, object> { ["@email"] = email }),
            CultureInfo.InvariantCulture);

    private static object ContactBody(string fullName, string email) => new
    {
        fullName,
        email,
        topicId = ContactTopicIds.General,
        message = "A question about the service from the integration tests.",
        language = LanguageTags.English,
    };

    private static async Task<string> SubmitAsync(ApiClient api, object body)
    {
        using var response = await api.PostAsync(ContactMessages, body, accessToken: null);
        return (await ApiClient.ReadAsync(response, HttpStatusCode.Created)).GetProperty(ReferenceMember).GetString()!;
    }

    /// <summary>Sends the form through the test server with a chosen client address (the rate limiter's partition).</summary>
    private Task<HttpContext> PostAsClientAsync(string clientAddress, object body) =>
        host.Factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(clientAddress);
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = ContactMessages;
            context.Request.ContentType = JsonMediaType;
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body)));
        });

    private async Task<int> CountAsync(string reference) =>
        Convert.ToInt32(
            await TestSqlServer.ScalarAsync(host.DatabaseName, MessageCount, new Dictionary<string, object> { ["@reference"] = reference }),
            CultureInfo.InvariantCulture);

    private static TimeZoneInfo RiyadhZone() => TimeZoneInfo.FindSystemTimeZoneById(NumberingOptions.DefaultTimeZoneId);

    private static int RiyadhYear(DateTimeOffset now) => TimeZoneInfo.ConvertTime(now, RiyadhZone()).Year;

    [GeneratedRegex(@"^CU-\d{4}-\d{3,}$")]
    private static partial Regex ReferenceShape();
}
