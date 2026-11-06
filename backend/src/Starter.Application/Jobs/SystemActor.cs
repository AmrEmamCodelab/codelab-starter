using Starter.Application.Auditing;

namespace Starter.Application.Jobs;

/// <summary>
/// The actor of work no person does: background jobs, outbox delivery, startup tasks. It is a name, never a user
/// account or a role, so a job's writes can never be mistaken for (or authorized as) a person's.
/// </summary>
public static class SystemActor
{
    /// <summary>The actor name recorded on job runs and in the audit trail (<see cref="AuditActors.System"/>).</summary>
    public const string Name = AuditActors.System;
}

/// <summary>
/// Marks the current async flow as system work. Inside a scope <c>ICurrentUserContext</c> reads as anonymous (no
/// subject, no session) and <c>IAuditContextAccessor</c> answers <see cref="AuditContext.System"/>, even when an HTTP
/// context is reachable (for example one that leaked into a background task). The jobs module opens one around every
/// run; open one yourself around any other work that must never be attributed to the caller.
/// </summary>
public interface ISystemActorScope
{
    /// <summary>True while a system scope is open on the current async flow.</summary>
    bool IsActive { get; }

    /// <summary>Opens a system scope until the returned handle is disposed (scopes nest; disposal restores the outer state).</summary>
    IDisposable BeginSystemScope();
}
