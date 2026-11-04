using Microsoft.Extensions.Logging;

namespace Codelab.Observability;

/// <summary>How a failure is recorded: its machine code and the severity it deserves.</summary>
/// <param name="Code">The error code (for example <c>INTERNAL_ERROR</c>); never an exception message.</param>
/// <param name="Level">The log level the failure class deserves.</param>
public readonly record struct FailureCode(string Code, LogLevel Level);

/// <summary>
/// Turns an exception into a <see cref="FailureCode"/> for places that record a failure off the request thread (the
/// <see cref="JobFailureLogFilter"/>, a job-run row). Codelab.Errors replaces the default with its ordered
/// classifier table, so a failure has the same code in a job log as on the wire.
/// </summary>
public interface IFailureCodeResolver
{
    /// <summary>The code and level for <paramref name="exception"/>; a null exception is an internal error.</summary>
    FailureCode Resolve(Exception? exception);
}

/// <summary>The resolver used without Codelab.Errors: every failure is <see cref="InternalErrorCode"/> at Error.</summary>
public sealed class DefaultFailureCodeResolver : IFailureCodeResolver
{
    /// <summary>The code of an unclassified failure; equal to Codelab.Errors' <c>ErrorCodes.InternalError</c>.</summary>
    public const string InternalErrorCode = "INTERNAL_ERROR";

    /// <inheritdoc />
    public FailureCode Resolve(Exception? exception) => new(InternalErrorCode, LogLevel.Error);
}
