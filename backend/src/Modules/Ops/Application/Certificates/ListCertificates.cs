using Starter.Application.Results;
using Starter.Modules.Ops.Domain.Certificates;

namespace Starter.Modules.Ops.Application.Certificates;

/// <summary>One certificate with its verdict.</summary>
/// <param name="Name">The configured name (or <c>idp-signing-N</c>).</param>
/// <param name="Subject">The subject, or null when unreadable.</param>
/// <param name="NotAfter">The end date, or null when unreadable.</param>
/// <param name="DaysLeft">Whole days left (rounded down, negative once expired), or null when unreadable.</param>
/// <param name="Status">The verdict (<see cref="CertificateExpiry"/>); an unreadable certificate is critical.</param>
/// <param name="FindingCode">Why it could not be read, or null.</param>
public sealed record CertificateReport(
    string Name,
    string? Subject,
    DateTimeOffset? NotAfter,
    int? DaysLeft,
    CertificateStatus Status,
    string? FindingCode);

/// <summary>
/// <c>GET api/ops/certificates</c>: every certificate of every <see cref="ICertificateSource"/>, judged against the
/// thresholds of <see cref="OpsSettings"/> on this host's clock, the most urgent first (status, then days left, then name).
/// </summary>
public sealed class ListCertificates(IEnumerable<ICertificateSource> sources, OpsSettings settings, TimeProvider clock)
{
    /// <summary>Lists the certificates.</summary>
    public async Task<Outcome<IReadOnlyList<CertificateReport>>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var facts = await Task.WhenAll(sources.Select(source => source.ReadAsync(cancellationToken)));
        var now = clock.GetUtcNow();
        IReadOnlyList<CertificateReport> reports =
        [
            .. facts.SelectMany(fact => fact)
                .Select(fact => Judge(fact, now))
                .OrderByDescending(report => report.Status)
                .ThenBy(report => report.DaysLeft ?? int.MinValue)
                .ThenBy(report => report.Name, StringComparer.Ordinal),
        ];
        return Outcome.Success(reports);
    }

    private CertificateReport Judge(CertificateFact fact, DateTimeOffset now)
    {
        var verdict = fact.NotAfter is { } notAfter
            ? CertificateExpiry.Evaluate(notAfter, now, settings.Thresholds)
            : CertificateExpiry.Unreadable;
        return new CertificateReport(fact.Name, fact.Subject, fact.NotAfter, verdict.DaysLeft, verdict.Status, fact.FindingCode);
    }
}
