using Codelab.Security.Tests.TestSupport;

namespace Codelab.Security.Tests.Storage;

/// <summary>
/// CODELAB_TEST_SQL is shared with the integration tier: a server name and a connection string must both work, and the
/// default must match that tier's default.
/// </summary>
public sealed class SqlTestServerTests
{
    private const string NamedLocalDb = @"(localdb)\P2Proof";

    [Fact]
    public void A_server_name_is_accepted_with_windows_authentication()
    {
        var builder = SqlTestServer.From(NamedLocalDb);

        Assert.Equal(NamedLocalDb, builder.DataSource);
        Assert.True(builder.IntegratedSecurity);
        Assert.True(builder.TrustServerCertificate);
    }

    [Fact]
    public void A_connection_string_is_used_as_given()
    {
        var builder = SqlTestServer.From($"Server={NamedLocalDb};Integrated Security=true;TrustServerCertificate=true");

        Assert.Equal(NamedLocalDb, builder.DataSource);
        Assert.True(builder.IntegratedSecurity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unset_value_falls_back_to_the_integration_tier_default(string? value)
    {
        Assert.Equal(@"(localdb)\MSSQLLocalDB", SqlTestServer.DefaultServer);
        Assert.Equal(SqlTestServer.DefaultServer, SqlTestServer.From(value).DataSource);
    }

    [Fact]
    public void Surrounding_whitespace_in_a_server_name_is_ignored()
    {
        Assert.Equal(NamedLocalDb, SqlTestServer.From($" {NamedLocalDb} ").DataSource);
    }
}
