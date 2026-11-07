using Starter.Application.Results;
using Starter.Modules.Ops.Domain;

namespace Starter.Modules.Ops.Application.Access;

/// <summary>One call to an Ops endpoint, as the endpoint filter sees it.</summary>
/// <param name="Operator">The raw operator header value, or null when absent.</param>
/// <param name="CallerService">The authenticated calling service (the assertion's issuer).</param>
/// <param name="Endpoint">The endpoint called.</param>
/// <param name="Subject">What the call asks about (the raw reference code), or null.</param>
/// <param name="CorrelationId">The call's correlation id, or null.</param>
public sealed record OperatorCall(string? Operator, string CallerService, OpsEndpoint Endpoint, string? Subject, string? CorrelationId);

/// <summary>
/// Runs before every Ops endpoint: refuses a call without a usable operator name (400
/// <see cref="OpsErrorCodes.OperatorRequired"/>), then records who called what in the Ops audit trail
/// (<see cref="IOperatorAccessLog"/>, best effort). A subject that is a well-formed reference code is recorded
/// normalized; anything else the operator typed is not recorded.
/// </summary>
public sealed class RecordOperatorAccess(IOperatorAccessLog log, TimeProvider clock)
{
    /// <summary>Validates and records <paramref name="call"/>.</summary>
    public async Task<Outcome<Done>> ExecuteAsync(OperatorCall call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        var operatorName = OperatorName.Normalize(call.Operator);
        if (operatorName is null)
        {
            return Refusal.Invalid(OpsErrorCodes.OperatorRequired);
        }

        var access = OperatorAccess.Record(new OperatorAccessDetails(
            Operator: operatorName,
            CallerService: call.CallerService,
            Endpoint: call.Endpoint,
            Subject: ReferenceCodeFormat.Normalize(call.Subject),
            CorrelationId: call.CorrelationId,
            At: clock.GetUtcNow()));
        await log.RecordAsync(access, cancellationToken);
        return Done.Value;
    }
}
