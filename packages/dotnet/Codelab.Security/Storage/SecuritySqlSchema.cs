using Microsoft.Data.SqlClient;

namespace Codelab.Security.Storage;

/// <summary>
/// The SQL Server schema of the SQL-backed stores: an idempotent script (embedded, and shipped in the package under
/// <c>sql/codelab-security-stores.sql</c> for the DBA) that creates the replay-nonce, session-version and revoked-session
/// tables when they are missing. No EF Core.
/// </summary>
public static class SecuritySqlSchema
{
    /// <summary>The embedded resource name of the script.</summary>
    public const string ScriptResourceName = "Codelab.Security.Sql.codelab-security-stores.sql";

    /// <summary>The replay nonce table.</summary>
    public const string ReplayNonceTable = "dbo.SecurityReplayNonce";

    /// <summary>The session version table.</summary>
    public const string SessionVersionTable = "dbo.SecuritySessionVersion";

    /// <summary>The revoked session table.</summary>
    public const string RevokedSessionTable = "dbo.SecurityRevokedSession";

    /// <summary>The idempotent script text.</summary>
    public static string ReadScript()
    {
        using var stream = typeof(SecuritySqlSchema).Assembly.GetManifestResourceStream(ScriptResourceName)
            ?? throw new InvalidOperationException($"The embedded script '{ScriptResourceName}' is missing from the package.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Runs the script against <paramref name="connectionString"/> (creates the tables when missing).</summary>
    public static async Task EnsureCreatedAsync(string connectionString, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(ReadScript(), connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>The SQL Server connection the SQL-backed stores share (<c>Codelab:Security:SqlStore</c>).</summary>
public sealed class SqlStoreOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = SecuritySections.SqlStore;

    /// <summary>The default command timeout for a single store call.</summary>
    public const int DefaultCommandTimeoutSeconds = 5;

    /// <summary>The connection string (from the environment or a secret store).</summary>
    public string? ConnectionString { get; set; }

    /// <summary>The command timeout for a single store call.</summary>
    public int CommandTimeoutSeconds { get; set; } = DefaultCommandTimeoutSeconds;

    /// <summary>Run the idempotent schema script when the host starts (default on).</summary>
    public bool EnsureSchemaOnStart { get; set; } = true;
}

/// <summary>A store that can delete its expired rows. The P5 jobs module resolves every registered purger and runs it.</summary>
public interface IExpiredEntryPurger
{
    /// <summary>A stable name for logs and the job catalog.</summary>
    string StoreName { get; }

    /// <summary>Deletes expired entries; returns how many were removed.</summary>
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken);
}

/// <summary>SQL Server error numbers the stores handle.</summary>
internal static class SqlErrorNumbers
{
    internal const int UniqueConstraintViolation = 2627;
    internal const int DuplicateKeyIndex = 2601;

    internal static bool IsDuplicateKey(SqlException exception) =>
        exception.Number is UniqueConstraintViolation or DuplicateKeyIndex;
}
