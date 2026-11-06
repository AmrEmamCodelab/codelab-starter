using Starter.Domain.Workflow;

namespace Starter.Application.Workflow;

/// <summary>The caller of a workflow operation, as the owning module resolved it for this request.</summary>
/// <param name="UserId">The caller's internal user id (compared with the record's owner).</param>
/// <param name="Side">The screens the caller acts from; selects the allowed actions offered.</param>
/// <param name="PermissionKeys">The permission keys the caller holds now (resolved live, never from the token).</param>
public sealed record WorkflowActor(long UserId, TransitionSide Side, IReadOnlySet<string> PermissionKeys);
