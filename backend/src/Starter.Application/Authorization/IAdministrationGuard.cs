namespace Starter.Application.Authorization;

/// <summary>
/// Privilege checks for account administration (implemented by the Access module, consumed by the Users module), so
/// a module that changes accounts never references Access. Live: it reads the current grants on every call.
/// </summary>
public interface IAdministrationGuard
{
    /// <summary>
    /// True when the current caller holds every permission the account with token subject
    /// <paramref name="targetSubjectId"/> would hold while active, each at least as widely (a global grant covers every
    /// scope; an own-organization grant covers an own-organization grant in the same organization). False for an
    /// anonymous caller or an unknown target. A caller may only reset, re-key, rebind, move or switch off an account
    /// whose access they could have granted themselves.
    /// </summary>
    Task<bool> CallerCoversAsync(Guid targetSubjectId, CancellationToken cancellationToken);

    /// <summary>
    /// True when switching off user <paramref name="userId"/> would leave no active access manager other than
    /// break-glass, where there was one before (the same rule as the grant editor's self-lockout guard).
    /// </summary>
    Task<bool> IsLastAccessManagerAsync(long userId, CancellationToken cancellationToken);
}
