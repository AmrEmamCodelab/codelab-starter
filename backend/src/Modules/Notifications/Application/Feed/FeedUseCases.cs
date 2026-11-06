using Starter.Application.Localization;
using Starter.Application.Results;
using Starter.Application.Settings;
using Starter.Application.Users;
using Starter.Domain.Localization;
using Starter.Modules.Notifications.Domain;

namespace Starter.Modules.Notifications.Application.Feed;

/// <summary>
/// The caller as a feed owner: the token subject resolved to the internal user id through the kernel
/// <see cref="IUserDirectory"/> port (Identity), so this module never references Identity.
/// </summary>
public sealed class CurrentFeedOwner(ICurrentUserContext currentUser, IUserDirectory users)
{
    /// <summary>The caller's feed, or null when the caller has no user account.</summary>
    public async Task<FeedOwner?> FindAsync(CancellationToken cancellationToken)
    {
        if (currentUser.SubjectId is not { } subject)
        {
            return null;
        }

        var user = await users.FindBySubjectAsync(subject, cancellationToken);
        return user is null ? null : new FeedOwner(user.UserId);
    }
}

/// <summary>A feed page request as the caller sent it.</summary>
/// <param name="BeforeId">The internal id of the oldest item the caller has (from its PublicId), or null for the newest page.</param>
/// <param name="Take">The page size asked for, or null for the administrator's default page size.</param>
public sealed record FeedRequest(long? BeforeId, int? Take)
{
    /// <summary>
    /// The page size to read: a missing or non-positive size is <paramref name="defaultPageSize"/> (the runtime setting
    /// <see cref="SettingKeys.NotificationsPageSize"/>); either is capped at <see cref="NotificationsLimits.MaxFeedPageSize"/>
    /// and is at least one.
    /// </summary>
    public int PageSize(int defaultPageSize) =>
        Math.Clamp(Take is > 0 ? Take.Value : defaultPageSize, 1, NotificationsLimits.MaxFeedPageSize);
}

/// <summary>One bell item in the caller's language.</summary>
/// <param name="Id">The internal id (the wire carries its PublicId).</param>
/// <param name="TitleText">The title.</param>
/// <param name="BodyText">The body (plain text).</param>
/// <param name="At">When it was created.</param>
/// <param name="Read">Whether the caller has read it.</param>
/// <param name="Link">The app-relative link, or null.</param>
public sealed record FeedItem(long Id, string TitleText, string BodyText, DateTimeOffset At, bool Read, string? Link)
{
    /// <summary>The item of <paramref name="notification"/> in <paramref name="language"/>.</summary>
    public static FeedItem Of(Notification notification, AppLanguage language)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return new FeedItem(
            notification.Id,
            notification.TitleIn(language),
            notification.BodyIn(language),
            notification.CreatedAt,
            Read: !notification.IsUnread,
            notification.Link);
    }
}

/// <summary>One feed page, newest first.</summary>
/// <param name="Items">The items.</param>
/// <param name="HasMore">True when older items exist.</param>
public sealed record FeedPage(IReadOnlyList<FeedItem> Items, bool HasMore)
{
    /// <summary>The empty page (a caller without an account).</summary>
    public static FeedPage Empty { get; } = new([], HasMore: false);
}

/// <summary>
/// The caller's bell items older than a cursor, newest first, in the request language. A request without a page size
/// gets the runtime setting <see cref="SettingKeys.NotificationsPageSize"/>, so an administrator's change applies within
/// the settings cache TTL.
/// </summary>
public sealed class GetNotificationFeed(CurrentFeedOwner owners, INotificationStore store, ICurrentLanguage language, IRuntimeSettings settings)
{
    /// <summary>One page; empty for a caller without an account.</summary>
    public async Task<Outcome<FeedPage>> ExecuteAsync(FeedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await owners.FindAsync(cancellationToken) is not { } owner)
        {
            return FeedPage.Empty;
        }

        var pageSize = request.PageSize(defaultPageSize: settings.GetInt32(SettingKeys.NotificationsPageSize));
        var rows = await store.ListInAppAsync(new FeedQuery(owner, request.BeforeId, pageSize + 1), cancellationToken);
        var items = rows.Take(pageSize).Select(row => FeedItem.Of(row, language.Language)).ToList();
        return new FeedPage(items, HasMore: rows.Count > pageSize);
    }
}

/// <summary>How many of the caller's bell items are unread (the badge).</summary>
public sealed class GetUnreadNotificationCount(CurrentFeedOwner owners, INotificationStore store)
{
    /// <summary>The count; 0 for a caller without an account.</summary>
    public async Task<Outcome<int>> ExecuteAsync(CancellationToken cancellationToken) =>
        await owners.FindAsync(cancellationToken) is { } owner ? await store.CountUnreadAsync(owner, cancellationToken) : 0;
}

/// <summary>Marks one of the caller's bell items read (idempotent; another user's item is not found).</summary>
public sealed class MarkNotificationRead(CurrentFeedOwner owners, INotificationStore store, TimeProvider timeProvider)
{
    /// <summary>Done, or <see cref="NotificationsErrorCodes.NotificationNotFound"/>.</summary>
    public async Task<Outcome<Done>> ExecuteAsync(long notificationId, CancellationToken cancellationToken)
    {
        var owner = await owners.FindAsync(cancellationToken);
        var notification = owner is null ? null : await store.FindInAppAsync(owner, notificationId, cancellationToken);
        if (notification is null)
        {
            return Refusal.NotFound(NotificationsErrorCodes.NotificationNotFound);
        }

        if (notification.MarkRead(timeProvider.GetUtcNow()))
        {
            await store.SaveChangesAsync(cancellationToken);
        }

        return Done.Value;
    }
}
