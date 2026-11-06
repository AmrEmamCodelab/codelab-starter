using System.Globalization;
using System.Net;
using Starter.Domain.Localization;
using Starter.Domain.Lookups;
using Starter.Infrastructure.Localization;
using Starter.Integration.Tests.Support;
using Starter.Integration.Tests.Users;

namespace Starter.Integration.Tests.Auditing;

/// <summary>
/// Exit criterion, the PII-leak canary: unique fake personal data (a national id, an e-mail, a phone number, a name)
/// goes in through the contact form, the Excel user import and the notifications (the inbox e-mail of the contact
/// message, and a test notification to a recipient whose e-mail and mobile are canaries), and the outbox delivers them.
/// None of it may reach a retained log sink (the Codelab.Testing canary sink inside the host's logger), and none may
/// appear unmasked in <c>audit.AuditEntries</c>, which must still hold the rows of those writes.
/// </summary>
[Collection(Phase5HostGroup.Name)]
public sealed class PiiCanaryTests(Phase5Host host)
{
    private const string ContactMessages = "/api/portal/contact-messages";
    private const string TestNotification = "/api/admin/notifications/test";
    private const string ContactAuditRows = "SELECT COUNT(*) FROM [audit].[AuditEntries] WHERE [EntityType] = 'ContactMessage' AND [ChangesJson] LIKE @reference";
    private const string AuditRowsWith =
        "SELECT COUNT(*) FROM [audit].[AuditEntries] WHERE [ChangesJson] LIKE @canary OR [EntityKey] LIKE @canary OR [Actor] LIKE @canary";

    [Fact]
    public async Task Planted_personal_data_never_reaches_the_logs_or_the_audit_trail_unmasked()
    {
        var canaries = host.Canaries;
        var adminToken = await host.AdminTokenAsync();
        using var api = host.CreateApi();

        var reference = await SubmitContactAsync(api, canaries);
        await ImportUserAsync(adminToken, canaries);
        using (host.Recipients.Use(new RecipientOverride(canaries.Email, canaries.Phone, AppLanguage.Arabic)))
        {
            using var accepted = await api.PostAsync(TestNotification, body: null, adminToken);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            await host.DrainOutboxAsync(); // delivers the inbox e-mail, the activation e-mail and the test notification
        }

        Assert.NotEmpty(host.Mail.To(canaries.Email)); // the canaries really went through delivery
        Assert.NotEmpty(host.Sms.To(canaries.Phone));
        host.LogSink.Check().ThrowIfFailed();
        Assert.True(await CountAsync(ContactAuditRows, "@reference", reference) > 0, "The contact message was not audited, so the audit check would be vacuous.");
        foreach (var canary in canaries.All)
        {
            Assert.True(await CountAsync(AuditRowsWith, "@canary", canary) == 0, $"A canary ({CanaryLabel(canary)}) is stored unmasked in audit.AuditEntries.");
        }
    }

    private static async Task<string> SubmitContactAsync(ApiClient api, PiiCanaries canaries)
    {
        var body = new
        {
            fullName = canaries.Name,
            email = canaries.Email,
            topicId = ContactTopicIds.Account,
            message = $"My national id is {canaries.NationalId} and my mobile is {canaries.Phone}; please call me.",
            language = LanguageTags.Arabic,
        };
        using var response = await api.PostAsync(ContactMessages, body, accessToken: null);
        return (await ApiClient.ReadAsync(response, HttpStatusCode.Created)).GetProperty("referenceNumber").GetString()!;
    }

    private async Task ImportUserAsync(string adminToken, PiiCanaries canaries)
    {
        using var users = new UsersApi(host.Factory.CreateClient());
        var suffix = TestNames.Suffix();
        var code = "PII-" + suffix;
        await users.CreateExternalOrganizationAsync(code, adminToken);
        var workbook = TestWorkbook.Build(
            ["UserName", "Display Name", "email", "KIND", "binding", "Organization_Code"],
            ["pii.user." + suffix, canaries.Name, canaries.Email, "external", "password", code]);
        using var previewResponse = await users.PostWorkbookAsync(workbook, adminToken);
        var preview = await UsersApi.ReadAsync(previewResponse, HttpStatusCode.OK);
        using var commit = await users.PostAsync(UsersApi.Commit(preview.GetProperty("batchId").GetString()!), adminToken);
        Assert.Single((await UsersApi.ReadAsync(commit, HttpStatusCode.OK)).GetProperty("created").EnumerateArray());
    }

    private async Task<int> CountAsync(string sql, string parameter, string contains) =>
        Convert.ToInt32(
            await TestSqlServer.ScalarAsync(host.DatabaseName, sql, new Dictionary<string, object> { [parameter] = $"%{contains}%" }),
            CultureInfo.InvariantCulture);

    /// <summary>The canary's label (<c>canary-email-…</c> gives <c>email</c>), so a failure never repeats the value.</summary>
    private static string CanaryLabel(string canary) => canary.Split('-')[1];
}
