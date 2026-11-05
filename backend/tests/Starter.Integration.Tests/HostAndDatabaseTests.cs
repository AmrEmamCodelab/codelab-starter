using System.Net;
using System.Text.Json;
using Codelab.Security.Storage;
using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests;

/// <summary>What the start did to the throwaway database, and the anonymous feature endpoint (Development host).</summary>
[Collection(DevelopmentHostGroup.Name)]
public sealed class HostAndDatabaseTests(DevelopmentHost host)
{
    private const string FlagsMember = "flags";
    private const string MigrationHistoryCount = "SELECT COUNT(*) FROM sys.tables WHERE name = '__EFMigrationsHistory'";
    private const string ModuleSchemaCount = "SELECT COUNT(*) FROM sys.schemas WHERE name IN ('identity', 'access')";

    [Fact]
    public async Task TheFixtureDatabaseExists() => Assert.True(await TestSqlServer.ExistsAsync(host.DatabaseName));

    [Fact]
    public async Task EveryModuleDbContextIsMigratedIntoItsOwnSchema()
    {
        Assert.Equal(2, await CountAsync(ModuleSchemaCount));
        Assert.Equal(2, await CountAsync(MigrationHistoryCount)); // one history table per module schema
    }

    [Theory]
    [InlineData(SecuritySqlSchema.ReplayNonceTable)]
    [InlineData(SecuritySqlSchema.SessionVersionTable)]
    [InlineData(SecuritySqlSchema.RevokedSessionTable)]
    public async Task TheSecurityStoreTablesExist(string table) =>
        Assert.True(await TestSqlServer.ScalarAsync(host.DatabaseName, $"SELECT OBJECT_ID(N'{table}')") is not (null or DBNull), $"{table} is missing");

    [Fact]
    public async Task FeaturesShowFlagsOnlyAndNoIdentities()
    {
        using var client = host.CreateClient();
        using var response = await client.GetAsync(StarterClient.Paths.Features, accessToken: null);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(text).RootElement;
        Assert.Equal([FlagsMember], root.EnumerateObject().Select(member => member.Name));
        Assert.All(root.GetProperty(FlagsMember).EnumerateObject(), flag => Assert.True(flag.Value.ValueKind is JsonValueKind.True or JsonValueKind.False));
        Assert.DoesNotContain(host.Accounts.BreakGlassUserName, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(host.Accounts.DevUserName, text, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> CountAsync(string sql) => Convert.ToInt32(await TestSqlServer.ScalarAsync(host.DatabaseName, sql), System.Globalization.CultureInfo.InvariantCulture);
}
