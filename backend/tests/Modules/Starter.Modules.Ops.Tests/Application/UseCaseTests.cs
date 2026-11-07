using Microsoft.Extensions.Time.Testing;
using Starter.Application.Jobs;
using Starter.Application.Results;
using Starter.Modules.Ops.Application;
using Starter.Modules.Ops.Application.Access;
using Starter.Modules.Ops.Application.Certificates;
using Starter.Modules.Ops.Application.Health;
using Starter.Modules.Ops.Application.Jobs;
using Starter.Modules.Ops.Application.ReferenceCodes;
using Starter.Modules.Ops.Domain;
using Starter.Modules.Ops.Domain.Certificates;
using Starter.Modules.Ops.Domain.Health;
using Starter.Modules.Ops.Tests.Fakes;

namespace Starter.Modules.Ops.Tests.Application;

public sealed class GetSystemHealthTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Every_component_is_reported_by_name_and_the_worst_is_the_system_status()
    {
        IComponentProbe[] probes =
        [
            new FixedProbe(new ComponentCheck("integration-gateway", ComponentStatus.Unhealthy, 5000, OpsFindingCodes.ProbeTimeout)),
            new FixedProbe(new ComponentCheck("database", ComponentStatus.Healthy, 3, null)),
            new FixedProbe(new ComponentCheck("attachment-service", ComponentStatus.Degraded, 2600, OpsFindingCodes.ProbeSlow)),
        ];

        var health = (await new GetSystemHealth(probes, _clock).ExecuteAsync(CancellationToken.None)).Value;

        Assert.Equal(ComponentStatus.Unhealthy, health.Status);
        Assert.Equal(_clock.GetUtcNow(), health.CheckedAt);
        Assert.Equal(["attachment-service", "database", "integration-gateway"], health.Components.Select(check => check.Name));
    }

    [Fact]
    public async Task All_healthy_is_healthy()
    {
        var health = (await new GetSystemHealth([new FixedProbe(new ComponentCheck("database", ComponentStatus.Healthy, 1, null))], _clock)
            .ExecuteAsync(CancellationToken.None)).Value;

        Assert.Equal(ComponentStatus.Healthy, health.Status);
    }
}

public sealed class ListJobStatusesTests
{
    [Fact]
    public async Task Jobs_are_ordered_by_key_with_their_newest_run()
    {
        var run = new JobLastRun(JobRunStatus.Failed, JobTrigger.Schedule, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "9C1E5-B7A20");
        var source = new FixedJobs(
            new JobStatus("security-replay-purge", "ar", "en", "0 2 * * *", LastRun: null),
            new JobStatus("outbox-drain", "ar", "en", "*/5 * * * *", run));

        var jobs = (await new ListJobStatuses(source).ExecuteAsync(CancellationToken.None)).Value;

        Assert.Equal(["outbox-drain", "security-replay-purge"], jobs.Select(job => job.Key));
        Assert.Equal(run, jobs[0].LastRun);
    }
}

public sealed class ListCertificatesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Certificates_are_judged_and_the_most_urgent_comes_first()
    {
        var sources = new ICertificateSource[]
        {
            new FixedCertificates(
                new CertificateFact("api-tls", "CN=api", Now.AddDays(200), null),
                new CertificateFact("signing", "CN=sign", Now.AddDays(10), null)),
            new FixedCertificates(
                new CertificateFact("idp-signing-1", "CN=idp", Now.AddDays(-1), null),
                new CertificateFact("missing", null, null, OpsFindingCodes.CertificateUnreadable),
                new CertificateFact("soon", "CN=soon", Now.AddDays(3), null)),
        };

        var reports = (await new ListCertificates(sources, TestSettings.Default, new FakeTimeProvider(Now)).ExecuteAsync(CancellationToken.None)).Value;

        Assert.Equal(["idp-signing-1", "missing", "soon", "signing", "api-tls"], reports.Select(report => report.Name));
        Assert.Equal(
            [CertificateStatus.Expired, CertificateStatus.Critical, CertificateStatus.Critical, CertificateStatus.Warning, CertificateStatus.Ok],
            reports.Select(report => report.Status));
        var missing = reports.Single(report => report.Name == "missing");
        Assert.Null(missing.DaysLeft);
        Assert.Equal(OpsFindingCodes.CertificateUnreadable, missing.FindingCode);
        Assert.Equal(200, reports.Single(report => report.Name == "api-tls").DaysLeft);
    }

    [Fact]
    public async Task No_source_is_an_empty_list() =>
        Assert.Empty((await new ListCertificates([], TestSettings.Default, new FakeTimeProvider(Now)).ExecuteAsync(CancellationToken.None)).Value);
}

public sealed class FindReferenceCodeTests
{
    private const string CorrelationId = "9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b";
    private static readonly DateTimeOffset At = new(2026, 11, 7, 9, 30, 0, TimeSpan.Zero);
    private readonly FakeSightings _sightings = new();

    [Theory]
    [InlineData(null)]
    [InlineData("nope")]
    [InlineData("9C1E5-B7A2Z")]
    public async Task A_malformed_code_is_400_invalid(string? code)
    {
        var outcome = await new FindReferenceCode(_sightings, TestSettings.Default).ExecuteAsync(code, CancellationToken.None);

        Assert.Equal(RefusalCategory.Invalid, outcome.Refusal!.Category);
        Assert.Equal(OpsErrorCodes.ReferenceCodeInvalid, outcome.Refusal.Code);
    }

    [Fact]
    public async Task An_unknown_code_is_404()
    {
        var outcome = await new FindReferenceCode(_sightings, TestSettings.Default).ExecuteAsync("AAAAA-BBBBB", CancellationToken.None);

        Assert.Equal(RefusalCategory.NotFound, outcome.Refusal!.Category);
        Assert.Equal(OpsErrorCodes.ReferenceCodeNotFound, outcome.Refusal.Code);
    }

    [Fact]
    public async Task A_known_code_in_any_case_answers_the_request_and_the_search_window()
    {
        _sightings.Stored.Add(Sightings.New(CorrelationId, At, status: 409, path: "/api/records/x"));

        var trace = (await new FindReferenceCode(_sightings, TestSettings.Default).ExecuteAsync("9c1e5-b7a20", CancellationToken.None)).Value;

        Assert.Equal("9C1E5-B7A20", trace.ReferenceCode);
        Assert.Equal(CorrelationId, trace.CorrelationId);
        Assert.Equal(CorrelationId, trace.TraceId);
        Assert.Equal(At, trace.FirstSeen);
        Assert.Equal(("GET", "/api/records/x", 409), (trace.Method, trace.Path, trace.StatusCode));
        Assert.Equal(At.AddMinutes(-15), trace.SearchFrom);
        Assert.Equal(At.AddMinutes(15), trace.SearchTo);
    }

    [Fact]
    public async Task A_correlation_id_that_is_not_a_trace_id_has_no_trace_id()
    {
        const string external = "9C1E5-B7A20";
        _sightings.Stored.Add(ReferenceCodeSighting.From(new ReferenceCodeSightingDetails(external, "9c1e5b7a20-not-hex-but-quoted", "GET", "/", 500, At))!);

        var trace = (await new FindReferenceCode(_sightings, TestSettings.Default).ExecuteAsync(external, CancellationToken.None)).Value;

        Assert.Null(trace.TraceId);
    }
}

public sealed class RecordOperatorAccessTests
{
    private readonly FakeAccessLog _log = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("a\nb")]
    public async Task A_call_without_a_usable_operator_is_400_and_records_nothing(string? operatorName)
    {
        var outcome = await new RecordOperatorAccess(_log, _clock).ExecuteAsync(Call(operatorName, subject: null), CancellationToken.None);

        Assert.Equal(RefusalCategory.Invalid, outcome.Refusal!.Category);
        Assert.Equal(OpsErrorCodes.OperatorRequired, outcome.Refusal.Code);
        Assert.Empty(_log.Recorded);
    }

    [Fact]
    public async Task A_call_is_recorded_with_the_operator_the_service_and_the_normalized_code()
    {
        var outcome = await new RecordOperatorAccess(_log, _clock).ExecuteAsync(Call(" CORP\\jdoe ", subject: "9c1e5-b7a20"), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        var access = Assert.Single(_log.Recorded);
        Assert.Equal("CORP\\jdoe", access.Operator);
        Assert.Equal("starter-ops-console", access.CallerService);
        Assert.Equal(OpsEndpoint.ReferenceCode, access.Endpoint);
        Assert.Equal("9C1E5-B7A20", access.Subject);
        Assert.Equal("corr", access.CorrelationId);
        Assert.Equal(_clock.GetUtcNow(), access.At);
    }

    [Fact]
    public async Task A_subject_that_is_not_a_reference_code_is_not_recorded()
    {
        await new RecordOperatorAccess(_log, _clock).ExecuteAsync(Call("op", subject: "<script>"), CancellationToken.None);

        Assert.Null(Assert.Single(_log.Recorded).Subject);
    }

    private static OperatorCall Call(string? operatorName, string? subject) =>
        new(operatorName, "starter-ops-console", OpsEndpoint.ReferenceCode, subject, "corr");
}

public sealed class ReferenceCodePurgeJobTests
{
    [Fact]
    public async Task The_purge_deletes_sightings_older_than_the_retention()
    {
        var now = new DateTimeOffset(2026, 11, 7, 2, 30, 0, TimeSpan.Zero);
        var sightings = new FakeSightings();
        sightings.Stored.Add(Sightings.New("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", now.AddDays(-31)));
        sightings.Stored.Add(Sightings.New("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", now.AddDays(-29)));
        var job = new ReferenceCodePurgeJob(sightings, TestSettings.Default, new FakeTimeProvider(now));

        await job.RunAsync(new JobRunContext(job.Key, RunId: 1, JobTrigger.Schedule, "corr"), CancellationToken.None);

        Assert.Equal(now.AddDays(-30), sightings.PurgedBefore);
        Assert.Equal("BBBBB-BBBBB", Assert.Single(sightings.Stored).ReferenceCode);
    }

    [Fact]
    public void The_job_is_a_well_formed_daily_job()
    {
        var job = new ReferenceCodePurgeJob(new FakeSightings(), TestSettings.Default, TimeProvider.System);

        Assert.True(JobKeyFormat.IsValid(job.Key));
        Assert.Equal(OpsJobs.DailyAtTwoThirty, job.Cron);
        Assert.False(string.IsNullOrWhiteSpace(job.NameAr));
        Assert.False(string.IsNullOrWhiteSpace(job.NameEn));
    }
}
