namespace Starter.Domain.Exceptions;

/// <summary>
/// The caller may not act on this resource (ERROR-HANDLING row 1); the host maps it to 403. Permission checks belong
/// to <c>[HasPermission]</c>; this is for rules a permission cannot express (ownership, organization scope).
/// The message is logged, never sent.
/// </summary>
public sealed class AccessDeniedException(string message) : Exception(message);
