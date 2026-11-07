using Starter.Infrastructure.Hosting;

namespace Starter.Infrastructure.Security;

/// <summary>The routes of the kernel's security-edge endpoints.</summary>
public static class SecurityEdgeRoutes
{
    /// <summary>
    /// The anonymous CSP report sink (<c>POST /api/security/csp-reports</c>): the SPA document's
    /// <c>report-uri</c> and its <c>Reporting-Endpoints</c> entry both point here.
    /// </summary>
    public const string CspReports = ApiRoutes.Prefix + ApiRoutes.Separator + "security" + ApiRoutes.Separator + "csp-reports";
}
