# Recipe: add a notification

Sends a new bilingual notification (bell, e-mail, SMS) from a module, through module 12, Notifications. There is no
generator: a notification is a template key, a migration that seeds its texts and one call to the kernel port. Module
docs: [backend/src/Modules/Notifications/MODULE.md](../../backend/src/Modules/Notifications/MODULE.md).

## 1. Decide the shape

| Question | Answer |
| --- | --- |
| Who receives it? | A user (`NotifyUserAsync`, by internal user id) or an address that is no user's (`SendToAddressAsync`, e-mail only). |
| On which channels? | `NotificationChannels` flags: `InApp` (the bell), `Email`, `Sms` (dropped while `Notifications:Sms:Enabled` is false). |
| Does a value carry a secret (a sealed ticket, a token, a one-time link)? | Then never put it in `NotifyUserAsync`: write an outbox message that names the secret by id, and from its handler call `SendImmediateEmailAsync` (nothing is stored). The account activation e-mail is the worked example. |
| Which placeholders? | `{{camelCase}}` names, case-sensitive. Values are the caller's text: e-mail bodies HTML-encode them, subjects and titles are kept on one line. |

## 2. Add the key and the placeholder names

In `Starter.Application/Notifications/NotificationTemplateKeys.cs` (or a `<Module>NotificationKeys` class in the
owning module), add the key constant (kebab-case, at most 64 characters) and document its placeholders and channels.
Add the placeholder names to `NotificationPlaceholders` (or the module's own constants class). Never write a key or a
placeholder name as a literal at the call site.

## 3. Seed the texts with a migration

Templates are seeded **only by migrations** (`notifications.NotificationTemplates`; no admin edit at run time). Add a
new migration to the Notifications module that inserts one row per channel and language: Arabic **and** English for
every channel the key is sent on. Freeze the texts in the migration (the `TemplateSeed` of `InitialCreate` is never
edited); see `Infrastructure/Persistence/Migrations/InitialCreate.cs` for the `InsertData` shape.

| Channel | Subject | Body |
| --- | --- | --- |
| `InApp` (1) | the bell title (one line, at most 256) | plain text; shown escaped by the web app |
| `Email` (2) | the e-mail subject (one line, at most 256) | HTML; wrap Arabic in `<div dir="rtl">` |
| `Sms` (4) | null | plain text, short |

Extend `TemplateSeedTests` (or add a test for the new migration's rows): every text uses exactly the documented
placeholders, and each channel has both languages.

## 4. Call the port

Inject `INotificationService` (kernel port, `Starter.Application.Notifications`) into the use case. Call it **after**
your own save succeeded (the notification row and the outbox save on their own contexts):

```csharp
await notifications.NotifyUserAsync(
    new UserNotification(
        UserId: request.OwnerUserId,
        TemplateKey: NotificationTemplateKeys.RequestApproved,
        Placeholders: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NotificationPlaceholders.ReferenceNumber] = request.ReferenceNumber,
        },
        Channels: NotificationChannels.InApp | NotificationChannels.Email,
        Link: RequestLinks.Detail(request.PublicId)),
    cancellationToken);
```

A missing text or placeholder throws `BusinessRuleException` (`NOTIFICATIONS_TEMPLATE_NOT_FOUND`,
`NOTIFICATIONS_PLACEHOLDER_MISSING`) before anything is written: it is a defect, and the unit test of the use case
catches it when it runs against the seeded texts (`TemplateSeed`-style fake template store). For a notification
that must never fail the committed operation, catch it per ERROR-HANDLING row 8.

## 5. Verify

- Unit tests of the calling use case with a fake `INotificationService` (assert the key, channels and placeholders).
- `E:\codelab-tools\dotnet10\dotnet.exe test backend/tests/Modules/Starter.Modules.Notifications.Tests`.
- Locally: run the stack, trigger the action, see the bell item in Arabic and English, the `.eml` file in
  `Notifications:Email:PickupDirectory` (default `%TEMP%\starter-mail`) and, with SMS enabled, the JSON line in
  `Notifications:Sms:FilePath` (default `%TEMP%\starter-sms\sms.jsonl`). The outbox drain job (`outbox-drain`, every
  5 minutes, or run it from the admin Jobs screen) delivers e-mail and SMS.
