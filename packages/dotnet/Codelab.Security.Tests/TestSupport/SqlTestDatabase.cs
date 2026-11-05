using System.Globalization;
using Codelab.Security.Storage;
using Microsoft.Data.SqlClient;

namespace Codelab.Security.Tests.TestSupport;

/// <summary>
/// A throwaway SQL Server database: created with a unique name for one test class and dropped when it is disposed.
/// The server comes from <c>CODELAB_TEST_SQL</c> through <see cref="SqlTestServer"/> (a server name or a connection
/// string; default <c>(localdb)\MSSQLLocalDB</c>, as for the integration tier). No server reachable: the test FAILS
/// with this message (never a silent skip).
/// </summary>
public sealed class SqlTestDatabase : IAsyncLifetime
{
    private const string NamePrefix = "CodelabSecurityTest_";
    private const string MasterDatabase = "master";
    private const int ConnectTimeoutSeconds = 5;

    private readonly string _serverConnectionString;

    public SqlTestDatabase()
    {
        var builder = SqlTestServer.FromEnvironment();
        builder.ConnectTimeout = ConnectTimeoutSeconds;
        builder.InitialCatalog = MasterDatabase;
        ServerName = builder.DataSource;
        _serverConnectionString = builder.ConnectionString;
        DatabaseName = NamePrefix + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        builder.InitialCatalog = DatabaseName;
        ConnectionString = builder.ConnectionString;
    }

    public string ServerName { get; }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    public async Task InitializeAsync()
    {
        try
        {
            await ExecuteOnServerAsync($"CREATE DATABASE [{DatabaseName}];");
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                $"The SQL store tests need a SQL Server. None was reachable at '{ServerName}' ({SqlTestServer.ServerVariable}, default {SqlTestServer.DefaultServer}). Set {SqlTestServer.ServerVariable} to a server name or a connection string for a server where this account may create databases. Cause: {ex.Message}",
                ex);
        }

        await SecuritySqlSchema.EnsureCreatedAsync(ConnectionString, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await ExecuteOnServerAsync($"IF DB_ID(N'{DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]; END;");
    }

    public SqlStoreOptions StoreOptions() => new() { ConnectionString = ConnectionString };

    public async Task<int> CountAsync(string table)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"SELECT COUNT(*) FROM {table};", connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new SqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
