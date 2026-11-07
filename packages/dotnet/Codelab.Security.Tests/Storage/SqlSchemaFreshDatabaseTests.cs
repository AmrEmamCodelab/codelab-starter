using System.Globalization;
using Codelab.Security.Storage;
using Codelab.Security.Tests.TestSupport;
using Microsoft.Data.SqlClient;

namespace Codelab.Security.Tests.Storage;

/// <summary>
/// A host on a database that did not exist when it was built: another component (the job scheduler's storage, for
/// one) tried the application connection string first and failed with "cannot open database", which puts SqlClient's
/// pool for that string into its blocking period. The database is then created (by the migrations) and the schema
/// step must still succeed instead of re-throwing the cached login failure.
/// </summary>
public sealed class SqlSchemaFreshDatabaseTests : IAsyncLifetime
{
    private const string NamePrefix = "CodelabSecurityFresh_";
    private const string MasterDatabase = "master";
    private const int ConnectTimeoutSeconds = 5;
    private const int CannotOpenDatabase = 4060;

    private readonly string _serverConnectionString;
    private readonly string _databaseName;
    private readonly string _connectionString;

    public SqlSchemaFreshDatabaseTests()
    {
        var builder = SqlTestServer.FromEnvironment();
        builder.ConnectTimeout = ConnectTimeoutSeconds;
        builder.InitialCatalog = MasterDatabase;
        _serverConnectionString = builder.ConnectionString;
        _databaseName = NamePrefix + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        builder.InitialCatalog = _databaseName;
        _connectionString = builder.ConnectionString;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await ExecuteOnServerAsync($"IF DB_ID(N'{_databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]; END;");
    }

    [Fact]
    public async Task The_schema_is_created_after_an_earlier_open_of_the_missing_database_failed()
    {
        var earlyOpen = await Assert.ThrowsAsync<SqlException>(() => OpenAndCloseAsync(_connectionString));
        Assert.Equal(CannotOpenDatabase, earlyOpen.Number);
        await ExecuteOnServerAsync($"CREATE DATABASE [{_databaseName}];");

        await SecuritySqlSchema.EnsureCreatedAsync(_connectionString, CancellationToken.None);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"SELECT COUNT(*) FROM {SecuritySqlSchema.ReplayNonceTable};", connection);
        Assert.Equal(0, (int)(await command.ExecuteScalarAsync())!);
    }

    private static async Task OpenAndCloseAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
    }

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new SqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
