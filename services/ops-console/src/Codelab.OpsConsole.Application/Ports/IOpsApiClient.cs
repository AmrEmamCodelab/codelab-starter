using System.Text.Json;
using Codelab.OpsConsole.Application.Certificates;
using Codelab.OpsConsole.Application.ReferenceCodes;

namespace Codelab.OpsConsole.Application.Ports;

/// <summary>
/// The starter API's <c>/api/ops</c> surface. Every call carries a fresh ES256 service assertion and the operator's
/// name. A failure to reach the API, or any answer but success (and 404 for an unknown reference code), throws
/// <c>Codelab.Errors.UpstreamUnavailableException</c>; cancellation propagates.
/// </summary>
public interface IOpsApiClient
{
    /// <summary>The API's health report, passed through as the API wrote it.</summary>
    Task<JsonElement> GetHealthAsync(OperatorName operatorName, CancellationToken cancellationToken);

    /// <summary>The API's background job status, passed through as the API wrote it.</summary>
    Task<JsonElement> GetJobsAsync(OperatorName operatorName, CancellationToken cancellationToken);

    /// <summary>The certificates the API watches, with their expiry verdicts.</summary>
    Task<IReadOnlyList<CertificateExpiry>> GetCertificatesAsync(OperatorName operatorName, CancellationToken cancellationToken);

    /// <summary>What the API knows about <paramref name="referenceCode"/> (normalized), or null when it knows nothing.</summary>
    Task<ApiReferenceCodeFacts?> FindReferenceCodeAsync(string referenceCode, OperatorName operatorName, CancellationToken cancellationToken);
}
