using System.Net;
using System.Text.Json;
using Codelab.Security.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Starter.Infrastructure.Auditing;
using Starter.Infrastructure.Messaging;
using Starter.Infrastructure.Numbering;
using Starter.Infrastructure.Persistence;
using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests;

/// <summary>What the start did to the throwaway database, and the anonymous feature endpoint (Development host).</summary>
[Collection(DevelopmentHostGroup.Name)]
public sealed class HostAndDatabaseTests(DevelopmentHost host)
{
    private const string FlagsMember = "flags";
    private const string MigrationHistoryCount = "SELECT COUNT(*) FROM sys.tables WHERE name = '__EFMigrationsHistory'";

    /// <summary>
    /// The kernel contexts (messaging: the outbox; numbering: request number sequences; audit: the audit trail) ship
    /// with every fork, so the schemas derived from the host must always include them. The module schemas are not
    /// listed: a removed module (remove-module) takes its context with it, and the expected set shrinks with it.
    /// </summary>
    private static readonly string[] KernelSchemas = [OutboxDbContext.SchemaName, NumberingDbContext.SchemaName, AuditDbContext.SchemaName];

    [Fact]
    public async Task TheFixtureDatabaseExists() => Assert.True(await TestSqlServer.ExistsAsync(host.DatabaseName));

    /// <summary>
    /// Every DbContext the host announces to the startup runner (<see cref="IModuleDatabase"/>) is migrated into its own
    /// schema with its own migration history. The Audit module has no schema of its own (it reads the kernel trail), and
    /// the jobs scheduler's schema (hangfire) is not an EF context and exists only on a job-server node.
    /// </summary>
    [Fact]
    public async Task EveryModuleDbContextIsMigratedIntoItsOwnSchema()
    {
        var schemas = RegisteredSchemas();
        var schemaList = string.Join(", ", schemas.Select(schema => $"'{schema}'"));

        Assert.Equal(schemas.Count, schemas.Distinct(StringComparer.OrdinalIgnoreCase).Count()); // one schema per context
        Assert.Equal(schemas.Count, await CountAsync($"SELECT COUNT(*) FROM sys.schemas WHERE name IN ({schemaList})"));
        Assert.Equal(schemas.Count, await CountAsync(MigrationHistoryCount)); // one history table per migrated schema
    }

    [Fact]
    public void TheRegisteredSchemasIncludeTheKernelContexts()
    {
        var schemas = RegisteredSchemas();
        Assert.All(KernelSchemas, kernelSchema => Assert.Contains(kernelSchema, schemas));
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

    private List<string> RegisteredSchemas()
    {
        using var scope = host.Factory.Services.CreateScope();
        return [.. scope.ServiceProvider.GetServices<IModuleDatabase>().Select(database => SchemaOf(scope.ServiceProvider, database))];
    }

    private static string SchemaOf(IServiceProvider services, IModuleDatabase database)
    {
        var databaseType = database.GetType();
        Assert.True(
            databaseType.IsGenericType && databaseType.GetGenericTypeDefinition() == typeof(ModuleDatabase<>),
            $"{databaseType} is not a {typeof(ModuleDatabase<>).Name}");
        var context = (DbContext)services.GetRequiredService(databaseType.GetGenericArguments()[0]);
        return context.Model.GetDefaultSchema() ?? throw new InvalidOperationException($"{database.Name} has no default schema.");
    }

    private async Task<int> CountAsync(string sql) => Convert.ToInt32(await TestSqlServer.ScalarAsync(host.DatabaseName, sql), System.Globalization.CultureInfo.InvariantCulture);
}
