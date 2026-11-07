using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Starter.Modules.Ops.Application;
using Starter.Modules.Ops.Domain;
using Starter.Modules.Ops.Domain.Certificates;

namespace Starter.Modules.Ops.Tests.Fakes;

internal static class TestSettings
{
    public static OpsSettings Default { get; } = new(
        Thresholds: new CertificateThresholds(WarningDays: 30, CriticalDays: 7),
        SearchMargin: TimeSpan.FromMinutes(15),
        SightingRetention: TimeSpan.FromDays(30));
}

internal sealed class FakeSightings : IReferenceCodeSightings
{
    public List<ReferenceCodeSighting> Stored { get; } = [];

    public DateTimeOffset? PurgedBefore { get; private set; }

    public Exception? FailWith { get; set; }

    public Task<ReferenceCodeSighting?> FindNewestAsync(string referenceCode, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.LastOrDefault(sighting => sighting.ReferenceCode == referenceCode));

    public Task AddAsync(IReadOnlyCollection<ReferenceCodeSighting> sightings, CancellationToken cancellationToken)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        Stored.AddRange(sightings);
        return Task.CompletedTask;
    }

    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        PurgedBefore = cutoff;
        return Task.FromResult(Stored.RemoveAll(sighting => sighting.OccurredAt < cutoff));
    }
}

internal sealed class FakeAccessLog : IOperatorAccessLog
{
    public List<OperatorAccess> Recorded { get; } = [];

    public Task RecordAsync(OperatorAccess access, CancellationToken cancellationToken)
    {
        Recorded.Add(access);
        return Task.CompletedTask;
    }
}

internal sealed class FixedProbe(ComponentCheck check) : IComponentProbe
{
    public string Name => check.Name;

    public Task<ComponentCheck> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(check);
}

internal sealed class FixedCertificates(params CertificateFact[] facts) : ICertificateSource
{
    public Task<IReadOnlyList<CertificateFact>> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CertificateFact>>(facts);
}

internal sealed class FixedJobs(params JobStatus[] jobs) : IJobStatusSource
{
    public Task<IReadOnlyList<JobStatus>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobStatus>>(jobs);
}

/// <summary>An HTTP handler that answers from a function (or throws what it returns).</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
{
    public int Calls { get; private set; }

    public static StubHandler Status(HttpStatusCode status, string body = "") =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));

    public static StubHandler Throws(Exception exception) => new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    public static StubHandler Hangs() => new(async (_, token) =>
    {
        await Task.Delay(Timeout.Infinite, token);
        return new HttpResponseMessage(HttpStatusCode.OK);
    });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        return answer(request, cancellationToken);
    }
}

/// <summary>Self-signed certificates made at run time (never a file from the repository).</summary>
internal static class TestCertificates
{
    public static X509Certificate2 SelfSigned(string subject, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    public static string Base64Der(X509Certificate2 certificate) => Convert.ToBase64String(certificate.Export(X509ContentType.Cert));
}

internal static class Sightings
{
    public static ReferenceCodeSighting New(string correlationId, DateTimeOffset at, int status = 500, string path = "/api/things") =>
        ReferenceCodeSighting.From(new ReferenceCodeSightingDetails(
            ReferenceCode: Codelab.Observability.ReferenceCode.From(correlationId),
            CorrelationId: correlationId,
            Method: "GET",
            Path: path,
            StatusCode: status,
            OccurredAt: at))!;
}
