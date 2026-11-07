using Codelab.OpsConsole.Application.Ports;

namespace Codelab.OpsConsole.Application.ReferenceCodes;

/// <summary>How a reference-code lookup ended.</summary>
public enum ReferenceCodeLookupOutcome
{
    /// <summary>Traced: <see cref="ReferenceCodeLookup.Trace"/> is set.</summary>
    Found = 1,

    /// <summary>The input is not a reference code or correlation id.</summary>
    Invalid = 2,

    /// <summary>The API knows nothing and the log store answered with no event.</summary>
    NotFound = 3,

    /// <summary>The API knows nothing and the log store could not be asked, so the answer is unknown.</summary>
    LogsUnavailable = 4,
}

/// <summary>The result of <see cref="LookUpReferenceCode"/>.</summary>
public sealed record ReferenceCodeLookup(ReferenceCodeLookupOutcome Outcome, ReferenceCodeTrace? Trace)
{
    /// <summary>A refusal without a trace.</summary>
    public static ReferenceCodeLookup Refused(ReferenceCodeLookupOutcome outcome) => new(outcome, Trace: null);
}

/// <summary>
/// The reference-code lookup an operator runs from a user's error dialog: normalize the code, ask the API's
/// <c>/api/ops</c> and the log store at the same time, then merge both into one trace. An API failure propagates
/// (<c>UpstreamUnavailableException</c>); the log store is best effort.
/// </summary>
public sealed class LookUpReferenceCode(IOpsApiClient api, ILogEventSource logs)
{
    /// <summary>Looks <paramref name="input"/> up on behalf of <paramref name="operatorName"/>.</summary>
    public async Task<ReferenceCodeLookup> ExecuteAsync(string? input, OperatorName operatorName, CancellationToken cancellationToken)
    {
        if (!ReferenceCodeInput.TryNormalize(input, out var referenceCode))
        {
            return ReferenceCodeLookup.Refused(ReferenceCodeLookupOutcome.Invalid);
        }

        var factsTask = api.FindReferenceCodeAsync(referenceCode, operatorName, cancellationToken);
        var logsTask = logs.FindByReferenceCodeAsync(referenceCode, cancellationToken);
        await Task.WhenAll(factsTask, logsTask);
        return Decide(referenceCode, await factsTask, await logsTask);
    }

    private static ReferenceCodeLookup Decide(string referenceCode, ApiReferenceCodeFacts? facts, LogSearchResult logs)
    {
        if (facts is null && !logs.Available)
        {
            return ReferenceCodeLookup.Refused(ReferenceCodeLookupOutcome.LogsUnavailable);
        }

        if (facts is null && logs.Events.Count == 0)
        {
            return ReferenceCodeLookup.Refused(ReferenceCodeLookupOutcome.NotFound);
        }

        return new ReferenceCodeLookup(ReferenceCodeLookupOutcome.Found, ReferenceCodeTraceMerge.Merge(referenceCode, facts, logs));
    }
}
