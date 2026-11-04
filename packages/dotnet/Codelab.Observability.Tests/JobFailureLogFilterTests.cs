using System.Reflection;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;

namespace Codelab.Observability.Tests;

/// <summary>
/// A failed background job leaves one line on <see cref="JobFailureLogFilter.LoggerCategory"/>, grouped by the
/// exception type and the job's type and method, never its arguments or message, with the resolver's code and level.
/// </summary>
public sealed class JobFailureLogFilterTests
{
    [Fact]
    public void Job_failure_carries_the_fingerprint_of_type_and_job_method_and_never_the_arguments()
    {
        var loggers = new RecordingLoggerFactory();
        var filter = new JobFailureLogFilter(loggers, new DefaultFailureCodeResolver());

        filter.OnStateElection(FailedElection(jobId: "17", argument: "record for jane@example.test", new TimeoutException("first message")));
        filter.OnStateElection(FailedElection(jobId: "18", argument: "another record", new TimeoutException("second message")));

        Assert.Equal(2, loggers.Entries.Count);
        Assert.All(loggers.Entries, e => Assert.Equal(JobFailureLogFilter.LoggerCategory, e.Category));
        Assert.All(loggers.Entries, e => Assert.StartsWith("Job failed ", e.Message, StringComparison.Ordinal));
        var expected = FailureFingerprint.For(new FingerprintInput(
            typeof(TimeoutException), $"job:{nameof(SampleJob)}.{nameof(SampleJob.Run)}"));
        Assert.All(loggers.Entries, e => Assert.Equal(expected, e.Properties[LogPropertyNames.ErrorFingerprint]));
        Assert.All(loggers.Entries, e => Assert.Equal(typeof(TimeoutException).FullName, e.Properties[LogPropertyNames.ExceptionType]));
        Assert.All(loggers.Entries, e => Assert.Equal(DefaultFailureCodeResolver.InternalErrorCode, e.Properties[LogPropertyNames.Code]));
        Assert.DoesNotContain(loggers.Entries, e => e.Message.Contains("jane@example.test", StringComparison.Ordinal));
    }

    [Fact]
    public void The_resolver_decides_code_and_level()
    {
        var loggers = new RecordingLoggerFactory();
        var filter = new JobFailureLogFilter(loggers, new FixedResolver(new FailureCode("RETRYABLE", LogLevel.Warning)));

        filter.OnStateElection(FailedElection(jobId: "19", argument: "x", new TimeoutException()));

        var entry = Assert.Single(loggers.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("RETRYABLE", entry.Properties[LogPropertyNames.Code]);
        Assert.Equal("19", entry.Properties[LogPropertyNames.JobId]);
        Assert.Equal(nameof(SampleJob.Run), entry.Properties[LogPropertyNames.Job]);
    }

    [Fact]
    public void A_state_other_than_failed_is_ignored()
    {
        var loggers = new RecordingLoggerFactory();
        var filter = new JobFailureLogFilter(loggers, new DefaultFailureCodeResolver());
        var context = FailedElection(jobId: "20", argument: "x", new TimeoutException());
        context.CandidateState = new SucceededState(result: null, latency: 0, performanceDuration: 0);

        filter.OnStateElection(context);

        Assert.Empty(loggers.Entries);
    }

    private static ElectStateContext FailedElection(string jobId, string argument, Exception exception)
    {
        var method = typeof(SampleJob).GetMethod(nameof(SampleJob.Run))!;
        var job = new BackgroundJob(jobId, new Job(typeof(SampleJob), method, argument), TimeProvider.System.GetUtcNow().UtcDateTime);
        var apply = new ApplyStateContext(
            new NullJobStorage(), NullProxy.Create<IStorageConnection>(), NullProxy.Create<IWriteOnlyTransaction>(),
            job, new FailedState(exception), oldStateName: ProcessingState.StateName);
        return new ElectStateContext(apply);
    }

    public sealed class SampleJob
    {
        public static void Run(string recordReference) => ArgumentNullException.ThrowIfNull(recordReference);
    }

    private sealed class FixedResolver(FailureCode code) : IFailureCodeResolver
    {
        public FailureCode Resolve(Exception? exception) => code;
    }

    private sealed class NullJobStorage : JobStorage
    {
        public override IMonitoringApi GetMonitoringApi() => throw new NotSupportedException();

        public override IStorageConnection GetConnection() => NullProxy.Create<IStorageConnection>();
    }

    /// <summary>A storage stand-in answering every call with its type's default: no parameter, no row.</summary>
    public class NullProxy : DispatchProxy
    {
        public static T Create<T>()
            where T : class => DispatchProxy.Create<T, NullProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.ReturnType is { IsValueType: true } valueType && valueType != typeof(void)
                ? Activator.CreateInstance(valueType)
                : null;
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public List<(string Category, LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Properties)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

        public void Dispose()
        {
            Entries.Clear();
        }

        private sealed class Logger(RecordingLoggerFactory owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Entries.Add((category, logLevel, formatter(state, exception),
                    (state as IEnumerable<KeyValuePair<string, object?>> ?? []).ToDictionary(p => p.Key, p => p.Value)));
        }
    }
}
