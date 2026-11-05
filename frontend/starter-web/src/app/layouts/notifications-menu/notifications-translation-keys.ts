/** The texts of the notifications menu and feed, under `layouts.notifications.*` in both bundles. */
export const NotificationsTranslationKeys = {
  /** Heading of the dropdown and the feed. */
  Title: 'layouts.notifications.title',
  /** Accessible name of the bell, with `{{count}}` unread. */
  Bell: 'layouts.notifications.bell',
  /** Live announcement of the unread count, with `{{count}}`. */
  UnreadStatus: 'layouts.notifications.unread-status',
  Empty: 'layouts.notifications.empty',
  MarkRead: 'layouts.notifications.mark-read',
  Unread: 'layouts.notifications.unread',
  ViewAll: 'layouts.notifications.view-all',
  FilterLabel: 'layouts.notifications.filter-label',
  FilterAll: 'layouts.notifications.filter-all',
  FilterUnread: 'layouts.notifications.filter-unread',
  FilterRead: 'layouts.notifications.filter-read',
  EmptyUnread: 'layouts.notifications.empty-unread',
  EmptyRead: 'layouts.notifications.empty-read',
  LoadMore: 'layouts.notifications.load-more',
  /** Live announcement of how many notifications the feed lists, with `{{count}}`. */
  Showing: 'layouts.notifications.showing',
} as const;
