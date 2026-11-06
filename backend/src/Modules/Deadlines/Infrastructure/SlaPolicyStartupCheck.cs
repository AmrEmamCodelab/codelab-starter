using Microsoft.Extensions.Logging;
using Starter.Application.Workflow;
using Starter.Infrastructure.Startup;
using Starter.Modules.Deadlines.Application;

namespace Starter.Modules.Deadlines.Infrastructure;

/// <summary>The SLA definitions are inconsistent (the host refuses to start). Lists every violation.</summary>
public sealed class SlaPolicyInvalidException : InvalidOperationException
{
    /// <summary>Creates the exception for <paramref name="violations"/>.</summary>
    public SlaPolicyInvalidException(IReadOnlyList<SlaPolicyViolation> violations)
        : base("The SLA definitions are invalid: " + string.Join("; ", violations))
    {
        Violations = violations;
    }

    /// <summary>Every violation.</summary>
    public IReadOnlyList<SlaPolicyViolation> Violations { get; }
}

/// <summary>
/// Refuses to start on an inconsistent SLA definition (<see cref="SlaPolicyCatalog.FindViolations"/>): a duplicated
/// record type and status, or a status the record type's workflow does not define or that is terminal. Pure: it reads no
/// database, so it is idempotent on every start of every node.
/// </summary>
public sealed partial class SlaPolicyStartupCheck(
    SlaPolicyCatalog policies,
    IRecordWorkflowCatalogs workflows,
    ILogger<SlaPolicyStartupCheck> logger) : IStartupTask
{
    /// <inheritdoc />
    public StartupPhase Phase => StartupPhase.ReferenceData;

    /// <inheritdoc />
    public Task RunAsync(CancellationToken cancellationToken)
    {
        var violations = policies.FindViolations(workflows);
        if (violations.Count > 0)
        {
            throw new SlaPolicyInvalidException(violations);
        }

        LogChecked(logger, policies.All.Count);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "SLA definitions checked: {Count} definitions")]
    private static partial void LogChecked(ILogger logger, int count);
}
