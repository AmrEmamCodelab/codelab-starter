# Starter.Application

## Purpose
The kernel Application project (ADR-004): **ports only**, plus the use-case result types. Modules code against these
interfaces; adapters live in Starter.Infrastructure or in the module that owns the data.

| Area | Types | Implemented by |
| --- | --- | --- |
| Results | `Outcome<T>`, `Outcome`, `Refusal`, `RefusalCategory`, `Done`, `PlatformErrorCodes` | (values) |
| Validation | `IValidator<T>`, `ValidationErrors`, `ValidationMessages`, `FieldNames` | each module |
| Persistence | `IUnitOfWork` | each module's `ModuleDbContext` |
| Users | `ICurrentUserContext`, `IUserDirectory`, `DirectoryUser` (+ `Kind`, `OrganizationId`, `DisplayName`) | kernel (HTTP) / Identity |
| Account administration | `IAccountAdministration`, `AccountSummary`, `NewAccount`, `AccountChanges`, `AccountQuery`, `AccountPage`, `ActivationIssue` | Identity (consumed by Users) |
| Authorization | `IPermissionCatalogSource`, `PermissionDefinition`, `PermissionKeyFormat`, `IUserPermissionGrants` | each module / Access |
| Organization scope | `IOrganizationScope`, `PermissionScope` (Global = 1, OwnOrganization = 2), `ScopeDecision` | Access |
| Messaging | IOutbox, OutboxMessage, OutboxMessageTypes (AccountActivation, NotificationEmail, NotificationSms), OutboxMessageStatus (Pending = 1, Sent = 2, Failed = 3, Processing = 4), AccountActivationPayload, OutboxLimits (TypeMaxLength, MaxAttempts = 8), IOutboxHandler, OutboxDelivery, IOutboxDrainRunner, OutboxDrainResult | kernel (EfOutbox, OutboxDrainRunner); handlers in the owning module |
| Numbering | IRequestNumberAllocator (NextAsync(prefix) -> PREFIX-YYYY-NNN), RequestNumberFormat (prefix 2-6 uppercase letters, counter padded to 3 digits, Format) | kernel (RequestNumberAllocator on SQL sequences) |
| Notifications | INotificationService (NotifyUserAsync, SendToAddressAsync, SendImmediateEmailAsync), UserNotification, AddressedNotification, NotificationChannels ([Flags] None = 0, InApp = 1, Email = 2, Sms = 4), INotificationRecipients, NotificationRecipient, NotificationTemplateKeys, NotificationPlaceholders | Notifications module (service); Identity (INotificationRecipients) |
| Settings | `IRuntimeSettings`, `SettingKeys`, `SettingValueType` (Text = 1, WholeNumber = 2, Boolean = 3, DurationSeconds = 4), `SettingsLimits` (CacheTtl 30 s) | Settings module |
| Auditing | `IAuditContextAccessor`, `AuditContext`, `IAuditTrailQueries` (`AuditTrailQuery`, `AuditTrailPage`, `AuditTrailEntry`, `AuditPropertyChange`), `AuditActors` (`System` = "SYSTEM") | kernel (`HttpAuditContextAccessor`, `EfAuditTrailQueries`) |
| Jobs | IJob (Key, NameAr, NameEn, Cron or null, RunAsync(JobRunContext)), JobRunContext, JobRunStatus (Queued = 1, Running = 2, Succeeded = 3, Failed = 4), JobTrigger (Schedule = 1, Manual = 2), SystemActor (Name = "SYSTEM", = AuditActors.System), ISystemActorScope (IsActive, BeginSystemScope), JobKeyFormat (lowercase kebab, 3-64) | jobs module (catalog, runs, Hangfire); kernel (SystemActorScope) |
| Lookups | `ILookupValues` (`FindActiveAsync(typeKey, id)`), `ActiveLookupValue` | Lookups module (`ActiveLookupValues`, over its per-node cache) |
| Attachments | `IAttachableRecordAccess` (`RecordType`, `CanViewAsync`, `CanChangeAsync`), `IRequiredAttachments` (`FindMissingTypeKeysAsync`; call it through `RequiredAttachmentsGate.RefuseWhileMissingAsync`, which answers the 409 refusal or null), `AttachmentErrorCodes.RequiredMissing` (`ATTACHMENTS_REQUIRED_MISSING`) | owning modules (record access); Attachments module (`RequiredAttachmentsCheck`) |
| Localization | `ICurrentLanguage` | kernel (HTTP) |

## Use
```csharp
public sealed class CloseRecord(IRecordRepository records, IRecordsUnitOfWork unitOfWork, IValidator<CloseRecordCommand> validator)
{
    public async Task<Outcome<Done>> HandleAsync(CloseRecordCommand command, CancellationToken cancellationToken)
    {
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);                                  // 400, every field error at once
        }

        var record = await records.FindAsync(command.Id, cancellationToken);
        if (record is null)
        {
            return Refusal.NotFound(RecordsErrorCodes.RecordNotFound);        // 404
        }

        record.Close();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Done.Value;
    }
}
```
Field names come from `FieldNames.Of(nameof(Command.Member))`; messages from `ValidationMessages`.

Another module's data goes through a port, never its tables:
```csharp
var created = await accounts.CreateAsync(new NewAccount(userName, displayName, email, LoginName: null, Upn: null,
    UserKind.External, AccountBinding.Password, organizationId), cancellationToken);
var scope = await organizationScope.ScopeForAsync(RecordsPermissionKeys.View, cancellationToken);
if (scope.Scope == PermissionScope.OwnOrganization) { query = query.Where(r => r.OrganizationId == scope.OrganizationId); }
await outbox.EnqueueAsync(new OutboxMessage(OutboxMessageTypes.AccountActivation,
    JsonSerializer.Serialize(new AccountActivationPayload(userPublicId, issue.TicketId, issue.ExpiresAt)), NotBefore: null), cancellationToken);
```

## Extend
- **Deliver an outbox message type:** implement IOutboxHandler in the owning module (MessageType = the OutboxMessageTypes constant; HandleAsync must be idempotent per OutboxDelivery.MessageId, because delivery is at least once) and register it in Add<Name>Module with services.AddOutboxHandler<THandler>(). Throw to retry (back-off 1/5/15/60 min, Failed after OutboxLimits.MaxAttempts); only the classified error code is stored. Exactly one handler per type.
- **A reference number:** inject IRequestNumberAllocator and call NextAsync(MyModuleNumberPrefixes.X) with a prefix constant (2-6 uppercase letters, e.g. "CU"); allocate after validation, since a number is consumed even when the caller's save fails (gaps, never duplicates).
- **Send a notification:** inject INotificationService and follow docs/recipes/add-notification.md (key constant, a Notifications migration with ar + en texts, call after your own save; secret-bearing links only through SendImmediateEmailAsync from an outbox handler).
- **A background job:** implement IJob in the owning module (key and cron from constants; RunAsync idempotent, throws to fail) and register it with services.AddStarterJob<TJob>() (Starter.Infrastructure.Jobs). The jobs module schedules, runs (as SYSTEM, in a system scope) and records it. Recipe: backend/src/Modules/Jobs/MODULE.md, 'Add a job'.
- **Work no person does:** wrap it in ISystemActorScope.BeginSystemScope(); inside, ICurrentUserContext is anonymous and the audit actor is SYSTEM.
- **A new cross-module port:** add the interface here (with records for its data), implement it in the owning module
  and register it in that module's `Add<Name>Module`. Never let one module reference another module's project.
- **A new outbox message type:** add a constant to `OutboxMessageTypes` and a payload record next to it; the payload
  names secrets (ticket ids) and never carries them.
- **A new refusal kind:** add a `RefusalCategory` member with the next explicit value, a factory on `Refusal`, the
  mapping in `Starter.Infrastructure/Results/OutcomeResults.cs` and tests on both sides.
- These types are frozen for the modules (contract §3): add members, never rename, remove or change a signature.

## Rules
- Ports only: no EF Core, ASP.NET Core, configuration, DI or Codelab package reference (`starterapplication.no-frameworks`).
- A use case returns `Outcome<T>`; it throws only for faults (ERROR-HANDLING rows 1 and 4).
- Permission keys match `PermissionKeyFormat.Pattern` (`^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$`, at most 128 characters).
- Validators are pure: they never throw and return every field error at once.
- `IAccountAdministration` never takes or returns a password, hash or default password; a Password-bound account gets
  an activation ticket (`IssueActivationAsync`), refused for break-glass and for ActiveDirectory/Sso bindings.
  Deactivation and a re-issued activation bump the user's session version (the next API call answers 401).
- An outbox payload never holds a password, token or sealed ticket (`AccountActivationPayload` names the ticket id only).
- Tests: `backend/tests/Starter.Application.Tests` (Outcome, Refusal, ValidationErrors, PermissionKeyFormat, FieldNames;
  `Messaging/KernelPortContractTests` for the phase 3 enum values, the `DirectoryUser` defaults and the payload shape).

## Gotchas
- **The outbox is not transactional with the caller yet** (until phase 5): `IOutbox.EnqueueAsync` saves in the
  kernel's own context, so enqueue after your module's save succeeded and keep payloads safe to deliver late or
  twice. Nothing drains the outbox yet; rows stay Pending.
- `DirectoryUser` gained `Kind` (default Internal) and `OrganizationId` (default null) as optional trailing members;
  an adapter that does not set them reports every user as an internal user without an organization.
- `Outcome<T>.Value` throws on a refusal; branch on `IsSuccess` first (the compiler knows `Refusal` is not null then).
- `PlatformErrorCodes.Validation` mirrors Codelab.Errors (`VALIDATION_FAILED`); this assembly references no package, so
  a test in `Starter.Api.Tests` fails when the two drift.
- `ValidationErrors.ToDictionary()` returns a copy; later `Add` calls do not change a refusal already built.
