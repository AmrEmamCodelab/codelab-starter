using Microsoft.Extensions.Logging;

namespace Codelab.Observability.Tests.Support;

/// <summary>The log calls the tests make, source-generated like production code (CA1848).</summary>
public static partial class TestLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Probe")]
    public static partial void Probe(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Below the configured level")]
    public static partial void BelowLevel(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unsafe for retained sinks")]
    public static partial void Unsafe(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Save failed")]
    public static partial void SaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Suppressed by override")]
    public static partial void Suppressed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Admitted by default")]
    public static partial void Admitted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applicant {@Applicant} for {RecordId}")]
    public static partial void Applicant(ILogger logger, object applicant, string recordId);
}
