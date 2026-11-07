using Codelab.OpsConsole.Application.Certificates;

namespace Codelab.OpsConsole.Api.Diagnostics;

/// <summary>
/// Writes one <c>CertificateNeedsAttention</c> warning per certificate that is not <see cref="CertificateStatus.Ok"/>
/// each time the console reads the list, so the Seq certificate-expiry signal and alert fire from what the operator saw.
/// </summary>
internal static class CertificateAttention
{
    internal static void Log(ILogger logger, IReadOnlyList<CertificateExpiry> certificates)
    {
        foreach (var certificate in certificates.Where(NeedsAttention))
        {
            OpsConsoleLog.CertificateNeedsAttention(logger, certificate.Name, (int)certificate.Status, certificate.DaysLeft);
        }
    }

    internal static bool NeedsAttention(CertificateExpiry certificate) => certificate.Status is not CertificateStatus.Ok;
}
