using Codelab.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Starter.Application.Auditing;
using Starter.Modules.Identity.Application.Auditing;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Infrastructure.Persistence;

namespace Starter.Modules.Identity.Infrastructure.Auditing;

/// <summary>
/// Writes the authentication trail to <c>identity.AuthEvents</c> and mirrors every event to the log first.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own short-lived DbContext.</b> The request's Identity context may hold pending changes of the deciding code
/// (a failure counter, a lockout); saving it here would commit them as a side effect of an audit row. A private
/// context built from the same registered options makes this write mean exactly itself.
/// </para>
/// <para>
/// <b>Fails open, deliberately.</b> The decision being recorded is a sign-in: a full log or a blocked write must not
/// stop every user, operators included, from signing in. The event is logged BEFORE the database write, so a failed
/// write downgrades the trail without losing the event (the log line says the row is missing). Cancellation
/// propagates.
/// </para>
/// <para>
/// <b>No personal data.</b> The typed identifier is stored and logged only as the Codelab.Observability keyed
/// <see cref="UserIdHash"/>; the user only as the internal id. No password, token or ticket ever reaches the sink.
/// </para>
/// </remarks>
public sealed partial class EfAuthEventSink(
    DbContextOptions<IdentityDbContext> options,
    IAuditContextAccessor auditContext,
    UserIdHash userHash,
    TimeProvider clock,
    ILogger<EfAuthEventSink> logger) : IAuthEventSink
{
    /// <inheritdoc />
    public async Task RecordAsync(AuthEventRecord authEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authEvent);
        var row = AuthEvent.Record(new NewAuthEvent(
            EventType: authEvent.EventType,
            FailureReason: authEvent.Reason,
            UserId: authEvent.UserId,
            UserNameHash: userHash.Of(authEvent.UserName),
            SignInMode: authEvent.Mode,
            OccurredAt: clock.GetUtcNow(),
            CorrelationId: auditContext.Current.CorrelationId));
        LogEvent(row);
        try
        {
            await using var context = new IdentityDbContext(options);
            context.AuthEvents.Add(row);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Load-bearing: the sign-in continues; the event is already in the log (see the remarks).
            LogPersistFailed(logger, ex, row.EventType, row.CorrelationId);
        }
    }

    private void LogEvent(AuthEvent row)
    {
        if (row.FailureReason is null)
        {
            LogSucceeded(logger, row.EventType, row.UserId, row.SignInMode, row.CorrelationId);
        }
        else
        {
            LogRefused(logger, row.EventType, row.FailureReason, row.UserNameHash, row.CorrelationId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "auth-event {EventType} user={UserId} mode={SignInMode} correlation={CorrelationId}")]
    private static partial void LogSucceeded(ILogger logger, AuthEventType eventType, long? userId, SignInMode? signInMode, string? correlationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "auth-event {EventType} REFUSED reason={FailureReason} name={UserNameHash} correlation={CorrelationId}")]
    private static partial void LogRefused(ILogger logger, AuthEventType eventType, AuthFailureReason? failureReason, string? userNameHash, string? correlationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "auth-event {EventType} was logged but its identity.AuthEvents row was not written; the trail is incomplete (correlation={CorrelationId}).")]
    private static partial void LogPersistFailed(ILogger logger, Exception exception, AuthEventType eventType, string? correlationId);
}
