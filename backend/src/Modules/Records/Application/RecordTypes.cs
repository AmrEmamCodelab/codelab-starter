using System.Globalization;
using Starter.Application.Records;
using Starter.Application.Workflow;
using Starter.Domain.Workflow;

namespace Starter.Modules.Records.Application;

/// <summary>
/// One record type as the Records module serves it: its registration (number prefix, view-own and queue permissions,
/// attachable record type) and its workflow catalog. Built only by <see cref="RecordTypes"/>, which guarantees the
/// catalog has one initial status and a <see cref="RecordLifecycleActions.Submit"/> rule out of it.
/// </summary>
public sealed class RegisteredRecordType
{
    internal RegisteredRecordType(RecordTypeRegistration registration, TransitionCatalog catalog, WorkflowStatusDefinition initial)
    {
        Registration = registration;
        Catalog = catalog;
        InitialStatus = initial;
        PermissionKeys = [.. catalog.Rules.Select(rule => rule.PermissionKey)
            .Append(registration.ViewOwnPermissionKey)
            .Append(registration.QueuePermissionKey)
            .Append(RecordsPermissionKeys.OversightView)
            .Distinct(StringComparer.Ordinal)];
        RequesterPermissionKeys = catalog.Rules
            .Where(rule => rule.Side == TransitionSide.Requester)
            .Select(rule => rule.PermissionKey)
            .Append(registration.ViewOwnPermissionKey)
            .ToHashSet(StringComparer.Ordinal);
        CreatePermissionKey = catalog.RulesFrom(initial.Status)
            .First(rule => string.Equals(rule.ActionKey, RecordLifecycleActions.Submit, StringComparison.Ordinal)).PermissionKey;
    }

    /// <summary>The record type key.</summary>
    public string RecordType => Registration.RecordType;

    /// <summary>The registration the owning module declared.</summary>
    public RecordTypeRegistration Registration { get; }

    /// <summary>The record type's workflow.</summary>
    public TransitionCatalog Catalog { get; }

    /// <summary>The status a new draft starts in.</summary>
    public WorkflowStatusDefinition InitialStatus { get; }

    /// <summary>Every permission key a decision about this record type can need (catalog rules, view own, queue, oversight).</summary>
    public IReadOnlyList<string> PermissionKeys { get; }

    /// <summary>
    /// The keys a requester uses on their own records: the view-own key and the keys of the requester-side rules. Only
    /// these count for the owner at any scope (<see cref="RecordStanding.Holds"/>).
    /// </summary>
    public IReadOnlySet<string> RequesterPermissionKeys { get; }

    /// <summary>The permission that lets a requester create a draft: the one of the submit rule out of the initial status.</summary>
    public string CreatePermissionKey { get; }

    /// <summary>The kebab key of <paramref name="status"/>, or its number when the catalog does not define it.</summary>
    public string StatusKeyOf(int status) => Catalog.FindStatus(status)?.Key ?? status.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// True when the owner may still change the record's own data and files in <paramref name="status"/>: the initial
    /// status, and every status a <see cref="RecordLifecycleActions.Resubmit"/> rule leaves (returned to the requester).
    /// </summary>
    public bool IsEditable(int status) =>
        status == InitialStatus.Status
        || Catalog.RulesFrom(status).Any(rule => string.Equals(rule.ActionKey, RecordLifecycleActions.Resubmit, StringComparison.Ordinal));
}

/// <summary>
/// Every record type of the host: each <see cref="IRecordTypeRegistrationSource"/> registration joined to its workflow
/// catalog. A singleton; <see cref="Infrastructure.RecordTypesStartupCheck"/> builds it at start so a wiring fault
/// stops the boot instead of the first request.
/// </summary>
public sealed class RecordTypes
{
    private readonly Dictionary<string, RegisteredRecordType> _byRecordType = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RegisteredRecordType> _byAttachableType = new(StringComparer.Ordinal);

    /// <summary>Joins every registration to its catalog.</summary>
    /// <exception cref="InvalidOperationException">
    /// The registrations are inconsistent; the message lists every problem (see <see cref="RecordTypeProblems.Find"/>).
    /// </exception>
    public RecordTypes(IEnumerable<IRecordTypeRegistrationSource> sources, IRecordWorkflowCatalogs catalogs)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(catalogs);
        var registrations = sources.SelectMany(source => source.Registrations).ToList();
        var problems = RecordTypeProblems.Find(registrations, catalogs);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "The record type registrations are invalid:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        foreach (var registration in registrations)
        {
            var catalog = catalogs.Find(registration.RecordType)!;
            var type = new RegisteredRecordType(registration, catalog, catalog.Statuses.Single(status => status.IsInitial));
            _byRecordType.Add(type.RecordType, type);
            _byAttachableType.Add(registration.AttachableRecordType, type);
        }

        All = [.. _byRecordType.Values.OrderBy(type => type.RecordType, StringComparer.Ordinal)];
    }

    /// <summary>Every record type, ordered by key.</summary>
    public IReadOnlyList<RegisteredRecordType> All { get; }

    /// <summary>The record type <paramref name="recordType"/>, or null when nothing registers it (also for a malformed key).</summary>
    public RegisteredRecordType? Find(string? recordType) =>
        recordType is null ? null : _byRecordType.GetValueOrDefault(recordType);

    /// <summary>The record type whose attachable record type is <paramref name="attachableRecordType"/>, or null.</summary>
    public RegisteredRecordType? FindByAttachableType(string? attachableRecordType) =>
        attachableRecordType is null ? null : _byAttachableType.GetValueOrDefault(attachableRecordType);
}

/// <summary>The consistency rules of the record type registrations (one owner, pure, so they are tested directly).</summary>
public static class RecordTypeProblems
{
    /// <summary>
    /// Every problem: a record type or attachable record type registered twice, a registration with no workflow catalog,
    /// a catalog without exactly one initial status, and a catalog without a submit rule out of its initial status.
    /// </summary>
    public static IReadOnlyList<string> Find(IReadOnlyList<RecordTypeRegistration> registrations, IRecordWorkflowCatalogs catalogs)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(catalogs);
        var problems = new List<string>();
        problems.AddRange(Duplicates(registrations.Select(registration => registration.RecordType), "record type"));
        problems.AddRange(Duplicates(registrations.Select(registration => registration.AttachableRecordType), "attachable record type"));
        foreach (var registration in registrations)
        {
            problems.AddRange(CatalogProblems(registration.RecordType, catalogs.Find(registration.RecordType)));
        }

        return problems;
    }

    private static IEnumerable<string> Duplicates(IEnumerable<string> keys, string what) =>
        keys.GroupBy(key => key, StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .Select(group => $"The {what} '{group.Key}' is registered more than once.");

    private static IEnumerable<string> CatalogProblems(string recordType, TransitionCatalog? catalog)
    {
        if (catalog is null)
        {
            yield return $"The record type '{recordType}' has no workflow; call AddRecordWorkflow<TDefinition>() in its module.";
            yield break;
        }

        var initial = catalog.Statuses.Where(status => status.IsInitial).ToList();
        if (initial.Count != 1)
        {
            yield return $"The workflow of '{recordType}' needs exactly one initial status.";
            yield break;
        }

        if (!catalog.RulesFrom(initial[0].Status).Any(rule => string.Equals(rule.ActionKey, RecordLifecycleActions.Submit, StringComparison.Ordinal)))
        {
            yield return $"The workflow of '{recordType}' has no '{RecordLifecycleActions.Submit}' rule out of its initial status.";
        }
    }
}
