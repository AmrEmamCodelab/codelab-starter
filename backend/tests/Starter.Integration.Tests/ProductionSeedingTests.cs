using System.Net;
using Starter.Integration.Tests.Support;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests;

/// <summary>Contract section 6: the Development user is never seeded outside Development (Production host, own database).</summary>
[Collection(ProductionHostGroup.Name)]
public sealed class ProductionSeedingTests(ProductionHost host)
{
    private const string InvalidCredentialsCode = "IDENTITY_INVALID_CREDENTIALS";

    [Fact]
    public async Task TheDevUserIsAbsentInProduction()
    {
        using var client = host.CreateClient();

        using var response = await client.PostLoginAsync(host.Accounts.DevUserName, host.Accounts.DevUserPassword);

        Assert.Equal(InvalidCredentialsCode, await StarterProblems.AssertProblemAsync(response, HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task TheBreakGlassAccountIsSeededInProduction()
    {
        using var client = host.CreateClient();

        var session = await client.LoginAsync(host.Accounts.BreakGlassUserName, host.Accounts.BreakGlassPassword);

        Assert.False(string.IsNullOrEmpty(session.AccessToken));
    }
}
