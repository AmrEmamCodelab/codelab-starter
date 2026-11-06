using Codelab.Security.PublicIds;
using Starter.Modules.Notifications.Application;
using Starter.Modules.Notifications.Application.Feed;

namespace Starter.Modules.Notifications.Api;

/// <summary>One bell item on the wire (texts already in the request language).</summary>
/// <param name="Id">The notification's PublicId.</param>
/// <param name="TitleText">The title.</param>
/// <param name="BodyText">The body (plain text).</param>
/// <param name="At">When it was created (ISO 8601, UTC).</param>
/// <param name="Read">Whether the caller has read it.</param>
/// <param name="Link">The app-relative link, or null.</param>
public sealed record NotificationResponse(PublicId<NotificationIds> Id, string TitleText, string BodyText, DateTimeOffset At, bool Read, string? Link)
{
    /// <summary>The wire shape of <paramref name="item"/>.</summary>
    public static NotificationResponse From(FeedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new NotificationResponse(PublicId<NotificationIds>.From(item.Id), item.TitleText, item.BodyText, item.At, item.Read, item.Link);
    }
}

/// <summary>One feed page: <c>{items, hasMore}</c>.</summary>
/// <param name="Items">The items, newest first.</param>
/// <param name="HasMore">True when older items exist (ask again with <c>before</c> = the last item's id).</param>
public sealed record NotificationPageResponse(IReadOnlyList<NotificationResponse> Items, bool HasMore)
{
    /// <summary>The wire shape of <paramref name="page"/>.</summary>
    public static NotificationPageResponse From(FeedPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new NotificationPageResponse(page.Items.Select(NotificationResponse.From).ToList(), page.HasMore);
    }
}

/// <summary>The unread count: <c>{count}</c>.</summary>
/// <param name="Count">How many of the caller's notifications are unread.</param>
public sealed record UnreadCountResponse(int Count);
