using System.Net;
using Starter.Integration.Tests.Support;
using Starter.Integration.Tests.Users;
using Starter.Modules.Identity.Application;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Activation;

/// <summary>
/// End-to-end activation on real SQL: an Excel-imported user with an e-mail address gets no link in the import
/// result; the outbox drain e-mails a fresh link (the <c>.eml</c> in the pickup folder), whose fragment ticket sets a
/// password; the user signs in; the same link used again is refused.
/// </summary>
[Collection(Phase5HostGroup.Name)]
public sealed class ActivationEmailFlowTests(Phase5Host host)
{
    private const string ActivationPath = "/activate";

    [Fact]
    public async Task An_imported_user_activates_from_the_emailed_link_signs_in_and_the_link_is_single_use()
    {
        using var client = host.CreateClient();
        using var users = new UsersApi(host.Factory.CreateClient());
        var adminToken = await host.AdminTokenAsync();
        var suffix = TestNames.Suffix();
        var code = "ACT-" + suffix;
        var userName = "activated." + suffix;
        var email = $"activated.{suffix}@{PiiCanaries.EmailDomain}";
        await users.CreateExternalOrganizationAsync(code, adminToken);

        var created = await ImportAsync(users, TestWorkbook.Build(
            ["UserName", "Display Name", "email", "KIND", "binding", "Organization_Code"],
            [userName, "Activated User", email, "external", "password", code]), adminToken);
        Assert.Equal(UsersApi.EmailDelivery, created.GetProperty("activationDelivery").GetInt32());
        Assert.Empty(host.Mail.To(email)); // nothing is sent before the drain

        await host.DrainOutboxAsync();

        var link = Assert.Single(host.Mail.To(email)).FirstLink;
        Assert.NotNull(link);
        Assert.StartsWith(Phase5Host.LinkBase + ActivationPath + UsersApi.ActivationFragment, link, StringComparison.Ordinal);
        var ticket = UsersApi.TicketOf(link);
        var chosen = NewPassword();

        using (var activated = await users.PostJsonAsync(UsersApi.Activate, ActivationBody(ticket, chosen), accessToken: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);
        }

        var session = await client.LoginAsync(userName, chosen);
        Assert.False(string.IsNullOrEmpty(session.AccessToken));

        using var reused = await users.PostJsonAsync(UsersApi.Activate, ActivationBody(ticket, NewPassword()), accessToken: null);
        Assert.Equal(IdentityErrorCodes.ActivationRejected, await StarterProblems.AssertProblemAsync(reused, HttpStatusCode.BadRequest));
        using var again = await client.PostLoginAsync(userName, chosen);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode); // the refused reuse changed nothing: the chosen password still works
    }

    private static async Task<System.Text.Json.JsonElement> ImportAsync(UsersApi users, byte[] workbook, string adminToken)
    {
        using var previewResponse = await users.PostWorkbookAsync(workbook, adminToken);
        var preview = await UsersApi.ReadAsync(previewResponse, HttpStatusCode.OK);
        Assert.Equal(1, preview.GetProperty("validCount").GetInt32());
        using var commitResponse = await users.PostAsync(UsersApi.Commit(preview.GetProperty("batchId").GetString()!), adminToken);
        var commit = await UsersApi.ReadAsync(commitResponse, HttpStatusCode.OK);
        return Assert.Single(commit.GetProperty("created").EnumerateArray());
    }

    private static object ActivationBody(string ticket, string password) => new { ticket, password };

    /// <summary>A strong password made at run time (never a literal in the repository).</summary>
    private static string NewPassword() => "Aa1!" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..TestNames.SuffixLength].ToUpperInvariant();
}
