using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Starter.Integration.Tests.Support;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Users;

/// <summary>
/// The Users module on a real database through the real host: organizations and accounts created by the break-glass
/// administrator, an activation link redeemed, a deactivation that ends the user's access on the next call, and the
/// Excel import (preview 3 rows, commit 2 accounts whose activation is e-mailed through Pending outbox rows, 1 rejected).
/// </summary>
[Collection(UsersHostGroup.Name)]
public sealed partial class UsersAdminFlowTests(UsersHost host)
{
    private const string MyPermissions = "/api/me/permissions";
    private const string PendingStatus = "1";
    private const string ActivationType = "account-activation";

    [Fact]
    public async Task An_admin_creates_an_organization_and_a_user_who_activates_signs_in_and_is_cut_off_by_deactivation()
    {
        using var client = host.CreateClient();
        using var api = new UsersApi(host.Factory.CreateClient());
        var admin = await client.LoginAsync(host.Accounts.BreakGlassUserName, host.Accounts.BreakGlassPassword);
        var suffix = Suffix();
        var organizationId = await api.CreateExternalOrganizationAsync("IT-" + suffix, admin.AccessToken);

        using var created = await api.PostJsonAsync(UsersApi.Users, NewUser("it.user." + suffix, organizationId), admin.AccessToken);
        var body = await UsersApi.ReadAsync(created, HttpStatusCode.Created);
        var userId = body.GetProperty("user").GetProperty("id").GetString()!;
        Assert.False(body.GetProperty("user").GetProperty("hasPassword").GetBoolean());
        Assert.Equal("IT-" + suffix + " (ar)", body.GetProperty("user").GetProperty("organizationName").GetString()); // Arabic by default
        var link = body.GetProperty("activation").GetProperty("activationLink").GetString()!;
        Assert.StartsWith(UsersHost.LinkBase + "/activate" + UsersApi.ActivationFragment, link, StringComparison.Ordinal);

        var chosen = NewPassword();
        using var activated = await api.PostJsonAsync(UsersApi.Activate, ActivationBody(UsersApi.TicketOf(link), chosen), accessToken: null);
        Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);
        var user = await client.LoginAsync("it.user." + suffix, chosen);
        using var before = await client.GetAsync(MyPermissions, user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var deactivated = await api.PostAsync(UsersApi.Deactivate(userId), admin.AccessToken);
        using var after = await client.GetAsync(MyPermissions, user.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
        await StarterProblems.AssertProblemAsync(after, HttpStatusCode.Unauthorized);
        using var reread = await api.GetAsync(UsersApi.UserPath(userId), admin.AccessToken);
        Assert.False((await UsersApi.ReadAsync(reread, HttpStatusCode.OK)).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task The_import_previews_three_rows_and_commits_two_accounts_with_pending_activation_emails()
    {
        using var client = host.CreateClient();
        using var api = new UsersApi(host.Factory.CreateClient());
        var admin = await client.LoginAsync(host.Accounts.BreakGlassUserName, host.Accounts.BreakGlassPassword);
        var suffix = Suffix();
        var code = "IMP-" + suffix;
        await api.CreateExternalOrganizationAsync(code, admin.AccessToken);
        var workbook = TestWorkbook.Build(
            ["UserName", "Display Name", "email", "KIND", "binding", "Organization_Code"],
            ["sample.one." + suffix, "Sample One", $"one.{suffix}@example.test", "external", "password", code],
            ["sample.two." + suffix, "Sample Two", $"two.{suffix}@example.test", "external", "password", code],
            ["sample.three." + suffix, "Sample Three", "not-an-email", "external", "password", code]);

        using var previewResponse = await api.PostWorkbookAsync(workbook, admin.AccessToken);
        var preview = await UsersApi.ReadAsync(previewResponse, HttpStatusCode.OK);

        Assert.Equal(2, preview.GetProperty("validCount").GetInt32());
        Assert.Equal(1, preview.GetProperty("invalidCount").GetInt32());
        var invalid = preview.GetProperty("rows").EnumerateArray().Single(row => !row.GetProperty("valid").GetBoolean());
        Assert.Equal(4, invalid.GetProperty("rowNumber").GetInt32());
        Assert.Contains("USERS_IMPORT_ROW_EMAIL_INVALID", invalid.GetProperty("errors").EnumerateArray().Select(error => error.GetString()));

        using var commitResponse = await api.PostAsync(UsersApi.Commit(preview.GetProperty("batchId").GetString()!), admin.AccessToken);
        var commit = await UsersApi.ReadAsync(commitResponse, HttpStatusCode.OK);

        var created = commit.GetProperty("created").EnumerateArray().ToList();
        Assert.Equal(2, created.Count);
        Assert.Equal(4, Assert.Single(commit.GetProperty("rejected").EnumerateArray()).GetProperty("rowNumber").GetInt32());
        foreach (var account in created)
        {
            await AssertImportedAccountAsync(api, account, admin.AccessToken);
        }

        using var again = await api.PostAsync(UsersApi.Commit(preview.GetProperty("batchId").GetString()!), admin.AccessToken);
        Assert.Equal("USERS_IMPORT_BATCH_COMMITTED", await StarterProblems.AssertProblemAsync(again, HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task A_user_without_the_permission_is_refused_and_bad_input_is_problem_json()
    {
        using var client = host.CreateClient();
        using var api = new UsersApi(host.Factory.CreateClient());
        var devUser = await client.LoginAsync(host.Accounts.DevUserName, host.Accounts.DevUserPassword);
        var admin = await client.LoginAsync(host.Accounts.BreakGlassUserName, host.Accounts.BreakGlassPassword);

        using var forbidden = await api.GetAsync(UsersApi.Users, devUser.AccessToken);
        using var invalid = await api.PostJsonAsync(UsersApi.Organizations, new { code = "bad code", kind = 9 }, admin.AccessToken);
        using var notXlsx = await api.PostWorkbookAsync([1, 2, 3], admin.AccessToken);

        await StarterProblems.AssertProblemAsync(forbidden, HttpStatusCode.Forbidden);
        Assert.Equal("VALIDATION_FAILED", await StarterProblems.AssertProblemAsync(invalid, HttpStatusCode.BadRequest));
        Assert.Equal("USERS_IMPORT_FILE_INVALID", await StarterProblems.AssertProblemAsync(notXlsx, HttpStatusCode.BadRequest));
    }

    private async Task AssertImportedAccountAsync(UsersApi api, JsonElement account, string adminToken)
    {
        var id = account.GetProperty("id").GetString()!;
        Assert.Equal(1, account.GetProperty("binding").GetInt32());
        Assert.Equal(JsonValueKind.Null, account.GetProperty("activationLink").ValueKind); // e-mailed, never shown to the administrator
        Assert.Equal(UsersApi.EmailDelivery, account.GetProperty("activationDelivery").GetInt32());

        using var detail = await api.GetAsync(UsersApi.UserPath(id), adminToken);
        Assert.False((await UsersApi.ReadAsync(detail, HttpStatusCode.OK)).GetProperty("hasPassword").GetBoolean());

        Assert.Matches(TokenShape(), id); // safe to put in the query below
        var pending = await TestSqlServer.ScalarAsync(
            host.DatabaseName,
            $"SELECT COUNT(*) FROM [messaging].[OutboxMessages] WHERE [Type] = '{ActivationType}' AND [Status] = {PendingStatus} AND [PayloadJson] LIKE '%{id}%'");
        Assert.Equal(1, Convert.ToInt32(pending, CultureInfo.InvariantCulture));
        var payload = (string)(await TestSqlServer.ScalarAsync(host.DatabaseName, $"SELECT TOP 1 [PayloadJson] FROM [messaging].[OutboxMessages] WHERE [PayloadJson] LIKE '%{id}%'"))!;
        Assert.DoesNotContain(UsersApi.ActivationFragment, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("sealed", payload, StringComparison.OrdinalIgnoreCase);
    }

    private static object NewUser(string userName, string organizationId) => new
    {
        userName,
        displayName = "Integration User",
        email = (string?)null, // no address: the activation link comes back to the administrator
        kind = UsersApi.ExternalKind,
        binding = UsersApi.PasswordBinding,
        organizationId,
    };

    private static object ActivationBody(string ticket, string password) => new { ticket, password };

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>A strong password made at run time (never a literal in the repository).</summary>
    private static string NewPassword() => "Aa1!" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex TokenShape();
}
