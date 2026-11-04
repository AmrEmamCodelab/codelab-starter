using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging;

namespace Codelab.Observability;

/// <summary>
/// Gives a failed background job the same one-line structured failure log the HTTP exception handler writes: the
/// job id (what an operator quotes), the code from <see cref="IFailureCodeResolver"/>, the job's method name, the
/// retry count, the <see cref="FailureFingerprint"/> and the exception type. Never the job's arguments, which can
/// carry personal data. It observes and does not handle: the job still fails and the scheduler still retries.
/// <para>
/// Registered in DI by <c>AddCodelabObservability</c>; the host adds it to its scheduler configuration, for example
/// <c>services.AddHangfire((sp, config) =&gt; config.UseFilter(sp.GetRequiredService&lt;JobFailureLogFilter&gt;()))</c>.
/// </para>
/// </summary>
public sealed class JobFailureLogFilter : JobFilterAttribute, IElectStateFilter
{
    /// <summary>The logger category of every background-failure line, so one source filter finds them all.</summary>
    public const string LoggerCategory = "Codelab.Jobs";

    /// <summary>The job parameter that holds the retry count.</summary>
    public const string RetryCountParameter = "RetryCount";

    /// <summary>The job name logged when the job's method could not be loaded.</summary>
    public const string UnknownJobName = "unknown";

    private const string Template = "Job failed {JobId} {Code} {Job} {Retry} {ErrorFingerprint} {ExceptionType}";

    private readonly ILogger _logger;
    private readonly IFailureCodeResolver _codes;

    /// <summary>Creates the filter.</summary>
    public JobFailureLogFilter(ILoggerFactory loggerFactory, IFailureCodeResolver codes)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(codes);

        _logger = loggerFactory.CreateLogger(LoggerCategory);
        _codes = codes;
    }

    /// <inheritdoc />
    public void OnStateElection(ElectStateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.CandidateState is not FailedState failed)
        {
            return;
        }

        var exception = failed.Exception;
        var failure = _codes.Resolve(exception);
        var job = context.BackgroundJob.Job;
        var site = job is null ? FailureFingerprint.UnknownJobSite : FailureFingerprint.JobSite(job.Type, job.Method.Name);
        var exceptionType = exception?.GetType() ?? typeof(Exception);

        var state = new StructuredLogState(Template,
        [
            KeyValuePair.Create(LogPropertyNames.JobId, (object?)context.BackgroundJob.Id),
            KeyValuePair.Create(LogPropertyNames.Code, (object?)failure.Code),
            KeyValuePair.Create(LogPropertyNames.Job, (object?)(job?.Method.Name ?? UnknownJobName)),
            KeyValuePair.Create(LogPropertyNames.Retry, (object?)context.GetJobParameter<int>(RetryCountParameter)),
            KeyValuePair.Create(LogPropertyNames.ErrorFingerprint, (object?)FailureFingerprint.For(new FingerprintInput(exceptionType, site))),
            KeyValuePair.Create(LogPropertyNames.ExceptionType, (object?)exceptionType.FullName),
        ]);
        _logger.Log(failure.Level, default, state, exception, StructuredLogState.Format);
    }
}
