using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Jobs;
using Starter.Application.SingleSignOn;
using Starter.Modules.Ops.Application;
using Starter.Modules.Ops.Domain;
using Starter.Modules.Ops.Domain.Health;
using Starter.Modules.Ops.Infrastructure.Certificates;
using Starter.Modules.Ops.Infrastructure.Health;
using Starter.Modules.Ops.Infrastructure.Jobs;
using Starter.Modules.Ops.Infrastructure.Options;
using Starter.Modules.Ops.Tests.Fakes;

namespace Starter.Modules.Ops.Tests.Infrastructure;

public sealed class ProbeTests
{
    private static readonly Uri ReadyUrl = new("https://attachments.example.test/health/ready");

    [Fact]
    public async Task A_2xx_answer_is_healthy()
    {
        var check = await Probe(StubHandler.Status(HttpStatusCode.OK)).CheckAsync(CancellationToken.None);

        Assert.Equal(("attachment-service", ComponentStatus.Healthy, (string?)null), (check.Name, check.Status, check.FindingCode));
    }

    [Fact]
    public async Task A_not_ready_answer_is_unhealthy()
    {
        var check = await Probe(StubHandler.Status(HttpStatusCode.ServiceUnavailable)).CheckAsync(CancellationToken.None);

        Assert.Equal((ComponentStatus.Unhealthy, OpsFindingCodes.ProbeNotReady), (check.Status, check.FindingCode));
    }

    [Fact]
    public async Task A_connection_or_tls_failure_is_unreachable()
    {
        var check = await Probe(StubHandler.Throws(new HttpRequestException("The SSL connection could not be established."))).CheckAsync(CancellationToken.None);

        Assert.Equal((ComponentStatus.Unhealthy, OpsFindingCodes.ProbeUnreachable), (check.Status, check.FindingCode));
    }

    [Fact]
    public async Task A_probe_that_does_not_answer_in_time_is_a_timeout()
    {
        var budget = new ProbeBudget(TimeProvider.System, TimeSpan.FromMilliseconds(50));

        var check = await new HttpReadinessProbe("integration-gateway", ReadyUrl, new HttpClient(StubHandler.Hangs()), budget).CheckAsync(CancellationToken.None);

        Assert.Equal((ComponentStatus.Unhealthy, OpsFindingCodes.ProbeTimeout), (check.Status, check.FindingCode));
    }

    [Fact]
    public async Task The_callers_own_cancellation_propagates()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Probe(StubHandler.Hangs()).CheckAsync(cancelled.Token));
    }

    [Fact]
    public async Task A_slow_ready_answer_is_degraded()
    {
        var clock = new FakeTimeProvider();
        var budget = new ProbeBudget(clock, TimeSpan.FromSeconds(4));

        var check = await budget.RunAsync(
            "database",
            _ =>
            {
                clock.Advance(TimeSpan.FromSeconds(3));
                return Task.FromResult<string?>(null);
            },
            CancellationToken.None);

        Assert.Equal((ComponentStatus.Degraded, OpsFindingCodes.ProbeSlow), (check.Status, check.FindingCode));
        Assert.Equal(3000, check.DurationMs);
    }

    private static HttpReadinessProbe Probe(StubHandler handler) =>
        new("attachment-service", ReadyUrl, new HttpClient(handler), new ProbeBudget(TimeProvider.System, TimeSpan.FromSeconds(5)));
}

public sealed class CertificateSourceTests
{
    private static readonly DateTimeOffset NotBefore = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NotAfter = new(2026, 11, 28, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_certificate_file_is_read_for_its_subject_and_end_date()
    {
        var path = Path.Combine(Path.GetTempPath(), $"starter-ops-{Guid.NewGuid():N}.cer");
        using (var certificate = TestCertificates.SelfSigned("CN=starter-api-tls", NotBefore, NotAfter))
        {
            File.WriteAllBytes(path, certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert));
        }

        try
        {
            var fact = ConfiguredCertificateSource.Read(new WatchedCertificateOptions { Name = "api-tls", Path = path });

            Assert.Equal(("api-tls", "CN=starter-api-tls", (string?)null), (fact.Name, fact.Subject, fact.FindingCode));
            Assert.Equal(NotAfter, fact.NotAfter);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_missing_file_or_store_entry_is_unreadable()
    {
        var missingFile = ConfiguredCertificateSource.Read(new WatchedCertificateOptions { Name = "gone", Path = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.cer") });
        var missingThumbprint = ConfiguredCertificateSource.Read(new WatchedCertificateOptions { Name = "nothere", Thumbprint = "00 11 22 33", StoreLocation = "CurrentUser" });

        Assert.Equal((null, null, OpsFindingCodes.CertificateUnreadable), (missingFile.Subject, missingFile.NotAfter, missingFile.FindingCode));
        Assert.Equal(OpsFindingCodes.CertificateUnreadable, missingThumbprint.FindingCode);
    }

    [Fact]
    public async Task The_configured_source_reads_every_entry()
    {
        var options = new OpsOptions();
        options.Certificates.Add(new WatchedCertificateOptions { Name = "a", Path = "missing-a.cer" });
        options.Certificates.Add(new WatchedCertificateOptions { Name = "b", Path = "missing-b.cer" });

        var facts = await new ConfiguredCertificateSource(Options.Create(options)).ReadAsync(CancellationToken.None);

        Assert.Equal(["a", "b"], facts.Select(fact => fact.Name));
    }

    [Fact]
    public void A_jwks_gives_one_row_per_signing_key_dated_by_its_certificate()
    {
        using var certificate = TestCertificates.SelfSigned("CN=idp-signing", NotBefore, NotAfter);
        var jwks = JsonDocument.Parse($$"""
            {"keys":[
              {"kty":"RSA","kid":"a","use":"sig","x5c":["{{TestCertificates.Base64Der(certificate)}}"]},
              {"kty":"RSA","kid":"b","use":"enc","x5c":["{{TestCertificates.Base64Der(certificate)}}"]},
              {"kty":"EC","kid":"c"},
              {"kty":"RSA","kid":"d","x5c":["not base64!"]}
            ]}
            """);

        var facts = JwksCertificates.Read(jwks.RootElement, "idp-signing");

        Assert.Equal(["idp-signing-1", "idp-signing-2", "idp-signing-3"], facts.Select(fact => fact.Name));
        Assert.Equal(("CN=idp-signing", (DateTimeOffset?)NotAfter), (facts[0].Subject, facts[0].NotAfter));
        Assert.Equal(OpsFindingCodes.IdentityProviderKeyWithoutCertificate, facts[1].FindingCode);
        Assert.Equal(OpsFindingCodes.CertificateUnreadable, facts[2].FindingCode);
    }

    [Fact]
    public void A_document_that_is_not_a_jwks_is_refused() =>
        Assert.Throws<JsonException>(() => JwksCertificates.Read(JsonDocument.Parse("""{"issuer":"x"}""").RootElement, "idp-signing"));

    [Fact]
    public async Task The_identity_provider_source_reads_the_jwks_url()
    {
        using var certificate = TestCertificates.SelfSigned("CN=idp", NotBefore, NotAfter);
        var handler = StubHandler.Status(HttpStatusCode.OK, $$"""{"keys":[{"kty":"RSA","x5c":["{{TestCertificates.Base64Der(certificate)}}"]}]}""");

        var facts = await IdpSource(handler, "https://idp.example.test/jwks").ReadAsync(CancellationToken.None);

        Assert.Equal("idp-signing-1", Assert.Single(facts).Name);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.OK, "<html>not json</html>")]
    [InlineData(HttpStatusCode.OK, """{"no":"keys"}""")]
    public async Task An_unreachable_or_unreadable_jwks_is_one_unavailable_row(HttpStatusCode status, string body)
    {
        var facts = await IdpSource(StubHandler.Status(status, body), "https://idp.example.test/jwks").ReadAsync(CancellationToken.None);

        var fact = Assert.Single(facts);
        Assert.Equal((IdentityProviderCertificateSource.NamePrefix, OpsFindingCodes.IdentityProviderKeysUnavailable), (fact.Name, fact.FindingCode));
    }

    [Fact]
    public async Task No_jwks_url_is_no_row_and_no_call()
    {
        var handler = StubHandler.Status(HttpStatusCode.OK);

        Assert.Empty(await IdpSource(handler, url: null).ReadAsync(CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task With_single_sign_on_the_jwks_comes_through_its_back_channel_and_the_url_is_never_called()
    {
        using var certificate = TestCertificates.SelfSigned("CN=idp-relayed", NotBefore, NotAfter);
        var handler = StubHandler.Status(HttpStatusCode.OK);
        var keySet = new StubKeySet($$"""{"keys":[{"kty":"RSA","x5c":["{{TestCertificates.Base64Der(certificate)}}"]}]}""");

        var facts = await IdpSource(handler, "https://idp.example.test/jwks", keySet).ReadAsync(CancellationToken.None);

        var fact = Assert.Single(facts);
        Assert.Equal(("idp-signing-1", "CN=idp-relayed", (DateTimeOffset?)NotAfter), (fact.Name, fact.Subject, fact.NotAfter));
        Assert.Equal((1, 0), (keySet.Calls, handler.Calls));
    }

    [Fact]
    public async Task With_single_sign_on_no_url_is_needed()
    {
        using var certificate = TestCertificates.SelfSigned("CN=idp-relayed", NotBefore, NotAfter);
        var keySet = new StubKeySet($$"""{"keys":[{"kty":"RSA","x5c":["{{TestCertificates.Base64Der(certificate)}}"]}]}""");

        Assert.Equal("idp-signing-1", Assert.Single(await IdpSource(StubHandler.Status(HttpStatusCode.OK), url: null, keySet).ReadAsync(CancellationToken.None)).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("<html>not json</html>")]
    [InlineData("""{"no":"keys"}""")]
    public async Task An_unavailable_back_channel_or_an_unreadable_answer_is_one_unavailable_row(string? answer)
    {
        var facts = await IdpSource(StubHandler.Status(HttpStatusCode.OK), url: null, new StubKeySet(answer)).ReadAsync(CancellationToken.None);

        var fact = Assert.Single(facts);
        Assert.Equal((IdentityProviderCertificateSource.NamePrefix, OpsFindingCodes.IdentityProviderKeysUnavailable), (fact.Name, fact.FindingCode));
    }

    [Fact]
    public async Task A_back_channel_that_does_not_answer_in_time_is_one_unavailable_row()
    {
        var source = new IdentityProviderCertificateSource(
            new HttpClient(StubHandler.Status(HttpStatusCode.OK)),
            Options.Create(new OpsOptions()),
            new ProbeBudget(TimeProvider.System, TimeSpan.FromMilliseconds(50)),
            new HangingKeySet());

        var fact = Assert.Single(await source.ReadAsync(CancellationToken.None));

        Assert.Equal(OpsFindingCodes.IdentityProviderKeysUnavailable, fact.FindingCode);
    }

    private static IdentityProviderCertificateSource IdpSource(StubHandler handler, string? url, IIdentityProviderKeySet? keySet = null) =>
        new(new HttpClient(handler), Options.Create(new OpsOptions { IdentityProviderJwksUrl = url }), new ProbeBudget(TimeProvider.System, TimeSpan.FromSeconds(5)), keySet);

    private sealed class StubKeySet(string? jwks) : IIdentityProviderKeySet
    {
        public int Calls { get; private set; }

        public Task<string?> ReadJwksAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(jwks);
        }
    }

    private sealed class HangingKeySet : IIdentityProviderKeySet
    {
        public async Task<string?> ReadJwksAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }
    }
}

public sealed class JobCatalogStatusSourceTests
{
    private static readonly DateTimeOffset Started = new(2026, 11, 7, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Finished = Started.AddSeconds(3);

    [Fact]
    public async Task A_succeeded_run_is_the_jobs_last_run()
    {
        var history = new StubHistory(new() { ["outbox-drain"] = new JobRunSummary(JobRunStatus.Succeeded, JobTrigger.Schedule, Started, Finished, ReferenceCode: null) });

        var status = Assert.Single(await new JobCatalogStatusSource([new StubJob("outbox-drain", "*/5 * * * *")], history).ListAsync(CancellationToken.None));

        Assert.Equal(new JobLastRun(JobRunStatus.Succeeded, JobTrigger.Schedule, Started, Finished, ReferenceCode: null), status.LastRun);
        Assert.Equal("*/5 * * * *", status.Cron);
    }

    [Fact]
    public async Task A_failed_run_carries_its_reference_code()
    {
        var history = new StubHistory(new() { ["manual-only"] = new JobRunSummary(JobRunStatus.Failed, JobTrigger.Manual, Started, Finished, "REF-7Q2K9X") });

        var status = Assert.Single(await new JobCatalogStatusSource([new StubJob("manual-only", null)], history).ListAsync(CancellationToken.None));

        Assert.Equal((JobRunStatus.Failed, JobTrigger.Manual, "REF-7Q2K9X"), (status.LastRun!.Status, status.LastRun.Trigger, status.LastRun.ReferenceCode));
    }

    [Fact]
    public async Task A_job_that_never_ran_has_no_last_run_and_every_key_is_asked_once()
    {
        var history = new StubHistory(new() { ["outbox-drain"] = new JobRunSummary(JobRunStatus.Running, JobTrigger.Schedule, Started, FinishedAt: null, ReferenceCode: null) });

        var statuses = await new JobCatalogStatusSource([new StubJob("outbox-drain", "*/5 * * * *"), new StubJob("never-ran", null)], history).ListAsync(CancellationToken.None);

        Assert.Equal(["outbox-drain", "never-ran"], statuses.Select(status => status.Key));
        Assert.Equal(JobRunStatus.Running, statuses[0].LastRun!.Status);
        Assert.Null(statuses[1].LastRun);
        Assert.Equal(["outbox-drain", "never-ran"], history.AskedKeys);
    }

    [Fact]
    public async Task Without_the_jobs_module_every_job_is_listed_without_a_run()
    {
        var statuses = await new JobCatalogStatusSource([new StubJob("outbox-drain", "*/5 * * * *"), new StubJob("manual-only", null)]).ListAsync(CancellationToken.None);

        Assert.Equal(["outbox-drain", "manual-only"], statuses.Select(status => status.Key));
        Assert.All(statuses, status => Assert.Null(status.LastRun));
    }

    private sealed class StubHistory(Dictionary<string, JobRunSummary> runs) : IJobRunHistory
    {
        public IReadOnlyCollection<string> AskedKeys { get; private set; } = [];

        public Task<IReadOnlyDictionary<string, JobRunSummary>> LatestPerJobAsync(IReadOnlyCollection<string> jobKeys, CancellationToken cancellationToken)
        {
            AskedKeys = jobKeys;
            return Task.FromResult<IReadOnlyDictionary<string, JobRunSummary>>(runs);
        }
    }

    private sealed class StubJob(string key, string? cron) : IJob
    {
        public string Key => key;

        public string NameAr => "ar";

        public string NameEn => "en";

        public string? Cron => cron;

        public Task RunAsync(JobRunContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
