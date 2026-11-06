/** The texts of the notifications page (`notifications.page.*`); the feed's own texts are `layouts.notifications.*`. */
export const NotificationsPageTranslationKeys = {
  Title: 'notifications.page.title',
  Loading: 'notifications.page.loading',
  FailedTitle: 'notifications.page.failed-title',
  FailedBody: 'notifications.page.failed-body',
  Retry: 'notifications.page.retry',
  SendTest: 'notifications.page.send-test',
} as const;

/** Every key the feature renders, for the bundle check. */
export const NOTIFICATIONS_TRANSLATION_KEYS: readonly string[] = Object.values(NotificationsPageTranslationKeys);
