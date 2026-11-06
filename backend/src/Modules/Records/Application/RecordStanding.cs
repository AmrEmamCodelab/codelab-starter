using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Application.Users;
using Starter.Application.Workflow;
using Starter.Domain.Workflow;
using Starter.Modules.Records.Domain;

namespace Starter.Modules.Records.Application;

/// <summary>The caller of a Records operation: an active account, resolved from the token subject on every request.</summary>
/// <param name="UserId">The internal user id.</param>
/// <param name="DisplayName">The name the timeline keeps for the actor.</param>
/// <param name="OrganizationId">The caller's organization (internal id), or null.</param>
public sealed record RecordCaller(long UserId, string DisplayName, long? OrganizationId);

/// <summary>Resolves the <see cref="RecordCaller"/> through the kernel <see cref="IUserDirectory"/> port (Identity).</summary>
public sealed class RecordCallers(ICurrentUserContext currentUser, IUserDirectory users)
{
    /// <summary>The actor name kept when the account has no display name.</summary>
    public const string UnnamedActor = "-";

    /// <summary>The caller, or null when the request carries no subject or the subject names no active account.</summary>
    public async Task<RecordCaller?> CurrentAsync(CancellationToken cancellationToken)
    {
        if (currentUser.SubjectId is not { } subject)
        {
            return null;
        }

        var user = await users.FindBySubjectAsync(subject, cancellationToken);
        return user is { IsActive: true }
            ? new RecordCaller(user.UserId, string.IsNullOrWhiteSpace(user.DisplayName) ? UnnamedActor : user.DisplayName, user.OrganizationId)
            : null;
    }
}

/// <summary>
/// What one caller may do with one record, decided once per request from the live permission resolve: the audience the
/// caller sees the record as (or none: the record is hidden), and which permission keys count.
/// </summary>
/// <remarks>
/// A requester key (<see cref="RegisteredRecordType.RequesterPermissionKeys"/>) counts for the OWNER when held at any
/// scope (the requester acts on their own record); every other key counts, for the owner too, only when its scope
/// reaches the record's organization (<see cref="ScopeDecision.Reaches"/>). The audience is
/// <see cref="WorkflowAudience.Requester"/> for the owner holding the view-own key, otherwise
/// <see cref="WorkflowAudience.Staff"/> with the queue key, otherwise <see cref="WorkflowAudience.Admin"/> with
/// <see cref="RecordsPermissionKeys.OversightView"/>. An owner never decides on their own record as staff: the workflow
/// engine refuses every staff rule to the owner (segregation of duties). A record never submitted (a draft) is private
/// to its owner.
/// </remarks>
public sealed class RecordStanding
{
    private readonly IReadOnlyDictionary<string, ScopeDecision> _decisions;
    private readonly IReadOnlySet<string> _requesterKeys;
    private readonly long? _organizationId;

    /// <summary>The standing of <paramref name="caller"/> on <paramref name="record"/> of <paramref name="type"/>.</summary>
    public RecordStanding(RecordCaller caller, Record record, RegisteredRecordType type, IReadOnlyDictionary<string, ScopeDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(decisions);
        Caller = caller;
        _decisions = decisions;
        _requesterKeys = type.RequesterPermissionKeys;
        _organizationId = record.OrganizationId;
        IsOwner = record.IsOwnedBy(caller.UserId);
        Audience = AudienceOf(type.Registration, wasSubmitted: record.SubmittedAt is not null);
    }

    /// <summary>The caller.</summary>
    public RecordCaller Caller { get; }

    /// <summary>True when the caller owns the record.</summary>
    public bool IsOwner { get; }

    /// <summary>How the caller sees the record, or null when the record is hidden from them.</summary>
    public WorkflowAudience? Audience { get; }

    /// <summary>True when the caller may see the record at all.</summary>
    public bool CanView => Audience is not null;

    /// <summary>The screens the caller acts from: requester for the requester audience, staff otherwise.</summary>
    public TransitionSide Side => Audience is WorkflowAudience.Requester ? TransitionSide.Requester : TransitionSide.Staff;

    /// <summary>True when <paramref name="permissionKey"/> counts for this caller on this record.</summary>
    public bool Holds(string permissionKey) =>
        _decisions.TryGetValue(permissionKey, out var decision)
        && (IsOwner && _requesterKeys.Contains(permissionKey) ? decision.Allowed : decision.Reaches(_organizationId));

    /// <summary>The workflow engine's view of the caller, acting from <see cref="Side"/>.</summary>
    public WorkflowStanding ForWorkflow() => new(Holds, IsOwner, Side);

    private WorkflowAudience? AudienceOf(RecordTypeRegistration registration, bool wasSubmitted)
    {
        if (IsOwner && Holds(registration.ViewOwnPermissionKey))
        {
            return WorkflowAudience.Requester;
        }

        if (!wasSubmitted)
        {
            return null;
        }

        if (Holds(registration.QueuePermissionKey))
        {
            return WorkflowAudience.Staff;
        }

        return Holds(RecordsPermissionKeys.OversightView) ? WorkflowAudience.Admin : null;
    }
}

/// <summary>Builds <see cref="RecordStanding"/> from the kernel <see cref="IOrganizationScope"/> (live, per request).</summary>
public sealed class RecordStandings(IOrganizationScope scope)
{
    /// <summary>The standing of <paramref name="caller"/> on <paramref name="record"/>: one scope decision per key of the type.</summary>
    public async Task<RecordStanding> ForAsync(RecordCaller caller, Record record, RegisteredRecordType type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(type);
        var decisions = new Dictionary<string, ScopeDecision>(StringComparer.Ordinal);
        foreach (var key in type.PermissionKeys)
        {
            decisions[key] = await scope.ScopeForAsync(key, cancellationToken);
        }

        return new RecordStanding(caller, record, type, decisions);
    }

    /// <summary>The caller's decision for one key (lists and draft creation, where no record exists yet).</summary>
    public Task<ScopeDecision> ScopeForAsync(string permissionKey, CancellationToken cancellationToken) =>
        scope.ScopeForAsync(permissionKey, cancellationToken);
}
