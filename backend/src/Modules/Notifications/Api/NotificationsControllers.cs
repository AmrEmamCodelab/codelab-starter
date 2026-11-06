using Codelab.Security.PublicIds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Application.Results;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Results;
using Starter.Modules.Notifications.Application;
using Starter.Modules.Notifications.Application.Feed;

namespace Starter.Modules.Notifications.Api;

/// <summary>The caller's own notifications. Any signed-in user may read and mark their own (allowlisted).</summary>
[ApiController]
[Route(NotificationsRoutes.Root)]
[Produces(NotificationsRoutes.JsonContentType)]
[AuthenticatedUser]
public sealed class NotificationsController(
    GetNotificationFeed getFeed,
    GetUnreadNotificationCount getUnreadCount,
    MarkNotificationRead markRead) : ControllerBase
{
    /// <summary>One page of the caller's notifications, newest first, in the request language.</summary>
    [HttpGet]
    [ProducesResponseType<NotificationPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NotificationPageResponse>> ListAsync(
        [FromQuery(Name = NotificationsRoutes.BeforeParameter)] PublicId<NotificationIds>? before,
        [FromQuery(Name = NotificationsRoutes.TakeParameter)] int? take,
        CancellationToken cancellationToken) =>
        (await getFeed.ExecuteAsync(new FeedRequest(before?.Value, take), cancellationToken)).ToActionResult(NotificationPageResponse.From);

    /// <summary>How many of the caller's notifications are unread.</summary>
    [HttpGet(NotificationsRoutes.UnreadCount)]
    [ProducesResponseType<UnreadCountResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadCountResponse>> UnreadCountAsync(CancellationToken cancellationToken) =>
        (await getUnreadCount.ExecuteAsync(cancellationToken)).ToActionResult(count => new UnreadCountResponse(count));

    /// <summary>Marks one of the caller's notifications read (idempotent).</summary>
    [HttpPost(NotificationsRoutes.Read)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> MarkReadAsync(PublicId<NotificationIds> id, CancellationToken cancellationToken) =>
        (await markRead.ExecuteAsync(id.Value, cancellationToken)).ToNoContentResult();
}

/// <summary>The administrator's notification tools.</summary>
[ApiController]
[Route(NotificationsRoutes.Admin)]
[Produces(NotificationsRoutes.JsonContentType)]
public sealed class AdminNotificationsController(SendTestNotification sendTest) : ControllerBase
{
    /// <summary>Sends the caller a test notification: the bell now, e-mail (and SMS when enabled) through the outbox.</summary>
    [HttpPost(NotificationsRoutes.Test)]
    [HasPermission(NotificationsPermissionKeys.Test)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SendTestAsync(CancellationToken cancellationToken) =>
        (await sendTest.ExecuteAsync(cancellationToken)).ToAcceptedResult();
}

/// <summary>The 202 mapping the kernel <c>OutcomeResults</c> does not have yet (one owner in this module).</summary>
public static class NotificationsResults
{
    /// <summary>202 without a body, or the refusal's problem+json.</summary>
    public static ActionResult ToAcceptedResult<T>(this Outcome<T> outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.IsSuccess ? new StatusCodeResult(StatusCodes.Status202Accepted) : outcome.Refusal.ToActionResult();
    }
}
