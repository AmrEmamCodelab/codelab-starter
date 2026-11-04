using System.Data;
using Codelab.Security.Storage;
using Microsoft.Data.SqlClient;

namespace Codelab.Security.Sessions;

/// <summary>Shared SQL plumbing of the session stores (one connection per call, the configured timeout).</summary>
public abstract class SqlSecurityStore
{
    /// <summary>The longest user key or session id the tables hold.</summary>
    public const int MaxKeyLength = 128;

    private readonly SqlStoreOptions _options;

    /// <summary>Creates the store.</summary>
    protected SqlSecurityStore(SqlStoreOptions options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString, nameof(options));
        ArgumentNullException.ThrowIfNull(clock);
        _options = options;
        Clock = clock;
    }

    /// <summary>The clock.</summary>
    protected TimeProvider Clock { get; }

    /// <summary>Runs <paramref name="sql"/> with parameters added by <paramref name="bind"/> and returns the scalar.</summary>
    protected async Task<object?> ScalarAsync(string sql, Action<SqlParameterCollection> bind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bind);
        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _options.CommandTimeoutSeconds };
        bind(command.Parameters);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    /// <summary>Adds a bounded key parameter.</summary>
    protected static void AddKey(SqlParameterCollection parameters, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, MaxKeyLength, nameof(value));
        parameters.Add(name, SqlDbType.NVarChar, MaxKeyLength).Value = value;
    }

    /// <summary>Adds a UTC timestamp parameter.</summary>
    protected static void AddTime(SqlParameterCollection parameters, string name, DateTimeOffset value)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Add(name, SqlDbType.DateTime2).Value = value.UtcDateTime;
    }
}

/// <summary>Session versions in <c>dbo.SecuritySessionVersion</c> (shared by every replica; the bump is one atomic MERGE).</summary>
public sealed class SqlSessionVersionStore(SqlStoreOptions options, TimeProvider clock) : SqlSecurityStore(options, clock), ISessionVersionStore
{
    private const string GetSql = "SELECT Version FROM " + SecuritySqlSchema.SessionVersionTable + " WHERE UserKey = @user;";

    private const string BumpSql =
        "MERGE " + SecuritySqlSchema.SessionVersionTable + " WITH (HOLDLOCK) AS target USING (SELECT @user AS UserKey) AS source ON target.UserKey = source.UserKey " +
        "WHEN MATCHED THEN UPDATE SET Version = target.Version + 1, UpdatedAtUtc = @now " +
        "WHEN NOT MATCHED THEN INSERT (UserKey, Version, UpdatedAtUtc) VALUES (@user, 1, @now) OUTPUT inserted.Version;";

    /// <inheritdoc />
    public async Task<long> GetVersionAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var value = await ScalarAsync(GetSql, parameters => AddKey(parameters, "@user", userId), cancellationToken);
        return value is long version ? version : 0;
    }

    /// <inheritdoc />
    public async Task<long> BumpAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var now = Clock.GetUtcNow();
        var value = await ScalarAsync(BumpSql, parameters =>
        {
            AddKey(parameters, "@user", userId);
            AddTime(parameters, "@now", now);
        }, cancellationToken);
        return (long)value!;
    }
}

/// <summary>Revoked sessions in <c>dbo.SecurityRevokedSession</c> (shared by every replica).</summary>
public sealed class SqlSessionRevocationStore(SqlStoreOptions options, TimeProvider clock) : SqlSecurityStore(options, clock), ISessionRevocationStore, IExpiredEntryPurger
{
    private const string RevokeSql =
        "MERGE " + SecuritySqlSchema.RevokedSessionTable + " WITH (HOLDLOCK) AS target USING (SELECT @sid AS SessionId) AS source ON target.SessionId = source.SessionId " +
        "WHEN MATCHED THEN UPDATE SET ExpiresAtUtc = CASE WHEN target.ExpiresAtUtc > @expires THEN target.ExpiresAtUtc ELSE @expires END " +
        "WHEN NOT MATCHED THEN INSERT (SessionId, ExpiresAtUtc, RevokedAtUtc) VALUES (@sid, @expires, @now);";

    private const string IsRevokedSql =
        "SELECT CASE WHEN EXISTS (SELECT 1 FROM " + SecuritySqlSchema.RevokedSessionTable + " WHERE SessionId = @sid AND ExpiresAtUtc > @now) THEN 1 ELSE 0 END;";

    private const string PurgeSql =
        "DELETE FROM " + SecuritySqlSchema.RevokedSessionTable + " WHERE ExpiresAtUtc <= @now; SELECT @@ROWCOUNT;";

    /// <inheritdoc />
    public string StoreName => nameof(SqlSessionRevocationStore);

    /// <inheritdoc />
    public async Task RevokeAsync(SessionRevocation revocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(revocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(revocation.SessionId, nameof(revocation));
        var now = Clock.GetUtcNow();
        await ScalarAsync(RevokeSql, parameters =>
        {
            AddKey(parameters, "@sid", revocation.SessionId);
            AddTime(parameters, "@expires", revocation.ExpiresAt);
            AddTime(parameters, "@now", now);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsRevokedAsync(string sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var now = Clock.GetUtcNow();
        var value = await ScalarAsync(IsRevokedSql, parameters =>
        {
            AddKey(parameters, "@sid", sessionId);
            AddTime(parameters, "@now", now);
        }, cancellationToken);
        return value is int flag && flag == 1;
    }

    /// <inheritdoc />
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        var now = Clock.GetUtcNow();
        var value = await ScalarAsync(PurgeSql, parameters => AddTime(parameters, "@now", now), cancellationToken);
        return value is int deleted ? deleted : 0;
    }
}
