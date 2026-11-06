using Codelab.Observability;
using Microsoft.AspNetCore.Http;
using Starter.Application.Auditing;
using Starter.Application.Users;

namespace Starter.Infrastructure.Users;

/// <summary>
/// <see cref="IAuditContextAccessor"/> over the current request: the acting subject, the remote address and the
/// correlation id (Codelab.Observability). Outside a request, and inside a system scope (<see cref="SystemActorScope"/>:
/// background jobs) even when an HTTP context is reachable, it answers <see cref="AuditContext.System"/> with the
/// current correlation id. It never throws: an unreadable caller is recorded as the system actor with whatever request
/// data exists.
/// </summary>
public sealed class HttpAuditContextAccessor(IHttpContextAccessor accessor, ICurrentUserContext currentUser) : IAuditContextAccessor
{
    /// <inheritdoc />
    public AuditContext Current
    {
        get
        {
            if (SystemActorScope.IsActiveOnCurrentFlow)
            {
                return AuditContext.System with { CorrelationId = CorrelationScope.Current ?? CorrelationId.Current(accessor.HttpContext) };
            }

            var http = accessor.HttpContext;
            if (http is null)
            {
                return AuditContext.System with { CorrelationId = CorrelationId.Current(context: null) };
            }

            return new AuditContext(
                SubjectId: currentUser.SubjectId,
                ClientAddress: http.Connection.RemoteIpAddress?.ToString(),
                CorrelationId: CorrelationId.Current(http));
        }
    }
}
