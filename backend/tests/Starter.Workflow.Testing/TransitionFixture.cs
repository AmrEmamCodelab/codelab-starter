using Starter.Application.Workflow;
using Starter.Domain.Workflow;

namespace Starter.Workflow.Testing;

/// <summary>
/// A fluent, immutable way to assert a transition table through the real <see cref="RecordWorkflowEngine"/>:
/// <code>
/// TransitionFixture.For(catalog).From(MyStatuses.Submitted).WithPermissions(MyPermissionKeys.Review)
///     .Do(MyActions.StartReview).ExpectStatus(MyStatuses.UnderReview)
///     .Do(MyActions.Return).ExpectRefused(WorkflowRefusal.ReasonRequired);
/// </code>
/// Every step returns a new fixture, so a shared starting point can branch. A failed expectation throws
/// <see cref="WorkflowExpectationException"/>.
/// </summary>
public sealed class TransitionFixture
{
    private TransitionFixture(TransitionCatalog catalog, int status, IReadOnlySet<string> permissions, bool isOwner, TransitionSide? side)
    {
        Catalog = catalog;
        Status = status;
        Permissions = permissions;
        IsOwner = isOwner;
        Side = side;
    }

    /// <summary>The catalog under test.</summary>
    public TransitionCatalog Catalog { get; }

    /// <summary>The status the next action starts from.</summary>
    public int Status { get; }

    /// <summary>The permission keys the actor holds.</summary>
    public IReadOnlySet<string> Permissions { get; }

    /// <summary>True when the actor owns the record.</summary>
    public bool IsOwner { get; }

    /// <summary>
    /// The side <see cref="ExpectAllowedActions"/> asks for (null: every side) and <see cref="Do"/> acts from (null: the
    /// requester side for the owner, the staff side otherwise).
    /// </summary>
    public TransitionSide? Side { get; }

    /// <summary>A fixture at the catalog's initial status (the first one when the catalog is invalid), no permissions, not the owner.</summary>
    public static TransitionFixture For(TransitionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var initial = catalog.Statuses.FirstOrDefault(status => status.IsInitial)
            ?? throw new WorkflowExpectationException($"Catalog '{catalog.RecordType}' has no initial status.");
        return new TransitionFixture(catalog, initial.Status, new HashSet<string>(StringComparer.Ordinal), isOwner: false, side: null);
    }

    /// <summary>Starts from <paramref name="status"/>.</summary>
    public TransitionFixture From(int status) => new(Catalog, status, Permissions, IsOwner, Side);

    /// <summary>The actor holds exactly <paramref name="permissionKeys"/>.</summary>
    public TransitionFixture WithPermissions(params string[] permissionKeys)
    {
        ArgumentNullException.ThrowIfNull(permissionKeys);
        return new TransitionFixture(Catalog, Status, permissionKeys.ToHashSet(StringComparer.Ordinal), IsOwner, Side);
    }

    /// <summary>The actor owns the record.</summary>
    public TransitionFixture AsOwner() => new(Catalog, Status, Permissions, isOwner: true, Side);

    /// <summary>The actor does not own the record.</summary>
    public TransitionFixture AsNonOwner() => new(Catalog, Status, Permissions, isOwner: false, Side);

    /// <summary><see cref="ExpectAllowedActions"/> asks for <paramref name="side"/> only, and <see cref="Do"/> acts from it.</summary>
    public TransitionFixture OnSide(TransitionSide side) => new(Catalog, Status, Permissions, IsOwner, side);

    /// <summary>
    /// Asserts the allowed actions from the current status are exactly <paramref name="actionKeys"/> (any order); for
    /// <see cref="Side"/> when set, otherwise across every side. Pass none to assert nothing is offered.
    /// </summary>
    public TransitionFixture ExpectAllowedActions(params string[] actionKeys)
    {
        ArgumentNullException.ThrowIfNull(actionKeys);
        var sides = Side is { } side ? [side] : Enum.GetValues<TransitionSide>();
        var actual = sides
            .SelectMany(each => RecordWorkflowEngine.AllowedActions(Catalog, Status, Permissions.Contains, IsOwner, each))
            .Select(action => action.ActionKey)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var expected = actionKeys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new WorkflowExpectationException(
                $"From status {Status} expected allowed actions [{string.Join(", ", expected)}] but got [{string.Join(", ", actual)}].");
        }

        return this;
    }

    /// <summary>
    /// Asks the engine to take <paramref name="actionKey"/> from the current status, acting from <see cref="Side"/> when
    /// set, otherwise from the requester screens as the owner and from the staff screens as anyone else.
    /// </summary>
    public TransitionOutcome Do(string actionKey, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(actionKey);
        var side = Side ?? (IsOwner ? TransitionSide.Requester : TransitionSide.Staff);
        var decision = RecordWorkflowEngine.Decide(Catalog, Status, actionKey, reason, new WorkflowStanding(Permissions.Contains, IsOwner, side));
        return new TransitionOutcome(this, actionKey, decision);
    }
}
