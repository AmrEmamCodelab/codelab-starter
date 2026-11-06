# Notifications module

## Purpose

Notifications (module 12), an in-process module of the API: there is no separate notification service to deploy or
to give its own connection string. It owns:

- **Templates** (`notifications.NotificationTemplates`): one text per key, channel and language, seeded **only by
  migrations**. Placeholders are `{{camelCase}}`. Seeded keys (`NotificationTemplateKeys`): `account-activation`
  (e-mail), `contact-received` (e-mail), `test-notification` (in-app, e-mail, SMS), each in Arabic and English;
  migration `AddServiceRequestTemplates` adds the deletable sample's `service-requests.submitted`, `.under-review`,
  `.returned`, `.resubmitted`, `.approved`, `.rejected`, `.cancelled` and `.sla-escalation` (in-app and e-mail,
  placeholder `{{recordNumber}}`, plus `{{dueAt}}` in the escalation). Those rows stay when the sample is removed.
- **Notifications** (`notifications.Notifications`): a user's bell item (title and body rendered at once in Arabic
  and English, a link, a read time) plus the delivery state of its e-mail and SMS channels (`ChannelDeliveryState`:
  NotRequested 1, Pending 2, Sent 3, NoAddress 4, Disabled 5), or an e-mail to an address that is no user's. The
  placeholder values are kept only while a delivery waits. The table is not audited (`[NotAudited]`): it is a delivery
  trail of its own and holds addresses and free text.
- **Delivery**: e-mail and SMS go through the kernel outbox (`notification-email`, `notification-sms`), retried with
  the outbox back-off (1/5/15/60 min, Failed after 8 attempts). Senders: `SmtpEmailSender` (the internal relay in the
  backend zone; no credentials, no internet egress, D2) or `FileDropEmailSender` (`.eml` files, Development, or a test or
  CI host that sets `AllowFileSink`), `FileSmsSender` (JSON lines, the same rule) or `GatewaySmsSender` (a stub until phase 8: the
  integration gateway SMS relay profile through Codelab.Integration with the ES256 service assertion).
- **The feed API** for the bell and the notifications page, and the administrator's test send.

## Use

Other modules use the kernel ports in `Starter.Application.Notifications`, never this module's types:

```csharp
await notifications.NotifyUserAsync(
    new UserNotification(userId, NotificationTemplateKeys.TestNotification, placeholders, NotificationChannels.InApp | NotificationChannels.Email, Link: null),
    cancellationToken);
```

- `NotifyUserAsync`: writes the bell item now (with `InApp`), queues e-mail and SMS. SMS ends `Disabled` while
  `Notifications:Sms:Enabled` is false. Address and language are read at delivery from `INotificationRecipients`
  (implemented by Identity); a user without an address for the channel ends `NoAddress` (not retried).
- `SendToAddressAsync`: an e-mail to an address in a given language (the contact inbox), queued.
- `SendImmediateEmailAsync`: renders and sends now in the recipient's language, stores nothing. For secret-bearing
  links (activation); call it from an outbox handler so a failure is retried there.
- A missing template text or placeholder value throws `BusinessRuleException` with `NOTIFICATIONS_TEMPLATE_NOT_FOUND`
  or `NOTIFICATIONS_PLACEHOLDER_MISSING` before anything is written; a malformed notification (no or unknown channel,
  blank key, link over 512) throws `NOTIFICATIONS_INVALID_NOTIFICATION`.

| Endpoint | Access | Body / answer |
| --- | --- | --- |
| `GET /api/notifications?before=<id>&take=<n>` | any signed-in user (allowlisted) | `{items:[{id, titleText, bodyText, at, read, link}], hasMore}`: own bell items, newest first, in the request language; `take` defaults to the runtime setting `notifications.page-size` (seeded 20), at most 50 |
| `GET /api/notifications/unread-count` | any signed-in user (allowlisted) | `{count}` |
| `POST /api/notifications/{id}/read` | any signed-in user (allowlisted) | 204; idempotent; another user's id is 404 `NOTIFICATIONS_NOTIFICATION_NOT_FOUND` |
| `POST /api/admin/notifications/test` | `notifications.test` | 202; notifies the caller with `test-notification` on in-app, e-mail and (when enabled) SMS; 404 `NOTIFICATIONS_RECIPIENT_NOT_FOUND` for a caller without an account |

Ids on the wire are `PublicId<NotificationIds>` (label `notifications`); times are ISO 8601 UTC. A caller without a
user account sees an empty feed and a zero count.

Configuration (validated on start; see `docs/generated/config-keys.md`):

| Key | Default | Meaning |
| --- | --- | --- |
| `Notifications:Email:Mode` | `2` Smtp (`1` in `appsettings.Development.json`) | `1` FileDrop (Development only, unless `AllowFileSink`), `2` Smtp |
| `Notifications:Email:AllowFileSink` | `false` | `true` lets a test or CI host use FileDrop outside Development; never on a server |
| `Notifications:Email:FromAddress` / `FromName` | `no-reply@example.test` / `Starter` | the sender; every deployment sets its own address |
| `Notifications:Email:PickupDirectory` | `%TEMP%\starter-mail` | absolute folder of the `.eml` files |
| `Notifications:Email:Smtp:Host` / `Port` / `EnableTls` / `TimeoutSeconds` | none / 25 / true / 30 | the internal relay (Host required in Smtp mode; port 1-65535; timeout 1-300 s) |
| `Notifications:Sms:Enabled` | `false` | SMS on or off (init ticks it per project) |
| `Notifications:Sms:Mode` | `1` File | `1` File (refused at start outside Development while enabled, unless `AllowFileSink`), `2` Gateway (refused at start while enabled, until phase 8) |
| `Notifications:Sms:AllowFileSink` | `false` | `true` lets a test or CI host use the SMS file outside Development |
| `Notifications:Sms:FilePath` | `%TEMP%\starter-sms\sms.jsonl` | absolute path of the SMS file |

## Extend

- **A new notification:** follow [the add-notification recipe](../../../../docs/recipes/add-notification.md): a key
  constant, a Notifications migration with the Arabic and English texts per channel, one call to the port.
- **Change wording:** a new migration that updates the rows; never edit `TemplateSeed` (it is frozen with
  `InitialCreate`).
- **A direct SMS adapter** (a provider reachable inside the backend zone): implement `ISmsSender` in
  `Infrastructure/Sending`, add an `SmsDeliveryMode` value with an explicit number and select it in
  `NotificationsModuleRegistration.AddSenders`. The default path (phase 8) is `GatewaySmsSender`.
- **New permission:** `node tools/scaffold/new-permission.mjs --module Notifications --action <kebab>`.

## Rules

- `module.rules.yaml` holds this module's rules; run `node tools/rules-engine/compile.mjs` after changing it.
- Never reference another module's project; users are reached through `INotificationRecipients`, `IUserDirectory`
  and `ICurrentUserContext`.
- Templates come only from migrations, in Arabic and English for every channel a key is sent on.
- Outbox payloads carry the notification id and channel only (`DeliveryPayload`). Logs carry ids, never addresses,
  placeholder values or rendered texts.
- No SMTP credentials (`NetworkCredential` is a forbidden pattern); no sender calls an internet host.
- Every use case, the renderer, the handlers, the senders and the options have unit tests in
  `backend/tests/Modules/Starter.Modules.Notifications.Tests`.

## Gotchas

- **Delivery is at least once.** A channel is sent only while it is Pending and recorded Sent right after the sender
  returns; a crash between the two re-sends it on the next drain (SMTP has no idempotency key).
- **Two saves, no shared transaction.** The notification row is saved, then the outbox messages (the outbox saves on
  its own context). A failure between them leaves the channel Pending with no message: it is never sent. Call the
  port after your own save.
- **E-mail and SMS of one notification run in separate drains.** Each records its own state; when both finish at
  the same moment the placeholder values may stay on the row (no other effect).
- **Placeholder values are stored until delivered.** That is why secrets go through `SendImmediateEmailAsync`.
- **A deployment must name its relay.** The default mode is Smtp and `Smtp:Host` has no default, so a server without
  `Notifications:Email:Smtp:Host` fails at start. FileDrop (and the SMS file) are refused outside Development unless
  `AllowFileSink` is set: the files would hold activation links and personal data in clear on the server and be
  marked Sent.
- **The SMTP sender is tested against an in-process relay stub, not smtp4dev.** `SmtpEmailSenderTests` sends through
  a loopback SMTP stub (`SmtpStub`: no STARTTLS, no credentials) and checks the envelope, the UTF-8 subject and the
  HTML body; with `EnableTls` true a relay that offers no STARTTLS is refused, never sent to in clear. The phase 5
  integration flows still read `.eml` files (FileDrop), because no SMTP sink runs in CI.
- **The bell texts are rendered when the notification is written**: a later wording migration changes new
  notifications only.
- `CLAUDE.md`, `BannedSymbols.generated.txt`, `.editorconfig` and `CodeMetricsConfig.txt` in this folder are generated
  by the rules engine; never edit them by hand.
