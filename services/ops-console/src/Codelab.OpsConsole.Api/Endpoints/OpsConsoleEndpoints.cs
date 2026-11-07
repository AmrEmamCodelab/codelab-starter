using System.Security.Claims;
using Codelab.Errors;
using Codelab.OpsConsole.Api.Authentication;
using Codelab.OpsConsole.Api.Diagnostics;
using Codelab.OpsConsole.Api.Hosting;
using Codelab.OpsConsole.Api.Upstream;
using Codelab.OpsConsole.Application;
using Codelab.OpsConsole.Application.Ports;
using Codelab.OpsConsole.Application.ReferenceCodes;
using Microsoft.Extensions.Options;

namespace Codelab.OpsConsole.Api.Endpoints;

/// <summary>
/// The backend-for-frontend endpoints. Read-only (GET only, so a forged cross-site request changes nothing) and thin:
/// each takes the operator from the authenticated principal, calls one client or use case and maps the result.
/// Every route is behind <see cref="OperatorAuthentication.OperatorPolicy"/> (401 without Windows sign-in, 403 outside
/// the operator group); an upstream failure is 502 <c>UPSTREAM_UNAVAILABLE</c> through Codelab.Errors.
/// </summary>
internal static class OpsConsoleEndpoints
{
    /// <summary>The route parameter of the reference-code lookup, also the field named in its validation error.</summary>
    internal const string CodeParameter = "code";

    internal const string LogCategory = "Codelab.OpsConsole.Api.Endpoints";

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var console = endpoints.MapGroup(OpsConsoleRoutes.Prefix).RequireAuthorization(OperatorAuthentication.OperatorPolicy);
        console.MapGet(OpsConsoleRoutes.Me, Me);
        console.MapGet(OpsConsoleRoutes.Health, HealthAsync);
        console.MapGet(OpsConsoleRoutes.Jobs, JobsAsync);
        console.MapGet(OpsConsoleRoutes.Certificates, CertificatesAsync);
        console.MapGet(OpsConsoleRoutes.ReferenceCode, ReferenceCodeAsync);
    }

    internal static IResult Me(ClaimsPrincipal user, IOptionsMonitor<OpsConsoleOptions> options) =>
        TypedResults.Ok(new MeResponse(OperatorOf(user).Value, [options.CurrentValue.OperatorGroup!.Trim()]));

    internal static async Task<IResult> HealthAsync(ClaimsPrincipal user, IOpsApiClient api, CancellationToken cancellationToken) =>
        TypedResults.Ok(await api.GetHealthAsync(OperatorOf(user), cancellationToken));

    internal static async Task<IResult> JobsAsync(ClaimsPrincipal user, IOpsApiClient api, CancellationToken cancellationToken) =>
        TypedResults.Ok(await api.GetJobsAsync(OperatorOf(user), cancellationToken));

    internal static async Task<IResult> CertificatesAsync(ClaimsPrincipal user, IOpsApiClient api, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        var certificates = await api.GetCertificatesAsync(OperatorOf(user), cancellationToken);
        CertificateAttention.Log(loggers.CreateLogger(LogCategory), certificates);
        return TypedResults.Ok(certificates);
    }

    internal static async Task<IResult> ReferenceCodeAsync(
        string code, ClaimsPrincipal user, LookUpReferenceCode lookUp, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        var lookup = await lookUp.ExecuteAsync(code, OperatorOf(user), cancellationToken);
        var outcome = lookup.Outcome;
        var eventCount = lookup.Trace?.Events.Count ?? 0;
        var logger = loggers.CreateLogger(LogCategory);
        OpsConsoleLog.ReferenceCodeLookedUp(logger, outcome, eventCount);
        return outcome switch
        {
            ReferenceCodeLookupOutcome.Found => TypedResults.Ok(ReferenceCodeResponse.From(lookup.Trace!)),
            ReferenceCodeLookupOutcome.Invalid => InvalidCode(),
            ReferenceCodeLookupOutcome.NotFound => CodedRefusal.NotFound(OpsConsoleErrorCodes.ReferenceCodeNotFound).ToResult(),
            _ => throw new UpstreamUnavailableException(SeqLogEventSource.HttpClientName),
        };
    }

    private static IResult InvalidCode() =>
        CodedRefusal.Invalid(
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [CodeParameter] = [OpsConsoleErrorCodes.ReferenceCodeInvalid] },
            OpsConsoleErrorCodes.ReferenceCodeInvalid).ToResult();

    /// <summary>The operator; the operator policy has already proven the principal has a name.</summary>
    private static OperatorName OperatorOf(ClaimsPrincipal user) =>
        OperatorGroupMembership.NameOf(user) ?? throw new InvalidOperationException("The operator policy admitted a principal without a name.");
}
