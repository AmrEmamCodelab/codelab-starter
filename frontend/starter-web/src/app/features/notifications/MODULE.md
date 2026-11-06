# In-app notifications (`features/notifications`)

## Purpose

Module 12's user interface: the signed-in reader's in-app notifications, wired to the API. It provides the real
`NotificationsSource` (the layouts' contract, `layouts/notifications-menu/notifications.ts`) that the header bell and
the feed render, and the full feed page at `/notifications`. The layouts stay presentational; this feature owns the
calls, the paging, the polling and the session and language rules.

| Piece | What it does | Files |
| --- | --- | --- |
| `NotificationsClient`, `NotificationsApiPaths` | `GET api/notifications?before=<id>[&take=<n>]` (newest first, the language asked for in `Accept-Language`), `GET api/notifications/unread-count`, `POST api/notifications/{id}/read` (204), through core `ApiService` (bearer, signed) | `data/notifications.client.ts` |
| Feed rules | `UNREAD_POLL_INTERVAL_MS` (one minute), `toNotificationItem()`, `feedFromPage()`, `appendOlder()` (no repeats), `mergeNewest()` (new first; restarts on a gap), `markReadIn()` | `data/notifications-feed.ts` |
| `ApiNotificationsSource` | The `NotificationsSource` (root): `items`, `hasMore`, `unreadCount` (the server's total), `status` (`NotificationsLoadStatus`), `loadingMore`; `markRead()`, `loadMore()`, `refresh()`. Follows `AuthService.isAuthenticated` and `LanguageService.current`: signing in or switching language loads the newest page; signing out clears everything and stops polling | `data/api-notifications.source.ts` |
| `NotificationsPageComponent` | `/notifications` in the staff shell: page title, loading/failed page feedback (retry), the layouts' `app-notifications-feed` (filter, mark-read, load more). Opening the page refreshes the newest page | `notifications-page/` |
| Routes and wiring | `NOTIFICATIONS_ROUTE_PATH`, `NOTIFICATIONS_LINK`, `NOTIFICATIONS_ROUTES` (lazy page); `provideApiNotifications()` (the source as `NOTIFICATIONS_SOURCE`, the page as `NOTIFICATIONS_FEED_LINK`) | `notifications.routes.ts`, `notifications.providers.ts` |
| Texts | `NotificationsPageTranslationKeys` (`notifications.page.*`); the feed's own texts are the layouts' `layouts.notifications.*` | `notifications-translation-keys.ts` |

## Use

```ts
// app.config.ts: one source for every bell and the page
providers: [...provideApiNotifications()]

// app.routes.ts: the page is a child of the staff shell (behind authGuard)
children: [ ..., ...NOTIFICATIONS_ROUTES ]
```

A shell shows the bell with `<app-notifications-bell />` (layouts), which reads `NOTIFICATIONS_SOURCE` and
`NOTIFICATIONS_FEED_LINK`. Nothing else imports this feature.

## Extend

- **A notification that opens a page:** the server sends `link` (an absolute in-app router link); the bell and feed
  render it as a router link and mark the notification read when it is followed.
- **A different poll cadence:** change `UNREAD_POLL_INTERVAL_MS`.
- **A different page size:** change the `notifications.page-size` runtime setting on the admin settings screen. The
  feed sends no `take`, so the server's setting decides; send `take` only for a deliberate one-off override.
- **Push instead of polling (later):** keep `ApiNotificationsSource` as the single writer; call its merge path from
  the push handler instead of the poll tick.

## Rules

- HTTP only through `NotificationsClient` on core `ApiService`. Background calls (first page, poll) are quiet
  (`quietContext()`): no error dialog. Reader-triggered calls (`markRead`, `loadMore`) use the default context, so the
  HTTP error interceptor reports a refusal; both never reject and leave the state as it was.
- The texts are written by the server in the reader's language; the client never translates a notification. A
  language switch reloads the newest page.
- An answer that arrives after a sign-out or a language switch is dropped (generation check).
- The page carries no permission key: every signed-in user has notifications, and the API answers them with
  `[AuthenticatedUser]`. It must stay inside the staff shell (behind `authGuard`) and listed in the route gate's
  signed-in pages.
- No literal "now" (`APP_CLOCK` in the layouts formats times); enums are integers; limits are named constants.

## Gotchas

- `ApiNotificationsSource` starts in its constructor's effect: the first injection (the shell's bell) loads and
  starts polling. Specs run the effect with `TestBed.tick()` and drive the poll with Vitest fake timers.
- The unread badge shows the server's total (`unreadCount`), which can exceed the loaded items; the bell falls back to
  counting `items` for a source without it (the showcase source).
- `mergeNewest` restarts the list when the newest page shares nothing with what is loaded (more than one page arrived
  between two polls); the reader loads older pages again.
- The page's retry button carries `data-public-action`: it reloads the reader's own data and needs no key.
