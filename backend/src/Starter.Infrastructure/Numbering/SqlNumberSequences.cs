using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Starter.Infrastructure.Numbering;

/// <summary>Named counters that never hand out a value twice.</summary>
public interface INumberSequences
{
    /// <summary>
    /// The next value (1, 2, ...) of the sequence <paramref name="sequenceName"/>, created on first use. Not part of any
    /// transaction: a value is consumed even when the caller rolls back.
    /// </summary>
    /// <param name="sequenceName">Letters, digits and underscores only (<see cref="RequestNumberAllocator.SequenceName"/>).</param>
    /// <param name="cancellationToken">Cancels the call (a value already drawn stays consumed).</param>
    Task<long> NextValueAsync(string sequenceName, CancellationToken cancellationToken);
}

/// <summary>
/// The <see cref="INumberSequences"/> on SQL Server: one <c>SEQUENCE</c> per name in schema <c>numbering</c>, created on
/// demand by the same batch that draws from it. Two first callers can race to create it; the loser's
/// "object already exists" error (2714) is swallowed in SQL and both draw distinct values from the one sequence.
/// </summary>
/// <remarks>
/// The sequences are <c>NO CACHE</c>, so a restart does not skip values; a rolled-back caller still leaves a gap. The
/// application login needs <c>CREATE SEQUENCE</c> on the database and <c>ALTER</c> on schema <c>numbering</c> (see the
/// Infrastructure MODULE.md).
/// </remarks>
public sealed class SqlNumberSequences(NumberingDbContext context) : INumberSequences
{
    /// <summary>The longest sequence name accepted.</summary>
    public const int MaxNameLength = 64;

    private const string NameParameter = "@name";
    private const string SchemaParameter = "@schema";

    private const string NextValueSql = """
        DECLARE @qualified nvarchar(300) = QUOTENAME(@schema) + N'.' + QUOTENAME(@name);
        IF OBJECT_ID(@qualified, N'SO') IS NULL
        BEGIN
            BEGIN TRY
                EXEC (N'CREATE SEQUENCE ' + @qualified + N' AS bigint START WITH 1 INCREMENT BY 1 MINVALUE 1 NO CYCLE NO CACHE;');
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() <> 2714 THROW;
            END CATCH
        END;
        DECLARE @next bigint;
        DECLARE @statement nvarchar(400) = N'SELECT @value = NEXT VALUE FOR ' + @qualified + N';';
        EXEC sys.sp_executesql @statement, N'@value bigint OUTPUT', @value = @next OUTPUT;
        SELECT @next;
        """;

    /// <inheritdoc />
    public async Task<long> NextValueAsync(string sequenceName, CancellationToken cancellationToken)
    {
        EnsureSafeName(sequenceName);
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = NextValueSql;
        command.Parameters.Add(new SqlParameter(SchemaParameter, SqlDbType.NVarChar, MaxNameLength) { Value = NumberingDbContext.SchemaName });
        command.Parameters.Add(new SqlParameter(NameParameter, SqlDbType.NVarChar, MaxNameLength) { Value = sequenceName });
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>Refuses a name that is not letters, digits and underscores (defence in depth; it is also quoted).</summary>
    /// <exception cref="ArgumentException">The name is blank, too long or has another character.</exception>
    public static void EnsureSafeName(string sequenceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceName);
        if (sequenceName.Length > MaxNameLength || !sequenceName.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            throw new ArgumentException($"A sequence name is at most {MaxNameLength} ASCII letters, digits and underscores.", nameof(sequenceName));
        }
    }
}
