using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace Starter.Integration.Tests.Support;

/// <summary>
/// The SQL Server the integration tests use: LocalDB <c>(localdb)\MSSQLLocalDB</c> unless the
/// <see cref="ServerVariable"/> environment variable names another one. Creates and drops only databases whose name
/// starts with the test prefix; it never touches any other database or the server itself.
/// </summary>
public static class TestSqlServer
{
    /// <summary>The environment variable that overrides the server.</summary>
    public const string ServerVariable = "CODELAB_TEST_SQL";

    /// <summary>The default server.</summary>
    public const string DefaultServer = @"(localdb)\MSSQLLocalDB";

    /// <summary>The LocalDB server prefix; such a server is started with sqllocaldb when it is stopped.</summary>
    public const string LocalDbPrefix = @"(localdb)\";

    /// <summary>The marker every test database name carries after the solution token (<c>StarterIt_</c>).</summary>
    public const string DatabaseMarker = "It_";

    private const char ConnectionStringSeparator = '=';
    private const string MasterDatabase = "master";
    private const string LocalDbTool = "sqllocaldb";
    private const string LocalDbStartVerb = "start";
    private const int LocalDbStartTimeoutMilliseconds = 60_000;

    /// <summary>
    /// The server in use. <see cref="ServerVariable"/> may hold a server name or a connection string (the form the
    /// package SQL tests read); for a connection string its data source is used.
    /// </summary>
    public static string Server => ServerFrom(Environment.GetEnvironmentVariable(ServerVariable));

    /// <summary>The server named by <paramref name="value"/>: a server name, a connection string, or empty for the default.</summary>
    public static string ServerFrom(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultServer;
        }

        return value.Contains(ConnectionStringSeparator, StringComparison.Ordinal)
            ? new SqlConnectionStringBuilder(value).DataSource
            : value;
    }

    /// <summary>The connection string of <paramref name="database"/> on <see cref="Server"/>.</summary>
    public static string ConnectionStringFor(string database) =>
        new SqlConnectionStringBuilder
        {
            DataSource = Server,
            InitialCatalog = database,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            MultipleActiveResultSets = false,
        }.ConnectionString;

    /// <summary>Starts the LocalDB instance when the server is LocalDB (a no-op when it already runs).</summary>
    public static async Task EnsureStartedAsync()
    {
        if (!Server.StartsWith(LocalDbPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var instance = Server[LocalDbPrefix.Length..];
        using var process = Process.Start(new ProcessStartInfo(LocalDbTool, [LocalDbStartVerb, instance])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException($"Could not run {LocalDbTool}.");
        using var timeout = new CancellationTokenSource(LocalDbStartTimeoutMilliseconds);
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{LocalDbTool} {LocalDbStartVerb} {instance} failed: {await process.StandardError.ReadToEndAsync()}");
        }
    }

    /// <summary>Creates an empty database.</summary>
    public static Task CreateDatabaseAsync(string database) =>
        ExecuteOnMasterAsync($"CREATE DATABASE {Quote(RequireTestName(database))}");

    /// <summary>Drops the database (closing every connection to it first). A missing database is not an error.</summary>
    public static async Task DropDatabaseAsync(string database)
    {
        var name = RequireTestName(database);
        SqlConnection.ClearAllPools();
        await ExecuteOnMasterAsync(
            $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE {Quote(name)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {Quote(name)}; END");
    }

    /// <summary>True when the database exists.</summary>
    public static async Task<bool> ExistsAsync(string database)
    {
        await using var connection = new SqlConnection(ConnectionStringFor(MasterDatabase));
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT DB_ID(@name)", connection);
        command.Parameters.AddWithValue("@name", RequireTestName(database));
        return await command.ExecuteScalarAsync() is not (null or DBNull);
    }

    /// <summary>Runs a scalar query against <paramref name="database"/>.</summary>
    public static async Task<object?> ScalarAsync(string database, string sql)
    {
        await using var connection = new SqlConnection(ConnectionStringFor(RequireTestName(database)));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    /// <summary>Runs a parameterized scalar query against <paramref name="database"/> (parameter names include the <c>@</c>).</summary>
    public static async Task<object?> ScalarAsync(string database, string sql, IReadOnlyDictionary<string, object> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        await using var connection = new SqlConnection(ConnectionStringFor(RequireTestName(database)));
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteScalarAsync();
    }

    /// <summary>Reads the first column of every row of a parameterized query against <paramref name="database"/> as text.</summary>
    public static async Task<IReadOnlyList<string>> ReadStringsAsync(string database, string sql, IReadOnlyDictionary<string, object> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        await using var connection = new SqlConnection(ConnectionStringFor(RequireTestName(database)));
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetValue(0).ToString() ?? string.Empty);
        }

        return values;
    }

    /// <summary>Runs a parameterized statement against <paramref name="database"/>; returns the affected row count.</summary>
    public static async Task<int> ExecuteAsync(string database, string sql, IReadOnlyDictionary<string, object> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        await using var connection = new SqlConnection(ConnectionStringFor(RequireTestName(database)));
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>Refuses any name that is not a test database name (letters, digits and underscores, with the marker).</summary>
    public static string RequireTestName(string database)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        var valid = database.Contains(DatabaseMarker, StringComparison.Ordinal) && database.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
        return valid ? database : throw new ArgumentException($"'{database}' is not an integration test database name.", nameof(database));
    }

    private static string Quote(string name) => $"[{name}]";

    private static SqlCommand Command(SqlConnection connection, string sql, IReadOnlyDictionary<string, object> parameters)
    {
        var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }

    private static async Task ExecuteOnMasterAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionStringFor(MasterDatabase));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
