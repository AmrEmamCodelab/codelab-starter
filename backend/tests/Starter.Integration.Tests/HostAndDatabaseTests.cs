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

    /// <summary>
    /// The migrated schemas: the modules (identity, access, users, lookups, settings, jobs, notifications, portal, attachments) and the
    /// kernel contexts (messaging: the outbox; numbering: request number sequences; audit: the audit trail). Each has its
    /// own migration history. The Audit module has no schema of its own (it reads the kernel trail), and the jobs
    /// scheduler's schema (hangfire) is not an EF context and exists only on a job-server node.
    /// </summary>
    private static readonly string[] MigratedSchemas =
        ["identity", "access", "users", "lookups", "settings", "jobs", "notifications", "portal", "attachments", "messaging", "numbering", "audit"];

    private static readonly string ModuleSchemaCount =
        $"SELECT COUNT(*) FROM sys.schemas WHERE name IN ({string.Join(", ", MigratedSchemas.Select(schema => $"'{schema}'"))})";

    [Fact]
    public async Task TheFixtureDatabaseExists() => Assert.True(await TestSqlServer.ExistsAsync(host.DatabaseName));

    [Fact]
    public async Task EveryModuleDbContextIsMigratedIntoItsOwnSchema()
    {
        Assert.Equal(MigratedSchemas.Length, await CountAsync(ModuleSchemaCount));
        Assert.Equal(MigratedSchemas.Length, await CountAsync(MigrationHistoryCount)); // one history table per migrated schema
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
