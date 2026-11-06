using Starter.Application.Records;
using Starter.Application.Workflow;
using Starter.Domain.Auditing;
using Starter.Domain.Exceptions;
using Starter.Domain.Records;
using Starter.Domain.Workflow;
using Starter.Modules.Records.Domain;

namespace Starter.Modules.Records.Application;

/// <summary>
/// Any transition of any record by the current user through the workflow engine: the loader, then
/// <see cref="RecordWorkflowEngine.Decide"/> (refusals mapped once by <see cref="WorkflowRefusals.ToRefusal"/>), then the
/// commit. <see cref="RecordLifecycleActions.Submit"/> and <see cref="RecordLifecycleActions.Resubmit"/> always go
/// through <see cref="RecordSubmissions"/>, so the generic endpoint can never skip the submit checks.
/// </summary>
public sealed class RecordTransitions(RecordLoader loader, RecordCommits commits, RecordSubmissions submissions)
{
    /// <summary>Takes <paramref name="actionKey"/> on record <paramref name="recordId"/> of <paramref name="recordType"/>.</summary>
    /// <param name="recordType">The record type (as the route names it).</param>
    /// <param name="recordId">The record id.</param>
    /// <param name="actionKey">The action.</param>
    /// <param name="reason">The reason the caller typed, or null.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task<TransitionResult> TakeAsync(
        string? recordType, long recordId, string actionKey, string? reason, CancellationToken cancellationToken)
    {
        if (RecordLifecycleActions.IsSubmit(actionKey))
        {
            return (await submissions.AttemptAsync(recordType, recordId, actionKey, cancellationToken)).ToTransitionResult();
        }

        var load = await loader.LoadAsync(recordType, recordId, cancellationToken);
        if (!load.IsSuccess)
        {
            return TransitionResult.Refuse(load.Refusal);
        }

        var loaded = load.Value;
        var decision = RecordWorkflowEngine.Decide(loaded.Type.Catalog, loaded.Record.Status, actionKey, reason, loaded.Standing.ForWorkflow());
        if (decision is TransitionDecision.Refused refused)
        {
            return TransitionResult.Refuse(WorkflowRefusals.ToRefusal(refused));
        }

        var committed = await commits.CommitAsync(loaded, (TransitionDecision.Allowed)decision, number: null, cancellationToken);
        return committed.IsSuccess
            ? new TransitionResult.Transitioned(committed.Value)
            : TransitionResult.Refuse(committed.Refusal);
    }
}

/// <summary>
/// The kernel <see cref="IRecordLifecycle"/>: what a record-type module (its typed draft endpoints) calls. Every operation
/// acts as the current user.
/// </summary>
public sealed class RecordLifecycle(RecordDrafts drafts, RecordSubmissions submissions, RecordTransitions transitions) : IRecordLifecycle
{
    /// <inheritdoc />
    /// <exception cref="ArgumentException">No record type <paramref name="recordType"/> is registered (a wiring fault of the caller).</exception>
    /// <exception cref="AccessDeniedException">The caller is no active account, or lacks the record type's create permission.</exception>
    public Task<RecordRef> CreateDraftAsync(string recordType, CancellationToken cancellationToken) =>
        drafts.CreateAsync(recordType, cancellationToken);

    /// <inheritdoc />
    public async Task<SubmitResult> SubmitAsync(RecordRef record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        return (await submissions.AttemptAsync(record.RecordType, record.Id, RecordLifecycleActions.Submit, cancellationToken)).ToSubmitResult();
    }

    /// <inheritdoc />
    public async Task<SubmitResult> ResubmitAsync(RecordRef record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        return (await submissions.AttemptAsync(record.RecordType, record.Id, RecordLifecycleActions.Resubmit, cancellationToken)).ToSubmitResult();
    }

    /// <inheritdoc />
    public Task<TransitionResult> TransitionAsync(RecordRef record, string actionKey, string? reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        return transitions.TakeAsync(record.RecordType, record.Id, actionKey, reason, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TransitionResult> CancelAsync(RecordRef record, string? reason, CancellationToken cancellationToken) =>
        TransitionAsync(record, RecordLifecycleActions.Cancel, reason, cancellationToken);
}

/// <summary>
/// Creates drafts: the caller must be an active account holding the record type's create permission (the permission of
/// its submit rule) at any scope; the record takes the caller's organization and the catalog's initial status, and its
/// first timeline entry (<see cref="RecordLifecycleActions.Create"/>, no from-status) is written with it.
/// </summary>
public sealed class RecordDrafts(RecordTypes types, RecordCallers callers, RecordStandings standings, IRecordStore store, TimeProvider clock)
{
    /// <summary>A new draft of <paramref name="recordType"/> owned by the current user.</summary>
    /// <exception cref="ArgumentException">No record type <paramref name="recordType"/> is registered (a wiring fault of the caller).</exception>
    /// <exception cref="AccessDeniedException">The caller is no active account, or lacks the record type's create permission.</exception>
    public async Task<RecordRef> CreateAsync(string recordType, CancellationToken cancellationToken)
    {
        var type = types.Find(recordType)
            ?? throw new ArgumentException($"No record type '{recordType}' is registered (IRecordTypeRegistrationSource).", nameof(recordType));
        var caller = await callers.CurrentAsync(cancellationToken)
            ?? throw new AccessDeniedException("Only an active account can create a record.");
        if (!(await standings.ScopeForAsync(type.CreatePermissionKey, cancellationToken)).Allowed)
        {
            throw new AccessDeniedException($"Creating a {type.RecordType} needs the permission {type.CreatePermissionKey}.");
        }

        var now = clock.GetUtcNow();
        var initial = type.InitialStatus.Status;
        var record = Record.StartDraft(new RecordDraft(
            type.RecordType, InitialStatus: initial, OwnerUserId: caller.UserId, OrganizationId: caller.OrganizationId, At: now));
        var id = await store.AddDraftAsync(record, recordId => FirstEntry(recordId, initial, caller, now), cancellationToken);
        return new RecordRef(id, type.RecordType);
    }

    private static RecordHistory FirstEntry(long recordId, int initialStatus, RecordCaller caller, DateTimeOffset at) => new(
        new RecordHistoryFacts(
            RecordId: recordId,
            Action: RecordLifecycleActions.Create,
            FromStatus: null,
            ToStatus: initialStatus,
            Reason: null,
            ActorUserId: caller.UserId,
            ActorName: caller.DisplayName,
            At: at),
        TransitionSide.Requester);
}

/// <summary>The kernel <see cref="IRecordDirectory"/>: record facts for other modules (queues, deadlines), no access check.</summary>
public sealed class RecordDirectory(IRecordStore store, RecordTypes types) : IRecordDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordSummary>> GetAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.Count == 0 ? [] : [.. (await store.ListAsync(ids, cancellationToken)).Select(ToSummary)];
    }

    /// <inheritdoc />
    /// <remarks>Records never submitted (drafts) are left out: they are private to their owner.</remarks>
    public async Task<PagedResult<RecordSummary>> QueryAsync(RecordQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = await store.PageAsync(new RecordPageQuery(query, OwnerUserId: null, SubmittedOnly: true), cancellationToken);
        return new PagedResult<RecordSummary>([.. page.Items.Select(ToSummary)], page.Page, page.PageSize, page.Total);
    }

    /// <summary>The summary of <paramref name="record"/> (terminal per its type's catalog; false for an unregistered type).</summary>
    public RecordSummary ToSummary(Record record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new RecordSummary(
            record.Id,
            record.RecordType,
            record.Number,
            record.Status,
            OwnerUserId: record.OwnerUserId,
            OrganizationId: record.OrganizationId,
            CreatedAt: record.CreatedAt,
            SubmittedAt: record.SubmittedAt,
            UpdatedAt: record.UpdatedAt,
            IsTerminal: types.Find(record.RecordType)?.Catalog.IsTerminal(record.Status) == true);
    }
}
