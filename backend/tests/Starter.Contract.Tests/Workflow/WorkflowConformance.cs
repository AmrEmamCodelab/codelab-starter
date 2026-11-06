using System.Reflection;
using Starter.Application.Workflow;
using Starter.Domain.Workflow;

namespace Starter.Contract.Tests.Workflow;

/// <summary>
/// The workflow conformance contract: every transition of every registered catalog has a known permission, both of
/// its statuses are worded for every audience, it has a label, and it carries a notification decision; and every
/// <see cref="IRecordWorkflowDefinition"/> a module declares is registered. Names no record type.
/// </summary>
public static class WorkflowConformance
{
    /// <summary>Every concrete <see cref="IRecordWorkflowDefinition"/> type in <paramref name="assemblies"/>.</summary>
    public static IReadOnlyList<Type> DefinitionTypesIn(IEnumerable<Assembly> assemblies) =>
        [.. assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IRecordWorkflowDefinition).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    /// <summary>Every breach; empty when the host conforms (also when it has no workflow at all).</summary>
    /// <param name="registered">The definitions the host's DI container serves.</param>
    /// <param name="declaredTypes">The definition types found in the module assemblies.</param>
    /// <param name="knownPermissionKeys">The permission catalog.</param>
    public static IReadOnlyList<string> Check(
        IReadOnlyCollection<IRecordWorkflowDefinition> registered,
        IEnumerable<Type> declaredTypes,
        IReadOnlySet<string> knownPermissionKeys)
    {
        var registeredTypes = registered.Select(definition => definition.GetType()).ToHashSet();
        var breaches = declaredTypes
            .Where(type => !registeredTypes.Contains(type))
            .Select(type => $"{type.FullName} implements {nameof(IRecordWorkflowDefinition)} but no module calls AddRecordWorkflow<{type.Name}>().")
            .ToList();
        foreach (var catalog in registered.Select(definition => definition.Catalog))
        {
            breaches.AddRange(catalog.Rules.SelectMany(rule => BreachesOf(catalog, rule, knownPermissionKeys)));
        }

        return breaches;
    }

    private static IEnumerable<string> BreachesOf(TransitionCatalog catalog, TransitionRule rule, IReadOnlySet<string> knownPermissionKeys)
    {
        var subject = $"[{catalog.RecordType}] {rule.Describe()}";
        if (string.IsNullOrWhiteSpace(rule.PermissionKey) || !knownPermissionKeys.Contains(rule.PermissionKey))
        {
            yield return $"{subject}: permission '{rule.PermissionKey}' is missing or not in the permission catalog.";
        }

        if (string.IsNullOrWhiteSpace(rule.LabelKey))
        {
            yield return $"{subject}: no label key.";
        }

        if (rule.Notification is null)
        {
            yield return $"{subject}: no notification template decision.";
        }

        foreach (var audience in UnwordedAudiences(catalog, rule))
        {
            yield return $"{subject}: no {audience} wording for one of its statuses.";
        }
    }

    private static IEnumerable<WorkflowAudience> UnwordedAudiences(TransitionCatalog catalog, TransitionRule rule)
    {
        var statuses = new[] { catalog.FindStatus(rule.FromStatus), catalog.FindStatus(rule.ToStatus) };
        return Enum.GetValues<WorkflowAudience>()
            .Where(audience => statuses.Any(status => status?.Wording is null || string.IsNullOrWhiteSpace(status.Wording.For(audience))));
    }
}
