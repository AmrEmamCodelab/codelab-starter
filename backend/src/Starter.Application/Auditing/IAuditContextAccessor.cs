namespace Starter.Application.Auditing;

/// <summary>
/// Supplies the <see cref="AuditContext"/> of the change being made. Implementations never throw: an unreadable caller
/// is recorded as <see cref="AuditContext.System"/> plus whatever request data exists.
/// </summary>
public interface IAuditContextAccessor
{
    /// <summary>The context of the current request or job.</summary>
    AuditContext Current { get; }
}
