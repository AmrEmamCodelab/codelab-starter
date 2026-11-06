using Starter.Domain.Workflow;

namespace Starter.Application.Workflow;

/// <summary>
/// Validates every registered workflow definition together: each catalog through
/// <see cref="TransitionCatalogValidator"/>, plus the record type checks across definitions. The host's startup check
/// and the contract tests share it.
/// </summary>
public static class RecordWorkflowValidation
{
    /// <summary>Every violation across <paramref name="definitions"/>; empty when the host may start.</summary>
    /// <param name="definitions">Every registered definition.</param>
    /// <param name="knownPermissionKeys">Every key of the permission catalog.</param>
    public static IReadOnlyList<CatalogViolation> ValidateAll(IEnumerable<IRecordWorkflowDefinition> definitions, IReadOnlySet<string> knownPermissionKeys)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(knownPermissionKeys);
        var violations = new List<CatalogViolation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var catalog = definition.Catalog;
            if (!string.Equals(definition.RecordType, catalog.RecordType, StringComparison.Ordinal))
            {
                violations.Add(Conflict(definition, $"the definition declares '{definition.RecordType}' but its catalog is for '{catalog.RecordType}'."));
            }

            if (!seen.Add(definition.RecordType))
            {
                violations.Add(Conflict(definition, "more than one workflow definition declares this record type."));
                continue;
            }

            violations.AddRange(TransitionCatalogValidator.Validate(catalog, knownPermissionKeys));
        }

        return violations;
    }

    private static CatalogViolation Conflict(IRecordWorkflowDefinition definition, string message) =>
        new(definition.RecordType, CatalogViolationKind.RecordTypeConflict, definition.GetType().Name, message);
}
