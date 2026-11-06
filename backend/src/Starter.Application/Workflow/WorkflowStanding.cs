using Starter.Domain.Workflow;

namespace Starter.Application.Workflow;

/// <summary>What the engine knows about the caller when it decides: the permissions held, ownership and the screens acted from.</summary>
/// <param name="HasPermission">True when the caller holds the permission key (resolved live by the caller's module).</param>
/// <param name="IsOwner">True when the caller owns the record.</param>
/// <param name="Side">
/// The screens the caller acts from (the owning module derives it from the caller's audience). Only rules of this side
/// can be taken, the same filter <see cref="RecordWorkflowEngine.AllowedActions"/> applies to the offer.
/// </param>
public sealed record WorkflowStanding(Func<string, bool> HasPermission, bool IsOwner, TransitionSide Side);
