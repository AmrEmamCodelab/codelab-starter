using System.Globalization;
using Codelab.Security.Storage;
using Microsoft.Data.SqlClient;

namespace Codelab.Security.Tests.TestSupport;

/// <summary>
/// A throwaway SQL Server database: created with a unique name for one test class and dropped when it is disposed.
/// The server comes from <c>CODELAB_TEST_SQL</c> (a connection string), default the local default instance with
/// Windows authentication. No server reachable: the test FAILS with this message (never a silent skip).
/// </summary>
public sealed class SqlTestDatabase : IAsyncLifetime
{
    public const string ServerVariable = "CODELAB_TEST_SQL";
    public const string DefaultServer = "Server=.;Integrated Security=true;TrustServerCertificate=true;Encrypt=true";

    private const string NamePrefix = "CodelabSecurityTest_";
    private const int ConnectTimeoutSeconds = 5;

    private readonly string _serverConnectionString;

    public SqlTestDatabase()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(ServerVariable) is { Length: > 0 } configured ? configured : DefaultServer)
        {
            ConnectTimeout = ConnectTimeoutSeconds,
            InitialCatalog = "master",
        };
        _serverConnectionString = builder.ConnectionString;
        DatabaseName = NamePrefix + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        builder.InitialCatalog = DatabaseName;
        ConnectionString = builder.ConnectionString;
    }

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
                $"The SQL store tests need a SQL Server. None was reachable with {ServerVariable} or the default local instance ({DefaultServer}). Set {ServerVariable} to a connection string for a server where this account may create databases. Cause: {ex.Message}",
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
