using Starter.Domain.Workflow;

namespace Starter.Infrastructure.Workflow;

/// <summary>
/// Thrown while the host starts when any registered workflow catalog is invalid; the host does not start. The
/// message lists every violation, one per line, so one failed boot shows everything to fix.
/// </summary>
public sealed class WorkflowCatalogInvalidException : Exception
{
    /// <summary>Creates the exception for <paramref name="violations"/>.</summary>
    public WorkflowCatalogInvalidException(IReadOnlyList<CatalogViolation> violations)
        : base(Describe(violations))
    {
        Violations = violations;
    }

    /// <summary>Every violation found.</summary>
    public IReadOnlyList<CatalogViolation> Violations { get; }

    private static string Describe(IReadOnlyList<CatalogViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);
        return $"The workflow catalogs are invalid ({violations.Count} violation(s)); the host does not start:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations);
    }
}
