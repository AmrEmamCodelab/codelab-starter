using Codelab.Security.Sessions;
using Microsoft.AspNetCore.Http;
using Starter.Application.Users;

namespace Starter.Infrastructure.Users;

/// <summary>
/// <see cref="ICurrentUserContext"/> over the validated principal of the current request: the subject from the
/// <c>sub</c> claim (a GUID) and the session id from <c>sid</c>. A missing or malformed subject reads as null, so
/// callers fail closed. Inside a system scope (<see cref="SystemActorScope"/>: background jobs) it reads as anonymous
/// whatever HTTP context is reachable, so system work is never attributed to a person.
/// </summary>
public sealed class HttpCurrentUserContext(IHttpContextAccessor accessor) : ICurrentUserContext
{
    /// <inheritdoc />
    public bool IsAuthenticated =>
        !SystemActorScope.IsActiveOnCurrentFlow && accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public Guid? SubjectId =>
        IsAuthenticated && Guid.TryParse(Claim(SecurityClaimTypes.Subject), out var subject) ? subject : null;

    /// <inheritdoc />
    public string? SessionId => IsAuthenticated ? Claim(SecurityClaimTypes.SessionId) : null;

    private string? Claim(string type) => accessor.HttpContext?.User.FindFirst(type)?.Value;
}
