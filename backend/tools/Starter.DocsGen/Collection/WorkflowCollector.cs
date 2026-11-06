using System.Reflection;
using System.Text.RegularExpressions;
using Starter.Application.Workflow;
using Starter.Domain.Workflow;

namespace Starter.DocsGen.Collection;

/// <summary>One record type's workflow as the host runs it.</summary>
/// <param name="RecordType">The record type key (kebab case); also the file name under <c>workflows/</c>.</param>
/// <param name="Owner">The module (or the kernel) that declares the definition.</param>
/// <param name="DefinitionType">The definition class's full name, so a reader finds the source.</param>
/// <param name="Catalog">The statuses and rules, exactly as registered.</param>
internal sealed record WorkflowDoc(string RecordType, string Owner, string DefinitionType, TransitionCatalog Catalog);

/// <summary>
/// Reads the workflows: every <see cref="IRecordWorkflowDefinition"/> the host registered
/// (<c>AddRecordWorkflow&lt;TDefinition&gt;()</c>), cross-checked against every concrete definition class declared in
/// the host's own assemblies (kernel and modules). A declared class the host does not register would document a
/// workflow nobody runs (or hide one), so it fails the collection. Zero definitions is a valid host (the sample
/// module deleted).
/// </summary>
internal static partial class WorkflowCollector
{
    /// <summary>The shape a record type must have to be a file name (the record-type key format).</summary>
    public const string RecordTypePattern = "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$";

    /// <summary>The workflows, sorted by record type (ordinal).</summary>
    /// <param name="registered">The definitions resolved from the host's services.</param>
    /// <param name="ownAssemblies">The host's own assemblies, scanned for declared definition classes.</param>
    /// <param name="owners">Names the owner of each definition.</param>
    /// <exception cref="InvalidOperationException">
    /// A declared definition is not registered, a record type is not a kebab key, or two definitions share one.
    /// </exception>
    public static IReadOnlyList<WorkflowDoc> Collect(
        IEnumerable<IRecordWorkflowDefinition> registered,
        IEnumerable<Assembly> ownAssemblies,
        OwnerNaming owners)
    {
        ArgumentNullException.ThrowIfNull(registered);
        ArgumentNullException.ThrowIfNull(ownAssemblies);
        ArgumentNullException.ThrowIfNull(owners);
        var definitions = registered.ToList();
        RequireRegistered(DeclaredDefinitionTypes(ownAssemblies), definitions);
        var workflows = definitions
            .Select(definition => new WorkflowDoc(
                RecordType: RequireFileSafe(definition.RecordType),
                Owner: owners.OwnerOf(definition.GetType()),
                DefinitionType: definition.GetType().FullName ?? definition.GetType().Name,
                Catalog: definition.Catalog))
            .OrderBy(workflow => workflow.RecordType, StringComparer.Ordinal)
            .ToList();
        RequireUnique(workflows);
        return workflows;
    }

    /// <summary>Every concrete class implementing <see cref="IRecordWorkflowDefinition"/> in <paramref name="assemblies"/>, sorted by full name.</summary>
    public static IReadOnlyList<Type> DeclaredDefinitionTypes(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        return [.. assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                && typeof(IRecordWorkflowDefinition).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];
    }

    private static void RequireRegistered(IReadOnlyList<Type> declared, IReadOnlyList<IRecordWorkflowDefinition> definitions)
    {
        var registeredTypes = definitions.Select(definition => definition.GetType()).ToHashSet();
        var unregistered = declared.Where(type => !registeredTypes.Contains(type)).Select(type => type.FullName).ToList();
        if (unregistered.Count > 0)
        {
            throw new InvalidOperationException(
                "Workflow definitions are declared but not registered with AddRecordWorkflow<TDefinition>(): "
                + string.Join(", ", unregistered) + ".");
        }
    }

    private static string RequireFileSafe(string recordType) =>
        recordType is not null && RecordTypeFormat().IsMatch(recordType)
            ? recordType
            : throw new InvalidOperationException($"The workflow record type '{recordType}' is not a kebab-case key.");

    private static void RequireUnique(IReadOnlyList<WorkflowDoc> workflows)
    {
        var duplicated = workflows
            .GroupBy(workflow => workflow.RecordType, StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .ToList();
        if (duplicated.Count > 0)
        {
            throw new InvalidOperationException($"More than one workflow definition declares the record type(s): {string.Join(", ", duplicated)}.");
        }
    }

    [GeneratedRegex(RecordTypePattern, RegexOptions.CultureInvariant)]
    private static partial Regex RecordTypeFormat();
}
