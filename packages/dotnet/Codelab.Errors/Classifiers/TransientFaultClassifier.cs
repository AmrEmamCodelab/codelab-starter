using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Codelab.Errors.Classifiers;

/// <summary>
/// 503 <see cref="ErrorCodes.Retryable"/> with <c>Retry-After</c> for transient contention: a deadlock victim, a
/// command timeout, throttling or a database server that cannot be reached right now. A 500 is a dead end; "retry"
/// is a real answer. A primary-key violation or a refused login stays unclassified (500), which is correct.
/// <para>
/// The <see cref="SqlException"/> is searched down the inner-exception chain: without a retrying execution strategy,
/// EF Core wraps a transient error in an <see cref="InvalidOperationException"/> over a <c>DbUpdateException</c>.
/// </para>
/// </summary>
public sealed class TransientFaultClassifier : IFailureClassifier
{
    /// <summary>The <c>Retry-After</c> value, in seconds.</summary>
    public const int RetryAfterSeconds = 5;

    /// <summary>Most inner exceptions followed looking for the SQL error; real chains are three deep.</summary>
    public const int MaxChainDepth = 8;

    internal const string Detail = "The system was busy. Please try again in a moment.";

    /// <summary>
    /// SQL Server error numbers worth retrying: 1205 deadlock victim, -2 command timeout, the throttling and transient
    /// cloud faults, and the connection failures of a server that cannot be reached (-1, 2, 53, 64, 121, 233, 10053,
    /// 10054, 10060, 10061, 11001). A refused login (18456) or an unopenable database (4060) is a deployment fault.
    /// </summary>
    public static readonly IReadOnlySet<int> TransientSqlErrorNumbers = new HashSet<int>
    {
        1205, -2, 40501, 40197, 40613, 49918, 49919, 49920, 10928, 10929,
        -1, 2, 53, 64, 121, 233, 10053, 10054, 10060, 10061, 11001,
    };

    /// <inheritdoc />
    public Failure? Classify(Exception exception, HttpContext? context)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var transient = exception is TimeoutException
            || (SqlErrorIn(exception) is { } sql && TransientSqlErrorNumbers.Contains(sql.Number));
        return transient
            ? new Failure(StatusCodes.Status503ServiceUnavailable, ErrorCodes.Retryable, Detail, LogLevel.Warning, RetryAfterSeconds)
            : null;
    }

    private static SqlException? SqlErrorIn(Exception exception)
    {
        var current = exception;
        for (var depth = 0; current is not null && depth < MaxChainDepth; depth++, current = current.InnerException)
        {
            if (current is SqlException sql)
            {
                return sql;
            }
        }

        return null;
    }
}
